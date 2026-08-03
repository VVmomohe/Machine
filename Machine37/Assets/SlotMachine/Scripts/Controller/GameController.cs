using Com.Back;
using UnityEngine;
using com.slot;
using Com.MagicBeans;

namespace Com.Controller
{
    /// <summary>
    /// 全局游戏控制器：负责生命周期与存档。
    /// 键盘输入采集已拆分到 InputManager；本类转发 m_keys 以兼容旧调用方式：
    ///   if (GameController.Instance.m_keys[(int)InputAction.Confirm] == (int)InputPhase.Down) { ... }
    /// 新代码建议直接用 InputManager.Instance.m_keys[...]。
    /// </summary>
    public class GameController : MonoBehaviour
    {
        private static GameController _instance;
        public static GameController Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new GameObject("GameController").AddComponent<GameController>();
                }
                return _instance;
            }
        }

        /// <summary>按键状态数组（转发到 InputManager，保留旧调用方式兼容）。</summary>
        public int[] m_keys => InputManager.Instance.m_keys;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // 确保输入采集器存在（未挂到场景时自动创建）
            var _ = InputManager.Instance;
        }

        /// <summary>MCU 推送的「投币键」key_id。协议 6.10 未定义 key_id 枚举，值待硬件方/协议附录确认；联调按实际 MCU 推送填写。</summary>
        private const byte KEY_COIN_IN = 0x01;
        private bool _coinKeySubscribed;

        void Update()
        {
            // 懒订阅 MCU 投币按键（等待 MagicBeansBridge 初始化好 comm 实例）
            if (!_coinKeySubscribed && MagicBeansBridge.Instance != null && MagicBeansBridge.Instance.comm != null)
            {
                MagicBeansBridge.Instance.comm.OnKey += OnMcuKey;
                _coinKeySubscribed = true;
            }

            if (m_keys[(int)InputAction.DebugUpCoin] == (int)InputPhase.Down)
            {
                var gm = GameManager.Instance;
                if (gm != null && !gm.IsBusy() && gm.m_player != null)
                    gm.m_player.AddCredits(100);
                else if (gm != null && gm.IsBusy())
                    Debug.Log("[GameController] F3 调试上分被忽略：游戏进行中/结算中禁止压分(上分)");
            }

            // F12 进入 / 退出后台（插件原生：MagicMenu 自动托管各屏为独立菜单）
            if (m_keys[(int)InputAction.OpenSetting] == (int)InputPhase.Down)
            {
                if (Backend.IsOpen) Backend.Exit();
                else Backend.OpenRoot();
            }
        }

        /// <summary>MCU → 安卓：投币键按下（KEY PUSH）时给玩家加币。游戏进行中/结算中（IsBusy）忽略，禁止压分(上分)。</summary>
        private void OnMcuKey(MbMessages.KeyPush key)
        {
            if (key.keyId == KEY_COIN_IN)
            {
                var gm = GameManager.Instance;
                if (gm != null && gm.IsBusy())
                {
                    Debug.Log("[GameController] MCU 投币被忽略：游戏进行中/结算中禁止压分(上分)");
                    return;
                }
                if (gm != null && gm.m_player != null)
                {
                    gm.m_player.AddCredits(100);
                    Debug.Log($"[GameController] MCU 投币 +100 credits (keyId=0x{key.keyId:X2})");
                }
            }
        }

        void OnApplicationQuit()
        {
            if (DataManager.Instance != null)
                DataManager.Instance.SaveData();
        }
    }
}
