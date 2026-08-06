using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    /// <summary>
    /// 转轮视图 —— 三七机 ModeB (4-4-6-6-8) / ModeA (4-4-4-4-4)。
    ///
    /// 滚动模型 = 真·卷轴 loop（学 Fire Link / Cash Falls 在线 demo）：
    /// - 每列是一条连续循环的符号带（来自 config.reelStrips[reel]），符号从顶部不断进、底部不断出，
    ///   像卷轴一样一直转（不是"整条新内容滑下来替换旧的"）。例如 4-4-6-6-8 的某列会看到 55779…连续滚。
    /// - 自动停轮：第1列先停 → 第2 → … → 第5，每列间隔 m_autoStagger（waterfall）。
    /// - 停止键（InputAction.Stop / S / RightShift）：按下后 1→2→3→4→5 间隔 0.2s 急停。
    /// - 火球（运行时由 config 同步，默认 12）：滚动期间不显示；本列滚停后该格隐藏，由 ShowFeature 从上方掉落中奖火球覆盖。
    ///
    /// 符号图自动从 Resources/Icon 读：icon{N}(普通)/icon{N}_2(中奖高亮)/icon12(火球)/icon12_2(火球中奖)。
    /// 符号 prefab（m_symbolPrefab）挂 ReelItem：m_image=图标 Image，m_text=火球倍率文字（特性时显示）；
    /// 留空则自动创建纯 Image GameObject（无倍率文字）。
    ///
    /// 本类按职责拆成多个 partial 文件：
    ///   ReelView.cs          —— 字段 / 生命周期 / Update / IsSpinning / ClearAll / ReelState
    ///   ReelView.Reels.cs    —— 卷轴滚动（静态棋盘 / ShowGrid / 停轮 / 布局 / 定格 / 急停）
    ///   ReelView.Fireball.cs —— 火球掉落 / 中奖高亮
    ///   ReelView.Symbols.cs  —— 符号资源 / GameObject 创建 / 精灵设置 / RNG
    ///   ReelView.Test.cs     —— Inspector 右键测试菜单
    /// </summary>
    public partial class ReelView : MonoBehaviour
    {
        [Header("5 列容器")]
        public GameObject m_fireNode;
        public GameObject[] m_node;

        [Header("单列火球计数(圈圈数)与 tong 动画（运行时 InitCounters 确保创建；场景可预绑）")]
        public ReelFireNum[] m_numObjs;
        public ReelTong[] m_tongs;

        [Header("符号 prefab：挂 ReelItem（m_image=图标, m_text=火球倍率）。留空则自动创建纯 Image")]
        public GameObject m_symbolPrefab;

        [Header("布局（由 config 同步，不写死）")]
        public List<int> m_reelRows = new List<int> { 4, 4, 6, 6, 8 };
        public List<List<int>> m_reelStrips;       // config.reelStrips：每列符号带（循环滚动用）
        public float m_cellSize = 96f;
        public float m_rowBaseY = 0f;

        [Header("转轮参数")]
        public float m_baseSpeed = 38f;    // 快转速度(cells/sec)
        public float m_autoStagger = 0.18f; // 自动 waterfall 停轮：每列间隔(5列→总约0.9秒)
        public float m_normalDecel = 9f;   // 自动停轮收敛速度(更干脆)
        public float m_quickDecel = 12f;   // 急停(停止键)收敛速度(约0.25s)
        public float m_minSpinTime = 0.6f; // 每列最少滚动时长(避免第1列瞬间停)
        public int m_buf = 2;              // 上下缓冲格数
        public int m_symbolMin = 1;     // 滚动随机符号范围（1-based，含Wild=10，不含Scatter=11/Fireball=12）
        public int m_symbolMax = 10;    // 随机普通符号范围上限（= WildId，由 SyncReelConfig 对齐）
        public int m_wildId = 10;       // ★ 百搭(Wild)判定专用 id（= config.WildId()），与 m_symbolMax 解耦：
                                        //   所有"第一列/顶行禁百搭"拦截都用它，不再依赖 m_symbolMax，杜绝场景序列化把 m_symbolMax 写错导致拦截整体失效。
        public int m_fireballSymbolId = 12;

        [Header("符号渲染（通用，非 Mini 专属）")]
        public float m_symbolAlpha = 1f;        // 普通符号全局透明度。Mini 棋盘设为 0.5 实现半透明；主游戏保持 1（行为不变）

        /// <summary>true=火球 overlay 挂在 m_node[reel]（持久节点，跨 ClearAll 存活）；Mini 棋盘用此项让火球固定不消失。</summary>
        [System.NonSerialized] public bool m_persistentFireOverlays = false;

        /// <summary>v2：Mini 本轮开始时已锁定的"老火球"位置集合（reel*100+row）。
        /// 这些位置在卷轴里抑制符号渲染（由持久 overlay 固定显示），新火球(不在集合内)照常渲染并随卷轴滚入。
        /// 每轮 PlayOneFreeSpin 开头重算；主游戏/Hold&Spin 保持 null（m_persistentFireOverlays=false 也不走此逻辑）。</summary>
        [System.NonSerialized] public HashSet<int> m_preLockedFireRows = null;

        /// <summary>true=当前处于 FreeSpins（免费游戏/Mini）棋盘：火球显示 m_freeFire 而非 m_fire。
        /// 仅 Mini 的独立 ReelView 在 SetupBoard 置 true；主游戏 ReelView 保持 false，行为不变。</summary>
        [System.NonSerialized] public bool m_inFreeSpins = false;

        // ===== 共享内部状态 =====

        List<ReelState> _reels = new List<ReelState>();         // 滚动中的列
        List<GameObject> _staticCells = new List<GameObject>(); // 开局静态棋盘
        bool _initDone;

        // ★ 基础旋转火球：key = reel*100+row → FireballCell（含 kind/倍率，从 holdSpinState 传入 ShowGrid）
        Dictionary<int, FireballCell> _baseFireMults = new Dictionary<int, FireballCell>();
        // ★ 火球条带位置→FireballCell：key = reel*100000+stripIdx（BeginStop 后按实际停位构建）
        Dictionary<int, FireballCell> _fbStripMult = new Dictionary<int, FireballCell>();

        /// <summary>单列滚动状态。</summary>
        class ReelState
        {
            public int reelIdx;
            public GameObject container;
            public List<GameObject> cells = new List<GameObject>();
            public List<ReelItem> cellItems = new List<ReelItem>(); // 缓存 ReelItem（prefab 上有 m_image/m_text），避免每帧 GetComponent
            public List<Image> cellImgs = new List<Image>();   // 缓存：无 prefab 时的回退 Image
            public int[] shownSym;                              // 每格当前显示的符号(缓存)；-1=未设，-999=隐藏
            public int rows;
            public float pos;          // 已滚过的 cell 数（浮点，连续增加）
            public float speed;        // cells/sec
            public int stripBase;      // 顶部 cell 对应的 reelStrips 起始索引
            public int[] finalSyms;    // 本局该列最终结果（停轮后对齐显示）
            public List<int> displayStrip;  // 显示用条带：火球/空格已一次性替换为稳定替身（滚动中直接取，不再每帧随机）
            public bool spinning;
            public bool stopping;
            public float stopAt;       // 目标停轮位置(整数格)
            public float decel;
            public float stopTimer;    // 自动停剩余延迟
            public bool autoStop;
        }

        // ===== 生命周期 =====

        void Start()
        {
            // 延迟一帧：确保 GameManager.Start 已把 config.reelRows/reelStrips 同步过来
            StartCoroutine(DelayedInit());
        }

        IEnumerator DelayedInit()
        {
            yield return null;
            if (!_initDone)
            {
                InitStaticGrid();    // 见 ReelView.Reels.cs
                InitCounters();      // 运行时确保 m_numObjs（火球倍率/倒计时计数器）已创建
                _initDone = true;
            }
        }

        // ===== 计数器(m_numObjs：火球倍率/倒计时) 运行时确保创建 =====

        /// <summary>运行时确保 m_numObjs 已创建：从场景现有 ReelFireNum（m_numObjs 已绑定的 + 自身子级）收集模板，
        /// 不足则复制补齐。场景里至少要有 1 个 ReelFireNum 作模板（挂在 ReelView 下或绑进 m_numObjs）。</summary>
        void InitCounters()
        {
            // ★ 计数器按“列(reel)”建：5 列 = 5 个，与 tong / 火球掉落一一对应。
            //   不要按 maxRows（模式 B 是 8 行）建，否则会多生成 3 个无用的计数器。
            int reelCount = (m_reelRows != null) ? m_reelRows.Count : 0;
            if (reelCount <= 0) return;

            // 若已足够且全部非空，跳过
            if (m_numObjs != null && m_numObjs.Length >= reelCount)
            {
                bool allOk = true;
                for (int i = 0; i < reelCount; i++) if (m_numObjs[i] == null) { allOk = false; break; }
                if (allOk) return;
            }

            // 收集场景里已有 ReelFireNum（m_numObjs 已绑定的 + 自身子级）
            var existing = new System.Collections.Generic.List<ReelFireNum>();
            if (m_numObjs != null)
                foreach (var n in m_numObjs)
                    if (n != null && !existing.Contains(n)) existing.Add(n);
            foreach (var c in GetComponentsInChildren<ReelFireNum>())
                if (!existing.Contains(c)) existing.Add(c);

            if (existing.Count == 0)
            {
                //Debug.LogError("[ReelView] 场景里没有可用的 ReelFireNum 对象，请先在场景里放一个（火球倍率/倒计时计数器）！");
                return;
            }

            var newArray = new ReelFireNum[reelCount];
            Transform parent = existing[0].transform.parent;

            for (int i = 0; i < reelCount; i++)
            {
                if (i < existing.Count)
                {
                    newArray[i] = existing[i];
                }
                else
                {
                    var template = existing[existing.Count - 1];
                    var go = Instantiate(template.gameObject, parent);
                    go.name = $"ReelFireNum_Reel{i}";
                    newArray[i] = go.GetComponent<ReelFireNum>();
                }
                if (newArray[i] != null)
                {
                    newArray[i].name = $"ReelFireNum_Reel{i}";
                    newArray[i].ResetMultiplier();   // 复位（隐藏倍率文本、恢复三圈）
                    // ★ 计数器(ReelFireNum)若压在底部火球格(view-row0≈m_rowBaseY)上，会遮挡该列第4颗火球，
                    //   判定：计数器当前 Y 与底行中心 Y 差 < 半格 → 视为压在底部格 → 下移一格到列下方。
                    var crt = newArray[i].transform as RectTransform;
                    if (crt != null && Mathf.Abs(crt.anchoredPosition.y - m_rowBaseY) < m_cellSize * 0.5f)
                        crt.anchoredPosition = new Vector2(crt.anchoredPosition.x, m_rowBaseY - m_cellSize);
                }
            }

            m_numObjs = newArray;

            // ★ 销毁多余的 ReelFireNum：场景里可能残留旧"按行"模式放的对象，
            //   不在 m_numObjs 数组内的全部清理掉，避免底部露出多余数字。
            foreach (var c in GetComponentsInChildren<ReelFireNum>())
                if (!existingInArray(c, newArray))
                    Destroy(c.gameObject);
        }

        static bool existingInArray(ReelFireNum fn, ReelFireNum[] arr)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++)
                if ((object)arr[i] == (object)fn) return true;
            return false;
        }

        void Update()
        {
            for (int i = 0; i < _reels.Count; i++)
                UpdateReel(_reels[i], Time.deltaTime);   // 见 ReelView.Reels.cs

            // ★ 释放中的火球 overlay（80% 幽灵）随卷轴滚走
            bool spinning = IsSpinning();
            if (_releaseReels.Count > 0)
            {
                if (spinning)
                {
                    // 卷轴在转 → 让待释放 overlay 随卷轴向下滚走
                    var offset = new Dictionary<int, float>();
                    for (int i = 0; i < _reels.Count; i++)
                        offset[i] = (_reels[i].spinning || _reels[i].stopping) ? _reels[i].pos : 0f;
                    MoveReleasingOverlays(offset);
                }
                else if (_wasSpinning)
                {
                    // ★ 边沿检测：上一帧还在转、这一帧刚停稳 → 才清理待释放 overlay。
                    //   若只是"特性结束回到待机(idle)、玩家还没按开始"，spinning 与 _wasSpinning 均为 false，
                    //   不进此分支，幽灵保留到下一局真正滚动时再随卷轴滚走（不再在待机时直接消失）。
                    DestroyReleasingOverlays();
                    _releaseReels.Clear();
                }
            }
            _wasSpinning = spinning;
        }

        /// <summary>是否有列仍在转（GameManager 用于判断 Start 是"转"还是"停"、autoPlay 门等）。
        /// ★ 单一真相源：基础局看 _reels[i].spinning/stopping；Hold&amp;Spin 视觉滚动看 _holdSpinning。
        ///   两种模式统一从这里查，避免"功能做两次"（之前 Hold 的忙碌判断散落在 _holdRolling 各处）。</summary>
        public bool IsSpinning()
        {
            for (int i = 0; i < _reels.Count; i++)
                if (_reels[i].spinning || _reels[i].stopping) return true;
            return false;
        }

        /// <summary>读取某格当前显示的符号 ID（供 GameManager 构建 respin 网格做线奖结算）。
        /// 火球格由 HoldSpinState 单独判定，这里只回普通格的 shownSym。</summary>
        public int GetVisibleSymbol(int reel, int row)
        {
            if (reel < 0 || reel >= _reels.Count) return -1;
            var st = _reels[reel];
            int k = m_buf + row;
            if (k < 0 || k >= st.shownSym.Length) return -1;
            return st.shownSym[k];
        }

        void ClearAll()
        {
            // 销毁非释放中的火球 overlay；释放中的（80% 幽灵）保留，随新一局卷轴滚走
            ClearFireballOverlaysExceptReleasing();
            StopWinAnims();
            _baseFireMults.Clear();    // 清空基础旋转火球倍率
            _fbStripMult.Clear();      // 清空条带位置→倍率映射
            foreach (var st in _reels) if (st.container != null) Destroy(st.container);
            _reels.Clear();
            foreach (var go in _staticCells) if (go != null) Destroy(go);
            _staticCells.Clear();
        }
    }
}
