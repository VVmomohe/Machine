using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlotMachine.Core
{
    /// <summary>
    /// 火球 Hold &amp; Spin 的有状态对象（交互式：每按一次 Start 推进一轮 respin）。
    /// 按「列(reel)」管理：
    ///   - 落火球的列 → 该列倒计时 = respinCount(锁3转)；火球锁定在各自位置。
    ///   - 每轮该列放新火球 → 倒计时重置为 respinCount；否则 -1（3→2→1→0）。
    ///   - 倒计时到 0 的列 → 释放：下一轮转普通符号，原有火球丢弃。
    ///   - 火球倍率取自 multipliers，按 multiplierWeights 抽取。
    ///   - 某列集满该列所有格 → 派彩 = bet × 该列倍率之和，该列锁定。
    ///   - 所有列都"已满"或"已释放" → 特性结束。
    /// </summary>
    public class HoldSpinState
    {
        public ReelConfig cfg;
        public ISlotRng rng;
        public float bet;

        public int reels;
        public FireballCell[][] cells;  // [reel][row]，filled=true 表示被火球占据(锁定)
        public int[] counter;           // 每列倒计时：>0 粘性，0 已释放/已满
        public bool[] isFull;           // 该列是否已集满
        public bool[] released;         // 该列是否已释放
        public float accumulated;       // 特性累计赢分（含倍数火球 + 彩金火球，统一按 ×bet 累加）
        public List<FireballKind> wonJackpots = new List<FireballKind>();  // 本特性中已中的彩金档（可重复/多档）
        public bool active;
        /// <summary>渐进彩金池（Mini/Minor/Major/Mega → 当前累积值，单位=信用点）。
        /// 彩金火球的 multiplier = Pots[tier] / bet（信用→倍率统一，使 ReelSum 结果 ×bet 恰为池值）。</summary>
        public IReadOnlyDictionary<string, float> Pots;

        /// <summary>从基础旋转落下的初始火球创建特性态。无火球的列直接标记 released。</summary>
        public static HoldSpinState Start(ReelConfig cfg, ISlotRng rng, float bet, List<FireballCell> initial,
            IReadOnlyDictionary<string, float> pots = null, bool allowFreeMode = false)
        {
            var st = new HoldSpinState
            {
                cfg = cfg,
                rng = rng,
                bet = bet,
                Pots = pots,
                reels = cfg.reelRows.Count,
            };
            st.cells = new FireballCell[st.reels][];
            for (int r = 0; r < st.reels; r++)
            {
                int rowN = cfg.reelRows[r];
                st.cells[r] = new FireballCell[rowN];
                for (int row = 0; row < rowN; row++)
                    st.cells[r][row] = new FireballCell { reel = r, row = row };
            }
            st.counter = new int[st.reels];
            st.isFull = new bool[st.reels];
            st.released = new bool[st.reels];

            // 放置初始火球（未指定 kind/multiplier 的按 RollFireball 随机成 倍数/彩金 火球）
            if (initial != null)
                foreach (var f in initial)
                    if (f.reel >= 0 && f.reel < st.reels && f.row >= 0 && f.row < st.cells[f.reel].Length)
                    {
                        f.filled = true;
                        if (f.kind == FireballKind.Multiplier && f.multiplier <= 0f)
                        {
                            var rolled = RollFireball(cfg, rng, bet, pots, allowFreeMode);
                            f.kind = rolled.kind;
                            f.multiplier = rolled.multiplier;
                        }
                        st.cells[f.reel][f.row] = f;
                    }

            // 每列倒计时：有火球=respinCount，无火球=0 且直接 released
            int rc = (cfg.holdSpin != null) ? cfg.holdSpin.respinCount : 3;
            for (int r = 0; r < st.reels; r++)
            {
                int cnt = 0;
                for (int row = 0; row < st.cells[r].Length; row++)
                    if (st.cells[r][row].filled) cnt++;
                if (cnt > 0) st.counter[r] = rc;
                else { st.counter[r] = 0; st.released[r] = true; }
            }

            // 初始即满列 → 直接派彩
            for (int r = 0; r < st.reels; r++)
                if (!st.isFull[r] && ReelFull(st, r))
                {
                    st.isFull[r] = true;
                    st.counter[r] = 0;
                    st.accumulated += bet * ReelSum(st, r);
                    RecordJackpots(st, r);
                }

            st.active = AnyActive(st);
            return st;
        }

        public bool IsOver() => !active;

        public static bool ReelFull(HoldSpinState st, int r)
        {
            int filled = 0;
            for (int row = 0; row < st.cells[r].Length; row++)
                if (st.cells[r][row].filled) filled++;
            bool full = filled == st.cells[r].Length;
            return full;
        }

        public static float ReelSum(HoldSpinState st, int r)
        {
            float s = 0f;
            for (int row = 0; row < st.cells[r].Length; row++)
                if (st.cells[r][row].filled) s += st.cells[r][row].multiplier;
            return s;
        }

        public static bool AnyActive(HoldSpinState st)
        {
            for (int r = 0; r < st.reels; r++)
                if (st.counter[r] > 0 || (!st.isFull[r] && !st.released[r])) return true;
            return false;
        }

        public static float PickMultiplier(ReelConfig cfg, ISlotRng rng)
        {
            var hc = cfg.holdSpin;
            if (hc == null || hc.multipliers == null || hc.multipliers.Count == 0) return 1f;
            var vals = hc.multipliers;
            var w = hc.multiplierWeights;
            int total = 0;
            for (int i = 0; i < vals.Count; i++) total += (i < w.Count) ? Math.Max(1, w[i]) : 1;
            if (total <= 0) return vals[vals.Count - 1];
            int roll = rng.Next(total);
            for (int i = 0; i < vals.Count; i++)
            {
                roll -= (i < w.Count) ? Math.Max(1, w[i]) : 1;
                if (roll < 0) return vals[i];
            }
            return vals[vals.Count - 1];
        }

        /// <summary>把某列中的彩金火球档位记录到 wonJackpots（用于显示/统计；倍数火球不计入）。</summary>
        public static void RecordJackpots(HoldSpinState st, int r)
        {
            for (int row = 0; row < st.cells[r].Length; row++)
            {
                var c = st.cells[r][row];
                // 免费模式火球(FreeSpins)不计入彩金（它只追加免费次数，无彩金档）
                if (c.filled && c.kind != FireballKind.Multiplier && c.kind != FireballKind.FreeSpins)
                    st.wonJackpots.Add(c.kind);
            }
        }

        /// <summary>随机生成一颗火球：按 jackpotRatio 决定是彩金火球（再按 jackpotWeights 选档）还是倍数火球（按 multiplierWeights 选倍率）。
        /// 彩金火球的 multiplier 优先取渐进池 pots[tier] / bet（信用→倍率统一），无池时回退 jackpotMultipliers。</summary>
        public static FireballCell RollFireball(ReelConfig cfg, ISlotRng rng, float bet,
            IReadOnlyDictionary<string, float> pots = null, bool allowFreeMode = false)
        {
            var hc = cfg.holdSpin;
            double r = rng.NextDouble();

            // ① 免费模式火球（仅在主游戏 Hold&Spin 内 allowFreeMode=true 时启用；Mini 不生成）：不派彩，multiplier=0。
            //   分区：r ∈ [0, freeModeRatio) → 免费模式；[freeModeRatio, freeModeRatio+jackpotRatio) → 彩金；其余 → 倍数。
            float effFreeRatio = (hc != null ? hc.freeModeRatio : 0f);
            bool isFree = allowFreeMode && effFreeRatio > 0f && r < effFreeRatio;
            if (isFree)
                return new FireballCell { filled = true, kind = FireballKind.FreeSpins, multiplier = 0f };

            // ② 彩金火球
            bool jackpot = hc != null && hc.jackpotEnabled
                && hc.jackpotMultipliers != null && hc.jackpotMultipliers.Count > 0
                && r < (effFreeRatio + (hc.jackpotRatio > 0f ? hc.jackpotRatio : 0f));

            var c = new FireballCell { filled = true };
            if (jackpot)
            {
                int tier = PickJackpotTier(cfg, rng);   // 0=Mini .. 3=Mega
                c.kind = (FireballKind)(tier + 1);
                // ★ 彩金火球按 UI 显示的渐进池值算；池值单位=信用点→需 /bet 统一到倍率
                string tierName = ((FireballKind)(tier + 1)).ToString();  // "Mini"/"Minor"/"Major"/"Mega"
                if (pots != null && pots.TryGetValue(tierName, out float potVal) && bet > 0)
                    c.multiplier = potVal / bet;
                else
                    c.multiplier = hc.jackpotMultipliers[tier];
            }
            else
            {
                c.kind = FireballKind.Multiplier;
                c.multiplier = PickMultiplier(cfg, rng);
            }
            return c;
        }

        /// <summary>按 jackpotWeights 选彩金档（0=Mini .. 3=Mega）。</summary>
        static int PickJackpotTier(ReelConfig cfg, ISlotRng rng)
        {
            var hc = cfg.holdSpin;
            if (hc == null || hc.jackpotWeights == null || hc.jackpotWeights.Count == 0) return 0;
            var w = hc.jackpotWeights;
            int total = 0;
            for (int i = 0; i < w.Count; i++) total += Math.Max(1, w[i]);
            if (total <= 0) return 0;
            int roll = rng.Next(total);
            for (int i = 0; i < w.Count; i++)
            {
                roll -= Math.Max(1, w[i]);
                if (roll < 0) return i;
            }
            return w.Count - 1;
        }
    }

    /// <summary>一轮 respin 的增量结果。</summary>
    public class HoldSpinStep
    {
        public List<FireballCell> newFireballs = new List<FireballCell>();
        public List<int> reelSpun = new List<int>();
        public List<FullReelInfo> fullReels = new List<FullReelInfo>();
        public int[] counters;
        public bool active;
        public int[][] respinGrid;  // [reel][row]
    }

    /// <summary>某列集满的派彩信息。</summary>
    public class FullReelInfo
    {
        public int reel;
        public float payout;
        public float sum;
    }
}
