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
                default:
                    if (c.multiplier <= 0f) return "";
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
