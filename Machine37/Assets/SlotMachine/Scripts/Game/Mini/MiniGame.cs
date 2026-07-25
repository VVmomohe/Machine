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

    [Header("计数器模板（从主游戏拖一个 ReelFireNum 过来作克隆模板）")]
    public ReelFireNum m_counterTemplate;

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
    ReelConfig m_miniCfg;                   // Mini 专用配置副本（仅 reelRows=8×5，其余共享主配置）

    public bool IsActive => _active;

    /// <summary>Mini 结算结果：交还 GameManager 入账。</summary>
    public class MiniResult
    {
        public float fireTotal;             // 全部火球派彩（倍数之和 + 中彩金档，已 ×bet）
        public int fireCount;               // 火球总颗数
        public List<FireballKind> jackpots; // 中过的彩金档（可重复/多档）
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
        rv.m_persistentFireOverlays = true;

        // ★ Mini 棋盘：火球显示 m_fire（与主游戏一致），普通图标 m_image 隐藏
        rv.m_inFreeSpins = false;

        // 5 列节点（若未绑定则运行时创建，并沿用主棋盘列布局）
        if (rv.m_node == null || rv.m_node.Length < 5) BuildColumns(rv);
        // 5 个计数器（克隆模板）
        if (rv.m_numObjs == null || rv.m_numObjs.Length < 5) BuildCounters(rv);
        rv.m_tongs = null;   // Mini 不接桶：掉桶动画回退到底部消失（不影响火球统计）
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

    void BuildCounters(ReelView rv)
    {
        if (m_counterTemplate == null)
        {
            Debug.LogError("[MiniGame] 未设置 m_counterTemplate（从主游戏拖一个 ReelFireNum 作克隆模板）");
            return;
        }
        rv.m_numObjs = new ReelFireNum[5];
        for (int i = 0; i < 5; i++)
        {
            var go = Instantiate(m_counterTemplate.gameObject, rv.transform);
            go.name = $"MiniCounter_{i}";
            var fn = go.GetComponent<ReelFireNum>();
            if (fn != null) fn.ResetMultiplier();
            go.SetActive(false);
            rv.m_numObjs[i] = fn;
        }
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
            yield return StartCoroutine(WaitForMiniConfirm());
        }

        // 结束：汇总全部火球（全场景一次性结算），回调交还 GameManager
        float bet = GameManager.Instance.m_machine != null && GameManager.Instance.m_machine.totalBet > 0
            ? GameManager.Instance.m_machine.totalBet : 1f;
        _fireTotal = 0f;
        float totalMult = 0f;                       // 最终总倍数（全部火球倍率之和，含彩金档折算倍数）
        foreach (var f in _allFires) { _fireTotal += bet * f.multiplier; totalMult += f.multiplier; }

        var result = new MiniResult
        {
            fireTotal = _fireTotal,
            fireCount = _allFires.Count,
            jackpots = CollectJackpots(),
        };

        // ★ 结算展示：用计数器模板(Counter Template)显示本次免费游戏的最终总倍数，停留约 2 秒后再回主游戏。
        //   （此时 Mini 棋盘仍可见；结算信息不再走 remainingText。）
        if (m_remainingText != null) m_remainingText.text = "";   // 先清空剩余次数文本

        // ★ 这 4 秒结算展示期间，主 HUD 也要亮出总派彩赢分（不再一直挂 0）。
        //   仅显示、不滚余额——余额滚入仍由回调用 AddFeatureWin 一次性完成，避免重复入账。
        if (GameManager.Instance != null && GameManager.Instance.m_player != null && result.fireTotal > 0f)
            GameManager.Instance.m_player.ShowWinValue((long)System.Math.Round(result.fireTotal));

        yield return StartCoroutine(ShowFinalMultiplier(totalMult));

        // ★ 彩金特效统一在 Mini 结束时播放（不是每轮播）
        var bonus = GameManager.Instance?.m_bonus;
        if (bonus != null && result.jackpots != null && result.jackpots.Count > 0)
            foreach (var kind in result.jackpots)
                bonus.ShowJackpotEffect(kind);

        RestoreMainBoard(result);
    }

    /// <summary>结算展示：用计数器模板(Counter Template)显示本次免费游戏的最终总倍数（如 "X21.5"），停留约 m_finalShowTime 秒。</summary>
    IEnumerator ShowFinalMultiplier(float totalMult)
    {
        if (m_counterTemplate != null && totalMult > 0f)
        {
            m_counterTemplate.gameObject.SetActive(true);
            m_counterTemplate.ResetMultiplier();          // 归 0 + 恢复内部初始态
            m_counterTemplate.AddMultiplier(totalMult);   // 显示 "X" + 最终总倍数（同时隐藏倒计时圈，仅留文字）
            yield return new WaitForSeconds(m_finalShowTime);
            m_counterTemplate.ResetMultiplier();
            m_counterTemplate.gameObject.SetActive(false);
        }
        else
        {
            // 无计数器 / 无火球：不展示，但仍停留 m_finalShowTime 秒保持节奏一致
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
                if (f.reel >= 0 && f.row >= 0) oldRows.Add(f.reel * 100 + f.row);
            m_reelView.m_preLockedFireRows = oldRows;
        }

        // 1) 基础旋转：生成棋盘，然后把已有火球锁回格子（它们在后续旋转中不消失）
        int[][] grid = OutcomeGenerator.Spin(cfg, GameManager.Instance.m_machine.rng);
        foreach (var f in _allFires)
            if (f.reel >= 0 && f.reel < grid.Length && f.row >= 0 && f.row < grid[f.reel].Length)
                grid[f.reel][f.row] = fbId;

        // 1.2) Mini 火球独立掷骰——概率远低于主游戏 RespinhSpin（火球永久不退，
        //   过高会导致几轮后全盘满火球）。固定 3% 每格，约每轮 1~2 颗新火球。
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
        if (cfg.freeSpins != null)
        {
            int sc = ScatterUtil.Count(grid, cfg);
            int add = cfg.freeSpins.ScatterAwardFor(sc);
            if (add > 0) AwardExtraSpins(add, "Scatter x" + sc);
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
                        newFires.Add(new FireballCell { reel = r, row = row, filled = true });
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

        // ★ 防御：Mini 中绝不应出现 FreeSpins 火球（只应在主游戏 Hold&Spin 生成）。
        //   若出现则降级为普通倍数火球，避免 m_freeFire 错误显示。
        foreach (var f in _allFires)
            if (f.kind == FireballKind.FreeSpins)
            {
                f.kind = FireballKind.Multiplier;
                f.multiplier = 1f;
                Debug.LogWarning($"[MiniGame] 防御：_allFires 出现 FreeSpins(reel={f.reel} row={f.row})，已降级为 Multiplier x1");
            }

        // 4) 构建 fireMults（主游戏同款格式：reel*100+row → FireballCell），
        //    传给 ShowGrid 让减速阶段就显示火球倍率/彩金（不等到停稳才出现）。
        var fireMults = new Dictionary<int, FireballCell>();
        foreach (var f in _allFires)
            fireMults[f.reel * 100 + f.row] = f;

        // 5) ShowGrid 启动卷轴旋转。容器设为首个子节点(底层)，火球 overlay 在持久节点上为末位(上层)。
        m_reelView.ShowGrid(grid, fireMults);

        // 6) 等停稳（按 Start 可急停）
        while (m_reelView.IsSpinning())
        {
            if (IsStartDown()) m_reelView.StopNow();
            yield return null;
        }

        // 7) 卷轴停稳后刷新火球 overlay（含旧火球 + 本轮新落火球）
        //    旧 overlay 被 ClearFireballOverlays 销毁，随后用最新 _allFires 重建。
        if (_allFires.Count > 0)
        {
            var hs = HoldSpinState.Start(cfg, GameManager.Instance.m_machine.rng, bet, _allFires, pots);
            m_reelView.ClearWinHighlight();
            m_reelView.ShowFeatureState(hs);
            m_reelView.HideAllCounters();
        }

        // ★ 每轮不结算也不触发彩金特效——全部攒到 Mini 结束时统一结算 + 统一播特效。
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

    List<FireballKind> CollectJackpots()
    {
        var list = new List<FireballKind>();
        foreach (var c in _allFires)
            // 免费模式火球(FreeSpins)不属于彩金档，排除（它只追加免费次数）
            if (c.kind != FireballKind.Multiplier && c.kind != FireballKind.FreeSpins) list.Add(c.kind);
        return list;
    }
}

}
