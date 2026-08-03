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
        ///   1) Mecanim：当前播放状态 normalizedTime ≥ 0.98（non-loop 演出，播完停最后一帧）视为完成；
        ///   2) UIImageAnimator 序列帧：叠加一次完整序列时长（frames/fps）；
        ///   3) 超时兜底（est×1.5，限 4~8s）：防止 clip 为 loop 或取不到时长时永久阻塞。
        /// 用于满列收集演出后阻塞流程，直到动画播完再进 Mini（替换"估算时长 WaitForSeconds"，更可靠）。</summary>
        public IEnumerator WaitDone()
        {
            float est = PlayDuration();
            float timeout = Mathf.Clamp(est * 1.5f, 4f, 8f);
            float t0 = Time.time;

            // 1) Mecanim 当前状态播完检测（non-loop 下 normalizedTime 从 0→1）
            if (m_ani != null && m_ani.runtimeAnimatorController != null && m_ani.isActiveAndEnabled)
            {
                var st = m_ani.GetCurrentAnimatorStateInfo(0);
                if (st.length > 0f)
                    yield return new WaitUntil(() =>
                        m_ani.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.98f
                        || (Time.time - t0 > timeout));
            }

            // 2) UIImageAnimator 序列帧：完整播一次
            if (m_uiAnis != null)
            {
                float maxUi = 0f;
                foreach (var u in m_uiAnis)
                    if (u != null && u.frames != null && u.frames.Length > 0)
                        maxUi = Mathf.Max(maxUi, u.frames.Length / Mathf.Max(u.fps, 1f));
                if (maxUi > 0f) yield return new WaitForSeconds(maxUi);
            }

            // 3) 最终兜底：不超过 timeout
            float remain = timeout - (Time.time - t0);
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
