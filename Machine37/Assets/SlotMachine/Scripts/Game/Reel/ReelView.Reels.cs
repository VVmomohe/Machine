using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;   // FireballCell / HoldSpinState 等

namespace com.slot
{
    /// <summary>ReelView 卷轴滚动部分：静态棋盘 / 启动滚动 / 每帧驱动 / 停轮 / 布局 / 定格 / 急停。</summary>
    public partial class ReelView
    {
        // ===== 初始静态棋盘 =====

        void InitStaticGrid()
        {
            if (m_node == null || m_node.Length == 0)
            {
                Debug.LogWarning("[ReelView] m_node 未设置，无法生成棋盘");
                return;
            }
            ClearAll();
            int n = Mathf.Min(m_node.Length, m_reelRows.Count > 0 ? m_reelRows.Count : 5);
            for (int reel = 0; reel < n; reel++)
            {
                int rows = (reel < m_reelRows.Count && m_reelRows[reel] > 0) ? m_reelRows[reel] : 4;
                var parent = m_node[reel].transform;
                for (int row = 0; row < rows; row++)
                {
                    var cell = CreateCell(parent, RandSymbol(), row);
                    _staticCells.Add(cell);
                }
            }
            Debug.Log($"[ReelView] 初始棋盘已生成：{n} 列，行数=[{string.Join(",", m_reelRows)}]");
        }

        // ===== Spin：启动卷轴 loop 滚动 =====

        /// <summary>Spin 结果回来后调用：用网格结果启动每列的卷轴 loop 滚动，并安排自动 waterfall 停轮。
        /// fireballMults: 基础旋转落了火球时，从 holdSpinState 提取的 reel*100+row → 倍率 字典，
        /// 让滚动/减速阶段就显示倍率（不再等到停稳后 ShowFeatureState 才出现）。</summary>
        public virtual void ShowGrid(int[][] grid, Dictionary<int, FireballCell> fireballMults = null)
        {
            if (m_node == null || m_node.Length == 0) return;
            ReleaseCollectedForNextSpin();   // 特性结束→基础局：把已收集满列的 80% 幽灵并入待释放列，随本局卷轴滚走
                                             //   此前放在"按确认"处但生效路径被绕过；改在 ShowGrid 入口——
                                             //   新基础局卷轴一开始转、火球 ghost 随之下滚，计数器恰好同步消失。
            ClearAll();
            _baseFireMults = fireballMults ?? new Dictionary<int, FireballCell>();
            int n = Mathf.Min(m_node.Length, grid.Length);
            for (int reel = 0; reel < n; reel++)
            {
                int rows = (reel < m_reelRows.Count && m_reelRows[reel] > 0) ? m_reelRows[reel] : grid[reel].Length;
                var st = new ReelState
                {
                    reelIdx = reel,
                    rows = rows,
                    pos = 0,
                    speed = m_baseSpeed,
                    stripBase = (m_reelStrips != null && reel < m_reelStrips.Count && m_reelStrips[reel].Count > 0)
                                    ? RandInt(0, m_reelStrips[reel].Count) : 0,
                    finalSyms = grid[reel],
                    spinning = true,
                    stopping = false,
                    autoStop = true,
                    stopTimer = m_minSpinTime + reel * m_autoStagger,   // 先保证最短滚动，再 waterfall 依次停
                    decel = m_normalDecel,
                };
                // 预生成显示条带：火球(默认8)/空格一次性替换为稳定替身，滚动时直接按此取，
                // 避免旧逻辑每帧调用 RandSymbol() 推进 RNG 导致该格图标在 6~11 间来回跳(闪烁)。
                st.displayStrip = BuildDisplayStrip(reel);
                var go = new GameObject($"Reel{reel}");
                go.transform.SetParent(m_node[reel].transform, false);
                go.transform.SetAsFirstSibling();  // 卷轴容器在底层，火球 overlay（SetAsLastSibling）始终在上
                st.container = go;

                int count = rows + 2 * m_buf;           // 可见行 + 上下缓冲
                st.cellImgs = new List<Image>(count);
                st.cellItems = new List<ReelItem>(count);
                st.shownSym = new int[count];
                for (int k = 0; k < count; k++)
                {
                    // ★ m_id 改为「创建前就决定」：该格(k)对应的静止行号 rowForK=k-m_buf，
                    //   直接用最终数据网格 grid[reel][rowForK] 当创建符号（顶/底缓冲延伸首/尾符号，
                    //   与 LayoutFinalReel 的 edgeSym 规则一致），保证停稳后 m_id==屏幕图标。
                    //   不再传 RandSymbol()（旧逻辑传随机占位值 → m_id 永远停在 2/6/4 这类垃圾值，
                    //   与图标脱钩，导致"图上鱼、ID 2/6/4"的误读）。
                    int rowForK = k - m_buf;
                    int createId;
                    if (rowForK >= 0 && rowForK < rows) createId = grid[reel][rowForK];
                    else if (rowForK < 0) createId = grid[reel][0];            // 顶缓冲：延伸首行
                    else createId = grid[reel][rows - 1];                      // 底缓冲：延伸尾行
                    var cell = CreateCell(go.transform, createId, 0);
                    st.cells.Add(cell);
                    st.cellImgs.Add(cell.GetComponent<Image>());      // 无 prefab 时的回退
                    st.cellItems.Add(cell.GetComponent<ReelItem>());  // prefab 上的 ReelItem（m_image/m_text）
                    st.shownSym[k] = -1;
                }
                _reels.Add(st);
            }
            // ★ 新基础局：重建卷轴后强制关闭所有列预警特效（prefab 默认 m_effect 可能 active，
            //   且上局 Hold&Spin 残留状态不应带入基础局），防止"一列没火球但 m_effect 仍亮"。
            ClearAllColumnEffects();
        }

        void UpdateReel(ReelState st, float dt)
        {
            if (!st.spinning && !st.stopping) return;

            if (st.stopping)
            {
                // 指数 ease-out 收敛到目标整数格
                float remain = st.stopAt - st.pos;
                if (Mathf.Abs(remain) < 0.02f)
                {
                    // 收敛完成：最终定格（此时应该已经显示 finalSyms，无跳变）
                    st.pos = st.stopAt;
                    st.stopping = false;
                    st.spinning = false;
                    SnapFinal(st);
                    // 当前列滚动停后：播放 event:/Sounds/1
                    if (FMODSoundMgr.Instance != null)
                        FMODSoundMgr.Instance.PlaySound("event:/Sounds/1");
                }
                else
                {
                    st.pos += remain * Mathf.Clamp01(dt * st.decel);

                    // ★ 关键修复：当剩余距离 < 2 格时，切换到 finalSyms 布局，
                    // 避免 displayStrip 滚动位置与最终结果不一致导致的"跳帧"
                    if (Mathf.Abs(remain) < 2f)
                    {
                        LayoutFinalReel(st, remain);
                        return;  // 不走下面的普通 LayoutReel
                    }
                }
            }
            else // st.spinning && !st.stopping
            {
                if (st.autoStop)
                {
                    st.stopTimer -= dt;
                    if (st.stopTimer <= 0f) BeginStop(st, quick: false);
                }
                // 若本帧 BeginStop 已触发，则不再匀速推进（交给收敛分支）
                if (!st.stopping) st.pos += st.speed * dt;
            }
            // 已停稳（spinning=false）后不再用 LayoutReel 覆盖 SnapFinal 的结果
            if (!st.spinning) return;
            LayoutReel(st);
        }

        /// <summary>构建单列显示条带：空格(0)替换为稳定随机替身（构建时算一次，滚动中不再变 → 不闪烁）。
        /// 火球(12)不再替换——火球像普通 icon 一样在卷轴里自然滚动。
        /// ★ reel0 过滤 Wild(m_symbolMax)：条带数据可能含 Wild，此处是显示层第一道关卡，
        ///   配合 SnapFinal/SetCell(百搭统一拦截点)/Symbols.cs 后续拦截，确保 reel0 永不显示百搭。</summary>
        List<int> BuildDisplayStrip(int reel)
        {
            if (m_reelStrips == null || reel >= m_reelStrips.Count) return null;
            var src = m_reelStrips[reel];
            var dst = new List<int>(src.Count);
            for (int i = 0; i < src.Count; i++)
            {
                int s = src[i];
                if (s == 0) s = RandSymbol();  // 仅空格替换；火球(12)保留原样
                // ★ reel0 永不显示 Wild（即使条带数据含 Wild 符号）
                if (s == m_wildId && reel == 0)
                    s = m_symbolMin + (i % (m_symbolMax - m_symbolMin));
                dst.Add(s);
            }
            return dst;
        }

        /// <summary>触发某列停轮：再转 extra 格后定格（quick=停止键急停，转得少）。
        /// 关键：stopAt 选在 displayStrip 与 finalSyms 对齐的位置，避免收敛到位时符号突变。</summary>
        void BeginStop(ReelState st, bool quick)
        {
            st.stopping = true;
            st.autoStop = false;
            st.decel = quick ? m_quickDecel : m_normalDecel;
            int extra = quick ? (1 + RandInt(0, 1)) : (3 + RandInt(0, 5));
            // 找最近的 align 位置：让 displayStrip[stripBase+stopAt+k] == finalSyms[k]
            int aligned = FindAlignedStopPos(st, Mathf.FloorToInt(st.pos) + extra);
            st.stopAt = aligned;

            // ★ 按实际停位构建 _fbStripMult：把 finalSyms 中的火球映射到 displayStrip 对应位置，
            //   LayoutReel 在减速阶段用此字典挂倍率（倍率在减速时就出现，不再等到停稳后 ShowFeatureState 才出现）
            BuildFbStripMult(st);
        }

        /// <summary>按实际停位构建本列火球→条带位置→倍率映射。BeginStop 后调用（停位已知）。</summary>
        void BuildFbStripMult(ReelState st)
        {
            int stripLen = (st.displayStrip != null) ? st.displayStrip.Count : 0;
            if (stripLen <= 0 || st.finalSyms == null) return;
            // 清除本列旧条目（预测/上一轮的残留）
            int prefix = st.reelIdx * 100000;
            var stale = new List<int>();
            foreach (var kv in _fbStripMult)
                if (kv.Key >= prefix && kv.Key < prefix + 100000) stale.Add(kv.Key);
            foreach (var key in stale) _fbStripMult.Remove(key);
            // 按实际停位构建
            for (int row = 0; row < st.finalSyms.Length; row++)
            {
                if (st.finalSyms[row] == m_fireballSymbolId)
                {
                    int k = m_buf + row;
                    int symIdx = ((st.stripBase + (int)st.stopAt + k) % stripLen + stripLen) % stripLen;
                    int mkey = st.reelIdx * 100 + row;
                    if (_baseFireMults.TryGetValue(mkey, out FireballCell cell))
                        _fbStripMult[st.reelIdx * 100000 + symIdx] = cell;
                }
            }
        }

        /// <summary>从 start 搜索 displayStrip 上与 finalSyms 完全匹配的最近位置。</summary>
        int FindAlignedStopPos(ReelState st, int start)
        {
            int stripLen = (st.displayStrip != null) ? st.displayStrip.Count : 0;
            if (stripLen <= 0 || st.finalSyms == null) return start;

            // 在 [start, start+stripLen) 范围内搜索（覆盖一整圈循环）
            for (int offset = 0; offset < stripLen; offset++)
            {
                int pos = start + offset;
                bool match = true;
                for (int r = 0; r < st.finalSyms.Length && match; r++)
                {
                    int idx = (st.stripBase + pos + r) % stripLen;
                    if (idx < 0) idx += stripLen;
                    if (idx >= 0 && idx < stripLen && st.displayStrip[idx] != st.finalSyms[r])
                        match = false;
                }
                if (match) return pos;
            }
            // 极罕见：找不到对齐（strip 被 BuildDisplayStrip 改过导致不一致）→ 回退
            UnityEngine.Debug.LogWarning($"[ReelView] ⚠️ reel{st.reelIdx} 找不到 align 位置，回退到 start={start}");
            return start;
        }

        /// <summary>设置某格可见性：隐藏窗口下沿以下的底部缓冲格（模式B 底部对齐，多余 buffer 不应露到盘面下方）。
        /// 顶部缓冲交由画面上边框遮挡，此处只裁底部，避免误改上方已正常的显示。</summary>
        void SetCellVisible(ReelState st, int k, bool visible)
        {
            if (k < 0 || k >= st.cells.Count) return;
            var go = st.cells[k];
            if (go != null && go.activeSelf != visible) go.SetActive(visible);
        }

        /// <summary>每帧布局：连续循环卷轴。cell 位置随 pos 下移，符号从 reelStrips 循环取（顶部不断进新图）。</summary>
        void LayoutReel(ReelState st)
        {
            int n = st.cells.Count;
            int stripLen = (st.displayStrip != null) ? st.displayStrip.Count : 0;
            int basePos = Mathf.FloorToInt(st.pos);
            float frac = st.pos - basePos;             // 0..1 滚动小数
            int topIdx = st.stripBase + basePos;
            for (int k = 0; k < n; k++)
            {
                float worldRow = (k - m_buf) - frac;   // 浮点行位置（顶部缓冲在负区）
                float y = worldRow * m_cellSize + m_rowBaseY;
                var rt = st.cells[k].transform as RectTransform;
                if (rt != null) rt.anchoredPosition = new Vector2(0f, y);
                // ★ 底部缓冲裁剪：只隐藏固定的底部 buffer 行（k < m_buf，即 2 个缓冲格），
                // 不受滚动 frac 影响——否则有效底行(row0, k=m_buf)在 frac 偏大时会被误裁，导致"多裁1个"。
                SetCellVisible(st, k, k >= m_buf);

                int symIdx = (topIdx + k) % stripLen;
                if (symIdx < 0) symIdx += stripLen;
                int sym = (stripLen > 0) ? st.displayStrip[symIdx] : RandSymbol();
                SetCell(st, k, sym);   // displayStrip 已不含火球/空格，滚动中符号稳定不闪
                // ★ 火球：减速阶段(BeginStop 后)_fbStripMult 已构建，命中则挂倍率（倍率在减速时就出现，
                //   不等到停稳后 ShowFeatureState 才出现）。Mini 持久 overlay 模式也走此路径——停稳后 overlay
                //   在最上层盖住滚动格，位置/文字一致，无重影（与主游戏 Hold&Spin 表现统一）。
                if (sym == m_fireballSymbolId)
                {
                    int skey = st.reelIdx * 100000 + symIdx;
                    if (_fbStripMult.TryGetValue(skey, out FireballCell cell)) SetCellFireballMult(st, k, cell);
                }
            }
        }

        /// <summary>减速后期布局（remain &lt; 2 格）：直接用 finalSyms 显示符号，
        /// 避免从 displayStrip 滚动位置硬切到 finalSyms 导致的跳帧。
        /// 位置仍随 pos 平滑过渡，但符号内容始终是最终结果。</summary>
        void LayoutFinalReel(ReelState st, float remain)
        {
            int n = st.cells.Count;
            float frac = st.pos - Mathf.FloorToInt(st.pos);

            for (int k = 0; k < n; k++)
            {
                int row = k - m_buf;

                // Y 位置：保持基于 pos 的平滑滚动，逐渐收敛到目标行
                float worldRow = (k - m_buf) - frac;
                float scrollY = worldRow * m_cellSize + m_rowBaseY;
                float targetY = row * m_cellSize + m_rowBaseY;

                // remain=2 时全用 scrollY，remain→0 时渐变到 targetY（无跳变）
                float t = Mathf.Clamp01(1f - Mathf.Abs(remain) / 2f);
                float y = Mathf.Lerp(scrollY, targetY, t);

                var rt = st.cells[k].transform as RectTransform;
                if (rt != null) rt.anchoredPosition = new Vector2(0f, y);
                // ★ 底部缓冲裁剪（定格期同 LayoutReel：固定裁底部 2 个 buffer 行 k < m_buf，有效底行 row0 始终保留）
                SetCellVisible(st, k, k >= m_buf);

                // 符号始终来自 finalSyms（这就是关键：不再读 displayStrip！）
                if (row >= 0 && row < st.finalSyms.Length)
                {
                    int sym = st.finalSyms[row];
                    SetCell(st, k, sym);
                    // ★ 火球：减速最后2格也挂倍率（与 LayoutReel 减速阶段衔接，无跳变）。
                    //   Mini 持久 overlay 模式也挂——停稳后 overlay 在最上层盖住，无重影。
                    if (sym == m_fireballSymbolId)
                    {
                        int mkey = st.reelIdx * 100 + row;
                        if (_baseFireMults.TryGetValue(mkey, out FireballCell cell)) SetCellFireballMult(st, k, cell);
                    }
                }
                else if (st.finalSyms != null && st.finalSyms.Length > 0)
                {
                    // 缓冲区：上下延伸显示首/尾符号（视觉连续）
                    int edgeSym = (row < 0) ? st.finalSyms[0] : st.finalSyms[st.finalSyms.Length - 1];
                    SetCell(st, k, edgeSym);
                }
            }
        }

        /// <summary>列停稳后：把可见行对齐为最终结果。火球格：本局要掉落的先隐藏（等掉落），
        /// 其余（滚出但不触发 Hold&Spin 的）正常显示普通火球图。</summary>
        void SnapFinal(ReelState st)
        {
            for (int row = 0; row < st.finalSyms.Length; row++)
            {
                int k = m_buf + row;
                if (k >= st.cells.Count) continue;
                int sym = st.finalSyms[row];
                SetCell(st, k, sym, true);
                // ★ 火球：定格时也挂倍率（与减速阶段衔接，无跳变；停稳后 ShowFeatureState 的 overlay 在最上层盖住、视觉一致）。
                //   Mini 持久 overlay 模式也挂——位置/文字与 overlay 完全一致，无重影。
                if (sym == m_fireballSymbolId)
                {
                    int mkey = st.reelIdx * 100 + row;
                    if (_baseFireMults.TryGetValue(mkey, out FireballCell cell)) SetCellFireballMult(st, k, cell);
                }
            }
        }

        // ===== 停止键：急停（1→2→3→4→5 间隔 0.2s） =====

        public void StopNow()
        {


            for (int i = 0; i < _reels.Count; i++)
            {
                var st = _reels[i];
                if (st.spinning && !st.stopping)
                    StartCoroutine(DelayedStop(st, i * 0.2f));
            }
        }

        IEnumerator DelayedStop(ReelState st, float delay)
        {
            if (delay > 0) yield return new WaitForSeconds(delay);
            if (st.spinning && !st.stopping) BeginStop(st, quick: true);
        }
    }
}
