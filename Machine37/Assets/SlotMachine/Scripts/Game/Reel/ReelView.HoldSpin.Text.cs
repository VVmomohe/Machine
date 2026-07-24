using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    /// <summary>ReelView 火球文字/倍率显示：FireballLabel、ApplyFireballText、SetCellFireballMult、GetFireballMult。</summary>
    public partial class ReelView
    {
        /// <summary>在底层滚动格(k)上显示火球文字（倍数火球→"x倍率"，彩金火球→"MINI/MINOR/MAJOR/MEGA"）。</summary>
        void SetCellFireballMult(ReelState st, int k, FireballCell cell)
        {
            if (cell == null) return;
            if (k < 0 || k >= st.cellItems.Count) return;
            var item = st.cellItems[k];
            if (item == null) return;
            item.m_type = cell.kind;
            item.m_rate = cell.multiplier;
            // ★ 诊断日志：若 kind 非法或 multiplier 与 kind 不匹配，输出详细值供定位
            if ((int)cell.kind < 0 || (int)cell.kind > 5 || (cell.kind == FireballKind.Multiplier && cell.multiplier > 10f))
                Debug.LogWarning($"[FireballLabel] kind={(int)cell.kind}({cell.kind}) mult={cell.multiplier} reel={st.reelIdx} k={k} → label={FireballLabel(cell)}");
            ApplyFireballText(item.gameObject, cell);
        }

        /// <summary>火球显示文字：倍数火球="x1.5"，彩金火球=档位大写名（MINI/MINOR/MAJOR/MEGA），免费模式火球="FREE"。</summary>
        static string FireballLabel(FireballCell c)
        {
            switch (c.kind)
            {
                case FireballKind.Mini: return "MINI";
                case FireballKind.Minor: return "MINOR";
                case FireballKind.Major: return "MAJOR";
                case FireballKind.Mega: return "MEGA";
                case FireballKind.FreeSpins: return "FREE";
                case FireballKind.Multiplier:
                    if (c.multiplier <= 0f) return "";
                    // ★ 防御：倍数火球的 multiplier 不应超过配置的 maxMultiplier（现 5）。
                    //   若出现 >10 说明 kind 被错误置为 Multiplier(0) 但 multiplier 是彩金值——按 multiplier 推断档位回退显示。
                    if (c.multiplier > 10f)
                    {
                        if (c.multiplier >= 2000f) return "MEGA";
                        if (c.multiplier >= 500f) return "MAJOR";
                        if (c.multiplier >= 100f) return "MINOR";
                        return "MINI";
                    }
                    return "x" + c.multiplier.ToString("0.##");
                default:
                    // 非法 kind（超出 0~5）：同样按 multiplier 推断档位，避免显示裸数字 x100
                    if (c.multiplier <= 0f) return "";
                    if (c.multiplier >= 2000f) return "MEGA";
                    if (c.multiplier >= 500f) return "MAJOR";
                    if (c.multiplier >= 100f) return "MINOR";
                    if (c.multiplier >= 20f) return "MINI";
                    return "x" + c.multiplier.ToString("0.##");
            }
        }

        /// <summary>在 go 上显示火球文字：优先用 ReelItem.m_text，缺失时按层级查找子 Text。</summary>
        void ApplyFireballText(GameObject go, FireballCell cell)
        {
            if (go == null || cell == null) return;
            var item = go.GetComponent<ReelItem>();
            var txt = (item != null && item.m_text != null) ? item.m_text : go.GetComponentInChildren<UnityEngine.UI.Text>();
            if (txt == null) return;
            if (cell.kind == FireballKind.FreeSpins)
            {
                txt.text = "";
                txt.gameObject.SetActive(false);
                txt.enabled = false;
                return;
            }
            string label = FireballLabel(cell);
            bool show = !string.IsNullOrEmpty(label);
            txt.text = label;
            txt.gameObject.SetActive(show);
            txt.enabled = show;
            txt.color = Color.white;
        }

        /// <summary>取火球 overlay 携带的倍率（ReelItem.m_rate）。</summary>
        float GetFireballMult(GameObject go)
        {
            var ri = go.GetComponent<ReelItem>();
            return ri != null ? ri.m_rate : 0f;
        }
    }
}
