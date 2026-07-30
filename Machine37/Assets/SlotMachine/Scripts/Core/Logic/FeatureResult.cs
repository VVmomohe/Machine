using System;
using System.Collections.Generic;

namespace SlotMachine.Core
{
    /// <summary>火球类型：倍数火球（×bet）、四档彩金火球（按档给彩金倍数），或免费模式火球（收集追加免费次数）。</summary>
    public enum FireballKind
    {
        Multiplier = 0,  // 倍数火球：multiplier 即 ×bet 值
        Mini = 1,
        Minor = 2,
        Major = 3,
        Mega = 4,
        FreeSpins = 5,   // 免费模式火球：不派彩，按列收集到一定数量追加免费次数（仅在主游戏 Hold&Spin 内生成）
    }

    /// <summary>棋盘上的一个火球格。filled=true 表示已被火球占据(锁定)。</summary>
    [Serializable]
    public class FireballCell
    {
        public int reel;
        public int row;
        public bool filled;
        public float multiplier;        // 火球值（xbet）：倍数火球=自身倍率；彩金火球=对应档的倍数（如 Mini=20）
        public FireballKind kind = FireballKind.Multiplier;  // 火球类型（决定显示文字与是否计入彩金）
        public int jackpotTier = -1;   // 彩金火球档位索引：0=Mini,1=Minor,2=Major,3=Mega；-1=非彩金火球。权威索引，避免枚举偏移。
    }

    /// <summary>一次完整游戏动作（基础旋转 + 可能的特性 + 免费旋转）的产出。</summary>
    [Serializable]
    public class GameResult
    {
        public int[][] baseGrid;                  // 基础旋转可见棋盘
        public float baseWin;                     // 基础连线/ways 赢分
        public List<Win> baseWins = new List<Win>(); // 基础连线/ways 中奖明细(含 positions，供视图高亮)
        public float scatterPayout;               // 基础 Scatter 赔付
        public int scatterCount;                  // 基础 Scatter 个数
        public float featureWin;                  // 特性赢分（Hold&Spin 累计）
        public float respinLineWin;               // Hold&Spin 每轮 respin 的普通线奖累计
        public float freeSpinsWin;                // 免费旋转内全部赢分
        public int freeSpinsAwarded;              // 实际奖励免费转次数
        public float totalPayout;                 // 全部赢分
        public HoldSpinState holdSpinState;       // Hold&Spin 态（基础旋转落火球时创建，null=未触发）
    }
}
