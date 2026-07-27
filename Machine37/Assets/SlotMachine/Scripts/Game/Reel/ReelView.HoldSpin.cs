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

            // ★ 本轮新落火球并入 _baseFireMults，使滚动中(SetCell)能按 kind 显示免费火球外观(freeFire)，与基础旋转 ShowGrid 行为一致。
            //   （GameManager 不可直接访问此私有字段，故在此(ReelView 内)并入；ApplyRespinStep 停稳后也会再写一遍，幂等。）
            if (newFireMults != null)
                foreach (var kv in newFireMults) _baseFireMults[kv.Key] = kv.Value;

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
            // ★ 确定性减速 tween 状态：进入减速时记录起点/时间/目标/时长，之后按归一化进度插值，
            //   保证精确落点、绝不 snap 跳格（根治"不按暂停自然停跳 1~4 格"）。
            var decelStartOffset = new Dictionary<int, float>();
            var decelStartTime = new Dictionary<int, float>();
            var decelTarget = new Dictionary<int, int>();
            var decelDur = new Dictionary<int, float>();

            // 干净循环：残留火球替换成普通符号，建成 respinGrid 周期循环带（火球格用普通符占位，由 overlay 显示）。
            // ★ 关键修复(自然停跳 1-2 格)：displayStrip 长度必须是 rows 的整数倍，否则卷轴滚过条带末端折返处
            //   会出现 (stripLen%rows) 格的逻辑错位（base 旋转靠 finalSyms 落地规避，Hold 只用周期带 → 必现）。
            //   此处把条带补齐到 rows 整数倍（按周期公式续写占位符），整圈无缝。
            foreach (int reel in spunReels)
            {
                if (reel < 0 || reel >= _reels.Count) continue;
                var st = _reels[reel];
                int oldLen = (st.displayStrip != null) ? st.displayStrip.Count : 0;
                if (oldLen <= 0) continue;
                // ★ 方案A：displayStrip 建成 respinGrid 的周期循环带（对齐基础局「整条带即结果」）。
                //   周期 = 该列行数(rows)，逻辑行 row 对应索引 (stripBase + m_buf + row)。
                //   ★ 本轮「新落」火球(respinGrid=12 且属 newFireMults)保留为真实条带符号(id12)随卷轴滚入；
                //     历史已锁定火球(respinGrid=12 但非本轮新落)用普通符占位，由已存在的 pinned overlay 显示、保持锁定不动。
                //   任意 basePos 显示均为 respinGrid 周期序列 → 急停就近不突变（与基础局同构）。
                int rowsN = st.rows;
                int newLen = Mathf.CeilToInt(oldLen / (float)rowsN) * rowsN;   // ★ 补齐到 rows 整数倍（无缝循环）
                var newStrip = new List<int>(newLen);
                for (int i = 0; i < newLen; i++)
                {
                    int row2 = (((i - st.stripBase - m_buf) % rowsN) + rowsN) % rowsN;
                    int sym2 = (respinGrid != null && reel < respinGrid.Length && respinGrid[reel] != null && row2 < respinGrid[reel].Length)
                        ? respinGrid[reel][row2] : 0;
                    // ★ 用户要求"火球像普通ICON一样滚进来"：本轮「新落」火球(respinGrid=12 且属于 step.newFireballs)保留为真实条带符号(id12)，
                    //   随卷轴自然滚入；停稳后由 ApplyRespinStep 在 m_fireNode 顶层生成锁定 overlay（固定不滚、压最上）。
                    //   非本轮新落(历史已锁定)火球 → 仍用普通符占位(由已存在的 pinned overlay 显示，保持锁定不动)。
                    bool isNewFireball = (sym2 == m_fireballSymbolId) && (newFireMults != null) && newFireMults.ContainsKey(reel * 100 + row2);
                    newStrip.Add(isNewFireball ? m_fireballSymbolId : ((sym2 <= 0 || sym2 == m_fireballSymbolId) ? RandNormalSymbol() : sym2));
                }
                st.displayStrip = newStrip;
            }

            // 初始：displayStrip 已是 respinGrid 周期循环带（行108-115 初始化写入），任意 basePos 显示均为 respinGrid 周期序列，
            //   故无需 PlaceRespinResult 重写落点（重写会引入 landOffset 相位错位 → 普通符变百搭/火球错位）。火球由 overlay 显示。
            // （PlaceRespinResult 已删除：方案A 周期带直接驱动，重写反成突变源。）

            FireballCell FindFireballCell(int reel, int k, int symIdx)
            {
                // ★ 关键修复(v2)：火球在条带(band)里的"逻辑行"由 band 索引 symIdx 决定（与滚动 offset 无关），
                //   必须用 symIdx 推逻辑行去 newFireMults 查倍数——之前用 (k - m_buf) 是"视图行"，
                //   只有停稳(offset≡0 mod rows)那一瞬视图行才等于 band 逻辑行，故滚动中途查不到 → "倍数停下才出"。
                //   改用 symIdx 推逻辑行：火球滚过的每个格都命中对应倍数 → 倍数随火球一起滚入（与正常局一致）。
                var rstate = (reel >= 0 && reel < _reels.Count) ? _reels[reel] : null;
                int rowsN = (rstate != null) ? rstate.rows : 5;
                int sb = (rstate != null) ? rstate.stripBase : 0;
                int row2 = ((symIdx - sb - m_buf) % rowsN + rowsN) % rowsN;
                int mkey = reel * 100 + row2;
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
                        // ★ 减速段：确定性 tween（ease-out quad），保证精确落点、绝不 snap 跳格。
                        //   首次进入减速时记录起点/时间/目标/时长，之后按 (t-startTime)/dur 缓出到目标；
                        //   自然停与急停统一走此路径，彻底消除"不按暂停自然停跳 1~4 格"（旧版 ease-out 渐近 + maxStep 限幅，
                        //   在条带末端/折返处可能未在 endTime 前收敛，settle 直接 snap offset=scrollCells 造成跳位）。
                        if (!decelStartOffset.ContainsKey(reel))
                        {
                            int cur = Mathf.FloorToInt(offset[reel]);
                            int tgt;
                            float dd;
                            if (_holdStopRequested)
                            {
                                // 急停：前方就近窗口起点(行数倍数)，相对按停位置总是前进、不回退。
                                tgt = st.rows * Mathf.CeilToInt(offset[reel] / (float)st.rows);
                                dd = 0.45f;
                            }
                            else
                            {
                                // 自然停：从当前位置向前取最近窗口起点(≡0 mod rows)，与基础局 BeginStop 自然停一致。
                                int extra = 3 + RandInt(0, 5);
                                int window = st.rows * Mathf.CeilToInt((cur + extra) / (float)st.rows);
                                if (window <= cur) window += st.rows;   // 兜底：严格在前方
                                tgt = window;
                                dd = 0.6f;
                            }
                            decelStartOffset[reel] = offset[reel];
                            decelStartTime[reel] = t;
                            decelTarget[reel] = tgt;
                            decelDur[reel] = dd;
                            scrollCells[reel] = tgt;   // 落点固定，settle 直接采用，无跳格
                            float need = t + dd + 0.15f;   // ★ 延长 endTime 兜底，确保该列 tween 完成前循环不退出
                            if (need > endTime) endTime = need;
                        }
                        float p = Mathf.Clamp01((t - decelStartTime[reel]) / decelDur[reel]);
                        float e = 1f - (1f - p) * (1f - p);   // ease-out quad
                        offset[reel] = decelStartOffset[reel] + (decelTarget[reel] - decelStartOffset[reel]) * e;
                        if (p >= 1f)
                        {
                            offset[reel] = decelTarget[reel];
                            if (!stoppedReels.Contains(reel))
                            {
                                stoppedReels.Add(reel);
                                if (FMODSoundMgr.Instance != null)
                                    FMODSoundMgr.Instance.PlaySound("event:/Sounds/1");
                            }
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
                        // ★ Hold 模式百搭不在此拦截：respinGrid 已由生成层保证不在 reel0/顶行放百搭，
                        //   故百搭按 respinGrid 原样显示（滚动中/停后一致，不再随 offset 闪烁/突现）。
                        //   滚动/定格的拦截兜底统一交由 SetCell（_holdSpinning 期间跳过，避免屏幕行误拦）。
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
                // ★ 自然停未收敛完（endTime 到但仍有列在滚）→ 延长循环让卷轴平滑收敛到 scrollCells，
                //   否则 while 退出后 settle 段直接 snap offset=scrollCells，造成 1~2 格跳位、整列符号瞬间重排
                //   （"普通图标突然变百搭"等观感）。最多续 2.5s 兜底，仍不收敛才允许 snap（极端兜底，正常不会到）。
                if (t >= endTime)
                {
                    if (t < endTime + 2.5f) endTime = t + 0.5f;
                    else break;
                }

                // ★ 火球 overlay 随卷轴滚动（与循环带同公式，停稳精确归位）；释放列交给 MoveReleasingOverlays 滚走销毁。
                MoveReleasingOverlays(offset);
                TrackFireballOverlays(offset);

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
                    var rt = st.cells[k].transform as RectTransform;
                    if (rt != null) rt.anchoredPosition = new Vector2(0f, RowToY(k - m_buf));

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
                    // ★ 用户原则：Hold 期间所有 ICON（普通 + 特殊/百搭/火球）只在开始(band-build 用 respinGrid 决定)决定，
                    //   定格段不再做任何符号替换。sym 直接来自 displayStrip(=respinGrid 周期带)，原样写入——
                    //   与滚动循环(已不拦截百搭)完全一致，根除"滚动是百搭、停下变普通ICON"的"中途修改"。
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
                // ★ 本轮新落火球：滚动时已作为真实条带符号(id12)随卷轴滚入并停稳；此处(停稳后)在 m_fireNode 顶层生成锁定 overlay
                //   —— 固定不滚、压最上层，供后续轮次保持锁定 + 结算/特效/满列统计查询(HasFireballOverlay/CountFireballsInColumn)。
                //   （不再在 AdvanceHoldSpin 滚动「前」预创建 → 避免"一开局就出现、没滚动进来"的突兀感，符合用户要求。）
                foreach (var c in step.newFireballs)
                {
                    _baseFireMults[c.reel * 100 + c.row] = c;
                    ShowFireballOverlay(c.reel, c.row, c, playSound: true);
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
