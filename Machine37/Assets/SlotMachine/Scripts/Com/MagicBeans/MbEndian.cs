using System;
using System.Collections.Generic;

namespace Com.MagicBeans
{
    /// <summary>大端（Big-Endian）字节写入。协议规定所有多字节整数为大端。</summary>
    public static class MbEndian
    {
        public static void PutU8(List<byte> b, byte v) { b.Add(v); }
        public static void PutU8(List<byte> b, int v) { b.Add((byte)(v & 0xFF)); }
        public static void PutU16(List<byte> b, ushort v)
        {
            b.Add((byte)((v >> 8) & 0xFF));
            b.Add((byte)(v & 0xFF));
        }
        public static void PutU32(List<byte> b, uint v)
        {
            b.Add((byte)((v >> 24) & 0xFF));
            b.Add((byte)((v >> 16) & 0xFF));
            b.Add((byte)((v >> 8) & 0xFF));
            b.Add((byte)(v & 0xFF));
        }
        public static void PutI32(List<byte> b, int v) { PutU32(b, (uint)v); }
    }

    /// <summary>大端读取器，从字节数组当前位置顺序读取。</summary>
    public class ByteReader
    {
        readonly byte[] _b;
        int _p;
        public ByteReader(byte[] b, int offset = 0) { _b = b; _p = offset; }
        public bool Has(int n) { return _p + n <= _b.Length; }
        public int Available { get { return _b.Length - _p; } }
        public byte U8() { return _b[_p++]; }
        public ushort U16()
        {
            ushort v = (ushort)((_b[_p] << 8) | _b[_p + 1]);
            _p += 2; return v;
        }
        public uint U32()
        {
            uint v = ((uint)_b[_p] << 24) | ((uint)_b[_p + 1] << 16) | ((uint)_b[_p + 2] << 8) | _b[_p + 3];
            _p += 4; return v;
        }
        public int I32() { return (int)U32(); }
        public byte[] Take(int n) { var a = new byte[n]; Array.Copy(_b, _p, a, 0, n); _p += n; return a; }
    }
}
