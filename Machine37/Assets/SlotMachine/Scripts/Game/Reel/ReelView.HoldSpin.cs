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

            RefreshColumnEffects(s, s.counter);   // 近满列(差1火球)→亮整列 m_effect
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
            float endTimeInitial = endTime;   // ★ 用于极端兜底上限判断（见下方循环尾）

            var scrollCells = new Dictionary<int, int>();   // 落点由减速段进入减速时按当前 offset 向前取窗口起点写入（见下方 decelStartOffset 分支），无需预预测

            _fbStripMult.Clear();   // ★ 每轮 respin 重建「条带位置→倍率」映射（实例字段，基础旋转同用）；避免上一轮残留导致倍率串台
            // ★ 确定性减速 tween 状态：进入减速时记录起点/时间/目标/时长，之后按归一化进度插值，
            //   保证精确落点、绝不 snap 跳格（根治"不按暂停自然停跳 1~4 格"）。
            var decelStartOffset = new Dictionary<int, float>();
            var decelStartTime = new Dictionary<int, float>();
            var decelTarget = new Dictionary<int, int>();
            var decelDur = new Dictionary<int, float>();

            // ★ feed 带：displayStrip 改为真实独立 reel 条带（m_reelStrips 拼接，见下方循环），不再是 respinGrid 周期循环带。
            //   火球(12)保留随真实条带滚入；落点窗口由 decel 分支按 respinGrid 强制写入（保证结果正确）。
            //   条带拼足够长（≥120），避免落点窗口折返重叠；不再要求长度为 rows 整数倍（沿条带取模即可）。
            foreach (int reel in spunReels)
            {
                if (reel < 0 || reel >= _reels.Count) continue;
                var st = _reels[reel];
                int rowsN = st.rows;
                // ★ feed 带：用真实独立 reel 条带（m_reelStrips）滚动，不再用 respinGrid 周期循环带（消除 loop 感）。
                //   重复拼接到足够长度，保证减速落点窗口在条带范围内、不折返重叠（真实卷轴本就是条带循环）。
                //   火球(12)由下方 decel 分支在落点窗口按 respinGrid 强制写入，随真实条带自然滚入；历史已锁定火球由 pinned overlay 盖住。
                var src = (m_reelStrips != null && reel < m_reelStrips.Count) ? m_reelStrips[reel] : null;
                if (src == null || src.Count <= 0)
                {
                    // 兜底：无条带数据则退回 respinGrid 周期循环带（保正确性，不抛错）
                    int newLen = Mathf.Max(rowsN * 4, (st.displayStrip != null ? st.displayStrip.Count : rowsN * 4));
                    newLen = Mathf.CeilToInt(newLen / (float)rowsN) * rowsN;
                    var fb = new List<int>(newLen);
                    for (int i = 0; i < newLen; i++)
                    {
                        int row2 = (((i - st.stripBase - m_buf) % rowsN) + rowsN) % rowsN;
                        int sym2 = (respinGrid != null && reel < respinGrid.Length && respinGrid[reel] != null && row2 < respinGrid[reel].Length) ? respinGrid[reel][row2] : 0;
                        bool isNewFireball = (sym2 == m_fireballSymbolId) && (newFireMults != null) && newFireMults.ContainsKey(reel * 100 + row2);
                        if (isNewFireball && newFireMults != null)
                        {
                            var cell = newFireMults[reel * 100 + row2];
                            if (cell != null) _fbStripMult[reel * 100000 + i] = cell;
                        }
                        fb.Add(isNewFireball ? m_fireballSymbolId : ((sym2 <= 0 || sym2 == m_fireballSymbolId) ? RandNormalSymbol() : sym2));
                    }
                    st.displayStrip = fb;
                    continue;
                }
                int minLen = Mathf.Max(src.Count * 3, 120);   // ★ 拼足够长，落点窗口不折返重叠
                var newStrip = new List<int>(minLen);
                for (int i = 0; i < minLen; i++)
                {
                    int s = src[i % src.Count];
                    if (s == 0) s = RandSymbol();                 // 空格→稳定替身（与基础旋转 BuildDisplayStrip 一致）
                    if (s == m_wildId && reel == 0) s = m_symbolMin + (i % (m_symbolMax - m_symbolMin)); // reel0 过滤 Wild
                    if (s == m_fireballSymbolId) s = RandNormalSymbol();   // ★ 剔除卷轴条带自带的"假火球"(无倍率数据)，避免滚动中显示无倍率火球
                    if (s > m_symbolMax) s = RandNormalSymbol();           // ★ Hold 滚动带不含 Scatter(=11)，与 respin 符号池一致
                    newStrip.Add(s);
                }
                // ★ 注入真实火球（来自 respinGrid/newFireMults）：使其在滚动过程中即带倍率/彩金档显示（与基础旋转一致），
                //   并随真实条带滚入。落点窗口在 decel 段仍按 respinGrid 强制写入（保证最终停稳结果正确）。
                //   每个真实火球在条带上占一个确定位置并写入 _fbStripMult，滚动中 FindFireballCell 即可按条带索引查到倍率→SetCellFireballMult 设 ReelItem.m_type/m_rate+文本。
                if (newFireMults != null && respinGrid != null && reel < respinGrid.Length)
                {
                    int placed = 0;
                    foreach (var kv in newFireMults)
                    {
                        int r = kv.Key / 100;
                        int logicalRow = kv.Key % 100;
                        if (r != reel) continue;
                        var cell = kv.Value;
                        if (cell == null) continue;
                        int p = (logicalRow * 23 + placed * 37 + 11) % newStrip.Count;  // 确定性分散落点（条带长≥120，必经过可见区）
                        newStrip[p] = m_fireballSymbolId;
                        _fbStripMult[reel * 100000 + p] = cell;
                        placed++;
                    }
                }
                st.displayStrip = newStrip;
            }

            // ★ feed 带：displayStrip 现为真实独立 reel 条带（m_reelStrips 拼接），任意 basePos 显示均为真实条带序列（非 respinGrid 周期），
            //   故滚动有真实"顶部进新符、停哪算哪"的 feed 带手感。落点窗口由 decel 分支按 respinGrid 强制写入（行207附近），
            //   保证最终停稳窗口严格等于 respinGrid、结果正确；火球由 overlay 显示（pinned，不随条带滚）。

            FireballCell FindFireballCell(int reel, int k, int symIdx)
            {
                // ★ feed 带下倍率查询：优先按「条带索引→倍率」直接映射（_fbStripMult，decel 落点窗口已填充），
                //   不依赖 symIdx→逻辑行反推（该公式仅对 stripLen%rowsN==0 的列准确；modeB reel4 为 8 行、stripLen=120 不可整除会错位）。
                FireballCell cell = null;
                if (_fbStripMult.TryGetValue(reel * 100000 + symIdx, out cell)) return cell;
                // 兜底：逻辑行反推（仅对 stripLen%rowsN==0 的列准确）
                var rstate = (reel >= 0 && reel < _reels.Count) ? _reels[reel] : null;
                int rowsN = (rstate != null) ? rstate.rows : 5;
                int sb = (rstate != null) ? rstate.stripBase : 0;
                int row2 = ((symIdx - sb - m_buf) % rowsN + rowsN) % rowsN;
                int mkey = reel * 100 + row2;
                if (newFireMults != null && newFireMults.TryGetValue(mkey, out cell)) return cell;
                return null;
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
                        // 匀速推进：进入减速前保持全速 m_baseSpeed，与减速段初速度严格连续，
                        // 避免"先被降到近0速、再突然加速前冲"造成的自然停向前跳格观感。
                        offset[reel] += m_baseSpeed * dt;
                    }
                    else
                    {
                        // ★ 减速段：匀减速（恒定减速度，初速度=v0=m_baseSpeed、末速度=0），速度连续、落点精确、绝无跳格。
                        //   旧版 ease-out quad 在起点处速度最大(=2*dist/dur)，且进入减速前又把速度降到近0，
                        //   形成"先停一下→猛地前冲→再减速"的观感（用户描述的"自然停向前跳 3~4 格"）。
                        //   现 offset = start + v0*τ - ½(v0/dd)τ²，dd=2*dist/v0 反解保证恰好停在 tgt 且速度连续。
                        if (!decelStartOffset.ContainsKey(reel))
                        {
                            int cur = Mathf.FloorToInt(offset[reel]);
                            int tgt;
                            if (_holdStopRequested)
                            {
                                // 急停：前方就近窗口起点(≡0 mod rows)，相对按停位置总是前进、不回退。
                                tgt = st.rows * Mathf.CeilToInt(offset[reel] / (float)st.rows);
                                if (tgt <= Mathf.FloorToInt(offset[reel])) tgt += st.rows;
                            }
                            else
                            {
                                // 自然停：从当前位置向前取最近窗口起点(≡0 mod rows)，与基础局 BeginStop 自然停一致。
                                int extra = 3 + RandInt(0, 5);
                                int window = st.rows * Mathf.CeilToInt((cur + extra) / (float)st.rows);
                                if (window <= cur) window += st.rows;   // 兜底：严格在前方
                                tgt = window;
                            }
                            float dist = tgt - offset[reel];            // 必为正（前方窗口）
                            float decelTime = (m_baseSpeed > 1f) ? (2f * dist / m_baseSpeed) : 0.6f;
                            decelStartOffset[reel] = offset[reel];
                            decelStartTime[reel] = t;
                            decelTarget[reel] = tgt;
                            decelDur[reel] = decelTime;
                            scrollCells[reel] = tgt;   // 落点固定，settle 直接采用，无跳格
                            // ★ feed 带：在落点窗口强制写入 respinGrid，保证最终停稳窗口严格等于 respinGrid（结果正确，与 base 旋转 finalSyms 对齐同理）。
                            //   历史已锁定火球(respinGrid=12 但非本轮新落)用普通符占位，由 pinned overlay 盖住（与旧周期带一致，避免收集后底层露火球图）。
                            if (respinGrid != null && reel < respinGrid.Length && respinGrid[reel] != null)
                            {
                                int sl = (st.displayStrip != null) ? st.displayStrip.Count : 0;
                                if (sl > 0)
                                {
                                    for (int k = m_buf; k < m_buf + st.rows && k < st.cells.Count; k++)
                                    {
                                        int logicalRow = k - m_buf;
                                        if (logicalRow >= respinGrid[reel].Length) break;
                                        int sym = respinGrid[reel][logicalRow];
                                        bool isNewFireball = (sym == m_fireballSymbolId) && (newFireMults != null) && newFireMults.ContainsKey(reel * 100 + logicalRow);
                                        if (sym == m_fireballSymbolId && !isNewFireball) sym = RandNormalSymbol();   // 历史火球：条带用普通符，overlay 盖住
                                        int idx = (st.stripBase + tgt + k) % sl;
                                        if (idx < 0) idx += sl;
                                        st.displayStrip[idx] = sym;
                                        if (isNewFireball && newFireMults != null)
                                        {
                                            var cell = newFireMults[reel * 100 + logicalRow];
                                            if (cell != null) _fbStripMult[reel * 100000 + idx] = cell;   // ★ feed 带下倍率按条带索引直接映射（FindFireballCell 优先查此）
                                        }
                                    }
                                }
                            }
                            float need = t + decelTime + 0.15f;   // ★ 延长 endTime 兜底，确保该列减速完成前循环不退出
                            if (need > endTime) endTime = need;
                        }
                        float v0 = m_baseSpeed;
                        float dd = decelDur[reel];
                        float tau = t - decelStartTime[reel];
                        if (v0 > 1f)
                        {
                            // 匀减速：速度由 v0 线性降到 0，偏移量精确收敛到 decelTarget（无 overshoot/回退）。
                            offset[reel] = decelStartOffset[reel] + v0 * tau - 0.5f * (v0 / dd) * tau * tau;
                            if (tau >= dd || offset[reel] >= decelTarget[reel]) offset[reel] = decelTarget[reel];
                        }
                        else
                        {
                            // 极端兜底（m_baseSpeed 过低）：用 ease-out 收敛，避免除零/异常。
                            float p = Mathf.Clamp01(tau / dd);
                            float e = 1f - (1f - p) * (1f - p);
                            offset[reel] = decelStartOffset[reel] + (decelTarget[reel] - decelStartOffset[reel]) * e;
                        }
                        if (offset[reel] >= decelTarget[reel] - 1e-4f && !stoppedReels.Contains(reel))
                        {
                            stoppedReels.Add(reel);
                            if (FMODSoundMgr.Instance != null)
                                FMODSoundMgr.Instance.PlaySound("event:/Sounds/1");
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
                    // ★ 未收敛完 → 继续平滑滚动直到全列停稳(allStopped)，绝不 snap（根治自然停跳格）。
                    //   仅保留极端兜底：超过初始 endTime + 6s 仍不收敛才强制退出（tween 0.6s 必完成，正常永不触发）。
                    if (t > endTimeInitial + 6f) break;
                    endTime = t + 0.5f;
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
                // ★ 落点归正：优先用减速分支精确整数目标 decelTarget（tween 已完成，offset 应已等于此值），
                //   防御浮点 floor 误差导致的 frac≈1 视觉跳格；否则 fallback scrollCells。
                if (decelTarget.ContainsKey(reel)) offset[reel] = decelTarget[reel];
                else if (scrollCells.ContainsKey(reel)) offset[reel] = scrollCells[reel];
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
                    //   定格段不再做任何符号替换。sym 直接来自 displayStrip（落点窗口已由 decel 分支按 respinGrid 强制写入），原样写入——
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

            RefreshColumnEffects(state, step.counters);   // 近满列(差1火球)→亮整列 m_effect
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
