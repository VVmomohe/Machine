using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlotMachine.Core
{
    /// <summary>
    /// 【模式B(Cash Falls / modeB_44668) 专属】火球 Hold &amp; Spin 的有状态对象（交互式：每按一次 Start 推进一轮 respin）。
    /// 模式A(China Street) 不创建此对象（holdMode="Direct" 直线结算，见 GameSession.A.cs）。
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
        /// <summary>彩金档名（与 JSON 配置 jackpots[].tier 一致）：[0]=Mini [1]=Minor [2]=Major [3]=Mega。
        /// 用 string 做池查表/清零，避免 FireballKind 枚举偏移导致键不匹配。</summary>
        public static readonly string[] JackpotTierNames = { "Mini", "Minor", "Major", "Mega" };

        public ReelConfig cfg;
        public ISlotRng rng;
        public float bet;

        public int reels;
        public FireballCell[][] cells;  // [reel][row]，filled=true 表示被火球占据(锁定)
        public int[] counter;           // 每列倒计时：>0 粘性，0 已释放/已满
        public bool[] isFull;           // 该列是否已集满
        public bool[] released;         // 该列是否已释放
        public float accumulated;       // 特性累计赢分（含倍数火球 + 彩金火球，统一按 ×bet 累加）
        public List<string> wonJackpots = new List<string>();  // 本特性中已中的彩金档名（"Mini"/"Minor"/"Major"/"Mega"，可重复/多档）
        // ★ 模式B 收集盘 FREE 火球单列累计（respin 跨轮持久）：freeCountByCol[reel]=该列已落 FREE 火球数；
        //   prevFreeAward[reel]=已授予的免费次数（升档只补差额，避免每轮重复授予）。
        public Dictionary<int, int> freeCountByCol = new Dictionary<int, int>();
        public Dictionary<int, int> prevFreeAward = new Dictionary<int, int>();
        public bool active;
        /// <summary>★ 模式B 展示用：本局 AdvanceHoldBoard 推进【之前】各列是否已跨局持有火球（持有中、非满非释放、有火球或圈数>0）。
        /// 供 GameManager.Flow.StartBaseSpin 区分"老持有列"(旋转期即显示圈圈) 与"本局新落列"(旋转期隐藏、停稳才显示)，
        /// 化解"圈圈旋转期消失"vs"圈圈没停稳就出现"两次相反反馈。注意：该数组是推进前的快照，不会被后续合并新火球改写。</summary>
        public bool[] preRoundHeldCols;
        /// <summary>★ 模式B 展示用：本局 AdvanceHoldBoard 推进【之前】每列倒计时圈数快照（= 上一局结束时的值）。
        /// 供 GameManager.Flow.StartBaseSpin 在旋转期显示"上一局值"（老持有列圈圈不消失、但不剧透本局已递减），
        /// 真正的递减(本局值)由 SettleBaseB 在停稳后才显示 → 圈圈"减"发生在滚动停后。</summary>
        public int[] preRoundCounter;
        /// <summary>渐进彩金池（Mini/Minor/Major/Mega → 当前累积值，单位=信用点）。
        /// 彩金火球的 multiplier = Pots[tier] / bet（信用→倍率统一，使 ReelSum 结果 ×bet 恰为池值）。</summary>
        public IReadOnlyDictionary<string, float> Pots;

        /// <summary>从基础旋转落下的初始火球创建特性态。无火球的列直接标记 released。
        /// payOnStart=true（默认，Mini 用）：初始即满列直接派彩+清池；
        /// payOnStart=false（模式B 收集盘用）：初始火球派彩由调用方逐颗处理（AdvanceHoldBoard / CheckFireballHoldSpin），此处只放置+置计数。</summary>
        public static HoldSpinState Start(ReelConfig cfg, ISlotRng rng, float bet, List<FireballCell> initial,
            IReadOnlyDictionary<string, float> pots = null, bool allowFreeMode = false, bool payOnStart = true)
        {
            var st = new HoldSpinState
            {
                cfg = cfg,
                rng = rng,
                bet = bet,
                Pots = pots,
                reels = cfg.reelRows.Count,
            };
            int rc = (cfg.holdSpin != null) ? cfg.holdSpin.respinCount : 3;

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
                            // ★ 必须拷 jackpotTier：RollFireball 已正确设档位(0=Mini..)，但此处只拷了 kind/multiplier，
                            //   漏拷会导致彩金火球 jackpotTier 停在默认 -1 → CollectJackpots/ResetJackpot 失效 → Mini 中彩金不清零。
                            f.jackpotTier = rolled.jackpotTier;
                        }
                        st.cells[f.reel][f.row] = f;
                    }

            StartReelFill(st, cfg, rc, bet, payOnStart);

            st.active = AnyActive(st);
            return st;
        }

        /// <summary>B 模式(ReelFill)状态初始化：逐列倒计时 + 初始即满列直接派彩（payOnStart=true 时；模式B respin 传 false 由调用方逐颗派彩）。</summary>
        static void StartReelFill(HoldSpinState st, ReelConfig cfg, int rc, float bet, bool payOnStart)
        {
            for (int r = 0; r < st.reels; r++)
            {
                int cnt = 0;
                for (int row = 0; row < st.cells[r].Length; row++)
                    if (st.cells[r][row].filled) cnt++;
                if (cnt > 0) st.counter[r] = rc;
                else { st.counter[r] = 0; st.released[r] = true; }
            }

            // 初始即满列 → 直接派彩（仅 payOnStart=true，如 Mini；模式B respin 由 CheckFireballHoldSpin 逐颗派彩）
            if (!payOnStart) return;
            for (int r = 0; r < st.reels; r++)
                if (!st.isFull[r] && ReelFull(st, r))
                {
                    st.isFull[r] = true;
                    st.counter[r] = 0;
                    st.accumulated += bet * ReelSum(st, r);
                    RecordJackpots(st, r);
                }
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
            => AnyActiveReelFill(st);

        static bool AnyActiveReelFill(HoldSpinState st)
        {
            for (int r = 0; r < st.reels; r++)
                if (st.counter[r] > 0 || (!st.isFull[r] && !st.released[r])) return true;
            return false;
        }

        /// <summary>是否存在任一已集满列（用于判定"收集盘已死但仍卡着满列"）。</summary>
        public static bool AnyFull(HoldSpinState st)
        {
            for (int r = 0; r < st.reels; r++) if (st.isFull[r]) return true;
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

        /// <summary>把某列中的彩金火球档位记录到 wonJackpots（用于显示/统计；倍数火球不计入）。
        /// 存档名 string（如 "Mini"）而非枚举，避免枚举偏移导致清零时键不匹配。</summary>
        public static void RecordJackpots(HoldSpinState st, int r)
        {
            for (int row = 0; row < st.cells[r].Length; row++)
            {
                var c = st.cells[r][row];
                // 免费模式火球(FreeSpins)和倍数火球不计入彩金
                if (c.filled && c.jackpotTier >= 0 && c.jackpotTier < JackpotTierNames.Length)
                {
                    string t = JackpotTierNames[c.jackpotTier];
                    st.wonJackpots.Add(t);
                    if (SlotDebug.VerboseLogs) UnityEngine.Debug.Log($"[RecordJackpots] reel={r} row={row} 记录中奖档={t} → wonJackpots=[{string.Join(",", st.wonJackpots)}]");
                }
            }
        }

        /// <summary>随机生成一颗火球：按 jackpotRatio 决定是彩金火球（再按 jackpotWeights 选档）还是倍数火球（按 multiplierWeights 选倍率）。
        /// 彩金火球的 multiplier 优先取渐进池 pots[tier] / bet（信用→倍率统一），无池时回退 jackpotMultipliers。</summary>
        public static FireballCell RollFireball(ReelConfig cfg, ISlotRng rng, float bet,
            IReadOnlyDictionary<string, float> pots = null, bool allowFreeMode = false)
        {
            var hc = cfg.holdSpin;
            double r = rng.NextDouble();

            // ★ 自 2026-07-30 起 A/B 两模式均为 holdMode="Direct"（直线结算，无 respin 循环）。
            //   是否生成 FREE 火球完全由调用方 allowFreeMode 决定：
            //   - B 模式 base-spin 传 allowFreeMode:true，且 JSON freeModeRatio>0 → 可生成 FREE 火球累加免费局（触发 Mini）；
            //   - A 模式 base-spin 同样传 allowFreeMode:true，但 freeModeRatio=0 → isFree 被 effFreeRatio>0 门控，等价于旧硬约束（不会生成 FREE）；
            //   - Mini 免费局(HoldSpinState.Start) 传 allowFreeMode:false → 不生成 FREE。
            //   故此处不再按 holdMode 强制关 FREE，避免 B 模式的 FREE 火球被误杀。

            // ① 免费模式火球（仅在主游戏 Hold&Spin 内 allowFreeMode=true 时启用；Mini 不生成）：不派彩，multiplier=0。
            //   分区：r ∈ [0, freeModeRatio) → 免费模式；[freeModeRatio, freeModeRatio+jackpotRatio) → 彩金；其余 → 倍数。
            float effFreeRatio = (hc != null ? hc.freeModeRatio : 0f);
            bool isFree = allowFreeMode && effFreeRatio > 0f && r < effFreeRatio;
            if (isFree)
            {
                // ★ 根因诊断（非防御）：一旦生成 FreeSpins，打印调用栈 + 当前 holdMode/allowFreeMode/effFreeRatio，
                //   直接定位"为什么生成了免费火球"。A 模式(modeA:holdMode=Direct, freeModeRatio=0)逻辑上不可能触发此处。
                UnityEngine.Debug.LogWarning($"[FreeSpins-GEN] 生成 FreeSpins 火球！根因诊断 → holdMode={hc?.holdMode} allowFreeMode={allowFreeMode} effFreeRatio={effFreeRatio} r={r:F4}\n{System.Environment.StackTrace}");
                return new FireballCell { filled = true, kind = FireballKind.FreeSpins, multiplier = 0f };
            }

            // ② 彩金火球
            bool jackpot = hc != null && hc.jackpotEnabled
                && hc.jackpotMultipliers != null && hc.jackpotMultipliers.Count > 0
                && r < (effFreeRatio + (hc.jackpotRatio > 0f ? hc.jackpotRatio : 0f));

            var c = new FireballCell { filled = true };
            if (jackpot)
            {
                int tier = PickJackpotTier(cfg, rng);   // 0=Mini .. 3=Mega
                c.kind = (FireballKind)(tier + 1);
                c.jackpotTier = tier;                   // ★ 权威索引，避免枚举偏移
                // ★ 池查表用 JackpotTierNames[tier]（"Mini"/"Minor"/"Major"/"Mega"），与 JSON 配置 key 一致
                string tierName = (tier >= 0 && tier < JackpotTierNames.Length) ? JackpotTierNames[tier] : "";
                if (pots != null && !string.IsNullOrEmpty(tierName) && pots.TryGetValue(tierName, out float potVal) && bet > 0)
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
}
