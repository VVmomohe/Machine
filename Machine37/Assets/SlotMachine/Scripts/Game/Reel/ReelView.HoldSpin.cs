using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    /// <summary>ReelView 火球 overlay 核心流程：ShowFeatureState（基础局钉持久火球显示）、SpinHoldRound、Release列。</summary>
    public partial class ReelView
    {
        List<GameObject> _fbOverlays = new List<GameObject>();
        HashSet<int> _releaseReels = new HashSet<int>();
        HashSet<int> _collectedReels = new HashSet<int>();
        bool _wasSpinning = false;

        //   故 StopNow 无法通过 st.spinning 命中。用这两个标志让 StopNow 能识别并提前打断 Hold 滚动。

        // ===== 基础局火球钉持久 overlay（原 ShowFeatureState 入口，HOLD respin 已移除）=====

        public virtual void ShowFeatureState(HoldSpinState s)
        {
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
            RefreshColumnEffects(s);   // 近满列(差1火球)→亮整列 m_effect
        }


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

        // ===== 模式B 收集盘 respin 辅助（轻量，不滚盘）=====

        /// <summary>重播某列的 tong（夹子/桶）动画：新火球落入 / 满列收集时调用。</summary>
        void PlayTong(int reel)
        {
            if (m_tongs != null && reel >= 0 && reel < m_tongs.Length && m_tongs[reel] != null)
                m_tongs[reel].Play();
        }

        /// <summary>销毁某列(reel)全部火球 overlay（释放列时调用，使其从屏上消失）。</summary>
        public void ClearColumnFireballs(int reel)
        {
            for (int i = _fbOverlays.Count - 1; i >= 0; i--)
            {
                var go = _fbOverlays[i];
                if (go == null) { _fbOverlays.RemoveAt(i); continue; }
                if (ParseReelRow(go.name, out int r, out _))
                {
                    if (r == reel) { Destroy(go); _fbOverlays.RemoveAt(i); }
                }
            }
            RefreshColumnEffects();
        }

        /// <summary>释放列“回归滚动队列”：该列火球位底层符号换回普通符（不再显示火球图标），计数器由调用方隐藏。
        /// 收集盘 respin 中 counter 归零的列调用——视觉上该列回到正常（下一局自然滚动）。</summary>
        public void ReleaseColumnToSpinQueue(int reel)
        {
            if (reel < 0 || reel >= _reels.Count) return;
            var st = _reels[reel];
            for (int k = m_buf; k < m_buf + st.rows && k < st.cells.Count; k++)
            {
                int logicalRow = k - m_buf;
                int sym = (st.finalSyms != null && logicalRow < st.finalSyms.Length) ? st.finalSyms[logicalRow] : 0;
                if (sym == m_fireballSymbolId) sym = RandNormalSymbol();  // 火球位换普通符（回归滚动队列）
                SetCell(st, k, sym);
            }
        }
    }
}
