using System;
using System.Collections;
using UnityEngine;

namespace com.slot
{
    /// <summary>
    /// 数字滚动动画驱动器（单例）。
    /// 把"按 0→1 进度插值驱动显示"的逻辑从具体面板抽出来，任何需要数字跳动的地方都能复用：
    ///   CreditRoller.Instance.Roll(duration, t => { ...更新显示... }, onDone, lead);
    /// 只跑一个动画；新 Roll 会先停掉上一个（调用方负责在启动新滚动前 Finalize 旧状态，避免丢值）。
    /// coroutine 必须挂在 MonoBehaviour 上，故用单例 MonoBehaviour 而非纯静态类。
    /// </summary>
    public class CreditRoller : MonoBehaviour
    {
        private static CreditRoller _instance;
        public static CreditRoller Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("CreditRoller");
                    _instance = go.AddComponent<CreditRoller>();
                    // 注意：不要 DontDestroyOnLoad。本工程单场景，若标了跨场景保留，
                    // 退出 Play Mode 时该对象不属于场景、无法被清理，
                    // 会触发 "Some objects were not cleaned up when closing the scene" 警告。
                }
                return _instance;
            }
        }

        /// <summary>安全停止：仅在已存在实例时停止，不会触发懒创建（避免在 OnDestroy/场景关闭时意外 new GameObject）。</summary>
        public static void StopIfAny()
        {
            if (_instance != null) _instance.Stop();
        }

        private void OnDestroy()
        {
            // 复位静态引用，避免退出 Play Mode 后 _instance 指向已销毁对象（悬空引用）。
            if (_instance == this) _instance = null;
        }

        private Coroutine _co;

        /// <summary>
        /// 进度 0→1 滚动动画。
        /// onTick 每帧收到当前进度(0..1)；duration 秒后 onDone 触发。
        /// lead&gt;0 时先等待 lead 秒再起步（给收分音起拍，参 PandaParadis 的 HarvestSoundLead）。
        /// 新调用会先停掉进行中的动画。
        /// </summary>
        public void Roll(float duration, Action<float> onTick, Action onDone = null, float lead = 0f)
        {
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(CoRoll(duration, onTick, onDone, lead));
        }

        /// <summary>是否正在滚动中。</summary>
        public bool IsRolling => _co != null;

        /// <summary>强制停止当前滚动（不触发 onDone；如需落账由调用方 Finalize）。</summary>
        public void Stop()
        {
            if (_co != null) { StopCoroutine(_co); _co = null; }
        }

        private IEnumerator CoRoll(float duration, Action<float> onTick, Action onDone, float lead)
        {
            if (lead > 0f) yield return new WaitForSeconds(lead);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                onTick?.Invoke(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            onTick?.Invoke(1f);
            _co = null;
            onDone?.Invoke();
        }

        /// <summary>
        /// 按 value 对数计算滚动时长(0.5s~8s)。
        /// 锚点(log10): 100→1s, 1000→2s, 10000→3s, 100000→5s, 1000000→8s。
        /// </summary>
        public static float DurationFor(long value)
        {
            // 最小 0.8s，保证小赢(如奖池 Mini)也能看清"跳动"
            if (value <= 5) return 0.8f;
            float log = Mathf.Log10((float)value);
            float dur = (log < 4f) ? (log - 0.5f)
                       : (log < 5f) ? (2f * log - 5f)
                                    : (3f * log - 10f);
            return Mathf.Clamp(dur, 0.8f, 8f);
        }
    }
}
