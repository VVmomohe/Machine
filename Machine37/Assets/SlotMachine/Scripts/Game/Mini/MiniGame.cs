using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using Com.Controller;        // GameController（读 Start 键）
using Com.Back;              // DataManager（auto 开关）
using SlotMachine.Core;   // OutcomeGenerator / HoldSpinState / GameSession / FireballCell / FireballKind
namespace com.slot
{

/// <summary>
/// Mini 免费小游戏（8×5 = 5 列 × 8 行）。
///
/// 设计目标：与主游戏（GameManager / 主 ReelView）尽量解耦——
///   - 本类拥有【自己独立的 ReelView 实例】（场景里挂在 m_miniGame 下），不复用主棋盘；
///   - 所有 Mini 流程（免费次数、基础旋转、火球记录与展示）都写在这里；
///   - 仅共享逻辑层（SlotMachine.config / rng）做判定，不碰主游戏的押注/余额结算；
///   - 结束时通过回调把"全部火球派彩"交还 GameManager 入账。
///
/// 规则：
///   - 普通 ICON 半透明 50%（仅设本棋盘 ReelView 的 m_symbolAlpha，不影响主游戏）。
///   - 火球一旦落在棋盘上就【固定在该位置不再消失】，后续旋转该格仍显示火球。
///   - 免费次数只减不加：每次旋转消耗 1 次，不像主游戏靠连线追加免费。
///   - 结束：汇总全部火球（倍数之和 + 中彩金档位，按 ×bet）一次性派彩。
/// </summary>
public class MiniGame : MonoBehaviour
{
    [Header("Mini 棋盘：独立的 ReelView 实例（挂在本 GameObject 下，GameManager 单例提供其余引用）")]
    public ReelView m_reelView;


    [Header("UI")]
    public UnityEngine.UI.Text m_remainingText;   // 剩余免费次数显示（拖场景里的 Text）

    [Header("节奏")]
    public float m_freeSpinGap = 0.5f;     // 每轮免费旋转之间的间隔（秒）
    public float m_finalShowTime = 4f;     // 结算时用计数器模板展示最终总倍数的停留时长（秒）

    const int kAbsoluteMaxRounds = 300;    // 终极安全网：即使配置 miniCap=0（不封顶），也最多转 300 轮强制结束，杜绝死循环

    // ===== 运行态 =====
    bool _active;
    int _freeSpinsLeft;
    int _roundsPlayed;                     // 已进行的免费旋转轮数（用于 miniCap 硬上限终止，防止 Scatter 重触发无限续命）
    float _fireTotal;                       // 全部火球累计派彩（×bet）
    List<FireballCell> _allFires;           // 全部火球（用于最终统计/显示）
    System.Action<MiniResult> _onDone;

    // ===== Mini 行锁定（config 驱动；A/B 模式共用 Mini）=====
    MiniLockConfig _lockCfg;                // MiniLockConfig（m_miniCfg.miniLock 引用）
    HashSet<int> _lockedRows;               // 当前仍锁定的行（0-indexed，0=最上）
    List<int> _lockOrder;                   // 锁定顺序（解锁时从尾部弹出 = 最外侧先解）
    List<FireballCell> _lockedFires;        // 锁定行上的火球：可见但【不计入】派彩（解锁时回收进 _allFires）
    int _locksRemaining;
    int _spinsSinceUnlock;
    MiniRowLockView _lockView;              // 锁视觉（运行时解析到的实例）
    [SerializeField] private MiniRowLockView m_rowLockView;  // ★ 首选：手动挂在 "Excel" 节点上的 MiniRowLockView，直接拖进来
    [SerializeField] private Transform m_excelNode;          // 兜底：未填上面时，从此节点(或按名找 "Excel")取组件；不会自动 AddComponent
    ReelConfig m_miniCfg;                   // Mini 专用配置副本（仅 reelRows=8×5，其余共享主配置）

    public bool IsActive => _active;

    /// <summary>Mini 结算结果：交还 GameManager 入账。</summary>
    public class MiniResult
    {
        public float fireTotal;             // 全部火球派彩（倍数之和 + 中彩金档，已 ×bet）
        public int fireCount;               // 火球总颗数
        public List<string> jackpots; // 中过的彩金档名（"Mini"/"Minor"/"Major"/"Mega"，可重复/多档）
    }

    // ===== 公开入口（由 GameManager 在进入免费游戏时调用） =====

    public void StartMini(int freeSpins, System.Action<MiniResult> onDone)
    {
        if (_active) { Debug.LogWarning("[MiniGame] 已在运行，忽略重复进入"); return; }
        if (m_reelView == null) { Debug.LogError("[MiniGame] 未绑定 m_reelView（Mini 棋盘）"); onDone?.Invoke(new MiniResult()); return; }

        _active = true;
        _freeSpinsLeft = Mathf.Max(1, freeSpins);
        _roundsPlayed = 0;
        _fireTotal = 0f;
        _allFires = new List<FireballCell>();
        _onDone = onDone;

        SetupBoard();
        InitRowLock();      // ★ 行锁定：根据 miniLock 配置计算锁定行并绑定锁视觉

        // 显示 Mini，隐藏 Main（显式控制两个独立的 ReelView）
        if (GameManager.Instance.m_mainGame != null) GameManager.Instance.m_mainGame.SetActive(false);
        if (GameManager.Instance.m_reelView != null) GameManager.Instance.m_reelView.gameObject.SetActive(false);   // 隐藏主棋盘
        if (m_reelView != null) m_reelView.gameObject.SetActive(true);            // 激活 Mini 独立棋盘
        gameObject.SetActive(true);

        // ★ 等2帧：让 Mini ReelView 的 DelayedInit(Start→yield null→InitStaticGrid) 先跑完，
        //   否则 DelayedInit 的 ClearAll 会把 MiniLoop 第一轮 ShowGrid 的 _reels 清掉。
        StartCoroutine(StartMiniCoroutine());
    }

    IEnumerator StartMiniCoroutine()
    {
        yield return null;   // ReelView.Start() 在本帧调
        yield return null;   // DelayedInit 恢复 + InitStaticGrid 在本帧完成
        StartCoroutine(MiniLoop());
    }

    // ===== 棋盘装配（仅作用于本类自己的 ReelView） =====

    void SetupBoard()
    {
        var rv = m_reelView;

        // 克隆主棋盘渲染参数，保证观感一致（布局/速度/符号范围）
        if (GameManager.Instance.m_reelView != null)
        {
            rv.m_cellSize = GameManager.Instance.m_reelView.m_cellSize;
            rv.m_rowBaseY = GameManager.Instance.m_reelView.m_rowBaseY;
            rv.m_buf = GameManager.Instance.m_reelView.m_buf;
            rv.m_baseSpeed = GameManager.Instance.m_reelView.m_baseSpeed;
            rv.m_autoStagger = GameManager.Instance.m_reelView.m_autoStagger;
            rv.m_normalDecel = GameManager.Instance.m_reelView.m_normalDecel;
            rv.m_quickDecel = GameManager.Instance.m_reelView.m_quickDecel;
            rv.m_minSpinTime = GameManager.Instance.m_reelView.m_minSpinTime;
            rv.m_symbolMin = GameManager.Instance.m_reelView.m_symbolMin;
            rv.m_symbolMax = GameManager.Instance.m_reelView.m_symbolMax;
            rv.m_wildId = GameManager.Instance.m_reelView.m_wildId;
            rv.m_symbolPrefab = GameManager.Instance.m_reelView.m_symbolPrefab;   // 火球文字/视觉依赖此 prefab
        }

        // 8×5 棋盘
        rv.m_reelRows = new List<int> { 8, 8, 8, 8, 8 };

        // 符号带 / 火球 id：与主配置同符号集（共享引用即可，ReelView 只读取不写）
        if (GameManager.Instance.m_machine != null && GameManager.Instance.m_machine.config != null)
        {
            rv.m_reelStrips = GameManager.Instance.m_machine.config.reelStrips;
            if (GameManager.Instance.m_machine.config.fireballSymbolId >= 0)
                rv.m_fireballSymbolId = GameManager.Instance.m_machine.config.fireballSymbolId;
            // ★ Mini 专用配置副本：仅把 reelRows 改成 8×5（Hold&Spin / 出格都按 8 行，
            //   否则会用主配置的 4-4-6-6-8，导致火球只落在上半部分）。其余字段共享主配置引用。
            m_miniCfg = BuildMiniConfig(GameManager.Instance.m_machine.config);
        }

        // ★ 普通符号半透明 50%（仅本棋盘，主游戏不受影响）
        rv.m_symbolAlpha = 0.5f;

        // ★ Mini 火球持久 overlay——与主游戏 Hold&Spin 一致，卷轴滚动中 overlay 固定在 m_node 上不随卷轴滚走，
        //   实现"火球锁定"视觉。ShowFeatureState 每轮停稳后 Clear + 重建，保证新火球也出现在正确位置。
        // ★ 真根修复：开启持久模式【前】必须先无差别清掉基础轮残留的火球 overlay（含基础轮可能合法产生的 FREE 火球）。
        //   否则下方 m_persistentFireOverlays=true 会让首轮 ShowGrid→ClearAll→ClearFireballOverlaysExceptReleasing
        //   直接 return（见 ReelView.cs 该行 if(m_persistentFireOverlays) return），基础轮残留 overlay 不被销毁、
        //   原样叠到 Mini 棋盘 → 表现为「Mini 里凭空出现 FREE 火球」。这正是真根（不是 Domain Reload 残留）。
        rv.ClearFireballOverlays();
        rv.m_persistentFireOverlays = true;

        // ★ Mini 棋盘：火球显示 m_fire（与主游戏一致），普通图标 m_image 隐藏
        rv.m_inFreeSpins = false;

        // 5 列节点（若未绑定则运行时创建，并沿用主棋盘列布局）
        if (rv.m_node == null || rv.m_node.Length < 5) BuildColumns(rv);
        // 5 个计数器（克隆模板）
    }

    /// <summary>构建 Mini 专用配置副本：仅 reelRows 改为 8×5（其余字段共享主配置引用，Mini 只读不写）。</summary>
    ReelConfig BuildMiniConfig(ReelConfig src)
    {
        if (src == null) return null;
        var c = new ReelConfig();
        c.modeName = src.modeName + "_Mini";
        c.reelCount = 5;
        c.reelRows = new List<int> { 8, 8, 8, 8, 8 };
        c.reelStrips = src.reelStrips;
        c.winEval = src.winEval;
        c.paylines = src.paylines;
        c.paytable = src.paytable;
        c.scatterPays = src.scatterPays;
        c.minMatch = src.minMatch;
        c.totalWays = 8 * 8 * 8 * 8 * 8;
        c.lines = src.lines;
        c.fireballSymbolId = src.fireballSymbolId;
        c.fireLinkSymbolId = src.fireLinkSymbolId;
        c.maxRows = 8;
        c.holdSpin = src.holdSpin;
        c.jackpots = src.jackpots;
        c.freeSpins = src.freeSpins;
        c.miniLock = src.miniLock;     // Mini 行锁定配置（A/B 共用，随主配置走）
        return c;
    }

    void BuildColumns(ReelView rv)
    {
        rv.m_node = new GameObject[5];
        for (int i = 0; i < 5; i++)
        {
            var col = new GameObject($"MiniCol_{i}");
            col.transform.SetParent(rv.transform, false);
            var rt = col.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            // 沿用主棋盘第 i 列的位置（若存在），否则 5 列居中排布
            if (GameManager.Instance.m_reelView != null && GameManager.Instance.m_reelView.m_node != null &&
                i < GameManager.Instance.m_reelView.m_node.Length && GameManager.Instance.m_reelView.m_node[i] != null)
            {
                var mrt = GameManager.Instance.m_reelView.m_node[i].transform as RectTransform;
                if (mrt != null) rt.anchoredPosition = mrt.anchoredPosition;
            }
            else
            {
                rt.anchoredPosition = new Vector2((i - 2) * rv.m_cellSize, 0f);
            }
            rt.sizeDelta = new Vector2(rv.m_cellSize, rv.m_cellSize * 8);
            rv.m_node[i] = col;
        }
    }

    // ===== Mini 行锁定（config 驱动：m_miniCfg.miniLock） =====

    /// <summary>进入 Mini 时按配置计算锁定行并绑定锁视觉。未启用或配置为空则整局无锁定。</summary>
    void InitRowLock()
    {
        _lockedRows = new HashSet<int>();
        _lockOrder = new List<int>();
        _lockedFires = new List<FireballCell>();
        _locksRemaining = 0;
        _spinsSinceUnlock = 0;
        _lockCfg = (m_miniCfg != null) ? m_miniCfg.miniLock : null;
        _lockView = null;

        if (_lockCfg != null && _lockCfg.enabled && m_miniCfg != null)
        {
            int totalRows = (m_miniCfg.reelRows != null && m_miniCfg.reelRows.Count > 0) ? m_miniCfg.reelRows[0] : 8;
            _lockOrder = ComputeLockedRows(_lockCfg.bottom, _lockCfg.lockRows, totalRows);
            foreach (var r in _lockOrder) _lockedRows.Add(r);
            _locksRemaining = _lockOrder.Count;
            int reelCount = (m_miniCfg.reelRows != null) ? m_miniCfg.reelRows.Count : 5;
            float cellSize = (m_reelView != null) ? m_reelView.m_cellSize : 135f;
            BindLockView(totalRows, cellSize, reelCount * cellSize);
            Debug.Log($"[MiniGame] 行锁定启用：底={_lockCfg.bottom} 锁定行=[{string.Join(",", _lockOrder)}] 每{_lockCfg.unlockEvery}轮解1锁");
        }
    }

    /// <summary>解析锁视觉（MiniRowLockView 由美术手动挂在 "Excel" 节点上）：
    /// 优先用 inspector 直填的 m_rowLockView，其次从 m_excelNode / 按名找到的 "Excel" 节点取组件。
    /// 若 m_box 未手动填，MiniRowLockView 会运行时自动生成占位锁行（供测试）；美术就绪填 m_box 即覆盖。
    /// totalRows/cellSize/boardWidth 仅用于自动生成占位行的布局。</summary>
    void BindLockView(int totalRows, float cellSize, float boardWidth)
    {
        _lockView = m_rowLockView;
        if (_lockView == null)
        {
            if (m_excelNode == null) m_excelNode = FindChildRecursive(transform, "Excel");
            if (m_excelNode != null) _lockView = m_excelNode.GetComponent<MiniRowLockView>();
        }
        if (_lockView == null)
        {
            Debug.LogWarning("[MiniGame] 行锁定：未找到 MiniRowLockView（请把它挂到 MINIGame/Excel 节点，或填 m_rowLockView）。本局仅跑逻辑，无锁视觉。");
            return;
        }
        _lockView.Init(_lockOrder, totalRows, cellSize, boardWidth);
        RefreshLockCounts();
    }

    /// <summary>刷新每行 Box 下的锁数文本 = 该行还需转几轮才解锁。
    /// _lockOrder 从尾部弹出解锁（最外侧先解），所以尾部第 k 个（k 从 1 起）在第 k×unlockEvery 轮解开，
    /// 剩余轮数 = k×unlockEvery − 本轮已累计的 _spinsSinceUnlock。</summary>
    void RefreshLockCounts()
    {
        if (_lockView == null || _lockCfg == null || _lockOrder == null) return;
        int every = Mathf.Max(1, _lockCfg.unlockEvery);
        for (int i = 0, k = 1; i < _lockOrder.Count; i++, k++)
            _lockView.SetRowCount(_lockOrder[i], k * every - _spinsSinceUnlock);
    }

    /// <summary>计算锁定行。行号一律 0-indexed，范围 0..totalRows-1（8 行棋盘 = 0~7，0=最上行）。
    /// bottom = 棋盘「底」所在的行号（0 或 totalRows-1，即 0 或 7），锁的总是【远离底】的那半：
    ///   底=0 → 锁下半 [4,5,6,7]；
    ///   底=7 → 锁上半 [3,2,1,0]。
    /// "从中间开始锁"：从中间行向该半延伸，所以 _lockOrder[0] 是最靠中间的行、尾部是最外侧行。
    /// 解锁时从 _lockOrder 头部弹出 → 最靠中间的行先解，最外侧行锁得最久。</summary>
    List<int> ComputeLockedRows(int bottom, int lockRows, int totalRows)
    {
        var list = new List<int>();
        if (totalRows <= 0 || lockRows <= 0) return list;
        lockRows = Mathf.Min(lockRows, totalRows);
        int mid = totalRows / 2;     // 8 → 4
        // bottom 落在上半(<=中位) → 底在顶部 → 锁**上半**(row 0~lockRows-1)；否则底在底部 → 锁**下半**。
        // ★ 列表顺序：**最靠近中间的行排在前面**（_lockOrder[0] 最先被头部弹出解锁）。
        //   这样 RefreshLockCounts 的 k=1 对应"最靠近中间的行"(最早解、数字最小)，
        //   k=lockRows 对应"最边缘的行"(最晚解、数字最大)，视觉上从上到下递减(10,7,4,1)。
        if (bottom <= (totalRows - 1) / 2)
        {
            // 锁上半 [0..lockRows-1]，但从最靠近中间的那行开始往前排
            for (int i = lockRows - 1; i >= 0; i--)
                list.Add(i);
        }
        else
        {
            // 锁下半 [totalRows-lockRows .. totalRows-1]，从最靠近中间的那行开始往后排
            int start = totalRows - lockRows;
            for (int i = 0; i < lockRows; i++)
                list.Add(start + i);
        }
        return list;
    }

    /// <summary>每转一轮调用：累计计数并刷新各行锁数文本；满 unlockEvery 轮解一个锁（中间行先解、最外侧最后解），
    /// 并回收该行"只显示不算"的火球进 _allFires。</summary>
    void AdvanceUnlock()
    {
        if (_lockCfg == null || !_lockCfg.enabled || _locksRemaining <= 0 || _lockOrder.Count == 0) return;
        _spinsSinceUnlock++;

        if (_spinsSinceUnlock >= _lockCfg.unlockEvery)
        {
            _spinsSinceUnlock = 0;

            int row = _lockOrder[0];
            _lockOrder.RemoveAt(0);
            _lockedRows.Remove(row);
            _locksRemaining--;

            // ★ 回收：该行上此前"只显示不算"的火球，解锁后正式计入 _allFires（kind 已在落入时 Roll 好）。
            for (int i = _lockedFires.Count - 1; i >= 0; i--)
            {
                if (_lockedFires[i].row == row)
                {
                    _allFires.Add(_lockedFires[i]);
                    _lockedFires.RemoveAt(i);
                }
            }

            if (_lockView != null) _lockView.RemoveLock(row);
            Debug.Log($"[MiniGame] 解锁行 row={row}，剩余锁={_locksRemaining}");
            // ★ 解锁瞬间立即重钉：该行原 _lockedFires 已转入 _allFires，马上 PinFireOverlays 让它们固定显示(不再等下一轮停稳)，消除确认等待期的"未固定"观感。
            PinFireOverlays();
        }

        RefreshLockCounts();   // 未解锁的轮次也要让数字倒数（3→2→1）
    }

    /// <summary>递归查找子节点（按名），兼容 Excel 节点非直接子级的情况。</summary>
    static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            var f = FindChildRecursive(c, name);
            if (f != null) return f;
        }
        return null;
    }

    // ===== 输入（Mini 自己读 Start 键，不依赖 GameManager 转发） =====

    /// <summary>本帧 Start 键是否按下。</summary>
    bool IsStartDown()
    {
        if (GameController.Instance == null || GameController.Instance.m_keys == null) return false;
        return GameController.Instance.m_keys[(int)InputAction.Start] == (int)InputPhase.Down;
    }

    /// <summary>等玩家按确认键才继续。auto 模式(auto==1)1.2s 后自动继续。
    /// Mini 进行中 GameManager.Input 的 _miniActive 拦截不影响这里——Mini 直接读 GameController。</summary>
    IEnumerator WaitForMiniConfirm()
    {
        // auto 模式：短暂展示后自动继续（DataManager.auto==1 或 主游戏 autoPlay 勾选）
        if ((DataManager.Instance != null &&
            DataManager.Instance.Setting != null &&
            DataManager.Instance.Setting.TryGetValue(1, out var sd) &&
            sd.auto == 1)
            || (GameManager.Instance != null && GameManager.Instance.autoPlay))
        {
            yield return new WaitForSeconds(1.2f);
            yield break;
        }
        // 手动模式：等 Start 键
        float guard = 0f;
        while (!IsStartDown())
        {
            guard += Time.deltaTime;
            if (guard > 30f) { Debug.LogWarning("[MiniGame] 等确认超时30s，自动继续"); yield break; }
            yield return null;
        }
    }

    // ===== 主循环：消耗免费次数 =====

    IEnumerator MiniLoop()
    {
        #if UNITY_EDITOR
        // ★ 编辑器开发便利：进入免费小游戏即暂停编辑器，便于在 Inspector 检视刚装配好的棋盘状态（行锁定/火球等）。
        //   仅作用于编辑器 Play 模式，玩家真机构建被 #if 剥离，无任何副作用。手动在编辑器点"继续"即可恢复。
        if (UnityEditor.EditorApplication.isPlaying && !UnityEditor.EditorApplication.isPaused)
        {
            Debug.Log("[MiniGame] 进入免费小游戏，编辑器已暂停以便检视（点编辑器 继续/Play 恢复）");
            UnityEditor.EditorApplication.isPaused = true;
        }
        #endif

        UpdateRemainingDisplay();   // 初始显示（如 "剩余 3 次"）

        while (_freeSpinsLeft > 0)
        {
            // ★ 硬上限：已转够 miniCap 轮仍没自然耗尽，强制结束（防止 Scatter 频繁重触发导致无限续命）。
            //   miniCap=0 视为"不封顶"，但仍有绝对安全网 kAbsoluteMaxRounds 兜底，杜绝真正死循环。
            int cap = (m_miniCfg != null && m_miniCfg.freeSpins != null && m_miniCfg.freeSpins.miniCap > 0)
                ? m_miniCfg.freeSpins.miniCap : kAbsoluteMaxRounds;
            if (_roundsPlayed >= cap)
            {
                Debug.LogWarning($"[MiniGame] 免费游戏已达 {cap} 轮上限，强制结束（剩余 {_freeSpinsLeft} 次未消耗）");
                _freeSpinsLeft = 0;
                break;
            }

            _freeSpinsLeft--;
            _roundsPlayed++;
            UpdateRemainingDisplay();
            yield return StartCoroutine(PlayOneFreeSpin());
            AdvanceUnlock();   // ★ 每转一轮计数；满 unlockEvery 轮解一个锁并回收该行火球
            yield return StartCoroutine(WaitForMiniConfirm());
        }

        // 结束：汇总全部火球（全场景一次性结算），回调交还 GameManager
        float bet = GameManager.Instance.m_machine != null && GameManager.Instance.m_machine.totalBet > 0
            ? GameManager.Instance.m_machine.totalBet : 1f;
        float totalMult = 0f;                       // 最终总倍数（全部火球倍率之和，含彩金档折算倍数）
        foreach (var f in _allFires) { _fireTotal += bet * f.multiplier; totalMult += f.multiplier; }

        var result = new MiniResult
        {
            fireTotal = _fireTotal,
            fireCount = _allFires.Count,
            jackpots = CollectJackpots(),
        };

        // ★ 彩金特效：全部火球停下、免费次数归零即刻播放（不再等倍数计数器展示播完）。
        var bonus = GameManager.Instance?.m_bonus;
        UnityEngine.Debug.Log($"[Mini结算] 中彩金档数={result.jackpots?.Count ?? 0} 档名=[{string.Join(",", result.jackpots ?? new List<string>())}]");
        if (bonus != null && result.jackpots != null && result.jackpots.Count > 0)
            foreach (var tierName in result.jackpots)
            {
                if (System.Enum.TryParse<FireballKind>(tierName, out var fk))
                    bonus.ShowJackpotEffect(fk, persistent: true);   // ★ 免费转中彩金 → 持续播，下一开局(OnStartKey/EnterHoldSpin)才隐藏
            }

        // ★ 结算展示：用计数器模板(Counter Template)显示本次免费游戏的最终总倍数，停留约 2 秒后再回主游戏。
        //   （此时 Mini 棋盘仍可见；结算信息不再走 remainingText。）
        if (m_remainingText != null) m_remainingText.text = "";   // 先清空剩余次数文本

        // ★ 这 4 秒结算展示期间，主 HUD 也要亮出总派彩赢分（不再一直挂 0）。
        //   仅显示、不滚余额——余额滚入仍由回调用 AddFeatureWin 一次性完成，避免重复入账。
        //   注意：此处 allowBigWin:false —— BIG WIN 统一由 onDone 回调的 ShowWinValue(combined) 播放，
        //   否则结算预览播一次、回调再播一次 → 免费游戏退出后 BIG WIN 播放 2 次。
        if (GameManager.Instance != null && GameManager.Instance.m_player != null && result.fireTotal > 0f)
            GameManager.Instance.m_player.ShowWinValue((long)System.Math.Round(result.fireTotal), allowBigWin: false);

        yield return StartCoroutine(ShowFinalMultiplier(totalMult));

        // ★ 彩金清零时机修正：中过彩金不在「结算展示/确认中」清零，改到 Mini 结算完结(展示完、回到主游戏前)才清，
        //   让彩金池在 Mini 庆祝展示期间仍显示中奖值。
        RestoreMainBoard(result);
        if (GameManager.Instance?.m_machine?.session != null && result.jackpots != null && result.jackpots.Count > 0)
            foreach (var tierName in result.jackpots)
                GameManager.Instance.m_machine.session.ResetJackpot(tierName);
    }        IEnumerator ShowFinalMultiplier(float totalMult)
        {
            if (m_remainingText != null && totalMult > 0f)
            {
                m_remainingText.text = $"X{totalMult:F1}";
                yield return new WaitForSeconds(m_finalShowTime);
                m_remainingText.text = "";
            }
            else
            {
                yield return new WaitForSeconds(m_finalShowTime);
            }
        }


    /// <summary>结算结束后：隐藏 Mini 棋盘、恢复主棋盘并回调交还结果。</summary>
        void RestoreMainBoard(MiniResult result)
        {
            // ★ 离开 Mini 时销毁全部火球 overlay：m_persistentFireOverlays=true 让 ClearAll 不清 overlay，
            //   否则上一局 Mini 的火球会残留进下一局 Mini（再次进入时 m_node 不重建、_fbOverlays 不空，
            //   旧火球浮在初始棋盘上，要等第一轮停稳 ShowFeatureState 才清 —— 观感"上一轮火球还在"）。
            if (m_reelView != null) m_reelView.ClearFireballOverlays();
            m_reelView.gameObject.SetActive(false);              // 隐藏 Mini 独立 ReelView
            gameObject.SetActive(false);                         // 隐藏 Mini GameObject
            if (GameManager.Instance.m_reelView != null) GameManager.Instance.m_reelView.gameObject.SetActive(true);  // 显示主棋盘
        if (GameManager.Instance.m_mainGame != null) GameManager.Instance.m_mainGame.SetActive(true);  // 显示 Main

        // ★ 清理行锁定状态与锁视觉（避免残留进下一局 Mini）
        _lockView?.Clear();
        _lockedRows?.Clear();
        _lockOrder?.Clear();
        _lockedFires?.Clear();
        _locksRemaining = 0;
        _spinsSinceUnlock = 0;
        _lockView = null;

        _active = false;
        _onDone?.Invoke(result);
    }

    void UpdateRemainingDisplay()
    {
        if (m_remainingText != null)
            m_remainingText.text = $"剩余 {_freeSpinsLeft} 次";
    }

    /// <summary>一轮免费旋转：生成棋盘(旧火球锁定) → 新火球预滚属性 → ShowGrid(fireMults) → 减速阶段显示倍率 → 停稳后 ShowFeatureState。</summary>
    IEnumerator PlayOneFreeSpin()
    {
        if (GameManager.Instance.m_machine == null || GameManager.Instance.m_machine.config == null || m_reelView == null || m_miniCfg == null) yield break;
        var cfg = m_miniCfg;
        float bet = GameManager.Instance.m_machine.totalBet > 0 ? GameManager.Instance.m_machine.totalBet : 1f;
        int fbId = cfg.fireballSymbolId;
        var pots = GameManager.Instance.m_machine.session != null
            ? GameManager.Instance.m_machine.session.Pots : null;

        // ★ v2：捕获本轮开始时已锁定的"老火球"位置（此时 _allFires 仅含上一轮及之前的火球）。
        //   卷轴渲染时仅抑制这些老火球符号（它们由持久 overlay 固定显示），新火球照常渲染、随卷轴自然滚入，
        //   避免选项 A 把所有火球都抑制成空白、导致新火球停稳时才"盖章"突兀出现。
        if (m_reelView != null)
        {
            var oldRows = new HashSet<int>();
            foreach (var f in _allFires)
                if (f.reel >= 0 && f.row >= 0) oldRows.Add(CellKey.Encode(f.reel, f.row));
            // ★ 锁定行火球也一并抑制：它们已落格，应像老火球一样钉在盘面(固定显示、不随卷轴重影/闪)；
            //   解锁后转入 _allFires 仍被抑制(已含)，逻辑不变。仅影响显示，不改计数(仍只看 _allFires)。
            foreach (var f in _lockedFires)
                if (f.reel >= 0 && f.row >= 0) oldRows.Add(CellKey.Encode(f.reel, f.row));
            m_reelView.m_preLockedFireRows = oldRows;
        }

        // 1) 基础旋转：生成棋盘，然后把已有火球锁回格子（它们在后续旋转中不消失）
        int[][] grid = OutcomeGenerator.Spin(cfg, GameManager.Instance.m_machine.rng, GameManager.Instance.m_testDoubleFireball);
        foreach (var f in _allFires)
            if (f.reel >= 0 && f.reel < grid.Length && f.row >= 0 && f.row < grid[f.reel].Length)
                grid[f.reel][f.row] = fbId;
        // ★ 锁定行火球：同样盖回盘面（保持可见），但绝不进 _allFires → 不算派彩（见下检测分支）。
        foreach (var f in _lockedFires)
            if (f.reel >= 0 && f.reel < grid.Length && f.row >= 0 && f.row < grid[f.reel].Length)
                grid[f.reel][f.row] = fbId;

        // 1.2) Mini 火球独立掷骰——概率远低于主游戏（火球永久不退，
        //   过高会导致几轮后全盘满火球）。固定 1.5% 每格（miniFbProb=0.015），约每轮 1~2 颗新火球。
        var rng = GameManager.Instance.m_machine.rng;
        const double miniFbProb = 0.015;
        for (int r = 0; r < grid.Length; r++)
        {
            for (int row = 0; row < grid[r].Length; row++)
            {
                if (grid[r][row] == fbId) continue;   // 已被旧火球占据
                if (rng.NextDouble() < miniFbProb)
                    grid[r][row] = fbId;
            }
        }

        // 1.5) 方式 A：本局棋盘上出现 N 颗 Scatter(icon 11) → 追加免费次数（3→+2 / 4→+5 / 5→+10）
        //    ★ 必须尊重 config 的 retrigger 开关：retrigger=false 时（如 modeB_44668 配置）Mini 内【不】追加，
        //      免费局严格等于进入时的 freeSpinsAwarded 次数（避免"本应 5 次却莫名涨到 8 次"）。
        //      BuildMiniConfig 已让 m_miniCfg.freeSpins 与主配置共享同一 FreeSpinsConfig 实例，
        //      故此处读到的 retrigger 即 JSON 中所设值（false）。
        if (cfg.freeSpins != null && cfg.freeSpins.retrigger)
        {
            int sc = ScatterUtil.Count(grid, cfg);
            int add = cfg.freeSpins.ScatterAwardFor(sc);
            if (add > 0) AwardExtraSpins(add, "Scatter x" + sc);
        }
        else if (cfg.freeSpins != null && !cfg.freeSpins.retrigger && SlotDebug.VerboseLogs)
        {
            Debug.Log($"[MiniGame] retrigger=false，本局 Scatter 不追加免费次数（保持进入次数 {_freeSpinsLeft}）");
        }

        // 2) 新火球检测（在 ShowGrid 之前，因为 ShowGrid→ClearAll 会销毁 overlay）
        var newFires = new List<FireballCell>();
        for (int r = 0; r < grid.Length; r++)
            for (int row = 0; row < grid[r].Length; row++)
                if (grid[r][row] == fbId)
                {
                    bool already = false;
                    foreach (var f in _allFires)
                        if (f.reel == r && f.row == row) { already = true; break; }
                    if (!already)
                        foreach (var f in _lockedFires)
                            if (f.reel == r && f.row == row) { already = true; break; }
                    if (!already)
                    {
                        var c = new FireballCell { reel = r, row = row, filled = true };
                        if (_lockedRows.Contains(row))
                        {
                            // ★ 锁定行火球：只显示、不计入。先 Roll 好 kind/倍率（解锁时直接回收进 _allFires，无需重 Roll）。
                            //   allowFreeMode=false → RollFireball 绝不产 FreeSpins（与 Mini 全局一致）。
                            var rolled = HoldSpinState.RollFireball(cfg, rng, bet, pots, allowFreeMode: false);
                            c.kind = rolled.kind;
                            c.multiplier = rolled.multiplier;
                            c.jackpotTier = rolled.jackpotTier;
                            _lockedFires.Add(c);
                        }
                        else
                        {
                            newFires.Add(c);
                        }
                    }
                }

        // 3) 新火球滚动 kind/multiplier 并加入全部火球集
        if (newFires.Count > 0)
        {
            HoldSpinState.Start(cfg, GameManager.Instance.m_machine.rng, bet, newFires, pots, allowFreeMode: false);
            _allFires.AddRange(newFires);
            // 创建火球：event:/Sounds/13
            if (FMODSoundMgr.Instance != null)
                FMODSoundMgr.Instance.PlaySound("event:/Sounds/13");
        }

        // ★ 纵深监控（非主修复）：Mini 内部 RollFireball 一律传 allowFreeMode:false，数学上不应产生 FreeSpins。
        //   若出现说明 RollFireball 在 Mini 路径被误传 allowFreeMode=true（或外部脏细胞未净），此处降级+告警便于定位，
        //   而非静默显示 FREE 外观。主修复见 StartMini 开头 ClearFireballOverlays（清基础轮残留 overlay）。
        int purgedCount = 0;
        foreach (var list in new[] { _allFires, _lockedFires })
            foreach (var f in list)
                if (f.kind == FireballKind.FreeSpins)
                {
                    f.kind = FireballKind.Multiplier;
                    f.multiplier = 1f;
                    purgedCount++;
                    Debug.LogWarning($"[MiniGame] 监控：_allFires/_lockedFires 出现 FreeSpins(reel={f.reel} row={f.row})，已降级为 Multiplier x1（请查 Mini 火球生成路径）");
                }
        if (purgedCount > 0)
            Debug.LogError($"[MiniGame] ★★ Mini 内部仍出现 {purgedCount} 个 FreeSpins 细胞，根因是 RollFireball 被误传 allowFreeMode=true 或外部脏数据未净，请查 PlayOneFreeSpin ★★");

        // 4) 构建 fireMults（主游戏同款格式：reel*100+row → FireballCell），
        //    传给 ShowGrid 让减速阶段就显示火球倍率/彩金（不等到停稳才出现）。
        //    ★ 锁定行火球虽不计入派彩，但已 stamp 进 grid 显示在盘面上，必须也写进 fireMults，
        //      否则 SetCellFireballMult 不会被调用 → 表现为"光有火球图标、没有倍数/彩金档名"。
        //      仅影响显示，不改变计数（计数仍只看 _allFires，锁定火球解锁时才回收计入）。
        var fireMults = new Dictionary<int, FireballCell>();
        foreach (var f in _allFires)
            if (f.kind != FireballKind.FreeSpins)
                fireMults[CellKey.Encode(f.reel, f.row)] = f;
        foreach (var f in _lockedFires)
            if (f.kind != FireballKind.FreeSpins)
                fireMults[CellKey.Encode(f.reel, f.row)] = f;

        // 5) ShowGrid 启动卷轴旋转。容器设为首个子节点(底层)，火球 overlay 在持久节点上为末位(上层)。
        m_reelView.ShowGrid(grid, fireMults);

        // 6) 等停稳（按 Start 可急停）
        while (m_reelView.IsSpinning())
        {
            if (IsStartDown()) m_reelView.StopNow();
            yield return null;
        }

        // 7) 卷轴停稳后刷新火球 overlay（含旧火球 + 本轮新落火球 + 锁定行火球）。
        //    ★ 锁定行火球也钉在盘面(固定显示、可见)，但【不计入派彩】(计数只看 _allFires，解锁时才回收计入)。
        //      旧 overlay 被 ClearFireballOverlays 销毁，随后用最新 _allFires+_lockedFires 重建。
        PinFireOverlays();

        // ★ 每轮不结算也不触发彩金特效——全部攒到 Mini 结束时统一结算 + 统一播特效。
    }

    /// <summary>重建火球 overlay：把当前所有"应钉在盘面"的火球(_allFires 已计入 + _lockedFires 锁定行只显示)一次性钉成持久 overlay。
    /// 区别：_allFires 计入派彩，_lockedFires 仅显示不计入(解锁时由 AdvanceUnlock 回收进 _allFires)。
    /// 每轮停稳、以及每次解锁后立即调用，保证锁定行火球也固定显示、不随卷轴闪。</summary>
    void PinFireOverlays()
    {
        if (m_reelView == null || m_miniCfg == null || GameManager.Instance?.m_machine == null) return;
        var cfg = m_miniCfg;
        float bet = GameManager.Instance.m_machine.totalBet > 0 ? GameManager.Instance.m_machine.totalBet : 1f;
        var pots = GameManager.Instance.m_machine.session != null ? GameManager.Instance.m_machine.session.Pots : null;
        var display = new List<FireballCell>(_allFires);
        display.AddRange(_lockedFires);   // ★ 锁定行火球也钉在盘面(固定显示)，但下面 ShowFeatureState 只用于显示、不改变计数
        // ★ 显示兜底：剔除 FreeSpins（若上面监控循环已降级则此处无作用；保留以防任何漏网细胞生成 FREE overlay 外观）
        display.RemoveAll(f => f.kind == FireballKind.FreeSpins);
        if (display.Count == 0) { m_reelView.ClearFireballOverlays(); return; }
        var hs = HoldSpinState.Start(cfg, GameManager.Instance.m_machine.rng, bet, display, pots, allowFreeMode: false);
        m_reelView.ClearWinHighlight();
        m_reelView.ShowFeatureState(hs);
    }

    // ===== 免费次数追加（方式 A / B） =====

    /// <summary>追加免费次数并刷新剩余显示；受 miniCap 上限约束（0=不封顶）。</summary>
    void AwardExtraSpins(int n, string reason)
    {
        if (n <= 0) return;
        _freeSpinsLeft += n;
        if (m_miniCfg != null && m_miniCfg.freeSpins != null && m_miniCfg.freeSpins.miniCap > 0)
            _freeSpinsLeft = Mathf.Min(_freeSpinsLeft, m_miniCfg.freeSpins.miniCap);
        UpdateRemainingDisplay();
        Debug.Log($"[MiniGame] 追加免费次数 +{n}（{reason}），剩余 {_freeSpinsLeft}");
    }

    // ===== 结算 =====

    List<string> CollectJackpots()
    {
        var list = new List<string>();
        foreach (var c in _allFires)
            // 免费模式火球(FreeSpins)和倍数火球不属于彩金档，排除
            if (c.jackpotTier >= 0 && c.jackpotTier < HoldSpinState.JackpotTierNames.Length)
                list.Add(HoldSpinState.JackpotTierNames[c.jackpotTier]);
        return list;
    }
}

}
