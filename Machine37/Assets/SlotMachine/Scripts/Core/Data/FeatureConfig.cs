using System;
using System.Collections.Generic;

namespace SlotMachine.Core
{
    /// <summary>四档奖池中的一档（Mini/Minor/Major/Mega）。value 默认是 ×totalBet 的倍数。
    /// 注：火球 Hold&amp;Spin 现已支持"彩金火球"（见 HoldSpinConfig.jackpotMultipliers/jackpotWeights），
    /// 火球可为 Mini/Minor/Major/Mega 之一（无自身倍率，按档给彩金倍数）；四档奖池仍可由其他入口触发。</summary>
    [Serializable]
    public class JackpotTier
    {
        public string tier = "Mini";        // Mini / Minor / Major / Mega
        public float value = 15f;           // 倍数(×bet) 或固定值（静态展示/兜底用）
        public bool valueIsMultiplier = true;
        public int weight = 1;              // 火球携带该奖池的相对权重（越大越常见）
        public float potRate = 0f;          // 渐进奖池基础注水率(四档统一)：彩金值 = 有效压分×betMult + potRate×局数
        public float betMult = 1f;          // 该档跟压分挂钩的倍数系数(可调)：压分越大彩金越高；四档各自不同拉开差距
        public float potCap = 0f;           // 渐进奖池硬上限(绝对值信用点)：>0 时封顶，防止极端膨胀；0=不封顶
    }

    /// <summary>Hold &amp; Spin 火球参数，全部数据驱动。
    /// 收集盘玩法（Cash Falls / Ultimate Fire Link 风格）：
    /// 普通局里用一个倒计时保证每 dropMin~dropMax 场至少掉 1 颗火球（绝不一次出一堆），
    /// 火球落入棋盘后收集进跨局持久收集盘；每收集 1 颗累加其倍率；
    /// 集满 bankTarget 颗 → 触发一次 Fire Link 大奖 = bet × 收集盘累计倍率之和，然后清空。</summary>
    [Serializable]
    public class HoldSpinConfig
    {
        // ===== 收集盘玩法（当前唯一使用）=====
        public int dropMin = 1;               // 每次掉落间隔下限（至少多少场后掉下一颗）
        public int dropMax = 3;               // 每次掉落间隔上限（最多多少场必须掉一颗）
        public int bankTarget = 8;            // 收集盘集满几颗触发 Fire Link 大奖

        // 火球倍率集合与权重（按出现频率从高到低；倍率越高权重越小=越稀有）。
        public List<float> multipliers = new List<float> { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2.5f, 3f, 5f };
        public List<int> multiplierWeights = new List<int> { 32, 24, 16, 12, 8, 4, 2, 1 };

        // ===== 彩金火球（四档：MINI/MINOR/MAJOR/MEGA，无自身倍率，按档给彩金倍数）=====
        public bool jackpotEnabled = true;                                  // 火球是否可能是彩金类型
        // 各档彩金倍数（×bet），顺序固定 [Mini, Minor, Major, Mega]
        public List<float> jackpotMultipliers = new List<float> { 20f, 100f, 500f, 2000f };
        // 各档相对权重（越大越常见）：Mini 最常见，Mega 最稀有。
        // 2026-07-29 调整：Major 6→2、Mega 2→1（压低大档概率），Mini/Minor 不变(80/12)。
        // 2026-07-30 调整1：Mini 命中概率 8%→9%（权重[80,12,2,1]→[90,12,2,1]，ratio 0.095→0.105）。
        // 2026-07-30 调整2：FreeSpins −0.3pp、倍数 −0.2pp 全给 Mini：权重[90,12,2,1]→[95,12,2,1]（和105→110），
        //   ratio 0.105→0.110，使 Mini=0.110×95/110=9.5%、Minor=1.2%/Major=0.2%/Mega=0.1% 不变；
        //   整条彩金火球占比 10.5%→11.0%，多出的 0.5pp 来自 FreeSpins(5.3→5.0)与倍数(84.2→84.0)。
        public List<int> jackpotWeights = new List<int> { 95, 12, 2, 1 };
        // 一颗新火球是彩金类型的概率（否则为倍数火球）
        public float jackpotRatio = 0.110f;

        // 一颗新火球是"免费模式"类型的概率（仅在主游戏基础旋转(Direct)内生成；Mini 免费局不生成 FreeSpins，见 HoldSpinState.RollFireball allowFreeMode）。
        // 免费模式火球不派彩，按列收集到一定数量追加免费次数（FireballKind.FreeSpins）。
        public float freeModeRatio = 0.050f;   // 每颗新火球是"免费模式"(FreeSpins)类型的概率；2026-07-30 由 0.053 → 0.050（−0.3pp 让给 Mini，实际运行取值以 JSON 为准）

        // ===== 旧 Hold&Spin 交互式参数 =====
        // respinCount / triggerMin 仍在使用（HoldSpinState.B.cs / GameSession.HoldB.cs 读取）。
        // fireballHitProb / fbProb 为死字段已删除（火球概率实际由 OutcomeGenerator / RollFireball 决定）。
        public int respinCount = 3;
        public int triggerMin = 1;

        // ===== 模式专用(holdMode 分支) =====
        public string holdMode = "Direct";     // 两模式现均为 "Direct"（直线结算，无 respin 循环）；"ReelFill" 为旧收集盘玩法残留值，已不使用。
        public int fullUnlockFireballs = 20;     // A 行解锁全开阈值：火球总数达此值解锁所有行(起始4行,8火球解第1额外行)
        public bool sequentialWinAnimation = false;  // true=A(China Street):赢线逐条顺序高亮播放(高亮一条→loop→还原→下一条); false=B:所有线同时高亮
    }

    /// <summary>免费旋转参数，由 Scatter 触发。奖励次数随 Scatter 数量变化（见 SpinsFor）。
    /// 免费局(Mini)内由方式 A 追加；方式 B 的 FREE 火球在主游戏基础旋转(Direct)按单列收集累加（FreeSpins 火球只在主游戏生成）：
    ///   A. Scatter 连消：单轮免费旋转棋盘上出现 N 颗 icon 11 → scatterRetrigger 档追加（Mini 内）。
    ///   B. 火球"免费模式"收集：主游戏基础旋转(Direct)按单列收集的 FreeSpins 类型火球数 → freeballTiers 档追加（扩展即将进入的 Mini）。</summary>
    [Serializable]
    public class FreeSpinsConfig
    {
        public int triggerScatter = 3;             // 几颗 Scatter 触发
        public float multiplier = 1f;              // 免费转内全部赢分的倍率
        public bool retrigger = true;              // 免费转内再凑齐 Scatter 是否追加次数
        public int awardSpins = 2;                 // 触发即奖励的免费转次数（3 个 Scatter → 2 次免费；Scatter 本身不派彩，仅触发免费转）
        public int maxSpins = 10;                  // 免费转次数上限（防极端值，正常情况下不封顶）

        // ===== 方式 A：免费局内 Scatter(icon 11) 连消追加次数 =====
        public List<int> scatterRetriggerCounts = new List<int> { 3, 4, 5 };  // 出现 3/4/5 颗 Scatter
        public List<int> scatterRetriggerAwards = new List<int> { 2, 5, 10 }; // 对应追加 2/5/10 次

        // ===== 方式 B：火球"免费模式"(FreeSpins 类型)按列收集追加次数 =====
        public List<int> freeballTiers = new List<int> { 1, 2, 3 };           // 单列收集 1/2/3+ 颗免费模式火球
        public List<int> freeballAwards = new List<int> { 2, 5, 10 };         // 对应累计追加 2/5/10 次（升档时只补差额）

        public int miniCap = 50;                   // 免费局(Mini)轮数硬上限：转够该轮数即强制结束（防止 Scatter 重触发无限续命）；0=不封顶（退化为 300 轮绝对安全网）

        // ===== A 模式专用：波动性免费转（scatter 在指定列各1个触发，选波动性）=====
        public bool useVolatility = false;                    // true=按波动性选免费局数+倍率（A）；false=按 Scatter 数量分档（B）
        public List<int> freeGameReels = new List<int>();     // A：出现 Free Games 符号即触发的列（0-indexed），如 [1,2,3]=reels 2/3/4
        public List<int> volatilitySpins = new List<int> { 5, 7, 10, 15, 20 };     // A 波动性选项：免费局数
        public List<int> volatilityMultipliers = new List<int> { 2, 3, 5, 10, 25 }; // A 波动性对应倍率

        /// <summary>按 Scatter 数量给免费转次数（分档，与火球免费模式同档次 2/5/10）：
        /// 1/2 个 → 0（不触发）；3 个 → 2 次；4 个 → 5 次；5+ 个 → 10 次。
        /// 达到 triggerScatter(默认3) 才触发，低于 3 个返回 0。
        /// 免费转压注沿用触发那次 bet（GameSession.Play 内统一用同一 bet）。</summary>
        public int SpinsFor(int count)
        {
            if (count < triggerScatter) return 0;
            int award = ScatterAwardFor(count);          // 分档 3→2 / 4→5 / 5+→10（scatterRetriggerCounts/Awards）
            if (award <= 0) award = awardSpins;          // 兜底（count>=triggerScatter 时理论上必有档）
            return (maxSpins > 0 && award > maxSpins) ? maxSpins : award;
        }

        /// <summary>方式 A：单轮免费旋转棋盘上出现 count 颗 Scatter(icon 11) 时追加的免费次数（取不超过 count 的最高档）。</summary>
        public int ScatterAwardFor(int count)
        {
            for (int i = scatterRetriggerCounts.Count - 1; i >= 0; i--)
                if (count >= scatterRetriggerCounts[i]) return scatterRetriggerAwards[i];
            return 0;
        }

        /// <summary>方式 B：单列收集到 count 颗"免费模式"火球时，应【累计】追加的免费次数（升档只补差额，见 MiniGame）。</summary>
        public int FreeballAwardFor(int count)
        {
            int award = 0;
            for (int i = 0; i < freeballTiers.Count; i++)
                if (count >= freeballTiers[i]) award = freeballAwards[i];
            return award;
        }
    }

    /// <summary>Mini 免费小游戏「行锁定」配置（数据驱动；A/B 模式共用 Mini，故两份 config 均可配）。
    /// 进入 Mini 先锁 lockRows 行，每转 unlockEvery 轮解一个锁；锁定行上的火球只显示、不计入派彩。
    /// 行号一律 0-indexed（8 行棋盘 = 0~7，0=最上行）。bottom = 棋盘「底」所在的行号（0 或 7），
    /// 锁的总是【远离底】的那半：底=0 → 锁下半 [4,5,6,7]；底=7 → 锁上半 [3,2,1,0]。锁从中间行向该半延伸。</summary>
    [Serializable]
    public class MiniLockConfig
    {
        public bool enabled = false;     // 是否启用 Mini 行锁定
        public int bottom = 0;           // 棋盘「底」的行号(0-indexed)：0=底在顶→锁下半[4,5,6,7] / 7=底在底→锁上半[3,2,1,0]
        public int lockRows = 4;         // 锁定行数
        public int unlockEvery = 3;      // 每转几轮解一个锁
    }
}
