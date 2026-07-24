using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    public class ReelFireNum : MonoBehaviour
    {

        public float m_rate;
        public Text m_text;
        public Image[] m_items;

        void Awake()
        {
            m_text.gameObject.SetActive(false);
            // ★ 只在 m_items 未配置时自动查找（确保只取直接子级 Image，排除背景/装饰图）
            if (m_items == null || m_items.Length == 0)
            {
                var imgs = new System.Collections.Generic.List<Image>();
                for (int i = 0; i < transform.childCount; i++)
                {
                    var img = transform.GetChild(i).GetComponent<Image>();
                    if (img != null) imgs.Add(img);
                }
                m_items = imgs.ToArray();
                if (m_items.Length != 3)
                    UnityEngine.Debug.LogWarning($"[ReelFireNum] {name} 自动查找得到 {m_items.Length} 个 Image(预期3)，请在 Inspector 手动绑定 m_items！");
            }
        }

        /// <summary>累加一个火球掉入桶的倍率：m_text 显示累计值，同时隐藏 m_items（倒计时圈）。
        /// 初始/复位时 m_rate=0、m_text 隐藏、m_items 显示（见 ResetMultiplier）。</summary>
        public void AddMultiplier(float mult, FireballKind kind = FireballKind.Multiplier)
        {
            // 彩金火球(Mini/Minor/Major/Mega)只显示档位字符串、不计入倍率累加 → 计数器只反映倍数火球，彩金不显示
            if (kind == FireballKind.Mini || kind == FireballKind.Minor || kind == FireballKind.Major || kind == FireballKind.Mega)
                return;
            m_rate += mult;
            gameObject.SetActive(true);   // ★ 父物体可能在 ShowFeatureState/HideAllCounters 里被整体 SetActive(false)，这里必须重新激活，否则只点亮子文本、整个计数器仍不可见
            if (m_text != null)
            {
                m_text.text = "X" + m_rate.ToString("0.##");   // 累计倍率（如 1.5、21.5），仅倍数火球
                m_text.gameObject.SetActive(true);
            }
            if (m_items != null)
                foreach (var it in m_items)
                    if (it != null) it.gameObject.SetActive(false);
        }

        /// <summary>复位：倍率归 0，隐藏 m_text，恢复显示 m_items（用于新特性开始 / 基础局开始）。</summary>
        public void ResetMultiplier()
        {
            m_rate = 0f;
            if (m_text != null) m_text.gameObject.SetActive(false);
            if (m_items != null)
                foreach (var it in m_items)
                    if (it != null) it.gameObject.SetActive(true);
        }
    }
}
