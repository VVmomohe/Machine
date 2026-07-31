using System.Collections.Generic;

namespace SlotMachine.Core
{
    /// <summary>模式B(Cash Falls / 收集盘) 收集盘 respin 一轮推进的产出（逻辑层，不依赖 Unity）。
    /// 仅做数据推进：落新火球 / 减圈 / 满列 / 释放 / FREE 单列累计免费次数 / 每颗火球 ×bet 派彩。
    /// 显示与动画（钉 overlay / tong / 计数器 / 满列演出）由 GameManager.Flow.B 驱动协程完成。</summary>
    public class RespinStep
    {
        public List<FireballCell> newFireballs = new List<FireballCell>(); // 本轮新落火球（含位置/倍率/档）
        public List<int> fullReels = new List<int>();        // 本轮刚集满的列（首个满的火球）
        public List<int> releasedReels = new List<int>();    // 本轮刚释放（火球回归滚动队列）的列
        public List<string> newJackpots = new List<string>(); // 本轮新中彩金档名（驱动据此 ResetJackpot + 播特效）
        public int[] newCounters;            // 各列新倒计时（respinCount→减1→0）
        public int freeSpinsAdded;           // 本轮 FREE 火球(单列收集)追加的免费次数（升档补差）
        public float roundWin;               // 本轮火球派彩（= 推进后 hs.accumulated − 推进前，仅供日志）
    }

    public partial class GameSession
    {
        /// <summary>模式B 收集盘 respin 一轮推进（不滚盘，纯逻辑）：
        /// 每个活跃列(reel)的【每个空位】按 fireballHitProb 独立落新火球（RollFireball allowFreeMode）；
        /// 该列有新火球 → counter=respinCount；否则 −1；counter 归零 → 释放（清该列火球，回归滚动队列）；
        /// 集满该列所有格 → 标记 isFull（不额外派彩，因每颗火球已按 ×bet 支付）。
        /// FREE 火球按【单列】累计 freeCountByCol，升档补差追加免费次数（freeballTiers[1,2,3]→[2,5,10]）。
        /// 每颗非 FREE 火球立即派彩（multiplier × bet 累加到 hs.accumulated）；彩金火球记 wonJackpots 并回传 newJackpots（清池由驱动调用 ResetJackpot）。
        /// 不在此处清彩金池，保证逻辑层可独立测试。</summary>
        public static RespinStep RespinHoldSpin(HoldSpinState hs, ReelConfig cfg, ISlotRng rng, float bet, bool allowFreeMode)
        {
            var step = new RespinStep();
            step.newCounters = new int[hs.reels];
            var hc = cfg.holdSpin;
            float hitProb = (hc != null) ? hc.fireballHitProb : 0.32f;
            int respinCount = (hc != null) ? hc.respinCount : 3;
            float before = hs.accumulated;

            for (int r = 0; r < hs.reels; r++)
            {
                step.newCounters[r] = hs.counter[r];
                if (hs.isFull[r] || hs.released[r]) continue;
                if (hs.counter[r] <= 0) { hs.released[r] = true; step.releasedReels.Add(r); continue; }

                bool gotNew = false;
                for (int row = 0; row < hs.cells[r].Length; row++)
                {
                    if (hs.cells[r][row].filled) continue;
                    if (rng.NextDouble() < hitProb)
                    {
                        var fb = HoldSpinState.RollFireball(cfg, rng, bet, hs.Pots, allowFreeMode);
                        fb.reel = r; fb.row = row; fb.filled = true;
                        hs.cells[r][row] = fb;
                        step.newFireballs.Add(fb);
                        gotNew = true;
                        if (fb.kind == FireballKind.FreeSpins)
                        {
                            if (!hs.freeCountByCol.ContainsKey(r)) hs.freeCountByCol[r] = 0;
                            hs.freeCountByCol[r]++;
                        }
                        else
                        {
                            hs.accumulated += bet * fb.multiplier;
                            if (fb.jackpotTier >= 0 && fb.jackpotTier < HoldSpinState.JackpotTierNames.Length)
                            {
                                string t = HoldSpinState.JackpotTierNames[fb.jackpotTier];
                                hs.wonJackpots.Add(t);
                                step.newJackpots.Add(t);
                            }
                        }
                    }
                }

                // 计数器：有新火球→重置为 respinCount；否则 −1
                if (gotNew) hs.counter[r] = respinCount;
                else hs.counter[r] -= 1;

                // 满列判定（优先于释放）
                if (!hs.isFull[r] && HoldSpinState.ReelFull(hs, r))
                {
                    hs.isFull[r] = true;
                    hs.counter[r] = 0;
                    step.fullReels.Add(r);
                }
                else if (hs.counter[r] <= 0 && !hs.isFull[r])
                {
                    // 倒计时归零且未集满 → 释放：清掉该列所有火球，回归滚动队列
                    hs.counter[r] = 0;
                    hs.released[r] = true;
                    for (int row = 0; row < hs.cells[r].Length; row++)
                        hs.cells[r][row] = new FireballCell { reel = r, row = row };
                    step.releasedReels.Add(r);
                }
                step.newCounters[r] = hs.counter[r];
            }

            // FREE 火球免费次数：单列累计，升档只补差额（避免每轮重复授予）
            if (cfg.freeSpins != null)
            {
                foreach (var kv in hs.freeCountByCol)
                {
                    int award = cfg.freeSpins.FreeballAwardFor(kv.Value);
                    int prev = hs.prevFreeAward.ContainsKey(kv.Key) ? hs.prevFreeAward[kv.Key] : 0;
                    if (award > prev) { step.freeSpinsAdded += (award - prev); hs.prevFreeAward[kv.Key] = award; }
                }
            }

            hs.active = HoldSpinState.AnyActive(hs);
            step.roundWin = hs.accumulated - before;
            return step;
        }
    }
}
