using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SlotMachine.Core
{
    /// <summary>
    /// 一次完整游戏动作编排（逻辑层，不依赖 UnityEngine）：
    ///   基础旋转 → 火球 Hold&amp;Spin（≥1颗火球触发） → Scatter 免费旋转。
    /// </summary>
    public class GameSession
    {
        private readonly ReelConfig _cfg;
        private readonly ISlotRng _rng;

        // 渐进奖池
        private Dictionary<string,float> _pots = new Dictionary<string,float>();
        private Dictionary<string,float> _seeds = new Dictionary<string,float>();
        private bool _potsInit;

        public GameSession(ReelConfig cfg, ISlotRng rng)
        {
            _cfg = cfg;
            _rng = rng;
        }

        public IReadOnlyDictionary<string,float> Pots => _pots;

        public void EnsurePots()
        {
            if (_potsInit) return;
            _potsInit = true;
            if (_cfg.jackpots == null || _cfg.jackpots.Count == 0) return;
            foreach (var j in _cfg.jackpots)
            {
                float seed = j.potSeed > 0 ? j.potSeed : (float)System.Math.Max(j.value, 1f);
                _seeds[j.tier] = seed;
                _pots[j.tier] = seed;
            }
        }

        public void Contribute(float bet)
        {
            EnsurePots();
            if (_cfg.jackpots == null) return;
            foreach (var j in _cfg.jackpots)
                if (j.potRate > 0 && _pots.ContainsKey(j.tier))
                    _pots[j.tier] += bet * j.potRate;
        }

        public GameResult Play(float bet)
        {
            var res = new GameResult();
            EnsurePots();

            // 1) 基础旋转
            int[][] grid = OutcomeGenerator.Spin(_cfg, _rng);
            var wins = EvaluateBase(grid, bet);
            res.baseWins = wins;
            float baseWin = 0;
            for (int i = 0; i < wins.Count; i++) baseWin += wins[i].payout;
            int sc = ScatterUtil.Count(grid, _cfg);
            float sp = ScatterUtil.Payout(sc, _cfg, bet);
            res.baseWin = baseWin;
            res.scatterPayout = sp;
            res.scatterCount = sc;

            // 注：scatter(免费游戏) 不加入 baseWins 高亮。scatter 走独立计数/赔付(res.scatterPayout)，
            // 不参与普通连线高亮动画，避免与连线高亮混淆。

            // 2) 火球检测：基础旋转落了火球？→ 创建 Hold&Spin 态
            if (_cfg.fireballSymbolId > 0)
                CheckFireballHoldSpin(grid, bet, res);

            // 3) grid 交给显示层
            res.baseGrid = grid;

            // 4) Scatter 免费旋转 → 只记奖励次数，交由 Mini（MiniGame）统一运行与结算。
            //    ★ 旧逻辑（在此处内部模拟 while 循环旋转、累加 res.freeSpinsWin）已删除，
            //      避免与 Mini 重复结算；免费游戏的实际旋转/连线加转/火球统计全部在 MiniGame 内进行。
            int fsAward = (_cfg.freeSpins != null) ? _cfg.freeSpins.SpinsFor(sc) : 0;

            res.freeSpinsAwarded = fsAward;
            // 注：res.freeSpinsWin 恒为 0（免费游戏赢分由 Mini 统计火球后经回调 AddFeatureWin 入账）。

            res.totalPayout = res.baseWin + res.scatterPayout + res.featureWin + res.freeSpinsWin;
            return res;
        }

        // ---- 内部 ----

        List<Win> EvaluateBase(int[][] grid, float bet)
        {
            switch (_cfg.winEval)
            {
                case WinEvalType.Rows:    return new RowEvaluator().Evaluate(grid, _cfg, bet);
                case WinEvalType.Paylines: return new PaylineEvaluator().Evaluate(grid, _cfg, bet);
                default:                  return new WaysEvaluator().Evaluate(grid, _cfg, bet);
            }
        }

        /// <summary>公开：在任意网格上按当前 winEval 评估线奖（供 Hold&amp;Spin 每轮 respin 结算普通连线用）。</summary>
        public List<Win> EvaluateGrid(int[][] grid, float bet)
        {
            return EvaluateBase(grid, bet);
        }

        /// <summary>
        /// 检查基础旋转是否落了火球。如果 ≥1 颗 → 创建 HoldSpinState 挂到结果上，
        /// 由 GameManager.Flow 在停轮后进入 Hold&amp;Spin 重转循环。
        /// </summary>
        void CheckFireballHoldSpin(int[][] grid, float bet, GameResult res)
        {
            if (_cfg.fireballSymbolId < 0 || _cfg.holdSpin == null) return;

            int fbId = _cfg.fireballSymbolId;
            var initial = new List<FireballCell>();

            for (int r = 0; r < grid.Length; r++)
                for (int row = 0; row < grid[r].Length; row++)
                    if (grid[r][row] == fbId)
                        initial.Add(new FireballCell { reel = r, row = row, filled = true });

            if (initial.Count == 0) return;  // 没火球 → 不触发

            // triggerMin: 最少几颗才触发（默认1=有就触发）
            int minTrigger = (_cfg.holdSpin.triggerMin > 0) ? _cfg.holdSpin.triggerMin : 1;
            if (initial.Count < minTrigger) return;

            res.holdSpinState = HoldSpinState.Start(_cfg, _rng, bet, initial, _pots, allowFreeMode: true);
        }

        /// <summary>
        /// 推进一轮 Hold&amp;Spin 重转（按「列(reel)」管理）：为每个活跃列的非锁定格生成新符号（垂直聚类），
        /// 返回本步增量（新火球/满列/计数器更新）。
        /// </summary>
        public static HoldSpinStep RespinHoldSpin(HoldSpinState state, ReelConfig cfg, ISlotRng rng,
            float bet, IReadOnlyDictionary<string, float> pots = null, bool allowFreeMode = false)
        {
            int fbId = cfg.fireballSymbolId;
            int rc = (cfg.holdSpin != null) ? cfg.holdSpin.respinCount : 3;
            var step = new HoldSpinStep
            {
                newFireballs = new List<FireballCell>(),
                fullReels = new List<FullReelInfo>(),
            };

            // 符号池（与 OutcomeGenerator 一致，但不含 Scatter=11）：
            // Hold&Spin 期间 Scatter 无意义（不触发免费转、特性内也未接免费转重转），
            // 若散落进非锁定格会每轮重转、出现「免费游戏突然变普通符号」的错觉，故排除。
            var normalPool = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 };
            var specialPool = new List<int> { 9, 10 };
            int wildId = cfg.WildId();

            // 初始化聚类显示网格
            step.respinGrid = new int[state.reels][];
            for (int r = 0; r < state.reels; r++)
                step.respinGrid[r] = new int[state.cells[r].Length];

            // ★ 清除已释放列的僵尸 cells（overlay 已销毁但数据仍 filled=true）：
            //   不清则①幽灵火球占位阻止新球下落 ②AwardFreeballSpinsFromMain(IsOver 收尾时)
            //   会把僵尸 FREE 火球重复累加 → freeSpinsAwarded 膨胀到 30+ 次。
            for (int r = 0; r < state.reels; r++)
            {
                if (state.released[r])
                {
                    var col = state.cells[r];
                    for (int row = 0; row < col.Length; row++)
                        col[row].filled = false;
                }
            }

            // ★ 按「列(reel)」推进：每轮所有"未集满"的列都参与，
            //   每个空位独立以 fbProb 落火球（纯随机，不限于被触发的列）。
            //   落新火球 → 该列倒计时重置为 respinCount 并取消释放（可"复活"）；
            //   否则倒计时 -1，到 0 则该列释放（火球 overlay 滚走），但仍继续参与后续滚动、
            //   仍有机会再落火球——从而实现"火球可在任意列随机出现"，不再锁死在初始列。
            for (int r = 0; r < state.reels; r++)
            {
                if (state.isFull[r]) continue;

                int rowN = state.cells[r].Length;

                // 生成该列垂直聚类符号
                var colSyms = GenerateClusteredColumn(rowN, normalPool, specialPool, rng);

                // 标锁定行（已落定的火球保持）
                var locked = new bool[rowN];
                for (int row = 0; row < rowN; row++)
                    locked[row] = state.cells[r][row].filled;

                // 火球概率：优先用配置 holdSpin.fbProb（解耦条带密度，便于对齐原游戏），
                // 否则回退到该列 reelStrips 火球占比（旧行为）。
                double fbProb = 0.05;
                if (cfg.reelStrips != null && r < cfg.reelStrips.Count)
                {
                    var strip = cfg.reelStrips[r];
                    if (strip != null && strip.Count > 0)
                        fbProb = (double)strip.Count(x => x == fbId) / strip.Count;
                }
                if (cfg.holdSpin != null && cfg.holdSpin.fbProb > 0f)
                    fbProb = cfg.holdSpin.fbProb;

                // ★ 火球可落在任意列（包括已释放列和新触发列），不锁死在初始触发列。
                //   已释放列的 counter 不会重置（下方 gotNewFireball 分支已保护），防止无限复活。
                bool gotNewFireball = false;
                for (int row = 0; row < rowN; row++)
                {
                    if (locked[row]) { step.respinGrid[r][row] = fbId; continue; }

                    int sym = colSyms[row];
                    if (sym == wildId && (r == 0 || row == 0))
                        sym = normalPool[rng.Next(normalPool.Count)];

                    if (rng.NextDouble() < fbProb)
                    {
                        sym = fbId;
                        var c = HoldSpinState.RollFireball(cfg, rng, bet, pots, allowFreeMode);
                        c.reel = r; c.row = row; c.filled = true;
                        state.cells[r][row] = c;
                        step.newFireballs.Add(c);
                        gotNewFireball = true;
                    }
                    else
                    {
                        state.cells[r][row] = new FireballCell { reel = r, row = row, filled = false };
                    }
                    step.respinGrid[r][row] = sym;
                }

                if (gotNewFireball)
                {
                    state.counter[r] = rc;
                    state.released[r] = false;
                }
                    else
                    {
                        int prevCounter = state.counter[r];
                        if (state.counter[r] > 0)
                            state.counter[r] = Math.Max(0, state.counter[r] - 1);
                        // ★ counter 减到 0 当轮立即释放（不再延迟一轮）。
                        //   倒计时时间线：3→2→1→0(当轮释放，火球回归滚动队列)。
                        //   旧逻辑要求 prevCounter==0（即"已经为 0 的下一轮"才释放），导致圈圈显示 0 但火球仍锁一整轮，
                        //   用户反馈"圈圈数为零但火球没有回归队列滚动"——已改为当场释放。
                        if (state.counter[r] == 0 && !state.released[r])
                    {
                        state.released[r] = true;
                        if (step.reelSpun == null) step.reelSpun = new List<int>();
                        step.reelSpun.Add(r);
                    }
                }
            }

            // 检查本轮新集满的列 → 派彩
            for (int r = 0; r < state.reels; r++)
            {
                if (!state.isFull[r] && HoldSpinState.ReelFull(state, r))
                {
                    state.isFull[r] = true;
                    state.counter[r] = 0;
                    float sum = HoldSpinState.ReelSum(state, r);
                    float pay = state.bet * sum;
                    state.accumulated += pay;
                    HoldSpinState.RecordJackpots(state, r);
                    step.fullReels.Add(new FullReelInfo { reel = r, payout = pay, sum = sum });
                }
            }

            state.active = HoldSpinState.AnyActive(state);
            step.counters = (int[])state.counter.Clone();
            step.active = state.active;
            return step;
        }

        /// <summary>
        /// 生成一列垂直聚类符号（不含火球，火球由调用方独立掷骰决定）。
        /// 规则与 OutcomeGenerator 一致：row0=单格, row1=连2, row2=连3, row3=连4, row4+=连5。
        /// 相邻游程强制不同符防止合并；特殊符号(9-11)以~12%概率散落不聚类。
        /// </summary>
        static int[] GenerateClusteredColumn(int rows, List<int> normalPool,
            List<int> specialPool, ISlotRng rng)
        {
            var grid = new int[rows];
            if (rows <= 0) return grid;

            double specialProb = specialPool.Count > 0 ? 0.12 : 0;
            int r = rows - 1;
            int belowSym = -1;

            while (r >= 0)
            {
                // 特殊符号(9-11)：单格散落
                bool useSpecial = specialPool.Count > 0 && rng.NextDouble() < specialProb;
                if (useSpecial)
                {
                    var spCandidates = (belowSym >= 9 && belowSym <= 11)
                        ? specialPool.Where(s => s != belowSym).ToList()
                        : specialPool;
                    grid[r] = spCandidates.Count > 0
                        ? spCandidates[rng.Next(spCandidates.Count)]
                        : normalPool[rng.Next(normalPool.Count)];
                    belowSym = grid[r];
                    r--;
                    continue;
                }

                // 普通符号游程
                int maxRun = GetMaxValidRunForResp(r);
                int runLen = Math.Max(1, 1 + rng.Next(maxRun));

                var candidates = (belowSym >= 1 && normalPool.Contains(belowSym))
                    ? normalPool.Where(s => s != belowSym).ToList()
                    : normalPool;
                int sym = candidates[rng.Next(candidates.Count)];

                for (int k = 0; k < runLen; k++)
                    grid[r - k] = sym;

                belowSym = sym;
                r -= runLen;
            }
            return grid;
        }

        /// <summary>RespinHoldSpin 用：返回行r及以上能启动的最大合法竖向游程。</summary>
        static int GetMaxValidRunForResp(int row)
        {
            int maxPossible = Math.Min(5, row + 1);
            for (int len = maxPossible; len >= 1; len--)
            {
                int topRow = row - len + 1;
                if (GetMaxRowForResp(topRow) >= len)
                    return len;
            }
            return 1;
        }

        static int GetMaxRowForResp(int row)
        {
            return row switch { 0 => 1, 1 => 2, 2 => 3, 3 => 4, _ => 5 };
        }
    }
}
