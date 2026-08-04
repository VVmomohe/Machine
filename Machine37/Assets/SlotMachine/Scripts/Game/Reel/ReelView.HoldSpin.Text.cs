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

            // ★ 诊断（非防御）：基础轮火球出现 FreeSpins 说明数据层异常（A 硬禁 / B 基础轮 allowFreeMode=false）。
            //   不篡改数据，如实显示并告警供定位根因（真正的 A/B 判定看 [ConfigLoad] 日志）。
            if (cell.kind == FireballKind.FreeSpins)
                Debug.LogWarning($"[FireballLabel] ⚠️ 出现 FreeSpins 火球(reel={st.reelIdx} k={k}) → 数据层异常，当前 holdMode 可能非 Direct");

            item.m_type = cell.kind;
            item.m_rate = cell.multiplier;
            // ★ 诊断日志：仅对非法 kind（超出 0~5）告警。multiplier 大小不再作为彩金档推断依据——
            //   用户已把 JSON multipliers 提到 [1,2,3,5,10,20,50,100]，x20/x50/x100 是合法高倍率，
            //   必须显示为 xN，不能因 >10 而回退成 MINI/MINOR/MAJOR/MEGA 造成视觉与 m_type 不一致。
            if ((int)cell.kind < 0 || (int)cell.kind > 5)
                Debug.LogWarning($"[FireballLabel] 非法 kind={(int)cell.kind}({cell.kind}) mult={cell.multiplier} reel={st.reelIdx} k={k} → label={FireballLabel(cell)}");
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
                    // ★ 倍数火球永远显示 xN，不因其数值大而回退成 MINI/MINOR/MAJOR/MEGA。
                    //   用户已将 JSON multipliers 提到 [1,2,3,5,10,20,50,100]，x20/x50/x100 都是合法高倍率。
                    //   彩金档必须且只由 kind=Mini/Minor/Major/Mega 决定，不能由 multiplier 数值推断，否则视觉与 m_type 不一致。
                    return "x" + c.multiplier.ToString("0.##");
                default:
                    // 非法 kind（超出 0~5）：按 multiplier 显示 xN，不再冒充 MINI 等彩金档名。
                    if (c.multiplier <= 0f) return "";
                    return "x" + c.multiplier.ToString("0.##");
            }
        }

        /// <summary>在 go 上显示火球文字：优先用 ReelItem.m_text，缺失时按层级查找子 Text。
        /// ★ 防御性：force-set color=white + enabled=true + raycastTarget=false + 字体保底；保证 prefab
        ///   实例化后任何子物体顺序/font/material 默认值不会让"x3"被 m_fire 遮住或不可见。</summary>
        void ApplyFireballText(GameObject go, FireballCell cell)
        {
            if (go == null || cell == null) return;
            var item = go.GetComponent<ReelItem>();
            var txt = (item != null && item.m_text != null) ? item.m_text : go.GetComponentInChildren<UnityEngine.UI.Text>();
            if (txt == null)
            {
                // ★ 诊断：prefab 缺 m_text / GetComponentInChildren 也找不到 → 该火球无法显示倍率文字
                Debug.LogWarning($"[ApplyFireballText] 无 Text 组件(reelItem={(item!=null)} kind={cell.kind} rate={cell.multiplier})");
                return;
            }
            if (cell.kind == FireballKind.FreeSpins)
            {
                txt.text = "";
                txt.gameObject.SetActive(false);
                txt.enabled = false;
                return;
            }
            string label = FireballLabel(cell);
            // ★ 防御：若 label 为空但火球本身是倍数火球，至少显示"x1"，避免完全没文字。
            if (string.IsNullOrEmpty(label) && cell.kind == FireballKind.Multiplier)
            {
                label = "x1";
                UnityEngine.Debug.LogWarning($"[ApplyFireballText] 倍数火球 label 为空，强制兜底=x1 (kind={cell.kind} mult={cell.multiplier})");
            }
            bool show = !string.IsNullOrEmpty(label);
            txt.text = label;
            txt.gameObject.SetActive(show);
            txt.enabled = show;
            txt.color = Color.white;
            // ★ 防御：避免 prefab 默认值遮蔽文字（半透明/无字体/被 raycastTarget 屏蔽都不至于让 x3 消失）
            var c2 = txt.color; c2.a = 1f; txt.color = c2;
            if (txt.raycastTarget) txt.raycastTarget = false;
            if (txt.font == null) txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (txt.fontSize <= 0) txt.fontSize = 36;
        }

        /// <summary>取火球 overlay 携带的倍率（ReelItem.m_rate）。</summary>
        float GetFireballMult(GameObject go)
        {
            var ri = go.GetComponent<ReelItem>();
            return ri != null ? ri.m_rate : 0f;
        }

        /// <summary>降级兜底：FreeSpins 被强制转为倍数火球时，给一个合理的默认倍率（取配置中间值 1.5）。</summary>
        static float PickMultiplierFallback() => 1.5f;
    }
}
