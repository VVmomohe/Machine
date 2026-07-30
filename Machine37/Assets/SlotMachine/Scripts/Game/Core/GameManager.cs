using System.Collections.Generic;
using UnityEngine;

using Com.Back;
using SlotMachine.Core;   // GameResult

namespace com.slot
{
    /// <summary>
    /// 三七机主控（MonoBehaviour）。按职责拆成多个 partial 文件：
    ///   GameManager.cs        —— 引用/字段 / 单例 / 生命周期 / 初始化辅助
    ///   GameManager.Input.cs  —— 每帧输入 / 开始·停止·加注键处理
    ///   GameManager.Flow.cs   —— 一局流程（上锁→滚动→等停稳→火球掉落→结算解锁）
    /// </summary>
    public partial class GameManager : MonoBehaviour
    {
        #region 引用 / 字段
        public SlotMachine m_machine;
        public PlayerView m_player;
        public BonusView m_bonus;
        public ReelView m_reelView;

        public GameObject m_mainGame;
        public GameObject m_miniGame;

        /// <summary>转轮滚动 / 火球掉落 / 结算期间为 true，防止重复触发新一局（狂按 Start 不会穿透）。</summary>
        private bool _spinPending;

        /// <summary>【自动游玩】Inspector 勾选（或运行时按 F1）后，系统自动按 Start 键：
        /// 自动开新局、自动推进 Hold&amp;Spin 每轮 respin、结算确认点自动过、Mini 免费游戏自动续轮。
        /// 转轮正在滚动时不触发（避免把正在转的卷轴急停），等其自然停稳后下一帧自动继续。取消勾选立即回手动。</summary>
        public bool autoPlay = false;

        /// <summary>【测试】火球概率翻倍开关（Inspector 勾选）。开启后基础旋转与 Mini 免费局中火球每格出现率约翻倍
        /// （章鱼/免费每格比例保持不变，普通被火球挤占）。仅用于调试，不影响正式手感配置。</summary>
        public bool m_testDoubleFireball = false;

        /// <summary>【自动结算/自动连转停留时长(秒)·外置可调】仅 autoPlay(F1自动连转) 或 sd.auto==1(设置项自动结算) 时，
        /// 结算后停留这么久才放下一局，避免「秒过」直接进下一局。默认 0.9s，可在 Inspector 改。
        /// 手动确认 / 连续按确认 不受影响（仍纯等确认键）。</summary>
        public float settleAutoShowSeconds = 0.9f;


        /// <summary>Hold&amp;Spin 特性进行中的状态（非 null=正在 Hold&Spin，Start 键=推进一轮而非开新局）。</summary>
        private HoldSpinState _activeHold;
        private GameResult _holdResult;      // 挂起的本局结果，Hold&Spin 结束后才最终结算
        private bool _holdRolling;           // 本轮 respin 是否正在滚动（防狂按穿透）
        private int _holdScatterSpins;       // 进入 HoldSpin 时 Scatter 触发的原始免费次数（不含 FREE 火球追加），用于区分 collectedFree

        /// <summary>等待用户按确认键（Start）后才开始滚动赢分到总分。该期间 Start 键不触发新局/respin。</summary>
        private bool _waitingConfirm;

        /// <summary>Mini 免费小游戏进行中：主游戏输入/流程暂停（Mini 自带流程，不依赖主游戏 Start 键）。</summary>
        private bool _miniActive = false;
        #endregion

        #region 单例
        private static GameManager _instance;
        public static GameManager Instance
        {
            get
            {
                if (_instance == null)
                    _instance = FindObjectOfType<GameManager>();
                return _instance;
            }
        }
        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
        #endregion

        #region 生命周期
        void Awake()
        {
            DataManager.Instance.LoadData();
        }

        void Start()
        {
            Application.runInBackground = true;

            InitPots();        // 起手初始化四档渐进奖池并显示
            SyncReelConfig();  // 把当前棋盘模式(行数/符号带/火球id)交给 ReelView
            if (m_reelView != null) m_reelView.HideAllCounters();  // 起手隐藏全部 respin 计数文本(次数=0不显示)

            FMODSoundMgr.Instance.PlayBGM("event:/Sounds/11");
        }
        #endregion

        #region 初始化辅助
        void InitPots()
        {
            if (m_machine == null || m_machine.session == null || m_bonus == null) return;
            // ★ 彩金池变化自动刷新 BonusView（Contribute/ResetJackpot 后触发，调用方无需手动 ShowPots）
            m_machine.session.OnPotsChanged = pots => m_bonus.ShowPots(pots);
            // ★ 压分变化：用当前压分重算彩金值（局数不变）再刷新 UI，让彩金随压分回落/上涨
            if (m_player != null) m_player.OnBetChanged = bet => m_machine.session.RefreshPots(bet);
            m_machine.session.EnsurePots();
            m_machine.totalBet = m_player.m_bet_num;
            m_machine.session.Contribute(m_player.m_bet_num);  // 末尾自动 ShowPots
        }

        /// <summary>把 config 的棋盘模式（行数/符号带/火球id）同步给 ReelView，覆盖 Inspector 默认值。</summary>
        void SyncReelConfig()
        {
            if (m_reelView == null || m_machine == null || m_machine.config == null) return;
            m_reelView.m_reelRows = new List<int>(m_machine.config.reelRows);
            m_reelView.m_reelStrips = m_machine.config.reelStrips;   // 卷轴 loop 滚动用的符号带
            if (m_machine.config.fireballSymbolId >= 0)
                m_reelView.m_fireballSymbolId = m_machine.config.fireballSymbolId;
            // ★ 关键：把"百搭判定 id"也从 config 对齐，覆盖场景 Inspector 默认值。
            //   之前 m_symbolMax / m_wildId 一直沿用场景序列化值（Game0 默认 10，但 Game1.unity 曾被写成 11），
            //   而所有"第一列/顶行禁百搭"拦截都拿它当 Wild 比——一旦场景值≠真实 WildId，拦截整体静默失效，
            //   导致「reel0 反复出现百搭」这类修不完的 bug。此处从 config.WildId() 单一真相源强制对齐。
            int wid = m_machine.config.WildId();
            if (wid > 0) { m_reelView.m_symbolMax = wid; m_reelView.m_wildId = wid; }
        }
        #endregion
    }
}
