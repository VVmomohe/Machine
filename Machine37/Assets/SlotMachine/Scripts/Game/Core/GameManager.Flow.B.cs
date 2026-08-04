using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using SlotMachine.Core;

namespace com.slot
{
    /// <summary>模式B(Cash Falls / 直线结算 holdMode="Direct") 专属结算 + 收集盘 respin：
    ///   基础轮落下的火球钉成持久 overlay(固定火球/收集盘显示)；若有火球则进入【收集盘 respin】——
    ///   只做显示+动画(钉 overlay / tong / 计数器圈数)，不滚盘：每轮推进(落新火球/减圈/满列/释放)，
    ///   满列 → 进 Mini(enterMiniByColumnFill)；FREE 火球单列累计 → 追加免费次数(进 Mini)。
    ///   与 A 模式(GameManager.Flow.A.cs) 完全分离；通用收尾(SettleRoundWins/FinishBaseSettle)在 Flow.cs。</summary>
    public partial class GameManager
    {
        #region 模式B 专属 (Cash Falls 收集盘结算 + respin)

        IEnumerator SettleBaseB(GameResult r)
        {
            yield return WaitReelsStop();

            // ★ 模式B 收集盘（跨局持有，圈圈只在"开新一局"时减一，不在单局内循环减）。
            //   逻辑层 AdvanceHoldBoard 已在 Play() 把本局推进到最终态（合并新火球 + 减一个圈圈 + 满列/释放）；
            //   此处只按盘当前态【展示】：钉全部已收集火球 + 显示各列圈数 + 满列 tong/释放列清理。
            if (r.holdSpinState != null && m_reelView != null)
            {
                var hs = r.holdSpinState;
                m_reelView.ShowFeatureState(hs);   // 钉全部已收集火球（覆盖基础局 + 历史跨局持有）
                m_reelView.ActivateCounters();
                // ★ 问题1/2 修复：满列 tong 演出必须【真正播完】才能进 Mini（PlayTongAndWait 内部等 Mecanim+序列帧+超时兜底），
                //   且圈圈显示 3→2→1→0（counter=0 时 ReelFireNum 显示“0”，不再用 counter>0 才显示）。
                for (int rr = 0; rr < hs.reels; rr++)
                {
                    if (hs.isFull[rr])
                    {
                        m_reelView.SetRespinCounterRow(rr, 0);
                        yield return m_reelView.CollectFullReelAnimation(rr);   // 满列：火球逐颗掉入桶 + 桶逐颗反应（替代孤立的一次 PlayTongAndWait）
                    }
                    else if (!hs.released[rr])
                        m_reelView.SetRespinCounterRow(rr, hs.counter[rr]);   // 显示当前圈数（含 0：3→2→1→0）
                    else
                        m_reelView.HideCounterRow(rr);          // 已释放列：隐藏计数器
                }
                // ★ 诊断：停轮后每列收集盘状态 + 计数器预期可见性（核对"有圈圈列是否真的显示了圈"）；受 SlotDebug.VerboseLogs 开关控制。
                if (SlotDebug.VerboseLogs)
                {
                    var sb = new System.Text.StringBuilder($"[SettleBaseB-diag] reels={hs.reels}");
                    for (int rr = 0; rr < hs.reels; rr++)
                    {
                        int filled = 0;
                        for (int row = 0; row < hs.cells[rr].Length; row++) if (hs.cells[rr][row].filled) filled++;
                        bool showCounter = !hs.released[rr] && hs.counter[rr] >= 0;
                        sb.Append($" | r{rr}:filled={filled}/{hs.cells[rr].Length} cnt={hs.counter[rr]} rel={hs.released[rr]} full={hs.isFull[rr]} =>counterShown={showCounter}");
                    }
                    UnityEngine.Debug.Log(sb.ToString());
                }
                // 释放列兜底：清 overlay + 底层符号回归普通（board 已清空这些列 cells，ShowFeatureState 不会重钉）
                for (int rr = 0; rr < hs.reels; rr++)
                    if (hs.released[rr]) { if (SlotDebug.VerboseLogs) UnityEngine.Debug.Log($"[RELEASE-B-DO] r{rr} 展示层执行释放：ClearColumnFireballs + ReleaseColumnToSpinQueue（火球回归滚动队列）"); m_reelView.ClearColumnFireballs(rr); m_reelView.ReleaseColumnToSpinQueue(rr); }

                // ★ 诊断快照（受 SlotDebug.VerboseLogs 控制）：每列 overlay 数 / 棋盘 filled 数 / counter / released / full，
                //   与展示层 [RELEASE-MOVE]/[CLEAR-EXCEPT] 对照，定位"有圈圈却火球回归队列"。
                if (SlotDebug.VerboseLogs)
                {
                    var ovByCol = new System.Collections.Generic.Dictionary<int, int>();
                    foreach (var go in m_reelView.GetFireballOverlays())
                    {
                        if (go == null) continue;
                        if (m_reelView.ParseReelRow(go.name, out int rcol, out _))
                        { if (!ovByCol.ContainsKey(rcol)) ovByCol[rcol] = 0; ovByCol[rcol]++; }
                    }
                    var sbSnap = new System.Text.StringBuilder("[SNAP] ");
                    for (int rr = 0; rr < hs.reels; rr++)
                    {
                        int bf = 0; for (int row = 0; row < hs.cells[rr].Length; row++) if (hs.cells[rr][row].filled) bf++;
                        int ov = ovByCol.ContainsKey(rr) ? ovByCol[rr] : 0;
                        sbSnap.Append($"r{rr}[ovl={ov} bd={bf} cnt={hs.counter[rr]} rel={hs.released[rr]} full={hs.isFull[rr]}] ");
                    }
                    UnityEngine.Debug.Log(sbSnap.ToString());
                }

                // 特性赢分/彩金/FREE 已在逻辑层 AdvanceHoldBoard 按"本局增量"算定（featureWin / wonJackpots / freeSpinsAwarded），此处只展示。
                if (r.enterMiniByColumnFill)
                    Debug.Log($"[MINI-TRIGGER] 模式B 整列集满 → 进 Mini（scatter={r.freeSpinsFromScatter} + FREE={r.freeSpinsFromFireball} = freeSpinsAwarded={r.freeSpinsAwarded}）");
            }
            else if (m_reelView != null && r.baseFireballs != null)
            {
                // 无收集盘（无持有火球且本局也无足够火球触发）：基础局固定火球兜底显示
                // ★ 防御：即便未触发收集盘，本局新落火球也应显示圈圈（按"新火球→3"），避免"固定了但没圈圈"。
                m_reelView.ActivateCounters();
                int rc = (m_machine.config != null && m_machine.config.holdSpin != null)
                    ? m_machine.config.holdSpin.respinCount : 3;
                var fbReels = new HashSet<int>();
                foreach (var c in r.baseFireballs)
                {
                    if (c == null || !c.filled) continue;
                    fbReels.Add(c.reel);
                    m_reelView.ShowFireballOverlay(c.reel, c.row, c, playSound: false);
                }
                int n = m_reelView.CounterCount();
                for (int rr = 0; rr < n; rr++)
                    if (fbReels.Contains(rr)) m_reelView.SetRespinCounterRow(rr, rc);
                    else m_reelView.HideCounterRow(rr);
            }

            // ★ 模式B：把"屏幕显示为火球"的所有位置合并进 baseGrid（filled → fireballSymbolId），使结算/日志/底层卷轴格
            //   与视觉 overlay 严格一致。火球锁定的格子不参与连线（Cash Falls 语义：收集盘格子是火球，不是线符号）。
            //   ★ 关键：合并源必须覆盖【本局新落 + 跨局持有但本局没新落】两类位置——
            //     r.baseFireballs 仅含本局新落；hs.cells（如果有）含所有已落火球（含跨局持有）。
            //     旧逻辑只用 r.baseFireballs → 跨局持有但本局没新落的格子 m_id 仍为底层 spun 符号（如 10 Wild），
            //     屏幕却因 ShowFeatureState 钉了 overlay 显示火球，造成"底下 Wild+上层火球"叠图。
            //   ★ 修法：用 hs.cells 为主（更全），r.baseFireballs 补漏（极端路径：hs 为 null 但仍有 baseFireballs）。
            int fbId = (m_machine.config != null) ? m_machine.config.fireballSymbolId : 0;
            bool merged = false;
            if (fbId > 0 && r.baseGrid != null)
            {
                // 第一遍：hs.cells 全部 filled（跨局持有全集）
                if (r.holdSpinState != null)
                {
                    var hs = r.holdSpinState;
                    for (int rr = 0; rr < hs.reels && rr < r.baseGrid.Length; rr++)
                    {
                        if (hs.released[rr]) continue;   // 释放列已回归滚动队列，保留 spun 符号
                        if (hs.isFull[rr]) continue;     // ★ 满列已进入收集演出（火球逐颗掉进桶），底层不再强制火球图，避免收走后残留火球
                        for (int row = 0; row < hs.cells[rr].Length && row < r.baseGrid[rr].Length; row++)
                            if (hs.cells[rr][row].filled)
                            {
                                r.baseGrid[rr][row] = fbId;
                                merged = true;
                            }
                    }
                }
                // 第二遍：r.baseFireballs 补漏（hs 为 null 但有本局火球的兜底路径）
                if (r.baseFireballs != null)
                {
                    foreach (var c in r.baseFireballs)
                    {
                        if (c == null || !c.filled) continue;
                        if (c.reel < 0 || c.reel >= r.baseGrid.Length) continue;
                        if (c.row < 0 || c.row >= r.baseGrid[c.reel].Length) continue;
                        if (r.holdSpinState != null && c.reel < r.holdSpinState.isFull.Length && r.holdSpinState.isFull[c.reel]) continue;  // 满列不强制火球图
                        r.baseGrid[c.reel][c.row] = fbId;
                        merged = true;
                    }
                }
                // 第三遍：以 ShowFeatureState 实际创建的 overlay 为最终权威，强制底层 grid 对齐为火球。
                // 防御前两遍合并遗漏（如 hs.cells / baseFireballs 与 overlay 不同步、或满列收集演出前
                // 的瞬时状态），确保"屏上有火球 overlay"的位置底层 m_id 一定为 12，避免 Inspector 误读。
                if (m_reelView != null && fbId > 0)
                {
                    foreach (var go in m_reelView.GetFireballOverlays())
                    {
                        if (go == null) continue;
                        if (!m_reelView.ParseReelRow(go.name, out int rr, out int row)) continue;
                        if (rr < 0 || rr >= r.baseGrid.Length) continue;
                        if (row < 0 || row >= r.baseGrid[rr].Length) continue;
                        // 满列 ghost 已由 CollectFullReelAnimation 接管，下一局滚走；此处不强制改写
                        if (r.holdSpinState != null && rr < r.holdSpinState.isFull.Length && r.holdSpinState.isFull[rr]) continue;
                        if (r.baseGrid[rr][row] != fbId)
                        {
                            r.baseGrid[rr][row] = fbId;
                            merged = true;
                            if (SlotDebug.VerboseLogs)
                                Debug.Log($"[SettleBaseB-overlay兜底] r{rr},row{row}: baseGrid 改为 fbId={fbId}");
                        }
                    }
                }
            }
            // ★ 视觉兜底：把合并后的 baseGrid 同步渲染回底层卷轴格，确保"逻辑 id=12(火球)"的位置屏幕上确实显示火球，
            //   不被底层 spun 普通符号覆盖（不再依赖 ShowFeatureState 的 overlay 恰好盖住该格）。
            //   overlay(ShowFeatureState / baseFireballs 兜底) 仍负责倍率/彩金文字，叠在最上层；此处保证即便 overlay 因任何原因没盖住，底层也是火球。
            if (merged) m_reelView.SyncBoardFromGrid(r.baseGrid);

            // ★ 诊断：列出"屏幕显示为火球"的全部位置 + 该位置 ReelItem 的 m_id / m_image.enabled / m_fire.activeInHierarchy
            //   确认"数据层=12"与"视觉火球"严格一致。合并源 = hs.cells ∪ baseFireballs，遍历后输出每颗的视觉状态。
            if (merged && fbId > 0 && SlotDebug.VerboseLogs)
            {
                var sbDiag = new System.Text.StringBuilder($"[SettleBaseB-fbdiag] 合并后火球位置:");
                var seen = new HashSet<int>();
                if (r.holdSpinState != null)
                {
                    var hs = r.holdSpinState;
                    for (int rr = 0; rr < hs.reels; rr++)
                    {
                        for (int row = 0; row < hs.cells[rr].Length; row++)
                        {
                            if (!hs.cells[rr][row].filled) continue;
                            int key = rr * 100 + row;
                            if (!seen.Add(key)) continue;
                            var ri = m_reelView.GetReelItem(rr, row);
                            if (ri == null) { sbDiag.Append($" | (r{rr},row{row})=nullReelItem"); continue; }
                            bool imgOn = ri.m_image != null && ri.m_image.enabled;
                            bool imgGo = ri.m_image != null && ri.m_image.gameObject != null && ri.m_image.gameObject.activeInHierarchy;
                            bool fireOn = ri.m_fire != null && ri.m_fire.activeInHierarchy;
                            bool textOn = ri.m_text != null && ri.m_text.gameObject != null && ri.m_text.gameObject.activeInHierarchy;
                            sbDiag.Append($" | r{rr},row{row}:mid={ri.m_id} kind={ri.m_type} rate={ri.m_rate:F2} imgEn={imgOn} imgGo={imgGo} fire={fireOn} txt={textOn} txtVal='{(ri.m_text != null ? ri.m_text.text : "")}'");
                        }
                    }
                }
                if (r.baseFireballs != null)
                {
                    foreach (var c in r.baseFireballs)
                    {
                        if (c == null || !c.filled) continue;
                        int key = c.reel * 100 + c.row;
                        if (!seen.Add(key)) continue;
                        var ri = m_reelView.GetReelItem(c.reel, c.row);
                        if (ri == null) { sbDiag.Append($" | (r{c.reel},row{c.row})=nullReelItem"); continue; }
                        bool imgOn = ri.m_image != null && ri.m_image.enabled;
                        bool imgGo = ri.m_image != null && ri.m_image.gameObject != null && ri.m_image.gameObject.activeInHierarchy;
                        bool fireOn = ri.m_fire != null && ri.m_fire.activeInHierarchy;
                        bool textOn = ri.m_text != null && ri.m_text.gameObject != null && ri.m_text.gameObject.activeInHierarchy;
                        sbDiag.Append($" | r{c.reel},row{c.row}:mid={ri.m_id} kind={ri.m_type} rate={ri.m_rate:F2} imgEn={imgOn} imgGo={imgGo} fire={fireOn} txt={textOn} txtVal='{(ri.m_text != null ? ri.m_text.text : "")}'");
                    }
                }
                UnityEngine.Debug.Log(sbDiag.ToString());

                // ★ 诊断：再打印每个火球 overlay 的 m_text 状态——若 overlay 没显示文字而底层格显示了，
                //   就能定位是 overlay 路径(prefab 子物体顺序/font/层级)还是底层格路径问题。
                if (m_reelView != null)
                {
                    var sbOvl = new System.Text.StringBuilder($"[SettleBaseB-ovldiag] overlay 文字状态:");
                    foreach (var go in m_reelView.GetFireballOverlays())
                    {
                        if (go == null) continue;
                        var item = go.GetComponent<ReelItem>();
                        if (item == null) continue;
                        bool textOn = item.m_text != null && item.m_text.gameObject != null && item.m_text.gameObject.activeInHierarchy;
                        string txtVal = (item.m_text != null) ? item.m_text.text : "";
                        bool fontOk = item.m_text != null && item.m_text.font != null;
                        int fontSz = (item.m_text != null) ? item.m_text.fontSize : 0;
                        sbOvl.Append($" | {go.name}:kind={item.m_type} rate={item.m_rate:F2} txt={textOn} txtVal='{txtVal}' font={(fontOk?"OK":"NULL")} size={fontSz}");
                    }
                    UnityEngine.Debug.Log(sbOvl.ToString());
                }
            }

            // ★ 持有火球格排除掩码（用于连线/Scatter 评估）：filled && !released，含满列(isFull)——
            // 这些位置在屏幕上都是火球，必须切断任何符号的连线、且不计入 Scatter。
            // 注意：此处不跳过 isFull 列——满列虽在显示合并时被跳过(交给收集演出)，
            // 但其底层新鲜卷轴符号仍可能被连成 phantom 赢分，故评估层必须排除。
            bool[][] heldMask = null;
            if (r.holdSpinState != null && r.holdSpinState.cells != null)
            {
                var hs = r.holdSpinState;
                heldMask = new bool[hs.cells.Length][];
                for (int rr = 0; rr < hs.cells.Length; rr++)
                {
                    var col = hs.cells[rr];
                    int h = (col != null) ? col.Length : 0;
                    heldMask[rr] = new bool[h];
                    bool released = (hs.released != null && rr < hs.released.Length) ? hs.released[rr] : false;
                    for (int row = 0; row < h; row++)
                        heldMask[rr][row] = !released && col[row] != null && col[row].filled;
                }
            }
            if (r.baseFireballs != null)   // 兜底：本局新落火球也一并排除（极端路径 hs 为 null 时）
            {
                if (heldMask == null)
                {
                    int reels = (r.baseGrid != null) ? r.baseGrid.Length : 5;
                    heldMask = new bool[reels][];
                }
                foreach (var c in r.baseFireballs)
                {
                    if (c == null || !c.filled) continue;
                    if (c.reel < 0 || c.reel >= heldMask.Length) continue;
                    if (c.row < 0 || c.row >= (heldMask[c.reel] != null ? heldMask[c.reel].Length : 0))
                    {
                        if (c.reel < heldMask.Length) heldMask[c.reel] = new bool[System.Math.Max(c.row + 1, (r.baseGrid != null && c.reel < r.baseGrid.Length) ? r.baseGrid[c.reel].Length : 0)];
                        else continue;
                    }
                    heldMask[c.reel][c.row] = true;
                }
            }

            // 数值结算（与 A 共用同一套评估口径）
            int sc;
            float bw = SettleRoundWins(r.baseGrid, m_machine.totalBet, out sc, heldMask);
            r.baseWin = bw;
            r.scatterCount = sc;
            r.totalPayout = r.baseWin + r.scatterPayout + r.featureWin;

            yield return FinishBaseSettle(r);
        }

        #endregion
    }
}
