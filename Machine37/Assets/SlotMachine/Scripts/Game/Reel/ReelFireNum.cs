using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    /// <summary>单列火球倒计时计数器（ReelFireNum）。
    ///
    /// ★ 显示 / 统计组件（用户拍板 2026-07-25）：本组件【单向】由游戏逻辑写入（SetCount），
    ///   倒计时 3→2→1→0 由 HoldSpinState.counter 镜像显示（跨局持有：每开一局减一，不滚盘）。
    ///   释放判定现由逻辑层(GameSession.AdvanceHoldBoard)按"该列倒计时归零且未集满"直接驱动（不再回读 m_engaged），
    ///   使"圈圈显示"与"火球离场"彻底同步；m_engaged 仅作兜底（m_num<=0 时被 CheckEngaged 清掉）。
    ///
    /// 极简模型（用户拍板，2026-07-25 修正）：
    ///   m_active 是【整个 Hold&amp;Spin 会话】的开关（进入=true，开新局=false），不能区分“哪一列有火球”。
    ///   因此新增列级状态 m_engaged = 该列【曾/现有火球】。
    ///   显示：① 进入 Hold&amp;Spin 且 该列【曾/现有火球】(engaged) → 显示倒计时圈(3→2→1→0，0=空圈静止帧)；
    ///         ② 该列【有倍率】(rate&gt;0) → 显示 X 文本（即使从未有火球）。
    ///   隐藏：③ 开新的一局（ResetAll 将 active、engaged、num、rate 全归零 → 隐藏）。
    ///   即 visible = active &amp;&amp; (engaged || rate &gt; 0)。
    ///
    ///   ※ 关键修复：之前用 active &amp;&amp; (num&gt;=0 || rate&gt;0)，因 num&gt;=0 对非负 int 恒真，
    ///     且 active 对所有列同时置 true，导致【没有任何火球的列】也显示空面板——本次用 m_engaged 解决。
    ///
    ///   数据清零时机：结算完成【不清零】——num/rate 保留显示到玩家按确认开新局；
    ///            开新局 ResetAll() 才把状态归零并彻底隐藏（含满列 X 倍列）。
    ///
    /// 外部只调（均为"游戏→显示"单向写入）：Activate / SetCount / ResetMultiplier / ResetAll。</summary>
    public class ReelFireNum : MonoBehaviour
    {

        public bool m_active;         // 是否在 Hold&Spin 周期（进入=true，开新局=false）——会话级门控
        public bool m_engaged;       // 该列【曾/现有火球】——列级门控（active 不能区分有无火球，故单列此标志）

        public int m_num;              // 倒计时圈数（0..N），仅显示用
        public float m_rate;           // 累计倍数火球倍率（彩金档不计入）
        public Text m_text;            // "X倍率" 文本
        public Image[] m_items;        // 倒计时圈（N 个）

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
            gameObject.SetActive(false);
        }

        /// <summary>进入 Hold&Spin / Mini：开启会话级门控(active=true)。
        /// 注意：不在此处 engaged 任何列——只有真正拿到火球的列才应显示（见 SetCount）。</summary>
        public void Activate()
        {
            m_active = true;
            Refresh();
        }

        /// <summary>设置倒计时圈数（0..N；0=空圈静止帧，仍可见）。
        /// 首次拿到火球(count&gt;0)即标记 engaged，之后即便 count 落到 0（满列掉落/释放）也一直保持显示到开新局。</summary>
        public void SetCount(int count)
        {
            if (count > 0)
                m_engaged = true;

            m_num = count;
            Refresh();
        }

        /// <summary>复位倍率并激活（清文本、恢复满圈）：用于 Mini 重置计数器显示 / 重建有火球的列。
        /// 该列确实有火球，故标记 engaged=true（不会让无火球的列误显）。</summary>
        public void ResetMultiplier()
        {
            m_engaged = true;
            m_rate = 0f;
            m_num = (m_items != null) ? m_items.Length : 0;
            Refresh();
        }

        /// <summary>开新的一局（按确认 / 开新局统一入口，Hold&amp;Spin 结算后开新局与正常基础局都走这）：
        /// 先把 active、rate 关掉，再按【当前真实】 m_num 判断——m_num&lt;=0（倒计时已归零/无火球）则清 engaged，
        /// 最后才把 m_num 归零。这样守卫检查的是归零前的真实状态，不恒真。</summary>
        public void ResetAll()
        {
            m_active = false;
            m_engaged = false;   // ★ 兜底：开新局必须清 engaged，否则超时未释放列的陈旧 engaged 会泄漏进下一局 Hold&Spin 误显空面板
            m_rate = 0f;
            m_num = 0;
            Refresh();
        }

        /// <summary>纯检查：按【当前真实】 m_num 同步 m_engaged（m_num&lt;=0 即视为“无火球”，清 engaged）。
        /// 不动 m_active、不整体隐藏——只清“曾有过火球”标记，并立即 Refresh 让可见性生效。
        /// OnStartKey 每次按确认都在最顶部先调它，保证 100% 执行（任何分支提前 return 都拦不住）。</summary>
        public void CheckEngaged()
        {
            // ★ 用户口径：每次按确认(含 Hold&Spin 每轮 respin 推进)都是"新的一局"→ 重算。
            //   所以这里先把 m_rate=0 / 按 num 重判 m_engaged，随后由本局 SetRespinCounterRow/AddMultiplier 重建显示；
            //   最终结算后"开新基础局"那次确认由 HideAllCounters→ResetAll(m_active=false) 负责隐藏，不靠这里清零。
            m_rate = 0;
            if (m_num <= 0)
                m_engaged = false;
            Refresh();
        }

        void Refresh()
        {
            // ★ 可见性（用户口径 2026-07-25 修正）：
            //   m_active     = Hold&Spin 会话门控（进入=true，开新局=false），对所有列同时生效，不能区分有无火球。
            //   m_engaged    = 该列【曾/现有火球】，列级门控；首次拿到火球(count>0)置 true，落到 0 也保持，直到 ResetAll。
            //   m_rate>0     = 该列有累计倍率（即便从未有火球也显示 X 文本）。
            //   → show = active && (engaged || rate>0)
            //   这样：无火球且无倍率的列永远不会显示；有火球的列 3→2→1→0 全程显示（含归0空面板，直到开新局）。
            bool show = m_active && (m_engaged || m_rate > 0f);
            gameObject.SetActive(show);
            if (!show) return;

            bool showText = m_rate > 0f;
            bool showCircles = !showText && m_num > 0;   // 有倍率时优先显文本；否则按 num 亮圈
            bool showZero = !showText && m_num <= 0;      // 圈圈归零(0)：显示“0”文本，体现 3→2→1→0 的最终态

            if (m_items != null)
                for (int i = 0; i < m_items.Length; i++)
                    if (m_items[i] != null) m_items[i].gameObject.SetActive(showCircles && i < m_num);

            if (m_text != null)
            {
                if (showText)
                {
                    m_text.text = "X" + m_rate.ToString("0.##");
                    m_text.gameObject.SetActive(true);
                }
                else if (showZero)
                {
                    m_text.text = "0";
                    m_text.gameObject.SetActive(true);
                }
                else m_text.gameObject.SetActive(false);
            }
        }
    }
}
