using UnityEngine;

using Com.Controller;

namespace com.slot
{
    /// <summary>GameManager 输入部分：每帧读键 + 开始/停止/加注键处理。</summary>
    public partial class GameManager
    {
        #region 每帧输入
        void Update()
        {
            if (m_machine == null || m_player == null) return;
            if (_miniActive) return;   // ★ Mini 进行中：主游戏忽略所有输入（Mini 自带流程）

            // ★ 运行时切换 autoPlay：编辑器/模拟器按 F1 开/关（Inspector 勾选同样生效）
            if (Input.GetKeyDown(KeyCode.F1))
            {
                autoPlay = !autoPlay;
                Debug.Log($"[AutoPlay] toggled -> {autoPlay}");
            }

            // ★ autoPlay = 系统自动按 Start 键：开新局 / 推进 Hold&Spin 每轮 / 过确认点。
            //   转轮正在滚动时(autoStart=false)不触发，避免把正在转的卷轴中途急停；
            //   等它自然停稳后下一帧即自动继续。OnStartKey 内部已对所有状态做守卫(防重复触发)。
            bool autoStart = autoPlay && (m_reelView == null || !m_reelView.IsSpinning());
            if (GameController.Instance.m_keys[(int)InputAction.Start] == (int)InputPhase.Down || autoStart)
            {
                if (_waitingConfirm) { _waitingConfirm = false; return; }
                OnStartKey();
            }

            if (GameController.Instance.m_keys[(int)InputAction.Enhance] == (int)InputPhase.Down)
                OnEnhanceKey();

            if (GameController.Instance.m_keys[(int)InputAction.Stop] == (int)InputPhase.Down)
                OnStopKey();
        }


        #endregion

        #region 输入处理
        /// <summary>开始 / 停止键。Hold&Spin 中→推进一轮；转轮滚动中→急停；否则开新一局。</summary>
        public void OnStartKey()
        {
            // ★ 每次按确认（任何模式 / 任何分支 / 任何守卫前）100% 先跑：滚动之前统一同步计数器。
            //   用户硬性要求：新基础局 与 Hold&Spin respin 两种模式，滚动之前都必须运行 CheckEngagedAll + HideAllCounters，且只跑一次。
            //   —— 直接回答"同一个方法为什么运行2次"：原先在 IsRolling 守卫前/后各调一次 HideAllCounters，现合并到顶部唯一一处。
            if (m_reelView != null)
                m_reelView.CheckEngagedAll();    // m_num<=0 → 清 engaged（无火球列不残留）

            // ★ Hold&Spin 进行中：Start 键同样 = 新的一轮（用户模型：按确认滚动就是新局）。
            //   与基础局的唯一区别只是"转的内容"：respin 只转空格、已锁火球保留；而非"是否新局"。
            //   计数器层面两者已完全统一——上方滚动前都已 CheckEngagedAll + HideAllCounters（先清），
            //   本分支 AdvanceHoldSpin 内 ApplyRespinStep 会 ActivateCounters 重算（后亮），即"每轮先清后重算"。
            //   不在此处拦截 IsRolling；但 AdvanceHoldSpin 续轮分支会在 yield break 前等到 m_player.IsRolling（本轮赢分滚动）
            //   结束才放行下一轮 Start（2026-07-25 拍板"急停+结算完才推进"）——即信用滚动播完前连按不会连开下一轮 respin。
            if (_activeHold != null)
            {
                // ★ 正在 respin 视觉滚动（_holdSpinning，已纳入 IsSpinning）→ 像普通局一样：按确认 = 急停。
                //   普通局 OnStartKey 下方也是"IsSpinning 时 StopNow"，这里把 Hold 模式统一到同一条路，不再分两套。
                //   之前 Hold 分支在滚动时直接 return（no-op），导致"按确认停下"在 Hold 无效——已修。
                if (m_reelView != null && m_reelView.IsSpinning())
                {
                    m_reelView.StopNow();
                    return;
                }
                // 否则（等确认 / 满列掉落动画等静默期）：不在协程内才启动 AdvanceHoldSpin，
                // 否则交给 WaitForConfirmKey 内部推进下一轮（原逻辑不变，防重入）。
                // ★ 防重入关键：_holdRolling 必须在 StartCoroutine【之前】同步置位——Unity 的 StartCoroutine 只是注册，
                //   协程首行(_holdRolling=true)要下一帧才执行，若不提前置位，同一帧狂按会继续/确认键会注册多个
                //   AdvanceHoldSpin 协程同时跑（多 SpinHoldRound 并发）→ 表现「疯狂」。提前置位即可堵死这一帧窗口。
                if (!_holdRolling)                       // 完全不在协程内才启动（防重入）
                {
                    _holdRolling = true;                 // 同步置位，挡同一帧狂按导致的多协程
                    StartCoroutine(AdvanceHoldSpin());   // ★ 每轮 respin 的扣压分已移进 AdvanceHoldSpin 的 while 循环内（每轮各扣一次）
                }
                return;
            }

            // ★ 赢分数字滚动期间，不允许真正开新局（防穿透 / 防打断收分动画）
            if (m_player != null && m_player.IsRolling) return;

            // 真正在转时才当"停止键"
            if (m_reelView != null && m_reelView.IsSpinning())
            {
                m_reelView.StopNow();
                return;
            }

            // 已在转 / 结算 / 火球掉落中 → 忽略重复 Start（防狂按穿透）
            if (_spinPending) return;

            // 没押注则先自动加最小押注（余额不足则跳过）
            if (m_player.m_bet_num <= 0) m_player.LastBet();
            if (m_player.m_bet_num <= 0) return;

            // ★ 开新一局：先清赢分显示(归 0)，让"0"出现在转轮启动这一刻而非上一局漏光时
            m_player.ResetWinDisplay();

            if (m_reelView != null)
                m_reelView.HideAllCounters();    // 归零并整体隐藏（开新局 / 特性每轮重算前都先清）

            m_machine.totalBet = m_player.m_bet_num;
            m_machine.session.Contribute(m_player.m_bet_num);

            var r = m_machine.Spin();
            if (r != null) StartBaseSpin(r);
        }

        void OnEnhanceKey()
        {
            m_player.BetUp();
            m_machine.totalBet = m_player.m_bet_num;
            m_machine.session.Contribute(m_player.m_bet_num);
            m_bonus.ShowPots(m_machine.session.Pots);
        }

        void OnStopKey()
        {
            if (m_reelView != null) m_reelView.StopNow();
        }
        #endregion
    }
}
