using System;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;   // GameResult
using Com.Back;          // DataManager(存档, 可选; 未初始化时降级为默认值)

namespace com.slot
{
    /// <summary>
    /// 三七机玩家面板：管理「压 / 总 / 赢」三个文本，并负责押注、收分滚动、存档。
    ///
    /// 参考 PandaParadis/PlayerView 的写法，但去掉扑克专属逻辑(pushHold / doubleUp / 小游戏提示)，
    /// 只面对 3 个文本。核心模式沿用参考：
    ///   - RefreshNumbers() 只刷数值(开销小, 滚动每帧调用)；RefreshUI() 在其基础上扩展提示(本类无提示, 等同调用)。
    ///   - 押注(BetUp/BetDown/LastBet)：从余额挪 step 到押注，受 m_betMax / 余额约束。
    ///   - 旋转结算(ApplySpinResult)：把赢分写进 m_win_num 并用 CoRollCredit 滚动进余额(押注在旋转时已被扣除)。
    ///   - 上分(AddCredits)：投币进余额，同样带滚动动画。
    ///   - 数值单位为「分」，与 GameResult.totalPayout / DataManager.Player[1] 一致。
    /// </summary>
    public class PlayerView : MonoBehaviour
    {
        [Tooltip("压注时每按一下加的数值")]
        public long m_betStep = 10;
        [Tooltip("压注上限")]
        public long m_betMax = 80;

        [Header("数值")]
        public long m_bet_num;     // 压(当前押注)
        public long m_win_num;     // 赢(上一局赢分)
        public long m_credit_num;  // 总(余额)
        public long m_lastBet = 0; // 上一局押注(复押用)

        [Header("Text 控件(对应 压/赢/总)")]
        public Text m_betText;
        public Text m_winText;
        public Text m_creditText;

        [Header("音效")]
        [Tooltip("是否播放收分音(需先在 FMOD 接入事件路径, 见 PlayWinSound)")]
        public bool m_playSound = false;

        /// <summary>是否正在滚动动画中(供外部等待动画完成)。</summary>
        public bool IsRolling => _rolling;

        /// <summary>收分音相对数字跳动的"起拍提前量"(秒)，参照 PandaParadis，想调 2 秒改这一个常量。</summary>
        public const float HarvestSoundLead = 0.5f;

        /// <summary>数码管显示封顶(8 位)，真实余额仍保留在 m_credit_num。</summary>
        public const long CreditDisplayCap = 99999999L;

        // 收分/投币数字跳动的上下文(实际动画在 CreditRoller 单例里跑, 这里只存本面板的状态)
        private bool _rolling;
        private long _rollStartCredit;
        private long _rollDelta;
        private bool _rollResetBet;
        /// <summary>滚动令牌：每次 StartRollCore 自增；OnRollDone 携带启动时的令牌，仅当仍等于当前令牌才落账。
        /// 防止"旧协程的 onDone 在新滚动启动后才触发"时误清 _rolling / 误调 FinalizeRoll（偶发丢分根因）。</summary>
        private int _rollToken;

        void Start()
        {
            LoadNum();
        }

        void OnDestroy()
        {
            // 收尾本面板状态(落账, 避免丢分)；动画在 CreditRoller 单例里, 一并停掉
            if (_rolling) FinalizeRoll();
            _rolling = false;
            CreditRoller.StopIfAny();   // 不触发懒创建(避免场景关闭时 new 出残留对象)
        }

        /// <summary>从 DataManager 读取余额/押注/赢分(未初始化则用默认值)。</summary>
        public void LoadNum()
        {
            var dm = DataManager.Instance;
            if (dm != null && dm.Player != null && dm.Player.ContainsKey(1))
            {
                var p = dm.Player[1];
                m_credit_num = p.score;
                m_bet_num = p.bet;
                m_win_num = p.win;
                // 存档押注超过当前上限(如后台调低最大押注)：超出部分退回余额
                if (m_bet_num > m_betMax)
                {
                    m_credit_num += m_bet_num - m_betMax;
                    m_bet_num = m_betMax;
                }
            }
            else
            {
                // DataManager 未就绪(纯逻辑测试/首启)：给一个初始余额, 押注/赢分清零
                m_credit_num = 1000;
                m_bet_num = 0;
                m_win_num = 0;
            }
            m_lastBet = m_bet_num;
            RefreshUI();
        }

        private void SaveData()
        {
            var dm = DataManager.Instance;
            if (dm == null || dm.Player == null || !dm.Player.ContainsKey(1)) return;
            dm.Player[1].score = m_credit_num;
            dm.Player[1].bet = m_bet_num;
            dm.Player[1].win = m_win_num;
            dm.SaveData();
        }

        /// <summary>只刷新数值显示(三个 Text)。开销小, 滚动动画每帧调用。</summary>
        public void RefreshNumbers()
        {
            if (m_betText != null) m_betText.text = m_bet_num.ToString();
            if (m_winText != null) m_winText.text = m_win_num.ToString();
            if (m_creditText != null)
            {
                // 超过 8 位时显示封顶 99999999(类似真实机台数码管封顶), 真实余额仍保留在 m_credit_num
                long disp = m_credit_num > CreditDisplayCap ? CreditDisplayCap : m_credit_num;
                m_creditText.text = disp.ToString();
            }
        }

        /// <summary>刷新界面(本类不含提示物件, 等同 RefreshNumbers)。</summary>
        public void RefreshUI()
        {
            RefreshNumbers();
        }

        /// <summary>加注：从余额挪一个 step 到押注(受上限与余额约束)。</summary>
        public void BetUp()
        {
            if (IsRolling) return;
            long maxAdd = System.Math.Min(m_betStep, System.Math.Min(m_credit_num, m_betMax - m_bet_num));
            if (maxAdd <= 0) return;

            m_bet_num += maxAdd;
            m_credit_num -= maxAdd;
            m_lastBet = m_bet_num;
            RefreshUI();
            SaveData();

            FMODSoundMgr.Instance.PlaySound("event:/Sounds/3");
        }

        /// <summary>复押上局：把余额补到上次押注(不足则全押剩余余额)。</summary>
        public void LastBet()
        {
            if (IsRolling) FinalizeRoll();   // 进行中的收分动画先落账，再继续提交本轮押注（Hold&Spin respin 期间按 Start 也要能扣到分）
            if (m_lastBet <= 0) return;
            long target = System.Math.Min(m_lastBet, m_betMax);
            long need = target - m_bet_num;
            if (need <= 0) return;

            long add = System.Math.Min(need, m_credit_num);
            m_bet_num += add;
            m_credit_num -= add;
            RefreshUI();
            SaveData();
        }

        /// <summary>消耗本轮押注：把押注清 0（余额已在本轮开始通过 LastBet 挪出，这里不退回）。
        /// Hold&amp;Spin 每轮 respin 结束调用——压的分已“花掉”，下一轮再按下注键重新提交。</summary>
        public void ResetBet()
        {
            if (_rolling) FinalizeRoll();   // 先收尾进行中的收分动画（其 _rollResetBet 沿用），避免丢分/状态错乱
            m_bet_num = 0;
            RefreshUI();
            SaveData();
        }

        /// <summary>上分(投币)：余额逐步增加, 带滚动动画。</summary>
        public void AddCredits(long amount)
        {
            if (amount <= 0) return;

            StartRollCore(amount, false);
            DataManager.Instance.Account[1].coin += amount;
            FMODSoundMgr.Instance.PlaySound("event:/Sounds/coin_1");
        }

        /// <summary>火球 Hold&amp;Spin 结束：把累计特性赢分(含本次押注倍数)滚进余额，并把"赢"显示为该金额。</summary>
        public void AddFeatureWin(float amount)
        {
            if (amount <= 0) return;
            long amt = (long)System.Math.Round(amount);
            m_win_num = amt;
            StartRollCore(amt, false);
        }

        /// <summary>仅把赢分滚进余额，不改 m_win_num 显示（调用方已用 ShowWinValue 显示过总额）。
        /// 用于 Hold&Spin 收尾"只补差额"：每轮即时落账的部分不再重复显示/重复加。</summary>
        public void AddWinToCredit(long amount)
        {
            if (amount <= 0) return;
            StartRollCore(amount, false);
        }

        /// <summary>仅显示赢分（不滚动入账），用于"转轮停稳后先亮出赢分，稍后再滚进总分"的第一拍。</summary>
        public void ShowWinValue(long win)
        {
            if (_rolling) FinalizeRoll();   // 先收尾进行中的收分动画，避免被新显示覆盖/丢分
            m_win_num = win;
            RefreshUI();
        }

        /// <summary>开新一局前清赢分显示(归 0)，让"赢分清零"发生在转轮启动那一刻，
        /// 而非上一局赢分漏光的那一刻，避免结算时看到 0→赢分 的来回跳。</summary>
        public void ResetWinDisplay()
        {
            if (_rolling) FinalizeRoll();
            m_win_num = 0;
            RefreshUI();
        }

        /// <summary>
        /// 收到一次旋转结果：把赢分写进 m_win_num 并用 CreditRoller 滚动进余额。
        /// 押注在旋转时已被引擎按 m_bet_num 扣除(见 GameManager 把 m_bet_num 赋给 machine.totalBet)，
        /// 故这里只需把赢分滚进余额, 并在动画结束时把押注清 0。
        /// </summary>
        public void ApplySpinResult(GameResult result)
        {
            if (result == null) return;
            long win = (long)System.Math.Round(result.totalPayout);
            m_win_num = win;
            StartRollCore(win, true);
        }

        private void StartRollCore(long delta, bool resetBet)
        {
            // 先收尾进行中的滚动(避免被新滚动打断时丢分), 再启动新滚动
            if (_rolling) FinalizeRoll();

            _rollStartCredit = m_credit_num;
            _rollDelta = delta;
            _rollResetBet = resetBet;
            _rolling = true;
            int myToken = ++_rollToken;   // 本轮令牌：旧 onDone 若在新滚动启动后才触发，令牌不匹配则忽略

            float dur = CreditRoller.DurationFor(delta);
            // 声音在 t=0 起拍；数字等待 HarvestSoundLead 后起步(与 PandaParadis 同步方式一致)
            PlayWinSound();
            CreditRoller.Instance.Roll(dur, OnRollTick, () => OnRollDone(myToken), HarvestSoundLead);
        }

        /// <summary>滚动每帧回调：按进度 t(0..1) 插值余额(往上滚入赢分)。
        /// 赢分(m_win_num)保持结算时显示的值不变(不再往下漏到 0)，避免"先 0→再 32→再漏 0"的视觉来回。</summary>
        private void OnRollTick(float t)
        {
            m_credit_num = _rollStartCredit + (long)(_rollDelta * t);
            RefreshNumbers();
        }

        /// <summary>滚动完成回调。带令牌校验：仅当本回调对应的滚动仍是"当前"滚动时才落账，
        /// 否则（已被新 StartRollCore 顶替）直接忽略——避免旧协程的 onDone 在新滚动进行中误将 _rolling 清零、
        /// 误调 FinalizeRoll，导致外部 IsRolling 守卫失效、自动连转(autoPlay)抢开新局而丢分（偶发 bug）。</summary>
        private void OnRollDone(int token)
        {
            if (token != _rollToken) return;
            _rolling = false;
            FinalizeRoll();
        }

        /// <summary>
        /// 收尾进行中的滚动：余额直接跳到目标值并落账, 赢分保持当前显示值不变(不回 0)。
        /// 避免被新滚动/押注打断时丢分。
        /// </summary>
        private void FinalizeRoll()
        {
            m_credit_num = _rollStartCredit + _rollDelta;
            if (_rollResetBet) m_bet_num = 0;
            RefreshNumbers();
            SaveData();
        }

        /// <summary>
        /// 收分音钩子。默认不播放(需先在 FMOD 接入事件并打开 m_playSound)。
        /// 例: if (FMODSoundMgr.Instance != null) FMODSoundMgr.Instance.PlaySound("event:/Common/收获音");
        /// </summary>
        private void PlayWinSound()
        {
            if (!m_playSound) return;
            // TODO(FMOD): 接入实际事件路径, 例如:
            // if (FMODSoundMgr.Instance != null) FMODSoundMgr.Instance.PlaySound("event:/Common/收获音");
        }
    }
}
