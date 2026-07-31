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

        //   故 StopNow 无法通过 st.spinning 命中。用这两个标志让 StopNow 能识别并提前打断 Hold 滚动。

        // ===== 进入 Hold&Spin =====        public virtual void ShowFeatureState(HoldSpinState s)
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


        // ===== 滚动一轮 =====


        // ===== 滚动结束后结算 =====

        // ===== 释放列 =====

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
    }
}
