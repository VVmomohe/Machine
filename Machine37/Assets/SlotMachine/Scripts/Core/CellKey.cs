using System;

namespace SlotMachine.Core
{
    /// <summary>棋盘格位置编码工具：把 (reel, row) 压成单一 int key 用作集合/字典键，并提供反向解码。
    /// 编码基数 RowBase=100 要求每行最多 100 格（当前最大 8 行，安全）；
    /// 火球条带倍率用的 (reel, symIdx) 编码基数 SymBase=100000（每列符号种类远小于此）。
    /// 集中定义避免散落魔法数字，并保证编码/解码口径一致。</summary>
    public static class CellKey
    {
        public const int RowBase = 100;       // (reel,row) 编码基数
        public const int SymBase = 100000;    // (reel,symIdx) 编码基数

        public static int Encode(int reel, int row) => reel * RowBase + row;
        public static int Reel(int key) => key / RowBase;
        public static int Row(int key) => key % RowBase;

        public static int EncodeSym(int reel, int symIdx) => reel * SymBase + symIdx;
        public static int SymReel(int key) => key / SymBase;
        public static int SymIdx(int key) => key % SymBase;
    }
}
