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

            // ★ autoPlay = 系统自动按 Start 键：开新局 / 过确认点（无 Hold&Spin respin 分支）。
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
        /// <summary>开始 / 停止键。转轮滚动中→急停；否则开新一局（A/B 基础旋转共用同一条开局路径，无 Hold&Spin respin 分支）。</summary>
        public void OnStartKey()
        {
            // ★★ 顺序修正（修"圈圈没到零但按确认就消失"）：
            //   HideAllCounters / HideAllJackpotEffects 原先放在所有守卫之【前】无条件执行 —— 但下面任一守卫
            //   (IsRolling / IsSpinning / _spinPending / 无押注) 都会 return 而【不开新局】，
            //   结果是"清了计数器却没有新局去重建" → 玩家一按确认，收集盘的圈圈就整片消失（火球还留着）。
            //   尤其：确认键 Down 可能跨帧持续，第 1 帧被 _waitingConfirm 吃掉、第 2 帧落到这里，
            //   此时 _spinPending 仍为 true → 清完就 return，圈圈永久消失。
            //   修法：所有守卫先跑，确认"真的要开新一局"了，再统一清一次（仍满足"开新基础局前只跑一次"）。

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

            // ★ 到这里已确定要开新一局：统一同步 + 清理上局残留（整个开局路径只跑一次）。
            if (m_reelView != null)
            {
                m_reelView.CheckEngagedAll();
                m_reelView.HideAllCounters();
            }
            if (m_bonus != null)
                m_bonus.HideAllJackpotEffects();
            else
                UnityEngine.Debug.LogWarning("[OnStartKey] m_bonus==null! 无法隐藏彩金特效（BonusView 未挂载或未赋值）");

            // ★ 开新一局：先清赢分显示(归 0)，让"0"出现在转轮启动这一刻而非上一局漏光时
            m_player.ResetWinDisplay();

            m_machine.totalBet = m_player.m_bet_num;
            m_machine.session.Contribute(m_player.m_bet_num);

            var r = m_machine.Spin();
            if (r != null) StartBaseSpin(r);
        }

        void OnEnhanceKey()
        {
            // ★ 游戏进行中/结算中禁止改注(压分)：街机规则，避免中途改注导致账目/手感歧义
            if (IsBusy()) return;
            m_player.BetUp();
            m_machine.totalBet = m_player.m_bet_num;
            // ★ 加注不注水：渐进池只在真正下注(Start)时 Contribute（Contribute 末尾自动刷新 BonusView）
        }

        void OnStopKey()
        {
            if (m_reelView != null) m_reelView.StopNow();
        }
        #endregion
    }
}
