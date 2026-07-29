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
        // MINI 占比 ≥80%（起马80%），大档逐级更稀有：Mini=80, Minor=12, Major=6, Mega=2。
        public List<int> jackpotWeights = new List<int> { 80, 12, 6, 2 };
        // 一颗新火球是彩金类型的概率（否则为倍数火球）
        public float jackpotRatio = 0.10f;

        // 一颗新火球是"免费模式"类型的概率（仅在主游戏 Hold&Spin 内生成；Mini 免费局不生成 FreeSpins，见 HoldSpinState.RollFireball allowFreeMode）。
        // 免费模式火球不派彩，按列收集到一定数量追加免费次数（FireballKind.FreeSpins）。
        public float freeModeRatio = 0.053f;   // 每颗新火球是"免费模式"(FreeSpins)类型的概率；2026-07-23 由 JSON 0.08 砍 1/3 → 0.053（实际运行取值以 JSON 为准）

        // ===== 旧 Hold&Spin 交互式参数（保留供 HoldSpinState 编译，收集盘玩法不使用）=====
        public int respinCount = 3;
        public int triggerMin = 1;
        public float fireballHitProb = 0.32f;

        // 火球在 Hold&Spin 每轮每空格的落球概率（覆盖条带火球密度）。
        // <0 或 0 表示回退到"该列 reelStrips 中火球占比"（旧行为）。
        // 2026-07-24：原游戏火球概率比当前实测 15.4% 低约 30%，故设为 ≈0.108 对齐。
        public float fbProb = -1f;
    }

    /// <summary>免费旋转参数，由 Scatter 触发。奖励次数随 Scatter 数量变化（见 SpinsFor）。
    /// 免费局(Mini)内由方式 A 追加；方式 B 在主游戏 Hold&Spin 内结算（FreeSpins 火球只在主游戏生成）：
    ///   A. Scatter 连消：单轮免费旋转棋盘上出现 N 颗 icon 11 → scatterRetrigger 档追加（Mini 内）。
    ///   B. 火球"免费模式"收集：主游戏 Hold&Spin 按单列收集的 FreeSpins 类型火球数 → freeballTiers 档追加（扩展即将进入的 Mini）。</summary>
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
}
