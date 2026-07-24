using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    /// <summary>ReelView 火球 Hold &amp; Spin 核心流程：ShowFeatureState、SpinHoldRound、ApplyRespinStep、LimitWildsOnBoard、Release列。</summary>
    public partial class ReelView
    {
        List<GameObject> _fbOverlays = new List<GameObject>();
        HashSet<int> _releaseReels = new HashSet<int>();
        HashSet<int> _collectedReels = new HashSet<int>();
        bool _wasSpinning = false;

        // ===== 进入 Hold&Spin =====

        public virtual void ShowFeatureState(HoldSpinState s)
        {
            HideAllCounters();
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
                    SetRespinCounterRow(r, (s.released != null && r < s.released.Length && s.released[r]) ? -1 : s.counter[r]);

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
                scrollCells[reel] = PredictScrollCells(stopAt[reel], settleTime);
            }

            var fbStripMult = new Dictionary<int, FireballCell>();

            foreach (int reel in spunReels)
            {
                if (reel < 0 || reel >= _reels.Count) continue;
                var st = _reels[reel];
                int stripLen = (st.displayStrip != null) ? st.displayStrip.Count : 0;
                if (stripLen <= 0) continue;
                for (int i = 0; i < stripLen; i++)
                    if (st.displayStrip[i] == m_fireballSymbolId) st.displayStrip[i] = RandNormalSymbol();
            }

            if (newFireMults != null && newFireMults.Count > 0)
            {
                var fireByReel = new Dictionary<int, List<KeyValuePair<int, FireballCell>>>();
                foreach (var kv in newFireMults)
                {
                    int rk = kv.Key / 100;
                    int row = kv.Key % 100;
                    if (!fireByReel.ContainsKey(rk)) fireByReel[rk] = new List<KeyValuePair<int, FireballCell>>();
                    fireByReel[rk].Add(new KeyValuePair<int, FireballCell>(row, kv.Value));
                }
                foreach (var kvp in fireByReel)
                {
                    int reel = kvp.Key;
                    if (reel < 0 || reel >= _reels.Count) continue;
                    if (!scrollCells.ContainsKey(reel)) continue;
                    var st = _reels[reel];
                    int stripLen = (st.displayStrip != null) ? st.displayStrip.Count : 0;
                    if (stripLen <= 0) continue;
                    int B = scrollCells[reel];
                    foreach (var fr in kvp.Value)
                    {
                        int row = fr.Key;
                        FireballCell cell = fr.Value;
                        int fbIdx = ((st.stripBase + B + m_buf + row) % stripLen + stripLen) % stripLen;
                        st.displayStrip[fbIdx] = m_fireballSymbolId;
                        fbStripMult[reel * 100000 + fbIdx] = cell;
                    }
                }
            }

            if (respinGrid != null)
            {
                foreach (int reel in spunReels)
                {
                    if (reel < 0 || reel >= _reels.Count) continue;
                    if (reel >= respinGrid.Length || respinGrid[reel] == null) continue;
                    var st2 = _reels[reel];
                    int sl2 = (st2.displayStrip != null) ? st2.displayStrip.Count : 0;
                    if (sl2 <= 0) continue;
                    int B2 = scrollCells[reel];
                    for (int row2 = 0; row2 < respinGrid[reel].Length; row2++)
                    {
                        int sym2 = respinGrid[reel][row2];
                        if (sym2 <= 0) continue;
                        if (sym2 == m_fireballSymbolId) continue;
                        int landIdx2 = ((st2.stripBase + B2 + m_buf + row2) % sl2 + sl2) % sl2;
                        st2.displayStrip[landIdx2] = sym2;
                    }
                }
            }

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

            int landWild = 0;
            float t = 0f;
            var stoppedReels = new HashSet<int>();
            while (t < endTime)
            {
                t += Time.deltaTime;
                float dt = Time.deltaTime;

                foreach (int reel in spunReels)
                {
                    if (reel < 0 || reel >= _reels.Count) continue;
                    var st = _reels[reel];
                    int stripLen = (st.displayStrip != null) ? st.displayStrip.Count : 0;
                    if (stripLen <= 0) continue;

                    if (t < stopAt[reel])
                    {
                        float remaining = stopAt[reel] - t;
                        float spd = (remaining < 0.35f) ? m_baseSpeed * Mathf.Clamp01(remaining / 0.35f) : m_baseSpeed;
                        offset[reel] += spd * dt;
                    }
                    else
                    {
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
                        else offset[reel] += diff * Mathf.Clamp01(dt * m_normalDecel);
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
                        if (sym == m_symbolMax && (reel == 0 || (k - m_buf) == st.rows - 1))
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
                                bool freeFire = m_inFreeSpins || cell.kind == FireballKind.FreeSpins;
                                var it = st.cellItems[k];
                                if (it != null) it.ShowFire(true, freeFire);
                            }
                        }
                    }
                }

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

                    int sym;
                    if (stripLen > 0)
                    {
                        int symIdx = (topIdx + k) % stripLen;
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
                    else if (sym == m_symbolMax)
                    {
                        if (reel == 0 || row == st.rows - 1) sym = RandNormalSymbol();
                        else { landWild++; if (landWild > 1) sym = RandNormalSymbol(); }
                    }
                    SetCell(st, k, sym);
                    if (sym == m_fireballSymbolId)
                    {
                        var mult = FindFireballCell(reel, k, ((stripLen > 0) ? ((topIdx + k) % stripLen) : 0));
                        if (mult != null)
                        {
                            bool freeFire = m_inFreeSpins || mult.kind == FireballKind.FreeSpins;
                            var it = st.cellItems[k];
                            if (it != null) it.ShowFire(true, freeFire);
                        }
                    }
                }
            }

            DestroyReleasingOverlays();

            _releaseReels.Clear();
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

            if (step.newFireballs != null)
            {
                foreach (var c in step.newFireballs)
                {
                    ShowFireballOverlay(c.reel, c.row, c);
                    _baseFireMults[c.reel * 100 + c.row] = c;
                }
            }

            if (step.counters != null)
            {
                for (int reel = 0; reel < step.counters.Length; reel++)
                {
                    // 已释放/集满的列：传 -1 哨兵隐藏计数器；其余按数据层 counter 显示（0..rc）
                    bool released = (state != null && reel < state.released.Length && state.released[reel]);
                    SetRespinCounterRow(reel, released ? -1 : step.counters[reel]);
                }
            }

            RefreshColumnEffects(state, step.counters);   // 近满列(差1火球)→亮整列 m_effect；已释放/集满列不亮

            {
                int cnt0 = 0;
                foreach (var o in _fbOverlays) if (o != null && o.name.StartsWith("FBOverlay_0_")) cnt0++;
                Debug.Log($"[ApplyRespinStep] 后 reel0 火球overlay数={cnt0}（本轮新火球={((step.newFireballs!=null)?step.newFireballs.Count:0)}）");
            }
        }

        public void LimitWildsOnBoard()
        {
            int wildId = m_symbolMax;
            var wilds = new List<ReelState>();
            var wildKs = new List<int>();
            for (int ri = 0; ri < _reels.Count; ri++)
            {
                var st = _reels[ri];
                int rows = st.rows;
                for (int row = 0; row < rows; row++)
                {
                    int k = m_buf + row;
                    if (k < 0 || k >= st.shownSym.Length) continue;
                    if (st.shownSym[k] != wildId) continue;
                    if (ri == 0) { SetCell(st, k, RandNormalSymbol()); continue; }
                    wilds.Add(st); wildKs.Add(k);
                }
            }
            if (wilds.Count <= 1) return;
            for (int i = 1; i < wilds.Count; i++)
                SetCell(wilds[i], wildKs[i], RandNormalSymbol());
        }

        // ===== 释放列 =====

        public void ReleaseReel(int reel)
        {
            if (reel < 0) return;
            _releaseReels.Add(reel);
            // 火球随本轮回滚回归队列：该列已释放，传 -1 哨兵彻底隐藏 ReelFireNum
            SetRespinCounterRow(reel, -1);
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
