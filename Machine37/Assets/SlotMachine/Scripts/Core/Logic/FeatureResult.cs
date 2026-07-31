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
        FreeSpins = 5,   // 免费模式火球：不派彩，按列收集到一定数量追加免费次数（仅在主游戏基础旋转(Direct)内生成）
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
        public float featureWin;                  // 特性赢分（火球等特性累计，A/B 直线结算计入）
        public float freeSpinsWin;                // 免费旋转内全部赢分
        public int freeSpinsAwarded;              // 实际奖励免费转次数（= freeSpinsFromScatter + freeSpinsFromFireball，A/B 基础局共用）
        public int freeSpinsFromScatter;          // 仅由 Scatter(数量分档)授予的免费次数（SpinsFor 结果，triggerScatter 未达则为 0）
        public int freeSpinsFromFireball;         // 仅由 FREE 火球(单列收集)追加的免费次数（SettleFireballsDirect / respin 累加，无火球则为 0）
        public bool enterMiniByColumnFill;        // 模式B：respin 中某列整列集满火球 → 触发 Mini（即便 freeSpinsAwarded=0 也进）
        public HoldSpinState holdSpinState;        // 模式B 收集盘 respin 态（基础局落火球后创建，由 GameManager.Flow.B 驱动循环；A 模式为 null）
        public float totalPayout;                 // 全部赢分
        public List<FireballCell> baseFireballs;  // 基础旋转落下的全部火球（每颗已定倍率/彩金档），用于基础轮即显示倍率文字（A/B 基础局火球显示通用）
        public List<string> wonJackpots;          // 直线结算(A/B 共用)本局中过的彩金档名("Mini"/"Minor"/"Major"/"Mega")，供显示层播特效（清池已在 GameSession 即时完成）
    }
}
