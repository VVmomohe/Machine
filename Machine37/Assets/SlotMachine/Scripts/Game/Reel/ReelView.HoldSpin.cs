using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    /// <summary>ReelView 火球 Hold &amp; Spin 核心流程：ShowFeatureState、SpinHoldRound、ApplyRespinStep、Release列。</summary>
    public partial class ReelView
    {
        List<GameObject> _fbOverlays = new List<GameObject>();
        HashSet<int> _releaseReels = new HashSet<int>();
        HashSet<int> _collectedReels = new HashSet<int>();
        bool _wasSpinning = false;

        // ★ Hold&Spin respin 滚动的急停支持：SpinHoldRound 是自定义协程（不走基础 _reels[i].spinning），
        //   故 StopNow 无法通过 st.spinning 命中。用这两个标志让 StopNow 能识别并提前打断 Hold 滚动。
        bool _holdSpinning = false;       // 当前是否处于 Hold&Spin 滚动协程中
        bool _holdStopRequested = false;  // StopNow 置位 → 下一帧 SpinHoldRound 循环 break 并立即结算定格

        // ===== 进入 Hold&Spin =====

        public virtual void ShowFeatureState(HoldSpinState s)
        {
            HideAllCounters();
            ActivateCounters();   // ★ 激活计数器（开 _active 标志），之后 SetCount 才能正常显示
            ClearFireballOverlays();
            Debug.Log($"[DIAG-ShowFeatureState] 进入 Hold&Spin 显示（计数器可见性由 ReelFireNum 自管）");

            for (int r = 0; r < s.reels && r < _reels.Count; r++)
            {
                var st = _reels[r];
                for (int row = 0; row < s.cells[r].Length; row++)
                {
                    var c = s.cells[r][row];
                    if (c.filled)
                        ShowFireballOverlay(r, row, c, playSound: false);
                }
            }
            if (s.counter != null)
                for (int r = 0; r < s.counter.Length; r++)
                    // 可见性由 ReelFireNum 自管：active 且 (有圈 或 有倍率) 才显示；开新局(active=false)才整体隐藏。
                    SetRespinCounterRow(r, s.counter[r]);

            RefreshColumnEffects(s, s.counter);   // 近满列(差1火球)→亮整列 m_effect；已释放/集满列不亮

            if (s.reels > 0 && _reels.Count > 0)
            {
                int cnt0 = 0;
                foreach (var o in _fbOverlays) if (o != null && o.name.StartsWith("FBOverlay_0_")) cnt0++;
                var s0 = _reels[0];
                Debug.Log($"[ShowFeatureState] reel0 火球overlay数={cnt0} / cells[0].Length={s.cells[0].Length} | buf={m_buf} rowBaseY={m_rowBaseY} cellSize={m_cellSize} st.rows={s0.rows}");
            }
        }

        // ===== 滚动一轮 =====

        public IEnumerator SpinHoldRound(List<int> spunReels, float dur,
            Dictionary<int, FireballCell> newFireMults = null,
            int[][] respinGrid = null)
        {
            if (spunReels == null || spunReels.Count == 0) yield break;

            _holdSpinning = true;
            _holdStopRequested = false;

            var offset = new Dictionary<int, float>();
            var stopAt = new Dictionary<int, float>();
            for (int i = 0; i < spunReels.Count; i++)
            {
                int reel = spunReels[i];
                offset[reel] = 0f;
                stopAt[reel] = dur + i * m_autoStagger;
            }
            float settleTime = 0.4f;
            float maxStop = 0f;
            foreach (var v in stopAt.Values) if (v > maxStop) maxStop = v;
            float endTime = maxStop + settleTime;

            var scrollCells = new Dictionary<int, int>();
            foreach (int reel in spunReels)
            {
                if (reel < 0 || reel >= _reels.Count) continue;
                var st = _reels[reel];
                int raw = PredictScrollCells(stopAt[reel], settleTime);
                // ★ 自然停落点(=offset 收敛目标)取纯行数倍数(≡0 mod rows)：displayStrip 已是 respinGrid 周期循环带，任意 basePos 显示均为
                //   周期序列，与 FindFireballCell(row=k-m_buf) 火球/百搭定位语义自洽 → 符号不突变（不再重写落点）。
                scrollCells[reel] = st.rows * Mathf.FloorToInt(raw / (float)st.rows);
            }

            var fbStripMult = new Dictionary<int, FireballCell>();
            // ★ 干净循环：displayStrip 建成 respinGrid 周期循环带（方案A，对齐基础局「整条带即结果」），落点只选窗口、滚动中不再重写符号。
            var quickStopped = new HashSet<int>();

            // 干净循环：残留火球替换成普通符号，建成 respinGrid 周期循环带（火球格用普通符占位，由 overlay 显示）
            foreach (int reel in spunReels)
            {
                if (reel < 0 || reel >= _reels.Count) continue;
                var st = _reels[reel];
                int stripLen = (st.displayStrip != null) ? st.displayStrip.Count : 0;
                if (stripLen <= 0) continue;
                // ★ 方案A：displayStrip 建成 respinGrid 的周期循环带（对齐基础局「整条带即结果」）。
                //   周期 = 该列行数(rows)，逻辑行 row 对应索引 (stripBase + m_buf + row)；火球格由 overlay 显示，符号带火球格用普通符占位。
                //   任意 basePos 显示均为 respinGrid 周期序列 → 急停就近不突变（与基础局同构）。
                int rowsN = st.rows;
                for (int i = 0; i < stripLen; i++)
                {
                    int row2 = (((i - st.stripBase - m_buf) % rowsN) + rowsN) % rowsN;
                    int sym2 = (respinGrid != null && reel < respinGrid.Length && respinGrid[reel] != null && row2 < respinGrid[reel].Length)
                        ? respinGrid[reel][row2] : 0;
                    st.displayStrip[i] = (sym2 <= 0 || sym2 == m_fireballSymbolId) ? RandNormalSymbol() : sym2;
                }
            }

            // 初始：displayStrip 已是 respinGrid 周期循环带（行108-115 初始化写入），任意 basePos 显示均为 respinGrid 周期序列，
            //   故无需 PlaceRespinResult 重写落点（重写会引入 landOffset 相位错位 → 普通符变百搭/火球错位）。火球由 overlay 显示。
            // （PlaceRespinResult 已删除：方案A 周期带直接驱动，重写反成突变源。）

            FireballCell FindFireballCell(int reel, int k, int symIdx)
            {
                int row = k - m_buf;
                int mkey = reel * 100 + row;
                FireballCell cell = null;
                if (newFireMults != null && newFireMults.TryGetValue(mkey, out cell)) { }
                if (cell == null)
                {
                    int skey = reel * 100000 + symIdx;
                    fbStripMult.TryGetValue(skey, out cell);
                }
                return cell;
            }

            // ★ 参与滚动的有效列（用于"全部停稳即提前结束"，避免停止键急停后还空等到 endTime 才结算）
            var participating = new List<int>();
            foreach (int reel in spunReels)
            {
                if (reel < 0 || reel >= _reels.Count) continue;
                var stC = _reels[reel];
                int sl = (stC.displayStrip != null) ? stC.displayStrip.Count : 0;
                if (sl <= 0) continue;
                participating.Add(reel);
            }

            float t = 0f;
            var stoppedReels = new HashSet<int>();
            // ★ 停止键急停：像普通局 StopNow→DelayedStop(i*0.2f) 那样，逐列错开 0.2s 才进入减速，
            //   形成"一列一列依次停下"的 waterfall 手感，而不是所有列一起停（那会显得很怪、不像普通局）。
            var stopStart = new Dictionary<int, float>();
            bool stopScheduled = false;
            while (t < endTime)
            {
                t += Time.deltaTime;
                float dt = Time.deltaTime;

                // 停止键首次触发：为每列排定错开的减速起始时刻（reel i → t + i*0.2s），并视情况延长 endTime 兜底。
                if (_holdStopRequested && !stopScheduled)
                {
                    stopScheduled = true;
                    float maxIdx = 0f;
                    foreach (int r in participating)
                    {
                        stopStart[r] = t + r * 0.2f;   // 对齐普通局 DelayedStop(i*0.2f)
                        if (r > maxIdx) maxIdx = r;
                    }
                    float needed = t + maxIdx * 0.2f + 1.5f;   // 末列错开停 + 收敛余量
                    if (needed > endTime) endTime = needed;
                }

                foreach (int reel in spunReels)
                {
                    if (reel < 0 || reel >= _reels.Count) continue;
                    var st = _reels[reel];
                    int stripLen = (st.displayStrip != null) ? st.displayStrip.Count : 0;
                    if (stripLen <= 0) continue;

                    // 该列是否已进入减速阶段：
                    //   自然停(dur 驱动) → 到 stopAt[reel] 才减速（一旦进入保持）；
                    //   急停(停止键)     → 到错开时刻 stopStart[reel] 才减速（未轮到本列则继续匀速，形成 waterfall）。
                    bool decelActive = (t >= stopAt[reel]) || (_holdStopRequested && stopStart.ContainsKey(reel) && t >= stopStart[reel]);

                    if (!decelActive)
                    {
                        // 匀速推进：自然停临近 stopAt 时轻微减速更顺；急停尚未轮到本列则保持全速（与普通局一致）。
                        float spd = m_baseSpeed;
                        if (!_holdStopRequested)
                        {
                            float remaining = stopAt[reel] - t;
                            if (remaining < 0.35f) spd = m_baseSpeed * Mathf.Clamp01(remaining / 0.35f);
                        }
                        offset[reel] += spd * dt;
                    }
                    else
                    {
                        // ★ 急停、且该列仍在匀速段时：把收敛目标从远处预测停位改到"前方就近格线"（仅此刻改 scrollCells），
                        //   像普通局 FindAlignedStopPos 对齐格线就近停——否则固定远停位会让按停止键后卷轴仍按原速爬到远处才停（像"没停"）。
                        //   ★ 不再重写 displayStrip：方案A 已把 displayStrip 建成 respinGrid 周期循环带（行108-115），任意 basePos 显示均为 respinGrid 周期序列，
                        //     与 FindFireballCell(row=k-m_buf) 火球/百搭定位语义在任意 basePos 下自洽 → 符号永不突变。
                        //     之前 PlaceRespinResult 把 respinGrid 写到 stripBase+landOffset+m_buf+row，landOffset 相位无法同时让"重写幂等"与"显示/火球语义一致"
                        //     （m_buf 偏移冲突）→ 重写窗口相对周期带错位 → 只有被重写的 Wild/火球格错配("普通符→百搭"或"火球突然出现")。删重写后该 bug 根除。
                        //     急停落点取 Ceil(offset) 前方整数格线 → 相对按停位置总是前进(不回退1格)，卷轴平滑收敛、符号不突变。
                        if (_holdStopRequested && !quickStopped.Contains(reel) && t < stopAt[reel])
                        {
                            quickStopped.Add(reel);
                            // ★ 周期带已保证任意 basePos 显示 respinGrid 周期序列，故急停只把收敛目标设为「前方就近格线」(前进、不回退)，
                            //   不再重写 displayStrip（重写会引入 landOffset 相位错位 → 普通符变百搭/火球错位）。火球由 overlay 显示。
                            scrollCells[reel] = Mathf.CeilToInt(offset[reel]);   // 前方就近整数格线（前进方向），周期带平滑收敛、符号不突变
                        }

                        float target = scrollCells[reel];
                        float diff = target - offset[reel];
                        if (Mathf.Abs(diff) < 0.01f)
                        {
                            offset[reel] = target;
                            if (!stoppedReels.Contains(reel))
                            {
                                stoppedReels.Add(reel);
                                // 当前列滚动停后：播放 event:/Sounds/1
                                if (FMODSoundMgr.Instance != null)
                                    FMODSoundMgr.Instance.PlaySound("event:/Sounds/1");
                            }
                        }
                        else
                        {
                            // ★ 收敛：急停落点已就近 → 直接 ease-out（m_quickDecel，无 maxStep 限幅，手感同普通局急停）；
                            //   自然停目标远 → 保留 maxStep 限幅防"突然前冲"的突兀感。
                            float decel = _holdStopRequested ? m_quickDecel : m_normalDecel;
                            float step = diff * Mathf.Clamp01(dt * decel);
                            if (!_holdStopRequested)
                            {
                                float maxStep = m_baseSpeed * dt;
                                if (Mathf.Abs(step) > maxStep) step = Mathf.Sign(diff) * maxStep;
                            }
                            offset[reel] += step;
                        }
                    }

                    int basePos = Mathf.FloorToInt(offset[reel]);
                    float frac = offset[reel] - basePos;
                    int topIdx = st.stripBase + basePos;
                    for (int k = 0; k < st.cells.Count; k++)
                    {
                        float worldRow = (k - m_buf) - frac;
                        float y = worldRow * m_cellSize + m_rowBaseY;
                        var rt = st.cells[k].transform as RectTransform;
                        if (rt != null) rt.anchoredPosition = new Vector2(0f, y);

                        int symIdx = (topIdx + k) % stripLen;
                        if (symIdx < 0) symIdx += stripLen;
                        int sym = st.displayStrip[symIdx];
                        if (sym == m_wildId && (reel == 0 || (k - m_buf) == st.rows - 1))
                            sym = m_symbolMin + (symIdx % (m_symbolMax - m_symbolMin));
                        if (sym == m_fireballSymbolId)
                        {
                            var cell = FindFireballCell(reel, k, symIdx);
                            if (cell != null) SetCellFireballMult(st, k, cell);
                        }
                        SetCell(st, k, sym);
                        if (sym == m_fireballSymbolId)
                        {
                            var cell = FindFireballCell(reel, k, symIdx);
                            if (cell != null)
                            {
                                // ★ freeFire 严格按火球自身 kind：FreeSpins 类型才显免费火球外观（m_inFreeSpins 是死代码，已移除）
                                bool freeFire = cell.kind == FireballKind.FreeSpins;
                                var it = st.cellItems[k];
                                if (it != null) it.ShowFire(true, freeFire);
                            }
                        }
                    }
                }

                // ★ 所有参与列都已停稳（自然停或停止键急停后收敛完成）→ 立即退出循环，
                //   不再空等到 endTime，避免急停后还延迟约 1 秒才结算。
                bool allStopped = true;
                foreach (int r in participating) if (!stoppedReels.Contains(r)) { allStopped = false; break; }
                if (allStopped) break;

                if (_releaseReels.Count > 0) MoveReleasingOverlays(offset);

                yield return null;
            }

            foreach (int reel in spunReels)
            {
                if (reel < 0 || reel >= _reels.Count) continue;
                var st = _reels[reel];
                int stripLen = (st.displayStrip != null) ? st.displayStrip.Count : 0;
                offset[reel] = scrollCells[reel];
                int basePos = Mathf.FloorToInt(offset[reel]);
                int topIdx = st.stripBase + basePos;
                if (stripLen > 0) st.stripBase = ((topIdx % stripLen) + stripLen) % stripLen;
                for (int k = 0; k < st.cells.Count; k++)
                {
                    int row = k - m_buf;
                    var rt = st.cells[k].transform as RectTransform;
                    if (rt != null) rt.anchoredPosition = new Vector2(0f, RowToY(row));

                    int symIdx = 0;
                    int sym;
                    if (stripLen > 0)
                    {
                        symIdx = (topIdx + k) % stripLen;
                        if (symIdx < 0) symIdx += stripLen;
                        sym = st.displayStrip[symIdx];
                    }
                    else
                        continue;

                    if (sym == m_fireballSymbolId)
                    {
                        var mult = FindFireballCell(reel, k, ((stripLen > 0) ? ((topIdx + k) % stripLen) : 0));
                        if (mult != null) SetCellFireballMult(st, k, mult);
                    }
                    else if (sym == m_wildId)
                    {
                        // ★ 与逐帧渲染保持一致：仅 reel0 / 最后一行 的百搭做确定性替换（m_symbolMin + symIdx%...），
                        //   其余百搭原样保留、不再在此做 landWild>1 的随机替换。
                        //   原因：逐帧渲染时多张百搭都正常显示，若此处用 RandNormalSymbol() 随机换掉第 2 张，
                        //   会造成"滚动时明明有百搭、停好之后却莫名变成别的图案"（用户反馈的 BUG）。
                        //   百搭总量/列位已由生成层 DecideWildPlan/DecideWildPlanRespin 提前定点（写一次不事后替换），
                        //   此处仅做 reel0/顶行显示拦截兜底（与 SetCell 的百搭统一拦截点一致），不再依赖 LimitWildsOnBoard。
                        if (reel == 0 || row == st.rows - 1)
                            sym = m_symbolMin + (symIdx % (m_symbolMax - m_symbolMin));
                    }
                    SetCell(st, k, sym, true);
                    if (sym == m_fireballSymbolId)
                    {
                        var mult = FindFireballCell(reel, k, ((stripLen > 0) ? ((topIdx + k) % stripLen) : 0));
                        if (mult != null)
                        {
                            // ★ freeFire 严格按火球自身 kind：FreeSpins 类型才显免费火球外观（m_inFreeSpins 是死代码，已移除）
                            bool freeFire = mult.kind == FireballKind.FreeSpins;
                            var it = st.cellItems[k];
                            if (it != null) it.ShowFire(true, freeFire);
                        }
                    }
                }
            }

            DestroyReleasingOverlays();

            _releaseReels.Clear();

            _holdSpinning = false;
        }

        int PredictScrollCells(float stopAtReel, float settleTime)
        {
            float simOffset = 0f;
            float simT = 0f;
            float simEnd = stopAtReel + settleTime;
            const float dt = 1f / 60f;
            while (simT < simEnd)
            {
                simT += dt;
                if (simT < stopAtReel)
                {
                    float remaining = stopAtReel - simT;
                    float spd = (remaining < 0.35f) ? m_baseSpeed * Mathf.Clamp01(remaining / 0.35f) : m_baseSpeed;
                    simOffset += spd * dt;
                }
                else
                {
                    float target = Mathf.Round(simOffset);
                    float diff = target - simOffset;
                    if (Mathf.Abs(diff) < 0.01f) simOffset = target;
                    else simOffset += diff * Mathf.Clamp01(dt * m_normalDecel);
                }
            }
            return Mathf.RoundToInt(simOffset);
        }

        // ===== 滚动结束后结算 =====

        public virtual void ApplyRespinStep(HoldSpinStep step, HoldSpinState state)
        {
            if (step == null) return;

            // ★ 重新激活计数器：OnStartKey 顶部在滚动前会 HideAllCounters（m_active=false 整体隐藏），
            //   本回合滚动结束后必须重新激活，否则下方 SetRespinCounterRow 只设 count 不置 active → 计数器永久隐藏。
            //   仅 Hold&Spin 流程会走到这里，故不会影响基础局（基础局无火球，m_active 本就应保持 false）。
            ActivateCounters();

            if (step.newFireballs != null)
            {
                foreach (var c in step.newFireballs)
                {
                    ShowFireballOverlay(c.reel, c.row, c);
                    _baseFireMults[c.reel * 100 + c.row] = c;
                }
            }

            if (step.counters != null)
                for (int reel = 0; reel < step.counters.Length; reel++)
                    // 可见性由 ReelFireNum 自管：active 且 (有圈 或 有倍率) 才显示；开新局(active=false)才整体隐藏。
                    SetRespinCounterRow(reel, step.counters[reel]);

            RefreshColumnEffects(state, step.counters);   // 近满列(差1火球)→亮整列 m_effect；已释放/集满列不亮

            {
                int cnt0 = 0;
                foreach (var o in _fbOverlays) if (o != null && o.name.StartsWith("FBOverlay_0_")) cnt0++;
                Debug.Log($"[ApplyRespinStep] 后 reel0 火球overlay数={cnt0}（本轮新火球={((step.newFireballs!=null)?step.newFireballs.Count:0)}）");
            }
        }

        // ===== 释放列 =====

        public void ReleaseReel(int reel)
        {
            if (reel < 0) return;
            _releaseReels.Add(reel);
            // ★ 不再隐藏该列计数器：保留其累计倍数显示，直到玩家按确认开新基础局(OnStartKey/ShowGrid)统一隐藏。
            // ★ 火球开始回归队列的瞬间立即关闭整列 m_effect 预警特效——
            //   之前只在 SpinHoldRound 结束（滚动停稳）→ DestroyReleasingOverlays→RefreshColumnEffects 才关，
            //   导致 m_effect 要等火球滚回队列并停下才消失。现在在 Release 那一刻即关，与火球回滚同步。
            SetColumnEffect(reel, false);
        }

        public bool IsReelReleasing(int reel)
        {
            return _releaseReels != null && _releaseReels.Contains(reel);
        }

        // ===== 满列收集后释放 =====

        public void ReleaseCollectedForNextSpin()
        {
            foreach (var r in _collectedReels) _releaseReels.Add(r);
            // ★ 兜底：直接遍历所有残留火球 overlay，把每个 overlay 的 reel 也并入待释放集合。
            //   原因：CollectFullReelAnimation 在协程末尾(line 293-294)才把收集列加回 _collectedReels/_releaseReels，
            //   而每个 respin 回合末 SpinHoldRound 会 _releaseReels.Clear()(line 289)。两者存在时序竞争，
            //   若回合末 Clear 跑在 CollectFullReelAnimation 收尾之后，该 reel 会从 _releaseReels 被抹掉，
            //   导致新基础局里该列火球 ghost 不随卷轴滚走、盖住转动的 Q（表现为"某列没转"）。
            //   改为按 _fbOverlays 实际残留兜底，保证任何残留 ghost 都被释放。
            //   ※ Mini 持久 overlay(m_persistentFireOverlays=true) 不参与此释放逻辑，必须跳过，否则会误把 Mini 火球当待释放滚走。
            if (!m_persistentFireOverlays)
            {
                foreach (var go in _fbOverlays)
                {
                    if (go == null) continue;
                    if (ParseReelRow(go.name, out int reel, out _)) _releaseReels.Add(reel);
                }
            }
            _collectedReels.Clear();
        }

        public void ReleaseCollectedReel(int reel)
        {
            if (_collectedReels == null) return;
            if (!_collectedReels.Remove(reel)) return;
            // ★ 满列收集后计数器不再中途隐藏（按用户要求一直显示到开新局），仅从已收集集合移除
        }
    }
}
