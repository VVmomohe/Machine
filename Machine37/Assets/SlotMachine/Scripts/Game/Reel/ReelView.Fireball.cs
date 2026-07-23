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

        List<Coroutine> _winCoroutines = new List<Coroutine>();
        Dictionary<Image, Sprite> _winOrig = new Dictionary<Image, Sprite>(); // 高亮前原 sprite，用于还原

        /// <summary>按 Win 列表高亮中奖格：循环播放该格符号对应的 icon{N}_2 精灵表。</summary>
        public virtual void HighlightWins(List<Win> wins)
        {
            ClearWinHighlight();
            if (wins == null || wins.Count == 0) return;

            foreach (var w in wins)
            {
                if (w.positions == null || w.positions.Count == 0) continue;
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
        }

        /// <summary>停掉所有中奖动画并还原 sprite + 缩放（开新局前 / 重新高亮前调用）。</summary>
        public void ClearWinHighlight()
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
        }

        /// <summary>开新局清场时停掉动画（不还原，因为 cell 即将销毁）。</summary>
        void StopWinAnims()
        {
            foreach (var c in _winCoroutines)
                if (c != null) StopCoroutine(c);
            _winCoroutines.Clear();
            _winOrig.Clear();
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
