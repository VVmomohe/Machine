using System;
using UnityEngine;
using SlotMachine.Core;

namespace com.slot
{
    /// <summary>
    /// 三七机运行时控制器（MonoBehaviour）。
    /// 逻辑层(Core)与表现层解耦：Spin() 产出 GameResult 交给 GameManager 渲染。
    /// 配置从 Resources/Configs/*.json 加载。
    /// </summary>
    public class SlotMachine : MonoBehaviour
    {

        [Header("配置(JSON, 放 Resources/Configs)")]
        public TextAsset configText;

        [Header("运行态")]
        [NonSerialized] public ReelConfig config;   // 不序列化：避免 Inspector 残留空 ReelConfig 导致 LoadConfig 被跳过
        public ISlotRng rng = new UnityRng();
        public GameSession session;

        [Header("押注")]
        public float totalBet = 1f;

        void Awake()
        {
            if (configText == null) configText = Resources.Load<TextAsset>("Configs/modeB_44668");
            if (configText != null) config = LoadConfig(configText);   // 强制从 JSON 重载，不依赖 config 是否已序列化
            ApplyConfig();
        }

        void ApplyConfig()
        {
            if (config == null) return;
            session = new GameSession(config, rng);
        }

        public static ReelConfig LoadConfig(TextAsset t)
        {
            if (t == null) return null;
            try
            {
                // 用 Newtonsoft.Json：Unity 自带 JsonUtility 不支持嵌套列表 List<List<int>> / int[][]
                return Newtonsoft.Json.JsonConvert.DeserializeObject<ReelConfig>(t.text);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[LoadConfig] JsonConvert FAILED: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        /// <summary>转一次（含火球/FireLink/奖池/免费转），返回结果并广播事件。</summary>
        public GameResult Spin()
        {
            if (config == null || session == null) return null;
            return session.Play(totalBet);
        }
    }
}
