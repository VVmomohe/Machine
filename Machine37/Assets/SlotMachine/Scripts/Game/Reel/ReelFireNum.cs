using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    /// <summary>火球统计显示组件（ReelFireNum，2026-07-25 改为统计模式）。
    ///
    /// ★ 纯显示 / 统计组件（单向：只由游戏逻辑写入，永不回读进玩法）。
    ///   原 Hold&Spin 的"倒计时 3→2→1→0"语义已随 Hold&Spin 去除而废弃。
    ///   现改为：基础旋转落火球后，显示"本局火球总倍率"（X 文本）。
    ///   火球自身的倍率 / 彩金档已各自显示在棋盘火球格上（ShowGrid 传 fireMults），
    ///   本组件只做汇总统计展示，玩法完全独立于本组件。
    ///
    /// 外部调用：ShowStats(totalMultiplier, fireballCount) 显示本局统计；ResetAll() / HideAllCounters 隐藏（开新局）。
    /// Activate / SetCount / AddMultiplier / ResetMultiplier / CheckEngaged 保留为兼容空 / 直通方法，不再驱动倒计时。</summary>
    public class ReelFireNum : MonoBehaviour
    {

        public int m_num;              // 统计：本局火球个数（仅显示）
        public float m_rate;           // 统计：本局火球总倍率（×bet），彩金档计入
        public Text m_text;            // "X倍率" / 个数 文本
        public Image[] m_items;        // 历史圈物体（统计模式下隐藏）

        void Awake()
        {
            // 自动绑定子物体 Image 作为圈（若未手动绑定）
            if (m_items == null || m_items.Length == 0)
            {
                var imgs = new List<Image>();
                for (int i = 0; i < transform.childCount; i++)
                {
                    var img = transform.GetChild(i).GetComponent<Image>();
                    if (img != null) imgs.Add(img);
                }
                m_items = imgs.ToArray();
            }
            m_num = 0; m_rate = 0f;
            if (m_text != null) m_text.gameObject.SetActive(false);
            if (m_items != null) foreach (var it in m_items) if (it != null) it.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }

        /// <summary>显示本局火球统计：优先显示总倍率 X 文本；无倍率但有火球数时显示个数。</summary>
        public void ShowStats(float totalMultiplier, int fireballCount = 0)
        {
            m_rate = totalMultiplier;
            m_num = fireballCount;
            Refresh();
        }

        // —— 以下为兼容旧调用保留，统计模式下无副作用（不再驱动倒计时）——
        public void Activate() { Refresh(); }
        public void SetCount(int count) { m_num = count; Refresh(); }
        public void AddMultiplier(float mult, FireballKind kind = FireballKind.Multiplier) { m_rate += mult; Refresh(); }
        public void ResetMultiplier() { m_rate = 0f; Refresh(); }

        /// <summary>开新的一局 / 隐藏统计（HideAllCounters 调用）。</summary>
        public void ResetAll()
        {
            m_num = 0;
            m_rate = 0f;
            Refresh();
        }

        /// <summary>兼容 OnStartKey 调用：统计模式下仅 Refresh（已无 engaged 概念）。</summary>
        public void CheckEngaged()
        {
            Refresh();
        }

        void Refresh()
        {
            // ★ 统计模式可见性：本局有火球倍率 或 有火球个数 即显示，否则隐藏。
            bool show = (m_rate > 0f) || (m_num > 0);
            gameObject.SetActive(show);
            if (!show) return;

            // 统计模式：圈物体隐藏，只用文本显示总倍率 / 个数
            if (m_items != null)
                foreach (var it in m_items)
                    if (it != null) it.gameObject.SetActive(false);

            if (m_text != null)
            {
                if (m_rate > 0f)
                {
                    m_text.text = "X" + m_rate.ToString("0.##");
                    m_text.gameObject.SetActive(true);
                }
                else
                {
                    m_text.text = m_num.ToString();
                    m_text.gameObject.SetActive(true);
                }
            }
        }
    }
}
