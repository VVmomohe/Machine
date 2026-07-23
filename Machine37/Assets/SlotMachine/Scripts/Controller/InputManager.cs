using System.Collections.Generic;
using UnityEngine;

namespace Com.Controller
{
    /// <summary>
    /// 全局键盘输入采集：只记录按键状态，不做派发。
    /// 每帧把各 InputAction 的阶段写入 m_keys[(int)action]（0=None/1=Down/2=Hold/3=Up），
    /// 其它系统自行读取，例如：
    ///   if (InputManager.Instance.m_keys[(int)InputAction.Confirm] == (int)InputPhase.Down) { ... }
    /// 按键映射逐条对齐 PandaParadise 的 KeyboardInputProvider.cs。
    /// </summary>
    public class InputManager : MonoBehaviour
    {
        private static InputManager _instance;
        public static InputManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new GameObject("InputManager").AddComponent<InputManager>();
                }
                return _instance;
            }
        }

        /// <summary>按键状态数组：下标 = (int)InputAction，值 = (int)InputPhase。</summary>
        public int[] m_keys = new int[32];

        /// <summary>InputAction -> 绑定的按键（主键盘 + 小键盘，对齐 KeyboardInputProvider）。</summary>
        private static readonly Dictionary<InputAction, KeyCode[]> s_actionToKeys =
            new Dictionary<InputAction, KeyCode[]>
        {
            { InputAction.Left,      new[] { KeyCode.LeftArrow } },
            { InputAction.Right,     new[] { KeyCode.RightArrow } },
            { InputAction.Up,        new[] { KeyCode.UpArrow } },
            { InputAction.Down,      new[] { KeyCode.DownArrow } },

            { InputAction.Confirm,   new[] { KeyCode.Return, KeyCode.KeypadEnter } },
            { InputAction.Cancel,    new[] { KeyCode.Escape } },
            { InputAction.MiniGame,  new[] { KeyCode.M } },

            { InputAction.Start,     new[] { KeyCode.Space } },
            { InputAction.Enhance,   new[] { KeyCode.Tab } },
            { InputAction.Ticket,    new[] { KeyCode.Backspace } },
            { InputAction.GameSwitch,new[] { KeyCode.G } },

            { InputAction.BetKey1,   new[] { KeyCode.Alpha1, KeyCode.Keypad1 } },
            { InputAction.BetKey2,   new[] { KeyCode.Alpha2, KeyCode.Keypad2 } },
            { InputAction.BetKey3,   new[] { KeyCode.Alpha3, KeyCode.Keypad3 } },
            { InputAction.BetKey4,   new[] { KeyCode.Alpha4, KeyCode.Keypad4 } },
            { InputAction.BetKey5,   new[] { KeyCode.Alpha5, KeyCode.Keypad5 } },
            { InputAction.BetKey6,   new[] { KeyCode.Alpha6, KeyCode.Keypad6 } },
            { InputAction.BetKey7,   new[] { KeyCode.Alpha7, KeyCode.Keypad7 } }, // F5 兼容旧测试
            { InputAction.BetKey8,   new[] { KeyCode.Alpha8, KeyCode.Keypad8 } },
            { InputAction.BetKey9,   new[] { KeyCode.Alpha9, KeyCode.Keypad9 } },
            { InputAction.BetKey10,  new[] { KeyCode.Alpha0, KeyCode.Keypad0 } },
            { InputAction.BetKey11,  new[] { KeyCode.Minus, KeyCode.KeypadMinus } },
            { InputAction.BetKey12,  new[] { KeyCode.Plus } },

            { InputAction.DebugUpCoin, new[] { KeyCode.F3 } },
            { InputAction.OpenSetting,  new[] { KeyCode.F12 } },
            { InputAction.Stop,         new[] { KeyCode.S, KeyCode.RightShift } },
        };

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable() { }
        private void OnDisable() { }

        public void Update()
        {
            // 状态转换：上一帧的 Down→Hold(按键仍按住的情况)，Up→None。
            // 放在 Update 开头而非 LateUpdate，保证 Down 至少存活到下一帧本函数运行，
            // 无论消费者(GameController/GameManager)的 Update 先跑还是后跑都能吃到一次 Down。
            for (int i = 0; i < m_keys.Length; i++)
            {
                if (m_keys[i] == (int)InputPhase.Down) m_keys[i] = (int)InputPhase.Hold;
                else if (m_keys[i] == (int)InputPhase.Up) m_keys[i] = (int)InputPhase.None;
            }

            foreach (var kv in s_actionToKeys)
            {
                int idx = (int)kv.Key;
                var keys = kv.Value;
                bool down = false, hold = false, up = false;
                for (int k = 0; k < keys.Length; k++)
                {
                    if (Input.GetKeyDown(keys[k])) down = true;
                    if (Input.GetKey(keys[k])) hold = true;
                    if (Input.GetKeyUp(keys[k])) up = true;
                }

                if (down) m_keys[idx] = (int)InputPhase.Down;
                else if (up) m_keys[idx] = (int)InputPhase.Up;
                else if (hold) m_keys[idx] = (int)InputPhase.Hold;
                else m_keys[idx] = (int)InputPhase.None;
            }
        }
    }
}
