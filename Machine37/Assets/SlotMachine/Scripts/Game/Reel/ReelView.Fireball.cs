using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    /// <summary>ReelView 中奖高亮部分：按 Win 列表高亮中奖格（循环播放 icon{N}_2 帧动画）。
    /// 注：火球 Hold&amp;Spin 已在 ReelView.HoldSpin.cs 以 in-grid overlay 实现，本文件不再负责掉落 / 收集盘。</summary>
    public partial class ReelView
    {
        // ===== 中奖高亮（循环播放 icon{N}_2 帧动画） =====

        [Header("中奖高亮")]
        public float m_winFps = 10f;                 // 帧率（帧/秒）

        [Header("顺序播放(A 模式逐条)")]
        public bool m_winSequential = false;         // 是否逐条顺序播放（A=China Street 风格：高亮一条→loop→还原→下一条）；false=所有线同时高亮
        public float m_winSeqDuration = 1.2f;        // 每条线高亮播放时长(秒)
        public float m_winSeqGap = 0.3f;             // 两条线之间间隔(秒)

        List<Coroutine> _winCoroutines = new List<Coroutine>();   // 逐格循环动画协程
        Dictionary<Image, Sprite> _winOrig = new Dictionary<Image, Sprite>(); // 高亮前原 sprite，用于还原
        Coroutine _winSeq = null;                    // 顺序播放协程(A 模式)，独立于 _winCoroutines 避免自停
        List<ReelItem> _winArtItems = new List<ReelItem>();   // 中奖时显示了专属美术(Starfish/Fish/Octopus/Wild/Scatter)的格，清除时还原

        /// <summary>按 Win 列表高亮中奖格。m_winSequential=true 时逐条顺序播放（A 模式），否则所有线同时高亮（B 模式）。</summary>
        public virtual void HighlightWins(List<Win> wins)
        {
            if (m_winSequential) HighlightWinsSequential(wins);
            else HighlightWinsAll(wins);
        }

        /// <summary>所有赢线同时高亮（默认 / B 模式）。</summary>
        void HighlightWinsAll(List<Win> wins)
        {
            StopWinHighlight();
            if (wins == null || wins.Count == 0) return;
            foreach (var w in wins) HighlightSingleWin(w);
        }

        /// <summary>A 模式：逐条顺序播放——高亮第 i 条、loop 一段时间、还原，再播下一条（播放完一条变回正常再播下一条）。</summary>
        void HighlightWinsSequential(List<Win> wins)
        {
            StopWinHighlight();
            if (wins == null || wins.Count == 0) return;
            _winSeq = StartCoroutine(SeqWinAnim(wins));
        }

        IEnumerator SeqWinAnim(List<Win> wins)
        {
            if (wins == null || wins.Count == 0) { _winSeq = null; yield break; }
            // 无限循环逐条高亮：一条高亮(loop)→还原→下一条→还原→… 直到下一转/清场
            // 调用 ClearWinHighlight / StopWinHighlight / StopWinAnims 时 StopCoroutine 杀掉本协程。
            while (true)
            {
                foreach (var w in wins)
                {
                    HighlightSingleWin(w);                       // 只停逐格协程、还原 sprite（不会杀本协程）
                    yield return new WaitForSeconds(m_winSeqDuration);
                    ClearWinCells();                             // 清掉这一条高亮（变回正常）
                    yield return new WaitForSeconds(m_winSeqGap);
                }
            }
        }

        /// <summary>Scatter 触发免费游戏时高亮所有 Scatter 格（使用 m_scatter 专属中奖美术），持续 dur 秒后自动清除。
        /// 进 Mini 前调用，让玩家明确看到"是这些 Scatter 触发了免费游戏"。</summary>
        public void HighlightScatterCells(int[][] grid, float dur)
        {
            if (grid == null) return;
            const int SCATTER_ID = 11;
            bool any = false;
            for (int reel = 0; reel < grid.Length && reel < _reels.Count; reel++)
            {
                var st = _reels[reel];
                for (int row = 0; row < grid[reel].Length && row < st.shownSym.Length; row++)
                {
                    if (grid[reel][row] != SCATTER_ID) continue;
                    int k = m_buf + row;
                    if (k < 0 || k >= st.cellItems.Count) continue;
                    var item = st.cellItems[k];
                    if (item != null && item.ShowWinArt(SCATTER_ID))
                    {
                        _winArtItems.Add(item);
                        any = true;
                    }
                }
            }
            if (any && dur > 0f)
                StartCoroutine(ClearScatterWinArtAfter(dur));
        }

        IEnumerator ClearScatterWinArtAfter(float dur)
        {
            yield return new WaitForSeconds(dur);
            foreach (var it in _winArtItems)
                if (it != null) it.HideWinArt();
            _winArtItems.Clear();
        }

        /// <summary>高亮单条 Win 的所有格子（起逐格循环动画协程，记入 _winCoroutines）。</summary>
        void HighlightSingleWin(Win w)
        {
            if (w.positions == null || w.positions.Count == 0) return;
            foreach (int pos in w.positions)
            {
                int reel = pos / 100;
                int row = pos % 100;
                if (reel < 0 || reel >= _reels.Count) continue;
                var st = _reels[reel];
                int k = m_buf + row;
                if (k < 0 || k >= st.cellItems.Count) continue;
                int shownId = (k < st.shownSym.Length) ? st.shownSym[k] : -1;
                if (shownId == m_fireballSymbolId) continue;   // 火球不参与普通连线高亮（且 m_image 已隐藏）
                var img = CellImage(st, k);
                if (img == null || img.gameObject == null) continue;

                // ★ 特殊符号(starfish/fish/octopus/wild/scatter)有专属中奖美术：激活它并隐藏 m_image，
                //   由美术 GameObject 接管中奖表现（不再对 m_image 播 _2 帧动画）。
                var item = (k < st.cellItems.Count) ? st.cellItems[k] : null;
                if (item != null && item.ShowWinArt(shownId))
                {
                    _winArtItems.Add(item);
                    continue;
                }

                // 用该格实际显示的符号对应的 _2 帧动画（wild 占位也播它自己的 _2）
                int animId = (shownId >= 0) ? shownId : w.symbolId;
                var frames = Resources.LoadAll<Sprite>($"Icon/icon{animId}_2");
                if (frames != null && frames.Length > 0)
                {
                    // 文件名 icon{N}_2_{i}.png 按字母序会乱（1,10,11..2,20,3），按帧号排序保证顺序正确
                    Array.Sort(frames, (a, b) => FrameIndex(a.name).CompareTo(FrameIndex(b.name)));
                }
                else
                {
                    frames = new[] { img.sprite };   // 无 _2 帧：兜底只播放原图（保持不动）
                }
                if (!_winOrig.ContainsKey(img))
                    _winOrig[img] = img.sprite;
                _winCoroutines.Add(StartCoroutine(ZoomWinAnim(img, frames)));
            }
        }

        /// <summary>停掉所有中奖动画并还原 sprite + 缩放（开新局前 / 重新高亮前 / 外部调用清场）。</summary>
        public void ClearWinHighlight()
        {
            if (_winSeq != null) { StopCoroutine(_winSeq); _winSeq = null; }
            ClearWinCells();
        }

        /// <summary>停掉逐格高亮协程并还原 sprite（不碰顺序播放协程 _winSeq，避免 SeqWinAnim 自停）。</summary>
        void ClearWinCells()
        {
            foreach (var c in _winCoroutines)
                if (c != null) StopCoroutine(c);
            _winCoroutines.Clear();
            foreach (var kvp in _winOrig)
            {
                if (kvp.Key != null && kvp.Key.gameObject != null)
                {
                    kvp.Key.sprite = kvp.Value;
                    var rt = kvp.Key.rectTransform;
                    if (rt != null) rt.localScale = Vector3.one;
                }
            }
            _winOrig.Clear();
            // ★ 还原专属中奖美术（Starfish/Fish/Octopus/Wild/Scatter）：隐藏美术、恢复 m_image
            foreach (var it in _winArtItems)
                if (it != null) it.HideWinArt();
            _winArtItems.Clear();
        }

        /// <summary>彻底停止高亮（含顺序播放协程）——开新局清场 / 重新高亮前调用。</summary>
        void StopWinHighlight()
        {
            if (_winSeq != null) { StopCoroutine(_winSeq); _winSeq = null; }
            ClearWinCells();
        }

        /// <summary>开新局清场时停掉动画（不还原，因为 cell 即将销毁）。</summary>
        void StopWinAnims()
        {
            if (_winSeq != null) { StopCoroutine(_winSeq); _winSeq = null; }
            foreach (var c in _winCoroutines)
                if (c != null) StopCoroutine(c);
            _winCoroutines.Clear();
            _winOrig.Clear();
            _winArtItems.Clear();   // cell 即将销毁，清掉追踪避免持有已销毁 ReelItem 引用
        }

        /// <summary>中奖格放大缩小 + 帧动画循环（参考原游戏：中奖符号缩放脉冲同时播 _2 动画）。</summary>
        [Header("中奖缩放")]
        public float m_winPulseAmp = 0.18f;   // 缩放振幅（1±amp，如 0.18 → 1.0~1.18）
        public float m_winPulsePeriod = 0.5f; // 一个完整 in-out 周期(秒)

        IEnumerator ZoomWinAnim(Image img, Sprite[] frames)
        {
            if (frames == null || frames.Length == 0) yield break;
            if (!img.enabled) img.enabled = true;   // 防御：确保高亮格可见（否则精灵动画/脉冲都看不到）
            float interval = 1f / Mathf.Max(1f, m_winFps);
            int i = 0;
            float pulseT = 0;
            while (img != null && img.gameObject != null)
            {
                img.sprite = frames[i % frames.Length];
                i++;

                // 缩放脉冲：正弦波 1→(1+amp)→1
                pulseT += interval;
                float s = 1f + m_winPulseAmp * Mathf.Sin(pulseT * (Mathf.PI * 2f) / m_winPulsePeriod);
                var rt = img.rectTransform;
                if (rt != null) rt.localScale = new Vector3(s, s, 1f);

                yield return new WaitForSeconds(interval);
            }
        }

        /// <summary>从精灵名(icon{N}_2_{i})提取帧号用于排序。</summary>
        static int FrameIndex(string name)
        {
            int i = name.LastIndexOf('_');
            if (i >= 0 && int.TryParse(name.Substring(i + 1), out int n)) return n;
            return 0;
        }
    }
}
