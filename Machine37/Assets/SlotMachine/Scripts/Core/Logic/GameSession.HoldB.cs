using System.Collections.Generic;

namespace SlotMachine.Core
{
    /// <summary>模式B(Cash Falls / 收集盘) 收集盘 respin 一轮推进的产出（逻辑层，不依赖 Unity）。
    /// 仅做数据推进（纯 HOLD，不随机补火球）：减圈 / 满列 / 释放 / FREE 单列累计免费次数；
    /// 每颗火球派彩已在基础局落定时计入 hs.accumulated（此处不再落新火球 ×bet）。
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
        /// <summary>模式B 收集盘 respin 一轮推进（不滚盘、纯 HOLD，不随机补火球，纯逻辑）：
        /// 每个活跃列(reel)只做【圈圈数倒计时】：counter −1；counter 归零 → 释放（清该列火球，回归滚动队列）；
        /// 满列判定：仅当基础局本身就落出整列火球（hs.cells 初始即满）才标记 isFull（进 Mini）。
        /// 不在 respin 期间往空格补新火球（杜绝从 1~2 颗种子凭空补满整列 → 进 Mini）。
        /// FREE 火球按【单列】累计 freeCountByCol（仅来自基础局落定的 FREE 火球），升档补差追加免费次数（freeballTiers[1,2,3]→[2,5,10]）。
        /// 不在此处清彩金池，保证逻辑层可独立测试。</summary>
        public static RespinStep RespinHoldSpin(HoldSpinState hs, ReelConfig cfg, ISlotRng rng, float bet, bool allowFreeMode)
        {
            var step = new RespinStep();
            step.newCounters = new int[hs.reels];
            var hc = cfg.holdSpin;
            float before = hs.accumulated;

            for (int r = 0; r < hs.reels; r++)
            {
                step.newCounters[r] = hs.counter[r];
                if (hs.isFull[r] || hs.released[r]) continue;
                if (hs.counter[r] <= 0) { hs.released[r] = true; step.releasedReels.Add(r); continue; }

                // ★ 纯 HOLD（不滚盘、不随机补火球）：respin 期间只做"锁定显示 + 圈圈数倒计时 + tong/释放演出"。
                //   不在空格补新火球（避免从 1~2 颗种子凭空补满整列 → 进 Mini）。
                //   满列判定：仅当基础局本身就落出整列火球（hs.cells 初始即满）才标记 isFull → 进 Mini。
                hs.counter[r] -= 1;

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
