using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    public class ReelItem : MonoBehaviour
    {
        /// <summary>火球类型（彩金类型）：普通符号=Multiplier(0)；火球=倍数火球或 Mini/Minor/Major/Mega 彩金火球。
        /// 仅在 ShowFire 火球时由视图层写入，用于区分火球种类（如不同颜色/特效）。</summary>
        public FireballKind m_type = FireballKind.Multiplier;
        public float m_rate;
        public int m_id;
        public int m_serial = -1;   // 创建时全局自增编号，用于追踪/调试（区分每个 Item(Clone)）

        public Image m_image;
        public GameObject m_fire;
        public GameObject m_freeFire;
        public GameObject m_effect;

        public GameObject m_starfihs;
        public GameObject m_fish;
        public GameObject m_octopus;
        public GameObject m_wild;
        public GameObject m_scatter;

        public Text m_text;

        /// <summary>是否为火球：是则显示火球对象、隐藏普通图标 m_image。
        /// freeFire=true（FreeSpins 免费游戏）时优先亮 m_freeFire；若 prefab 未配置 m_freeFire（图形缺失）则
        /// 退化为普通火球图形 m_fire，避免火球整颗变空白“消失”。文字由 ApplyFireballText 处理（显 "FREE"）。
        /// 非火球：两个火球对象都隐藏、显示普通图标 m_image。
        /// ★ 双保险：m_image 隐藏时同时设 enabled=false + gameObject.SetActive(false)，确保 Iconimate 子 GameObject
        ///   整体停掉渲染（包括子组件如 UIImageAnimator），火球底下完全不露普通符号。</summary>
        public void ShowFire(bool isFire, bool freeFire = false)
        {
            // ★ FreeSpins 优先用 m_freeFire；未配置则退化为 m_fire（保证可见）
            bool useFree = freeFire && m_freeFire != null;
            if (m_fire != null) m_fire.SetActive(isFire && !useFree);
            if (m_freeFire != null) m_freeFire.SetActive(isFire && useFree);
            if (m_image != null)
            {
                m_image.enabled = !isFire;
                // ★ 双保险：火球时整个 Image GameObject 停掉（避免底层 m_image 子组件任何残留渲染透出到火球底下）
                if (m_image.gameObject != null)
                    m_image.gameObject.SetActive(!isFire);
            }
            // 免费游戏时隐藏 m_text；主游戏 Hold&Spin 的 "FREE" 文字由 ApplyFireballText 重新显示。
            if (m_text != null && freeFire)
                m_text.gameObject.SetActive(false);
        }

        /// <summary>中奖时显示专属美术（starfish/fish/octopus/wild/scatter，对应 paytable id 7/8/9/10/11），
        /// 并隐藏普通图标 m_image。仅这 5 个特殊符号有专属中奖美术。
        /// 返回 true = 命中并接管中奖表现（调用方不要再对 m_image 播 _2 帧动画）；false = 该符号无专属美术，走默认 m_image 帧动画。</summary>
        public bool ShowWinArt(int symId)
        {
            GameObject art = null;
            switch (symId)
            {
                case 7:  art = m_starfihs; break;   // Starfish
                case 8:  art = m_fish;     break;   // Fish
                case 9:  art = m_octopus;  break;   // Octopus
                case 10: art = m_wild;     break;   // Wild
                case 11: art = m_scatter;  break;   // Scatter
            }
            if (art == null) return false;   // 无专属美术 → 交给默认 m_image 帧动画
            art.SetActive(true);
            if (m_image != null) m_image.enabled = false;
            return true;
        }

        /// <summary>还原：隐藏全部专属美术、恢复普通 m_image 显示（中奖高亮清除时调用）。</summary>
        public void HideWinArt()
        {
            if (m_starfihs != null) m_starfihs.SetActive(false);
            if (m_fish != null) m_fish.SetActive(false);
            if (m_octopus != null) m_octopus.SetActive(false);
            if (m_wild != null) m_wild.SetActive(false);
            if (m_scatter != null) m_scatter.SetActive(false);
            if (m_image != null) m_image.enabled = true;
        }
    }
}
