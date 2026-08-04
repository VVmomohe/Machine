using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.slot
{
    public class ReelTong : MonoBehaviour
    {

        public Animator m_ani;
        public GameObject m_light;
        public UIImageAnimator[] m_uiAnis;

        // 防快速重复触发：只有最新一次 Play 的协程才允许隐藏 m_light
        private int _playToken;

        // Start is called before the first frame update
        void Start()
        {
            // 起手隐藏光效：仅在播放时才显示
            if (m_light != null) m_light.SetActive(false);
        }

        /// <summary>重新播放 1 次（火球落下时调用）：Mecanim 动画从当前状态第 0 帧重启，
        /// 每个 UIImageAnimator 序列帧也从第 0 帧重播。播放期间显示 m_light，播完自动隐藏。</summary>
        public void Play()
        {
            int token = ++_playToken;

            // 播放时显示光效
            if (m_light != null) m_light.SetActive(true);

            if (m_ani != null && m_ani.runtimeAnimatorController != null)
            {
                m_ani.enabled = true;
                var st = m_ani.GetCurrentAnimatorStateInfo(0);
                if (st.fullPathHash != 0)
                    m_ani.Play(st.fullPathHash, 0, 0f);   // 当前状态从第 0 帧重播
                else
                    m_ani.Play("", 0, 0f);                 // 兜底：播默认/入场状态
            }
            if (m_uiAnis != null)
            {
                foreach (var u in m_uiAnis)
                    if (u != null) u.Restart();            // 序列帧从第 0 帧重播
            }

            // 播完（按动画时长）隐藏光效
            StartCoroutine(HideLightAfter(token, PlayDuration()));
        }

        /// <summary>动画时长 = Mecanim 最长 clip 与 各 UIImageAnimator 序列帧时长 的最大值（至少 0.1s）。
        /// ★ public：满列收集演出需要在流程层"等这段动画播完"再进 Mini（见 GameManager.SettleBaseB）。</summary>
        public float PlayDuration()
        {
            float dur = 0.1f;
            if (m_ani != null && m_ani.runtimeAnimatorController != null)
            {
                var clips = m_ani.runtimeAnimatorController.animationClips;
                if (clips != null)
                    foreach (var c in clips)
                        if (c != null && c.length > dur) dur = c.length;
            }
            if (m_uiAnis != null)
            {
                foreach (var u in m_uiAnis)
                {
                    if (u != null && u.frames != null && u.frames.Length > 0)
                    {
                        float d = u.frames.Length / Mathf.Max(u.fps, 1f);
                        if (d > dur) dur = d;
                    }
                }
            }
            return dur;
        }

        /// <summary>协程：等到本列 tong 演出【真正播完】再返回。
        ///   Play() 时 Mecanim 与 UIImageAnimator 序列帧【并行】从第 0 帧起播，故到 PlayDuration()(=max(二者)) 时两者都已放完。
        ///   - 仅等待 Mecanim 播完检测 + 确保 est 结束(+0.15s 保险)；不再"额外串行等一遍 UI 序列帧"，也不补 4~8s 超时（旧逻辑会白等数秒）。
        ///   - 超时保险仅作兜底：est×1.3，封顶 1.6s（防 clip 为 loop / 取不到时长时永久阻塞）。
        /// 用于满列收集演出后阻塞流程，直到动画播完再进 Mini。</summary>
        public IEnumerator WaitDone()
        {
            float est = PlayDuration();                                   // = max(Mecanim最长clip, UI序列帧)，二者在 Play() 时并行起播
            float timeout = Mathf.Clamp(est * 1.3f, 0.8f, 1.6f);          // 仅作保险：正常 ~est 即放行；异常(loop/取不到时长)时封顶 1.6s
            float t0 = Time.time;

            // 1) Mecanim 当前状态播完检测（non-loop 下 normalizedTime 从 0→1；loop 时到不了 0.98，由 timeout 放行）
            if (m_ani != null && m_ani.runtimeAnimatorController != null && m_ani.isActiveAndEnabled)
            {
                var st = m_ani.GetCurrentAnimatorStateInfo(0);
                if (st.length > 0f)
                    yield return new WaitUntil(() =>
                        m_ani.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.98f
                        || (Time.time - t0 > timeout));
            }

            // 2) UI 序列帧与 Mecanim 在 Play() 时并行起播，到 est 时已同时放完；
            //    只确保 est 结束(+0.15s 保险)即放行，不再重复等一遍、也不补 4~8s 超时兜底。
            float remain = (est + 0.15f) - (Time.time - t0);
            if (remain > 0f) yield return new WaitForSeconds(remain);
        }

        IEnumerator HideLightAfter(int token, float sec)
        {
            yield return new WaitForSeconds(sec);
            // 只隐藏"最新一次 Play"对应的光效（防止快速重播时旧协程误隐藏）
            if (token == _playToken && m_light != null) m_light.SetActive(false);
        }
    }
}
