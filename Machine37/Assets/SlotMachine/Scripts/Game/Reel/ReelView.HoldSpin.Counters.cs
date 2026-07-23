using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    /// <summary>ReelView 火球倒计时计数器：SetRespinCounterRow、HideAllCounters、AddFireballToCounter。</summary>
    public partial class ReelView
    {
        /// <summary>把火球倍率累加到该列(reel)的计数器文本（ReelFireNum.AddMultiplier）。</summary>
        void AddFireballToCounter(int reel, float mult)
        {
            if (m_numObjs == null || reel < 0 || reel >= m_numObjs.Length || m_numObjs[reel] == null) return;
            m_numObjs[reel].AddMultiplier(mult);
        }

        /// <summary>设置某列(reel)的 respin 倒计时：点亮前 count 个圈，其余熄灭；count&lt;=0 显示 0 圈（全灭），
        /// 待该列火球随卷轴回归滚动队列时由 SpinHoldRound 在回合结束时统一隐藏（不再额外延迟）。</summary>
        public void SetRespinCounterRow(int reel, int count)
        {
            if (m_numObjs == null || reel < 0 || reel >= m_numObjs.Length) return;
            var fn = m_numObjs[reel];
            if (fn == null) return;
            if (_collectedReels != null && _collectedReels.Contains(reel))
            {
                Debug.Log($"[COUNTER] reel{reel} count={count} → 被_collectedReels拦截，跳过");
                return;
            }

            Debug.Log($"[COUNTER] reel{reel} count={count} activeSelf={fn.gameObject.activeSelf}");

            // count<=0：显示 0 圈（所有圈熄灭），不在此处延迟隐藏——
            // 该列火球在本轮回滚时随卷轴回归队列，计数器由 SpinHoldRound 在回合结束时一并隐藏。
            if (count <= 0)
            {
                if (!fn.gameObject.activeSelf) { Debug.Log($"[COUNTER] reel{reel} count={count} → activeSelf=false，直接return(防闪现)"); return; }
                fn.gameObject.SetActive(true);
                var items = fn.m_items;
                if (items != null)
                {
                    for (int i = 0; i < items.Length; i++)
                        if (items[i] != null) items[i].gameObject.SetActive(false);
                }
                return;
            }
            fn.gameObject.SetActive(true);
            var items2 = fn.m_items;
            if (items2 != null)
            {
                for (int i = 0; i < items2.Length; i++)
                    if (items2[i] != null) items2[i].gameObject.SetActive(i < count);
            }
        }

        /// <summary>隐藏全部倒计时（新基础局 / 特性结束）。</summary>
        public void HideAllCounters()
        {
            if (m_numObjs == null) return;
            for (int i = 0; i < m_numObjs.Length; i++)
            {
                if (m_numObjs[i] != null)
                {
                    m_numObjs[i].ResetMultiplier();
                    m_numObjs[i].gameObject.SetActive(false);
                }
            }

            // 关闭所有列的预警特效（新局开始 / Hold&Spin 结束）
            for (int reel = 0; reel < _reels.Count; reel++)
                SetColumnEffect(reel, false);
        }
    }
}
