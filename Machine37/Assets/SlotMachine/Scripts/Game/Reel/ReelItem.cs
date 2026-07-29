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

        public Text m_text;

        /// <summary>是否为火球：是则显示火球对象、隐藏普通图标 m_image。
        /// freeFire=true（FreeSpins 免费游戏）时优先亮 m_freeFire；若 prefab 未配置 m_freeFire（图形缺失）则
        /// 退化为普通火球图形 m_fire，避免火球整颗变空白“消失”。文字由 ApplyFireballText 处理（显 "FREE"）。
        /// 非火球：两个火球对象都隐藏、显示普通图标 m_image。</summary>
        public void ShowFire(bool isFire, bool freeFire = false)
        {
            // ★ FreeSpins 优先用 m_freeFire；未配置则退化为 m_fire（保证可见）
            bool useFree = freeFire && m_freeFire != null;
            if (m_fire != null) m_fire.SetActive(isFire && !useFree);
            if (m_freeFire != null) m_freeFire.SetActive(isFire && useFree);
            if (m_image != null) m_image.enabled = !isFire;
            // 免费游戏时隐藏 m_text；主游戏 Hold&Spin 的 "FREE" 文字由 ApplyFireballText 重新显示。
            if (m_text != null && freeFire)
                m_text.gameObject.SetActive(false);
        }
    }
}
