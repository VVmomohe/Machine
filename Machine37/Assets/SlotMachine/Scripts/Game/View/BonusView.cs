using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;   // GameResult / ReelConfig

namespace com.slot
{
    /// <summary>
    /// 彩金(jackpot)面板：对应 Fire Link / Cash Falls 类玩法顶部的 MINI / MINOR / MAJOR / MEGA 四档进度奖池。
    ///
    /// 表现：四档文本常显当前奖池值（整数，无小数位）；某档中奖时做"放大脉冲"高亮(不变色)。
    /// 数据：四档彩金值由 GameSession 计算（= 有效压分×betMult + potRate×局数），本类只负责显示与表现，
    ///       不持有任何彩金状态。每次池变化（Contribute/RefreshPots/ResetJackpot）由 OnPotsChanged 回调触发 ShowPots。
    /// </summary>
    public class BonusView : MonoBehaviour
    {
        [Header("四档彩金文本(对应 Mini/Minor/Major/Mega)")]
        public Text m_miniText;
        public Text m_minorText;
        public Text m_majorText;
        public Text m_megaText;

        public GameObject m_miniEffect;
        public GameObject m_minorEffect;
        public GameObject m_majorEffect;
        public GameObject m_megaEffect;

        [Header("中奖高亮表现")]
        public float m_pulseScale = 1.4f;     // 中奖时放大倍数
        public float m_pulseDuration = 0.6f;  // 单次脉冲时长(放大半 + 缩回半)

        [Header("中奖金额弹窗")]
        [Tooltip("可选：拖一个 Text 进来专门显示奖金额；留空则运行时自动在对应档文本下生成")]
        public Text m_awardText;
        public float m_awardShowTime = 2.2f;  // 弹窗停留时长(秒)
        public float m_awardScale = 1.8f;     // 弹窗放大峰值

        // 档名 -> 文本
        private Dictionary<string, Text> _textByTier;
        // 档位文本的原生 base scale（Awake 记录，脉冲/弹窗放大后必须复位到此，而非 Vector3.one）
        private Dictionary<Text, Vector3> _nativeScale;
        // Pulse 防中断：每个 Text 最多跑一个 Pulse，新的取消旧的
        private Dictionary<Text, Coroutine> _pulseCoroutines;
        // 彩金特效：FireballKind → GameObject
        private Dictionary<FireballKind, GameObject> _effectByKind;
        // ★ 当前中奖档集合（持久显示，直到开新局 HideAllJackpotEffects 清空）。各档特效同时播放，开新局统一隐藏。
        private HashSet<FireballKind> _wonTiers = new HashSet<FireballKind>();

        void Awake()
        {
            _textByTier = new Dictionary<string, Text>
            {
                { "Mini",  m_miniText },
                { "Minor", m_minorText },
                { "Major", m_majorText },
                { "Mega",  m_megaText },
            };
            _nativeScale = new Dictionary<Text, Vector3>();
            foreach (var t in _textByTier.Values)
                if (t != null) _nativeScale[t] = t.transform.localScale;
            _pulseCoroutines = new Dictionary<Text, Coroutine>();
            _effectByKind = new Dictionary<FireballKind, GameObject>
            {
                { FireballKind.Mini,  m_miniEffect },
                { FireballKind.Minor, m_minorEffect },
                { FireballKind.Major, m_majorEffect },
                { FireballKind.Mega,  m_megaEffect },
            };
        }

        // ============== 逻辑：刷新四档显示值 ==============

        /// <summary>
        /// 显示渐进奖池的当前累积值（每次下注后增长，中奖后重置为该档基数）。
        /// pots 来自 GameSession.Pots，由 GameManager 每注后推送。
        /// </summary>
        public void ShowPots(IReadOnlyDictionary<string,float> pots)
        {
            if (pots == null) return;
            foreach (var kv in pots)
                if (_textByTier.TryGetValue(kv.Key, out var t) && t != null)
                {
                    t.transform.localScale = GetNativeScale(t);   // 防御：脉冲/弹窗放大后强制复位到原生 base scale
                    t.text = FormatValue(kv.Value);
                    //UnityEngine.Debug.Log($"[ShowPots] tier={kv.Key} 显示={FormatValue(kv.Value)} (raw={kv.Value:F4})");
                }
        }

        /// <summary>静态兜底：无渐进池时按 tier.value × bet 显示。当前主流程用 ShowPots。</summary>
        public void Refresh(ReelConfig cfg, float bet)
        {
            if (cfg == null || cfg.jackpots == null) return;
            foreach (var j in cfg.jackpots)
            {
                if (_textByTier.TryGetValue(j.tier, out var t) && t != null)
                {
                    float val = j.value * (j.valueIsMultiplier ? bet : 1f);
                    t.text = FormatValue(val);
                }
            }
        }

        // ============== 表现：中奖档高亮 ==============

        /// <summary>播放某一档中奖表现(放大脉冲, 不变色)。tierName 须与 cfg.jackpots[].tier 对应。</summary>
        public void PlayJackpot(string tierName)
        {
            if (!_textByTier.TryGetValue(tierName, out var t) || t == null) return;
            // 防中断：取消同 Text 上一次未完成的 Pulse（避免 scale 停在放大状态）
            if (_pulseCoroutines.TryGetValue(t, out var old) && old != null)
                StopCoroutine(old);
            _pulseCoroutines[t] = StartCoroutine(Pulse(t));
        }

        /// <summary>播放一整次 GameResult 的全部中奖档(预留入口，当前收集盘玩法不产生奖池中奖)。</summary>
        public void PlayJackpots(GameResult result)
        {
            if (result == null) return;
            // 收集盘玩法不产生分档奖池中奖，预留入口供后续扩展
        }

        // ============== 彩金特效 ==============

        /// <summary>
        /// 记录某档彩金中奖，加入中奖档集合并由轮播协程负责展示。
        /// 多档同中（如同时中 Mini+Minor）时，轮播协程会一档一档轮流展示，确保每档都播到（避免四档全屏特效堆叠只显顶层）。
        /// 单档时集合只有一档→轮播恒显该档=持续播，直到开新局 HideAllJackpotEffects 清空。
        /// 同档重复中奖只加一次（幂等）。
        /// </summary>
        public void ShowJackpotEffect(FireballKind kind, float duration = 2.5f, bool persistent = false)
        {
            if (_effectByKind == null) return;
            if (!_effectByKind.TryGetValue(kind, out var go) || go == null)
            {
                // ★ 诊断：若某档 GameObject 未绑定，日志会明确指出——用于排查"只播某档"是否因 Minor/Major/Mega 未绑。
                UnityEngine.Debug.LogWarning($"[ShowJackpotEffect] kind={kind} 特效GameObject未绑定(Inspector m_{kind}Effect 为空)！跳过播放（若为'只播某档'根因，请在 Inspector 绑定该特效）");
                return;
            }

            bool added = _wonTiers.Add(kind);
            if (added)
                UnityEngine.Debug.Log($"[ShowJackpotEffect] kind={kind} 已激活 (同时播放模式，不再轮播；开新局 HideAllJackpotEffects 才隐藏)");
            // ★ 直接激活该档特效：特效本身是 loop 的（自动循环播放）。多档同中时各自独立 SetActive(true)，
            //   可同时叠加显示（不再隐藏其它档、不再轮播排队）。
            go.SetActive(true);
        }

        /// <summary>开新局(OnStartKey / EnterHoldSpin)统一隐藏全部彩金特效——与 ReelFireNum.HideAllCounters 同一时机(开新局才隐藏)。
        /// 清空中奖档集合并停掉轮播协程，防竞态把已隐藏的特效又打开。</summary>
        public void HideAllJackpotEffects()
        {
            UnityEngine.Debug.Log($"[HideAllJackpotEffects] 开始 _effectByKind=({(_effectByKind!=null?_effectByKind.Count.ToString():"null")})");
            if (_effectByKind == null)
            {
                UnityEngine.Debug.LogWarning("[HideAllJackpotEffects] _effectByKind==null! Awake 可能未执行或 BonusView 未初始化");
                return;
            }

            // ★ 清空中奖档集合（必须在隐藏 GameObject 之前）
            _wonTiers.Clear();

            int hidden = 0, nullCount = 0;
            foreach (var kv in _effectByKind)
            {
                if (kv.Value != null)
                {
                    var wasActive = kv.Value.activeSelf;
                    kv.Value.SetActive(false);
                    if (wasActive) hidden++;
                }
                else
                {
                    nullCount++;
                }
            }

            UnityEngine.Debug.Log($"[HideAllJackpotEffects] 隐藏全部彩金特效 (开新局, hidden={hidden}, null={nullCount})");
        }

        /// <summary>
        /// 弹出某档中奖金额（如 "MINI +12.34"），放大后淡出。金额从引擎回传(已折算 bet)，
        /// 让玩家清楚看到"奖池分"落进了总分。该弹窗不影响 pots 文本(由 ShowPots 刷新)。
        /// </summary>
        public void ShowJackpotAward(string tierName, float amount)
        {
            if (!_textByTier.TryGetValue(tierName, out var anchor) || anchor == null) return;

            Text award;
            Transform parent = anchor.transform.parent != null ? anchor.transform.parent : anchor.transform;
            if (m_awardText != null)
            {
                award = m_awardText;
                award.gameObject.SetActive(true);
                award.transform.SetParent(parent, false);
            }
            else
            {
                var go = new GameObject("JackpotAward_" + tierName);
                go.transform.SetParent(parent, false);
                award = go.AddComponent<Text>();
                // 继承锚点档文本的同款字体/对齐，保证弹窗和面板一致
                award.font = anchor.font;
                award.alignment = TextAnchor.MiddleCenter;
                award.color = new Color(1f, 0.95f, 0.4f);   // 金色，区别于常规文本
                award.fontSize = anchor.fontSize > 0 ? anchor.fontSize : 36;
            }

            award.text = $"{tierName}\n+{amount:F2}";
            award.rectTransform.anchoredPosition = anchor.rectTransform.anchoredPosition + Vector2.up * 40f;
            StartCoroutine(AwardPopup(award, m_awardText == null));
        }

        private IEnumerator AwardPopup(Text award, bool destroyWhenDone)
        {
            if (award == null) yield break;
            float half = m_awardShowTime * 0.35f;
            // 放大 + 淡入
            float e = 0f;
            while (e < half)
            {
                e += Time.deltaTime;
                float k = Mathf.Clamp01(e / half);
                award.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, m_awardScale, k);
                Color c = award.color; c.a = k; award.color = c;
                yield return null;
            }
            // 停留
            yield return new WaitForSeconds(m_awardShowTime - 2f * half);
            // 淡出 + 缩回(scale 一起收，避免永久放大)
            e = 0f;
            while (e < half)
            {
                e += Time.deltaTime;
                float k = Mathf.Clamp01(e / half);
                award.transform.localScale = Vector3.one * Mathf.Lerp(m_awardScale, 1f, k);
                Color c = award.color; c.a = 1f - k; award.color = c;
                yield return null;
            }
            // 无论复用还是新建，都强制复位缩放，防止字体停在放大状态回不去
            award.transform.localScale = GetNativeScale(award);
            if (destroyWhenDone)
            {
                Destroy(award.gameObject);
            }
            else if (m_awardText != null)
            {
                // 复用的 Text：复位 alpha 并隐藏，下次显示才是正常大小/不透明
                Color c = m_awardText.color; c.a = 1f; m_awardText.color = c;
                m_awardText.gameObject.SetActive(false);
            }
        }

        private IEnumerator Pulse(Text t)
        {
            Vector3 baseScale = GetNativeScale(t);   // 以原生 scale 为基准脉冲，而非强行 1
            float half = m_pulseDuration * 0.5f;
            // 放大
            float e = 0f;
            while (e < half)
            {
                e += Time.deltaTime;
                float k = Mathf.Clamp01(e / half);
                t.transform.localScale = baseScale * Mathf.Lerp(1f, m_pulseScale, k);
                yield return null;
            }
            // 缩回
            e = 0f;
            while (e < half)
            {
                e += Time.deltaTime;
                float k = Mathf.Clamp01(e / half);
                t.transform.localScale = baseScale * Mathf.Lerp(m_pulseScale, 1f, k);
                yield return null;
            }
            t.transform.localScale = baseScale;
            _pulseCoroutines.Remove(t);   // 清理引用
        }

        /// <summary>取文本原生 base scale（Awake 记录）；未登记者回退 Vector3.one。</summary>
        private Vector3 GetNativeScale(Text t)
        {
            return (_nativeScale != null && _nativeScale.TryGetValue(t, out var s)) ? s : Vector3.one;
        }

        // ============== 工具 ==============

        private static string FormatValue(float v)
        {
            // 彩金显示整数（无小数位）；累积仍用小数精度，只在显示时截断。
            return ((int)System.Math.Floor(v)).ToString("N0");
        }
    }
}
