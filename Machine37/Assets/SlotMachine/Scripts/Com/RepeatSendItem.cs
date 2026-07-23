using System;
using UnityEngine;

namespace Com
{
    public class RepeatSendItem
    {
        public readonly uint Pid;
        public readonly byte[] Data;


        private const int MaxRepeatSendCount = 5;
        public int Count { get; private set; } = -1;


        public RepeatSendItem(byte[] data)
        {
            Data = data;
            SerialPortTrans.RepeatSendItemDict.TryAdd(Pid, this);
        }
        
        public byte[] GetDataToSend()
        {
            ++Count;
            if (Count == 0) return Array.Empty<byte>();

            if (Count > MaxRepeatSendCount)
            {
                SerialPortTrans.RepeatSendItemDict.TryRemove(Pid, out _);
                Debug.LogWarning($"未回复 {BitConverter.ToString(Data)}");
                return Array.Empty<byte>();
            }

            Debug.LogWarning(this);
            return Data;
        }


        public override string ToString()
        {
            return $"重发数据{Count}次:{BitConverter.ToString(Data)}";
        }
    }
}