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
            // ★ Hold&Spin 进行中：Start 键 = 推进一轮 respin（不是开新局）
            //   不在此处拦截 IsRolling——Hold&Spin 连续多轮，上一轮 credit roll 不应阻塞下一轮 Start。
            //   下方的 LastBet 会自动 FinalizeRoll 收尾进行中的滚动（不丢分），再扣新一轮押注。
            if (_activeHold != null)
            {
                if (!_holdRolling)                       // 上一轮还在滚动时忽略（防狂按）
                    StartCoroutine(AdvanceHoldSpin());   // ★ 每轮 respin 的扣压分已移进 AdvanceHoldSpin 的 while 循环内（每轮各扣一次）
                return;
            }

            // ★ 赢分数字滚动期间，不允许任何操作（防穿透开新局 / 防打断收分动画）
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
            if (m_reelView != null) m_reelView.HideAllCounters();   // 开新基础局才清掉上一局 Hold&Spin 的火球计数器（满列收集/列释放中途不再隐藏，一直撑到此刻）

            m_machine.totalBet = m_player.m_bet_num;
            m_machine.session.Contribute(m_player.m_bet_num);

            // ★ 测试开关：强制本局触发免费游戏（进入 Mini，奖励 5 次免费旋转）
            if (m_machine.session != null) m_machine.session.testForceFreeGame = testForceFreeGame;

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
