using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Com
{
    public class SerialPortTrans
    {
        public static readonly ConcurrentDictionary<long, RepeatSendItem> RepeatSendItemDict = new();

        /// <summary>
        ///     提取到一条完整数据后，触发事件
        /// </summary>
        private readonly Action<byte[]> _receiveAction;

        private readonly ISerialPort _serialPort;
        private readonly object _writeLock = new();

        public bool IsOpen => isOpen;
        private bool isOpen;

        public string Name;

        #region 串口参数

        /// <summary>
        ///     读取缓冲区
        ///     2^12
        /// </summary>
        protected byte[] buffer = new byte[10240];

        protected Thread ReadThread;

        protected Thread RepeatWriteThread;

        #endregion

        public SerialPortTrans(ISerialPort serialPort, Action<byte[]> receiveAction)
        {
            this._serialPort = serialPort;
            this._receiveAction = receiveAction;
        }

        public bool Open(string name)
        {
            Name = name;
            lock (_writeLock)
            {
                isOpen = _serialPort.Open(Name);
            }

            if (isOpen)
            {
                ReadThread = new Thread(DataReceiveFunc)
                {
                    IsBackground = true,
                };
                ReadThread.Start();

                RepeatWriteThread = new Thread(RepeatWrite)
                {
                    IsBackground = true
                };
                RepeatWriteThread.Start();
                Debug.Log(Name + "串口打开成功");
            }

            return isOpen;
        }


        /// <summary>
        ///     串口数据接收入队列
        ///     游标读取
        ///     读取串口与取串口数据分离
        ///     buffer:  ----------------------
        ///     anchor: 取队列数据时的锚定位置,与包头的意义不同
        ///     anchor 到下一个包头的数据长度为某条完整指令
        /// </summary>
        private void DataReceiveFunc()
        {
            //  读取到的数据长度
            var size = 0;
            // buffer 缓存的游标定位 头
            var anchor = 0;
            // buffer 缓存的游标定位 每次读取便宜
            var offset = 0;


            while (true)
            {
                // 间隔读写
                Thread.Sleep(40);
                // 缓冲区buffer检测，是否超出
                try
                {
                    if (buffer.Length <= offset + ComHelper.MaxLen)
                    {
                        var tmpBytes = new byte[1024];
                        // Debug.LogWarning($"anchor:{anchor}--Fe:{fcOffset}--sour:{sourceOffset}");

                        // 往前移动（通过循环索引会遇到Read方法 前后两次Read或更耗性能）
                        offset = offset - anchor;
                        for (var i = 0; i < offset; i++) tmpBytes[i] = buffer[anchor + i];

                        Array.Clear(buffer, 0, buffer.Length);

                        for (var i = 0; i < offset; i++) buffer[i] = tmpBytes[i];

                        anchor = 0;
                        // Debug.LogWarning("COM DATA RESET!" + BitConverter.ToString(buffer));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"刷新读串口缓存错误\tsourceOffset:{offset}\tanchor:{anchor}\n{e.Message}");
                }


                // Debug.Log(BitConverter.ToString(buffer));
                // 读取缓冲区
                var tmpRead = new byte[ComHelper.MaxLen];

                size = _serialPort.Read(tmpRead);


                Buffer.BlockCopy(tmpRead, 0, buffer, offset, size);
                offset += size;

                Debug.Log(BitConverter.ToString(tmpRead));
                // Debug.LogWarning($"anchor:{anchor} \thead:{fcOffset} \tsourceOffset:{sourceOffset}");
                // Debug.Log(BitConverter.ToString(buffer.Take(sourceOffset).ToArray()));

                if (size < ComHelper.MinLen)
                    continue;
                // 当定位不到包头时,或读取的字节数不足够显示完整指令时，break
                while (true)
                {
                    // 定位包头
                    int headPos = Array.IndexOf(buffer, ComHelper.HeadFlag, anchor);
                    // 定位包头,没有则清空buffer
                    if (headPos == -1)
                    {
                        Array.Clear(buffer, 0, offset);
                        anchor = 0;
                        offset = 0;
                        break;
                    }

                    int headPos2 = Array.IndexOf(buffer, ComHelper.HeadFlag2, anchor);
                    if (headPos2 != headPos + 1)
                    {
                        Array.Clear(buffer, 0, offset);
                        anchor = 0;
                        offset = 0;
                        break;
                    }


                    // 包长度位偏移 
                    int packageLenOffset = headPos + ComHelper.PayloadLen;

                    // 包长度 /校验位偏移
                    int packageLen = buffer[packageLenOffset] + ComHelper.MinLen;


                    // 数据流长度不足够显示完整指令
                    if (offset - anchor < packageLen)
                        break;

                    // !!!游标偏移!!!
                    anchor = headPos + packageLen;


                    // 截取待校验数据
                    var data = new byte[packageLen];
                    Buffer.BlockCopy(buffer, headPos, data, 0, data.Length);
                    // Debug.LogWarning($"3 anchor:{anchor} \t head:{headOffset} \tsourceOffset:{sourceOffset}");

                    // 校验不通过，游标移动时使用的数据是错误的，
                    if (!ComHelper.CheckSum(data))
                    {
                        continue;
                    }

                    // Debug.Log($"{DateTime.Now.Second}:{DateTime.Now.Millisecond}\tRec\t{BitConverter.ToString(data)}");
                    Debug.Log($"Rec\t{BitConverter.ToString(data)}");
                    _receiveAction.Invoke(data);
                }
            }
        }

        private void RepeatWrite()
        {
            while (true)
            {
                List<byte> dataToSend = new List<byte>();
                foreach (var item in RepeatSendItemDict.Values)
                {
                    var data = item.GetDataToSend();
                    if (data.Length > 0)
                        dataToSend.AddRange(data);
                }

                _serialPort?.Write(dataToSend.ToArray());

                Thread.Sleep(5000);
            }
        }


        public void WriteOnce(byte[] sendData)
        {
            if (!isOpen)
            {
                Debug.LogError("串口未打开！");
                return;
            }

            lock (_writeLock)
            {
                // Debug.Log($"{DateTime.Now.Second}:{DateTime.Now.Millisecond}\tSend\t{BitConverter.ToString(sendData)}");


#if UNITY_EDITOR
                Debug.Log($"Send\t{BitConverter.ToString(sendData).Replace("-", "")}");
#else
                Debug.Log($"Send\t{BitConverter.ToString(sendData)}");
#endif
                _serialPort.Write(sendData);
            }
        }

        public void WriteRepeat(byte[] sendData)
        {
#if !UNITY_EDITOR
            new RepeatSendItem(sendData);
#endif
            WriteOnce(sendData);
        }


        public void Close()
        {
            ReadThread?.Abort();
            RepeatWriteThread?.Abort();
            _serialPort?.Close();
            isOpen = false;
        }
    }
}