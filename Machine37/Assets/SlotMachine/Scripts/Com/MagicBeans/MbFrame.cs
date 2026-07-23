using System;
using System.Collections.Generic;

namespace Com.MagicBeans
{
    /// <summary>消息类型（帧 TYPE 字段）。</summary>
    public enum MsgType : byte
    {
        REQ = 0x01,   // SOC → MCU 请求
        RESP = 0x02,  // MCU → SOC 响应
        PUSH = 0x03,  // MCU → SOC 主动推送
    }

    /// <summary>命令字（帧 CMD 字段）。完整消息 = TYPE + CMD。</summary>
    public enum Cmd : byte
    {
        HELLO = 0x01,
        STATUS = 0x02,
        HEARTBEAT = 0x03,
        ERROR = 0x04,
        LINE_SET = 0x10,
        BET_SET = 0x11,
        SPIN = 0x20,
        BONUS = 0x21,
        FREE_SPIN = 0x22,
        DOUBLE = 0x23,
        BALANCE = 0x30,
        LAST_RESULT = 0x31,
        JP_POOL = 0x40,
        KEY = 0x41,
    }

    /// <summary>游戏状态码（附录 C），用于 STATUS 与 HEARTBEAT 的 game_state。</summary>
    public enum GameState : byte
    {
        Idle = 0x00,          // 空闲（等待 Spin）
        SpinSettling = 0x01,  // Spin 结算中
        WaitBonusDoor = 0x02, // 等待玩家 Bonus 选门
        BonusSettling = 0x03, // Bonus 结算中
        FreeSpin = 0x04,      // Free Game 进行中
        Double = 0x05,        // Double Game 进行中
        Initializing = 0x10,  // 设备初始化中
        Error = 0x11,         // 错误状态（等待恢复）
        Fatal = 0xFF,         // 严重故障
    }

    /// <summary>Bean 触发类型（SPIN RESP bean_trigger_type）。</summary>
    public enum BeanTriggerType : byte
    {
        None = 0x00,
        ThreeBeansBonus = 0x01, // 3 Bean → Bonus
        FourBeansJp2 = 0x02,    // 4 Bean → JP2
        FiveBeansJp1 = 0x03,    // 5 Bean → JP1
    }

    /// <summary>Bonus 子游戏 ID（附录 D）。</summary>
    public enum BonusGame : byte
    {
        Stove = 0x00,     // Hidden in the Stove
        Treasure = 0x01,  // Capturing the Treasure
        Harp = 0x02,      // Stealing the Magic Harp
    }

    /// <summary>一帧原始数据（已解出 TYPE/CMD/PAYLOAD）。</summary>
    public struct Frame
    {
        public MsgType Type;
        public Cmd Cmd;
        public byte[] Payload;

        /// <summary>REQ/RESP 的 PAYLOAD 首字段是 u32 seq；PUSH 帧无 seq（返回 0）。</summary>
        public uint Seq
        {
            get
            {
                if (Payload == null || Payload.Length < 4) return 0;
                return ((uint)Payload[0] << 24) | ((uint)Payload[1] << 16) | ((uint)Payload[2] << 8) | Payload[3];
            }
        }

        public const byte STX1 = 0x5A;
        public const byte STX2 = 0xA5;

        public static byte Checksum(MsgType type, Cmd cmd, byte[] payload)
        {
            int s = (byte)type + (byte)cmd + (payload == null ? 0 : payload.Length);
            if (payload != null) foreach (var x in payload) s += x;
            return (byte)(s & 0xFF);
        }

        /// <summary>编码一帧为字节流。</summary>
        public static byte[] Build(MsgType type, Cmd cmd, byte[] payload)
        {
            payload = payload ?? new byte[0];
            var b = new List<byte>();
            b.Add(STX1);
            b.Add(STX2);
            b.Add((byte)type);
            b.Add((byte)cmd);
            b.Add((byte)payload.Length);
            b.AddRange(payload);
            b.Add(Checksum(type, cmd, payload));
            return b.ToArray();
        }
    }

    /// <summary>状态机式帧解析器：从字节流中按 STX1/STX2 + LEN 定长提取完整帧，校验失败则丢弃重同步。</summary>
    public class FrameParser
    {
        readonly List<byte> _buf = new List<byte>();

        public void Feed(byte[] data, int len)
        {
            for (int i = 0; i < len; i++) _buf.Add(data[i]);
        }

        public List<Frame> Drain()
        {
            var outp = new List<Frame>();
            int i = 0;
            while (i < _buf.Count)
            {
                if (_buf[i] != Frame.STX1) { i++; continue; }
                if (i + 1 >= _buf.Count) break;                 // 等 STX2
                if (_buf[i + 1] != Frame.STX2) { i++; continue; }
                if (i + 5 > _buf.Count) break;                 // 等 TYPE/CMD/LEN
                byte type = _buf[i + 2];
                byte cmd = _buf[i + 3];
                byte len = _buf[i + 4];
                int total = 5 + len + 1;
                if (i + total > _buf.Count) break;             // 等 PAYLOAD + CHECKSUM
                byte[] payload = new byte[len];
                for (int k = 0; k < len; k++) payload[k] = _buf[i + 5 + k];
                byte cs = _buf[i + 5 + len];
                byte calc = Frame.Checksum((MsgType)type, (Cmd)cmd, payload);
                if (calc == cs)
                {
                    outp.Add(new Frame { Type = (MsgType)type, Cmd = (Cmd)cmd, Payload = payload });
                    i += total;
                }
                else
                {
                    i++; // 校验失败，丢弃这个 0x5A，从下一字节重同步
                }
            }
            if (i > 0) _buf.RemoveRange(0, i);
            return outp;
        }
    }
}
