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
        /// <summary>把火球倍率累加到该列(reel)的计数器文本（ReelFireNum.AddMultiplier）。kind 透传给计数器：彩金档显示档名而非裸数字。</summary>
        void AddFireballToCounter(int reel, float mult, FireballKind kind = FireballKind.Multiplier)
        {
            if (m_numObjs == null || reel < 0 || reel >= m_numObjs.Length || m_numObjs[reel] == null) return;
            m_numObjs[reel].AddMultiplier(mult, kind);
        }

        /// <summary>设置某列(reel)的 respin 倒计时：点亮前 count 个圈，其余熄灭；count==0 显示 0 圈（全灭，仍可见，对应"延迟一轮释放"的静止帧）；
        /// count&lt;0（= -1 哨兵）表示该列已释放/集满，彻底隐藏整个 ReelFireNum。</summary>
        public void SetRespinCounterRow(int reel, int count)
        {
            if (m_numObjs == null || reel < 0 || reel >= m_numObjs.Length) return;
            var fn = m_numObjs[reel];
            if (fn == null) return;

            // count < 0（= -1 哨兵）：该列已释放/集满，彻底隐藏 ReelFireNum。
            if (count < 0)
            {
                fn.ResetMultiplier();
                fn.gameObject.SetActive(false);
                return;
            }

            // count >= 0：保持可见。
            // ※ counter 从 1→0 的当轮火球仍锁定（延迟一轮才释放滚走），显示 0 圈但不隐藏，
            //   避免"圈圈还有一个时计数器消失"；真正释放/集满由调用方传 -1 哨兵隐藏。
            fn.gameObject.SetActive(true);
            if (fn.m_text != null) fn.m_text.gameObject.SetActive(false);
            var items = fn.m_items;
            if (items != null)
            {
                int lit = Mathf.Clamp(count, 0, items.Length);
                for (int i = 0; i < items.Length; i++)
                    if (items[i] != null) items[i].gameObject.SetActive(i < lit);
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
