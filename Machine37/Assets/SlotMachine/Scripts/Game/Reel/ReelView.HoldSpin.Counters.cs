using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    /// <summary>ReelView 火球倒计时计数器：仅做转发，可见性/圈数逻辑全部在 ReelFireNum 内部（Refresh）。
    /// 模型极简：显示 = (有火球[active] 或 有倍率)；隐藏 = 开新局(active=false)。</summary>
    public partial class ReelView
    {
        /// <summary>把火球倍率累加到该列(reel)的计数器文本（ReelFireNum.AddMultiplier）。kind 透传给计数器：彩金档显示档名而非裸数字。</summary>
        void AddFireballToCounter(int reel, float mult, FireballKind kind = FireballKind.Multiplier)
        {
            if (m_numObjs == null || reel < 0 || reel >= m_numObjs.Length || m_numObjs[reel] == null) return;
            m_numObjs[reel].AddMultiplier(mult, kind);
        }

        /// <summary>设置某列(reel)的 respin 倒计时圈数（0..N）。可见性由 ReelFireNum 推导：active 且 (有圈 或 有倍率) 才显示。</summary>
        public void SetRespinCounterRow(int reel, int count)
        {
            if (m_numObjs == null || reel < 0 || reel >= m_numObjs.Length) return;
            if (m_numObjs[reel] == null) return;
            m_numObjs[reel].SetCount(count);
        }

        /// <summary>激活全部计数器（进入 Hold&Spin 时调用）。之后 SetCount/AddMultiplier 才能正常显示。</summary>
        public void ActivateCounters()
        {
            if (m_numObjs == null) return;
            for (int i = 0; i < m_numObjs.Length; i++)
                if (m_numObjs[i] != null) m_numObjs[i].Activate();
        }

        /// <summary>结算完成：不再清零（保留 num/rate 显示到玩家按确认开新局）。
        /// 隐藏统一在 HideAllCounters→ResetAll（开新局时 num/rate 归零，(num==0&amp;&amp;rate==0) 即隐藏）。</summary>
        public void SettleCounters()
        {
            // 故意留空：结算阶段不清零、不隐藏，保持计数器显示（含满列 X 倍）到开新局。
        }

        /// <summary>隐藏全部倒计时（新基础局 / 特性结束 / 进 Mini）：每列 ResetAll（active=false → 强制隐藏）。</summary>
        public void HideAllCounters()
        {
            if (m_numObjs == null) return;
            int activeBefore = 0;
            for (int i = 0; i < m_numObjs.Length; i++)
                if (m_numObjs[i] != null && m_numObjs[i].gameObject.activeSelf) activeBefore++;

            for (int i = 0; i < m_numObjs.Length; i++)
                if (m_numObjs[i] != null) m_numObjs[i].ResetAll();

            // 关闭所有列的预警特效（新局开始 / Hold&Spin 结束）
            for (int reel = 0; reel < _reels.Count; reel++)
                SetColumnEffect(reel, false);
        }

        /// <summary>100% 同步 engaged：每列调 ReelFireNum.CheckEngaged（m_num&lt;=0 即清 engaged）。
        /// OnStartKey 最顶部调用，保证每次按确认都先跑（任何分支提前 return 都拦不住）。</summary>
        public void CheckEngagedAll()
        {
            if (m_numObjs == null) return;
            for (int i = 0; i < m_numObjs.Length; i++)
                if (m_numObjs[i] != null) m_numObjs[i].CheckEngaged();
        }

        /// <summary>读取各列计数器 engaged 状态（m_engaged），供逻辑层 RespinHoldSpin 按"该列是否还有火球"判定释放。
        /// 释放判定改由显示层 m_engaged 驱动（用户拍板 2026-07-25）：m_engaged==false 即该列无火球/倒计时已归零 → 火球回归滚动队列。</summary>
        public bool[] GetEngagedColumns()
        {
            if (m_numObjs == null) return null;
            var arr = new bool[m_numObjs.Length];
            for (int i = 0; i < m_numObjs.Length; i++)
                arr[i] = (m_numObjs[i] != null) && m_numObjs[i].m_engaged;
            return arr;
        }
    }
}
