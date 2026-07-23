using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Com; // ISerialPort

namespace Com.MagicBeans
{
    /// <summary>
    /// Magic Beans 协议通信客户端：基于 ankh.com 的 ISerialPort 做传输。
    /// 职责：
    ///   1) 帧编解码（STX + TYPE + CMD + LEN + PAYLOAD + CHECKSUM，校验和重同步）；
    ///   2) REQ/RESP 按 u32 seq 配对（协议规定同一业务共用 CMD，用 seq 配对/去重）；
    ///   3) 超时重发（默认 500ms，重试 3 次；重发同一 seq，MCU 去重不重复结算）；
    ///   4) PUSH 帧（HEARTBEAT/JP_POOL/KEY/ERROR）以事件形式分发到主线程。
    /// 线程模型：后台线程读串口并解析帧入队；主线程调用 Pump() 把帧分发出去（回调在主线）。
    /// </summary>
    public class MagicBeansComm
    {
        ISerialPort _port;
        Thread _recvThread;
        volatile bool _running;
        readonly FrameParser _parser = new FrameParser();
        readonly ConcurrentQueue<Frame> _incoming = new ConcurrentQueue<Frame>();

        readonly object _lock = new object();
        readonly Dictionary<uint, Pending> _pending = new Dictionary<uint, Pending>();
        uint _seq;

        public bool IsOpen { get { return _port != null && _running; } }

        // PUSH 事件（在主线程触发）
        public event Action<MbMessages.HeartbeatPush> OnHeartbeat;
        public event Action<MbMessages.JpPoolPush> OnJpPool;
        public event Action<MbMessages.KeyPush> OnKey;
        public event Action<MbMessages.ErrorPush> OnError;

        class Pending
        {
            public Cmd Cmd; public uint Seq; public byte[] FrameBytes;
            public Action<Frame> OnResp; public Action OnTimeout;
            public DateTime SentAt; public int TimeoutMs; public int RetriesLeft;
        }

        // ---- 打开 / 关闭 ----
        public bool Open(ISerialPort port, string portName)
        {
            Close();
            _port = port;
            if (_port == null || !_port.Open(portName)) return false;
            _running = true;
            _recvThread = new Thread(RecvLoop) { IsBackground = true };
            _recvThread.Start();
            return true;
        }

        /// <summary>按平台选端口：Editor/PC 用 PCSerialPort，Android 用 AndroidSerialPort。</summary>
        public bool OpenDefault(string portName)
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            return Open(new Com.PCCom.PCSerialPort(), portName);
#else
            return Open(new Com.AndroidCOM.AndroidSerialPort(), portName);
#endif
        }

        public void Close()
        {
            _running = false;
            try { _port?.Close(); } catch { }
            try { _recvThread?.Join(500); } catch { }
            _recvThread = null; _port = null;
            lock (_lock) _pending.Clear();
        }

        // ---- 发送请求 ----
        /// <summary>
        /// 发送一个 REQ。payload 为「seq 之后的业务字段」（不含 seq，本方法自动拼 u32 seq 到 PAYLOAD 首部）。
        /// onResp 在主线程回调，入参为原始 Frame（用 MbMessages.*.Parse(frame.Payload) 取结构）。
        /// 超时/重发策略遵循协议 6.0 通用请求规则。
        /// </summary>
        public void SendRequest(Cmd cmd, byte[] payload, Action<Frame> onResp, Action onTimeout = null, int timeoutMs = 500, int maxRetries = 3)
        {
            uint seq = NextSeq();
            var p = new List<byte>();
            MbEndian.PutU32(p, seq);
            if (payload != null) p.AddRange(payload);
            byte[] frame = Frame.Build(MsgType.REQ, cmd, p.ToArray());
            var pend = new Pending
            {
                Cmd = cmd, Seq = seq, FrameBytes = frame,
                OnResp = onResp, OnTimeout = onTimeout,
                SentAt = DateTime.UtcNow, TimeoutMs = timeoutMs, RetriesLeft = maxRetries,
            };
            lock (_lock) _pending[seq] = pend;
            WriteFrame(frame);
        }

        uint NextSeq() { _seq = (_seq >= 0xFFFFFFFFu) ? 1u : _seq + 1u; return _seq; }

        void WriteFrame(byte[] frame)
        {
            if (_port != null) _port.Write(frame);
        }

        // ---- 后台接收 ----
        void RecvLoop()
        {
            var buf = new byte[1024];
            while (_running)
            {
                int n = 0;
                try { n = _port.Read(buf); }
                catch { break; }
                if (n > 0)
                {
                    _parser.Feed(buf, n);
                    var frames = _parser.Drain();
                    foreach (var f in frames) _incoming.Enqueue(f);
                }
                else
                {
                    Thread.Sleep(5); // 无数据，短暂让出 CPU
                }
            }
        }

        // ---- 主线程 Pump（每帧调用）----
        public void Pump()
        {
            Frame f;
            while (_incoming.TryDequeue(out f))
            {
                if (f.Type == MsgType.RESP)
                {
                    Pending pend = null;
                    lock (_lock) { if (_pending.TryGetValue(f.Seq, out pend)) _pending.Remove(f.Seq); }
                    if (pend != null && pend.OnResp != null) pend.OnResp(f);
                }
                else if (f.Type == MsgType.PUSH)
                {
                    DispatchPush(f);
                }
            }
            CheckTimeouts();
        }

        void DispatchPush(Frame f)
        {
            try
            {
                if (f.Cmd == Cmd.HEARTBEAT && OnHeartbeat != null) OnHeartbeat(MbMessages.HeartbeatPush.Parse(f.Payload));
                else if (f.Cmd == Cmd.JP_POOL && OnJpPool != null) OnJpPool(MbMessages.JpPoolPush.Parse(f.Payload));
                else if (f.Cmd == Cmd.KEY && OnKey != null) OnKey(MbMessages.KeyPush.Parse(f.Payload));
                else if (f.Cmd == Cmd.ERROR && OnError != null) OnError(MbMessages.ErrorPush.Parse(f.Payload));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[MagicBeans] PUSH 解析失败 cmd=" + f.Cmd + " " + e.Message);
            }
        }

        void CheckTimeouts()
        {
            var now = DateTime.UtcNow;
            List<Pending> expired = null;
            lock (_lock)
            {
                foreach (var kv in _pending)
                {
                    var pend = kv.Value;
                    if ((now - pend.SentAt).TotalMilliseconds >= pend.TimeoutMs)
                    {
                        if (pend.RetriesLeft > 0)
                        {
                            pend.RetriesLeft--;
                            pend.SentAt = now;
                            WriteFrame(pend.FrameBytes); // 重发同一 seq（MCU 去重，不重复结算）
                        }
                        else
                        {
                            if (expired == null) expired = new List<Pending>();
                            expired.Add(pend);
                        }
                    }
                }
                if (expired != null) foreach (var e in expired) _pending.Remove(e.Seq);
            }
            if (expired != null) foreach (var e in expired) e.OnTimeout?.Invoke();
        }
    }
}
