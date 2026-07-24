using UnityEngine;

namespace Com.MagicBeans
{
    /// <summary>
    /// 运行期入口示例：挂到场景中任意 GameObject 上。负责开串口、每帧 Pump、演示启动握手。
    /// 接入游戏时：
    ///   - 把 OnHeartbeat / OnJpPool / OnKey / OnError 事件接到你的 UI / GameManager；
    ///   - 玩家按 Spin 时调用 DoSpin()（或自行 comm.SendRequest(Cmd.SPIN, ...)）；
    ///   - 在 GameManager 的 Update 里调用 comm.Pump()，或挂本脚本由它 Pump。
    /// 注意：MCU 是盘面/赢分/余额/奖池的真值来源，前端一律以 MCU 返回为准。
    /// </summary>
    public class MagicBeansBridge : MonoBehaviour
    {
        [Header("串口号：Editor 下改为本机 COMx；Android 下为 ttySx 设备路径")]
        public string portName = "COM3";

        public MagicBeansComm comm = new MagicBeansComm();
        public static MagicBeansBridge Instance { get; private set; }

        void Awake() { Instance = this; }

        void Start()
        {
            // PUSH 事件（心跳用于判断链路；连续 3 秒收不到应判定断线）
            comm.OnHeartbeat += h => Debug.Log($"[MB] heartbeat uptime={h.uptimeSec}s state={h.gameState}");
            comm.OnJpPool += j => Debug.Log($"[MB] JP1={j.jp1Display} JP2={j.jp2Display} JP3={j.jp3Display} online={j.jp1Eligible}/{j.jp2Eligible}/{j.jp3Eligible}");
            comm.OnKey += k => Debug.Log($"[MB] KEY 0x{k.keyId:X2} pressed");
            comm.OnError += e => Debug.LogWarning($"[MB] ERROR code=0x{e.errorCode:X2} severity={e.severity} ctxSeq={e.contextSeq}");

            if (comm.OpenDefault(portName))
                Debug.Log("[MB] 串口已打开 " + portName);
            else
                Debug.LogError("[MB] 串口打开失败 " + portName + "（检查串口号/权限/占用）");

            // 标准启动握手：STATUS → BALANCE（HELLO 可选，调试阶段可省略）
            comm.SendRequest(Cmd.STATUS, null, f =>
            {
                var s = MbMessages.StatusResp.Parse(f.Payload);
                Debug.Log($"[MB] STATUS state={s.gameState} balance={s.balance} bet={s.currentBet} lines={s.currentLines}");
            });
            comm.SendRequest(Cmd.BALANCE, null, f =>
            {
                var b = MbMessages.BalanceResp.Parse(f.Payload);
                Debug.Log($"[MB] BALANCE={b.balance} lastSeq={b.lastSeq}");
            });
        }

        void Update()
        {
            if (comm != null) comm.Pump();
        }

        void OnDestroy()
        {
            if (comm != null) comm.Close();
        }

        // 示例：发起一次 SPIN（seq 由通信层自动维护）。接入时放在“玩家按 Spin”处。
        public void DoSpin()
        {
            comm.SendRequest(Cmd.SPIN, null, f =>
            {
                var s = MbMessages.SpinResp.Parse(f.Payload);
                Debug.Log($"[MB] SPIN win={s.spinWinTotal} balanceAfter={s.balanceAfter} beans={s.beanTriggerCount} type={s.beanTriggerType}");
            },
            onTimeout: () => Debug.LogWarning("[MB] SPIN 超时（已重发 3 次仍无响应）"));
        }
    }
}
