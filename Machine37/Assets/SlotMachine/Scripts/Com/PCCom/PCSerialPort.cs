#if UNITY_EDITOR || UNITY_STANDALONE
using System;
using System.IO.Ports;
using UnityEngine;

namespace Com.PCCom
{
    public class PCSerialPort : ISerialPort
    {
        private const int BaudRate = 115200; //波特率
        private const Parity Parity = System.IO.Ports.Parity.None; //效验位
        private const int DataBits = 8; //数据位
        private const StopBits StopBits = System.IO.Ports.StopBits.One; //停止位

        private SerialPort sp;

        public void Close()
        {
            sp.Close();
        }

        public bool Open(string name)
        {
            sp = new SerialPort(name, BaudRate, Parity, DataBits, StopBits);
            try
            {
                sp.Open();
            }
            catch (Exception e)
            {
                Debug.LogError($"[{name}]{e.Message}");
            }

            return sp.IsOpen;
        }

        public void Write(byte[] buffer)
        {
            sp.Write(buffer, 0, buffer.Length);
        }

        public int Read(byte[] buffer)
        {
            return sp.Read(buffer, 0, buffer.Length);
        }
    }
}
#endif
