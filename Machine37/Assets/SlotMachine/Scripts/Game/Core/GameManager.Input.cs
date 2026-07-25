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
        /// <summary>开始 / 停止键。每次按确认都是新的一局（基础旋转）：火球已改为"落地即结算"，不再有 Hold&Spin 多轮 respin。</summary>
        public void OnStartKey()
        {
            // ★ 每次按确认（任何守卫前）100% 先跑：滚动之前统一同步计数器（用户硬性要求）。
            //   现 Hold&Spin 已去除，每次按确认都是新基础局，计数器在滚动前先清、结算后由统计显示。
            if (m_reelView != null)
                m_reelView.CheckEngagedAll();    // 统计模式下为兼容调用保留（无害）

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
