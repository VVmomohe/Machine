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
    public partial class GameSession
    {
        private readonly ReelConfig _cfg;
        private readonly ISlotRng _rng;

        // 渐进奖池
        private Dictionary<string,float> _pots = new Dictionary<string,float>();
        private bool _potsInit;
        // 每档各自累计局数（中该档时清零回 0，未中的档继续累计）
        private Dictionary<string,int> _tierSpinCount = new Dictionary<string,int>();
        // 最近一次用于计算的压分（Contribute/RefreshPots 写入，ResetJackpot 清局数后重算用）
        private float _lastBet = 0f;

        /// <summary>彩金池变化通知（注水/清零后自动触发）。由表现层(GameManager)挂接 BonusView.ShowPots，
        /// 逻辑层不直接引用 View。挂 null 即关闭自动刷新。</summary>
        public Action<IReadOnlyDictionary<string,float>> OnPotsChanged;

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
                // 初始值用下限压分(10)算（局数=0）：等首次 Contribute/RefreshPots 用真实压分覆盖
                _pots[j.tier] = MinBetForPot * j.betMult;
                _tierSpinCount[j.tier] = 0;
            }
        }

        /// <summary>用当前压分重算各档彩金值（局数不变）。供压分变化时调用（OnBetChanged），
        /// 让彩金随压分回落/上涨，且不会像 Contribute 那样让局数+1。</summary>
        public void RefreshPots(float bet)
        {
            EnsurePots();
            _lastBet = bet;
            if (_cfg.jackpots == null) return;
            foreach (var j in _cfg.jackpots)
            {
                if (j.potRate > 0 && _pots.ContainsKey(j.tier))
                {
                    int cnt = _tierSpinCount.ContainsKey(j.tier) ? _tierSpinCount[j.tier] : 0;
                    RecomputeTier(j, bet, cnt);
                }
            }
            OnPotsChanged?.Invoke(_pots);
        }

        /// <summary>下注：每档局数+1 后用当前压分重算彩金值（彩金随局数缓慢增长）。</summary>
        public void Contribute(float bet)
        {
            EnsurePots();
            _lastBet = bet;
            if (_cfg.jackpots == null) return;
            foreach (var j in _cfg.jackpots)
            {
                if (j.potRate > 0 && _pots.ContainsKey(j.tier))
                {
                    // 每档局数 +1（中过该档清零的档会从 0 重新累计）
                    if (!_tierSpinCount.ContainsKey(j.tier)) _tierSpinCount[j.tier] = 0;
                    _tierSpinCount[j.tier]++;
                    int cnt = _tierSpinCount[j.tier];
                    RecomputeTier(j, bet, cnt);
                }
            }
            OnPotsChanged?.Invoke(_pots);
        }

        /// <summary>压分下限：低于此值按此值算彩金（避免压分过小彩金趋零）。</summary>
        const float MinBetForPot = 10f;

        /// <summary>核心：彩金值 = 有效压分×betMult + potRate×该档局数（直接赋值，非累加）。
        /// 有效压分 = max(bet, 下限10)；压分变→值变（回落），中彩金清局数→回落到 有效压分×betMult。</summary>
        void RecomputeTier(JackpotTier j, float bet, int cnt)
        {
            float effBet = System.Math.Max(bet, MinBetForPot);
            float val = effBet * j.betMult + j.potRate * cnt;
            float before = _pots[j.tier];
            _pots[j.tier] = val;
            bool capped = false;
            if (j.potCap > 0 && _pots[j.tier] > j.potCap)
            {
                _pots[j.tier] = j.potCap;
                capped = true;
            }
            UnityEngine.Debug.Log($"[{(_tierSpinCount[j.tier] == cnt ? "Refresh" : "Contribute")}] bet={bet}(eff={effBet}) tier={j.tier} ={val:F4} (effBet×betMult={effBet*j.betMult:F4}+potRate×局数={j.potRate*cnt:F4}[局数={cnt}])  {before:F2}→{_pots[j.tier]:F2}{(capped ? " [CAPPED@"+j.potCap+"]" : "")}");
        }

        /// <summary>中奖重置：清掉该档累计局数并据此重算彩金值（回落到 有效压分×betMult）。
        /// 彩金火球的 multiplier 在生成时已锁定，重置不影响已结算入账的金额。
        /// 供主游戏 Hold&Spin 收尾 / Mini 结算时，对本次中过的档调用。</summary>
        public void ResetJackpot(string tier)
        {
            if (string.IsNullOrEmpty(tier) || _cfg.jackpots == null) return;
            for (int i = 0; i < _cfg.jackpots.Count; i++)
            {
                var j = _cfg.jackpots[i];
                if (j.tier == tier && _pots.ContainsKey(tier))
                {
                    float oldVal = _pots[tier];
                    _tierSpinCount[tier] = 0;   // ★ 只清该档局数，其他档继续累计
                    // 局数清0 → 值自然回落到 effBet×betMult
                    RecomputeTier(j, _lastBet, 0);
                    UnityEngine.Debug.Log($"[JackpotReset] tier={tier} old={oldVal} → new={_pots[tier]} (effBet={System.Math.Max(_lastBet, MinBetForPot)}), 该档局数清零");
                    OnPotsChanged?.Invoke(_pots);
                    return;
                }
            }
            UnityEngine.Debug.LogWarning($"[JackpotReset] tier={tier} 未匹配到配置项！pots keys=[{string.Join(",", _pots.Keys)}]");
        }

        public void ResetJackpots(IEnumerable<string> tiers)
        {
            if (tiers == null) return;
            foreach (var t in tiers) ResetJackpot(t);
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
            int fsAward = 0;
            if (_cfg.freeSpins != null)
            {
                // A 模式(useVolatility)：Free Games 符号在指定列(freeGameReels)各出现 1 个 → 波动性选局数+倍率；
                // B 模式：按 Scatter 数量分档(3→2/4→5/5+→10)。两者都只记次数，免费局由 MiniGame 运行。
                fsAward = _cfg.freeSpins.useVolatility
                    ? PickVolatilityFreeSpins(grid)
                    : _cfg.freeSpins.SpinsFor(sc);
            }

            res.freeSpinsAwarded = fsAward;
            // 注：res.freeSpinsWin 恒为 0（免费游戏赢分由 Mini 统计火球后经回调 AddFeatureWin 入账）。

            res.totalPayout = res.baseWin + res.scatterPayout + res.featureWin + res.freeSpinsWin;
            return res;
        }

        /// <summary>A 模式波动性免费转触发+选择：freeGameReels 指定的每一列都出现 ≥1 个 Scatter(=Free Games 符号) 即触发；
        /// 触发后随机选一档波动性( volatilitySpins[i] 局 + 对应 volatilityMultipliers[i] 倍 )，并把倍率写入 cfg.freeSpins.multiplier
        /// （供 MiniGame 结算免费局赢分时使用；当前 MiniGame 未消费该倍率，已标记 TODO）。
        /// 未满足指定列条件则不触发(返回 0)。</summary>
        int PickVolatilityFreeSpins(int[][] grid)
        {
            var fs = _cfg.freeSpins;
            if (fs.freeGameReels == null || fs.freeGameReels.Count == 0) return 0;
            int sid = _cfg.ScatterId();
            foreach (int reel in fs.freeGameReels)
            {
                if (reel < 0 || reel >= grid.Length) return 0;
                bool has = false;
                for (int row = 0; row < grid[reel].Length; row++)
                    if (grid[reel][row] == sid) { has = true; break; }
                if (!has) return 0;
            }
            int n = Math.Min(fs.volatilitySpins.Count, fs.volatilityMultipliers.Count);
            if (n <= 0) return 0;
            int idx = _rng.Next(n);
            int spins = fs.volatilitySpins[idx];
            int mult = fs.volatilityMultipliers[idx];
            fs.multiplier = mult;   // ★ 写入倍率供 MiniGame 结算用（当前 MiniGame 未消费，标记 TODO）
            UnityEngine.Debug.Log($"[FreeSpins-A] 波动性触发：列[{string.Join(",", fs.freeGameReels)}] 各含 Free Games 符号 → {spins} 局 ×{mult}");
            return spins;
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
                    {
                        // ★ 基础旋转每颗火球立刻定倍率/彩金档（与 Hold&Spin 同口径 RollFireball），
                        //   使基础轮火球即显示倍率文字（China Street 类玩法：火球在底轮就带 x倍率/彩金档）。
                        //   allowFreeMode:false → 底轮火球只可能是 倍数/彩金 火球（不出现 FREE 火球外观）。
                        var c = HoldSpinState.RollFireball(_cfg, _rng, bet, _pots, allowFreeMode: false);
                        c.reel = r; c.row = row; c.filled = true;
                        initial.Add(c);
                        if (res.baseFireballs == null) res.baseFireballs = new List<FireballCell>();
                        res.baseFireballs.Add(c);
                    }

            if (initial.Count == 0) return;  // 没火球 → 不触发

            // triggerMin: 最少几颗才触发（默认1=有就触发）
            int minTrigger = (_cfg.holdSpin.triggerMin > 0) ? _cfg.holdSpin.triggerMin : 1;
            if (initial.Count < minTrigger) return;

            // ★ A 模式(直线结算：holdMode="Direct")：落 ≥triggerMin 火球直接在基础轮算分，不进 Hold&Spin、不锁定、不 respin。
            //   所有火球倍率之和 ×bet 计入 featureWin；彩金火球落定即中(即时清池)，中奖档记 res.wonJackpots 供显示层播特效。
            //   ★ 逻辑抽到 GameSession.A.cs（模式A 专属，与 B 的 HoldSpinState.Start 分支彻底分离）。
            if (_cfg.holdSpin.holdMode == "Direct")
            {
                SettleFireballsDirect(initial, bet, res);
                return;  // 不创建 holdSpinState
            }

            // ★ initial 与 res.baseFireballs 同源（同一批已定倍率的 FireballCell），Start 不会再重掷，Hold&Spin 与基础轮倍率完全一致。
            res.holdSpinState = HoldSpinState.Start(_cfg, _rng, bet, initial, _pots, allowFreeMode: true);
        }

        /// <summary>
        /// 推进一轮 Hold&amp;Spin 重转（按「列(reel)」管理）：为每个活跃列的非锁定格生成新符号（垂直聚类），
        /// 返回本步增量（新火球/满列/计数器更新）。
        /// </summary>
        public static HoldSpinStep RespinHoldSpin(HoldSpinState state, ReelConfig cfg, ISlotRng rng,
            float bet, IReadOnlyDictionary<string, float> pots = null, bool allowFreeMode = false, bool[] engaged = null)
        {
            int fbId = cfg.fireballSymbolId;
            int rc = (cfg.holdSpin != null) ? cfg.holdSpin.respinCount : 3;
            var step = new HoldSpinStep
            {
                newFireballs = new List<FireballCell>(),
                fullReels = new List<FullReelInfo>(),
            };

            // 符号池（与 OutcomeGenerator 一致，但不含 Scatter=11、不含 Wild=10）：
            // Hold&Spin 期间 Scatter 无意义（不触发免费转、特性内也未接免费转重转），
            // 若散落进非锁定格会每轮重转、出现「免费游戏突然变普通符号」的错觉，故排除。
            // Wild(10) 同样不进 specialPool——改由下方 wildTargets 生成前定点（写一次，不事后替换）。
            var normalPool = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 };
            var specialPool = new List<int> { 9 };
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

            // ★ 百搭预先决定（写一次，不事后替换）：最多 maxWildsPerSpin 颗，排除第一列/顶行/已锁定(火球)格。
            //   旧逻辑把 wild 放进 specialPool 每格 12% 随机、再靠 LimitWildsOnBoard 事后砍到 1（"中途换"），该方法已删除。
            //   现改为生成前定点，与基础旋转 DecideWildPlan 同源。respin 不应用 wildSpawnChance 门控
            //   （旧行为几乎每轮必有 1 颗百搭，门控会降低出现率），只要有合法空格就放满 maxWilds 颗。
            var wildTargets = new HashSet<int>();
            {
                int wId = cfg.WildId();
                if (wId >= 0 && cfg.maxWildsPerSpin > 0)
                {
                    var cells = new List<(int col, int row)>();
                    for (int c = 0; c < state.reels; c++)
                    {
                        if (!cfg.wildAllowedInFirstReel && c == 0) continue;
                        int rows = state.cells[c].Length;
                        for (int row = 1; row < rows; row++)   // 排除顶行(data row0)，与视图 toprow 拦截一致
                            if (!state.cells[c][row].filled)   // 不落在已锁定(火球)格
                                cells.Add((c, row));
                    }
                    if (cells.Count > 0)
                    {
                        RandomHelper.Shuffle(cells, rng);
                        int place = Math.Min(cfg.maxWildsPerSpin, cells.Count);
                        for (int i = 0; i < place; i++)
                            wildTargets.Add(cells[i].col * 100 + cells[i].row);
                    }
                }
            }

            // ★ 按「列(reel)」推进：每轮所有"未集满"的列都参与，
            //   每个空位独立以 fbProb 落火球（纯随机，不限于被触发的列）。
            //   落新火球 → 该列倒计时重置为 respinCount 并取消释放（可"复活"），但仅限倒计时>0 / 未到释放点的轮次；
            //   否则倒计时 -1，到 0 则该列 m_engaged 清掉 → 下一轮由 m_engaged==false 触发释放（火球 overlay 滚走），
            //   但仍继续参与后续滚动、仍有机会再落火球——从而实现"火球可在任意列随机出现"，不再锁死在初始列。
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

            // ★ 火球可落在任意「未满列」（含从未出过火球的列、已释放列），不锁死在初始触发列，
            //   与「普通局」出火球方式一致（纯随机、任意位置）——故【空列】不受任何限制，可正常落新火球。
            // ★ 但「圈圈已归零(即将/正在释放)的列」禁止落新火球，防止其"复活"续命：
            //   判定 = 该列当前有火球(hasFireballs) 且 (counter==0 或 m_engaged==false)。
            //   空列 hasFireballs=false → 不命中 → 仍可落新球；有火球且 counter>0 的计数中列 → 不命中 → 可续命(正常)。
            //   有火球且 counter==0(圈圈=0 这一轮) 或 m_engaged 已清(释放轮) → 命中 → 不落新球，火球按 3→2→1→0(锁一轮)→释放 回归队列。
            bool hasFireballs = false;
            for (int row = 0; row < rowN; row++)
                if (state.cells[r][row].filled) { hasFireballs = true; break; }
            bool atCounterZero = hasFireballs && state.counter[r] == 0;   // 圈圈=0 这一轮：禁落新球防复活
            bool dueToRelease;
            if (engaged != null && r < engaged.Length)
                dueToRelease = hasFireballs && !engaged[r];
            else
                dueToRelease = hasFireballs && (state.counter[r] == 0 && !state.released[r]); // 兜底：无 engaged 输入时保持旧计数判定
            bool gotNewFireball = false;
                for (int row = 0; row < rowN; row++)
                {
                    if (locked[row]) { step.respinGrid[r][row] = fbId; continue; }

                    int sym = colSyms[row];
                    // ★ 预先决定的百搭落点：直接定值，不事后替换（解决"中途换 ICON"）。
                    if (wildTargets.Contains(r * 100 + row))
                        sym = wildId;

                    if (!atCounterZero && rng.NextDouble() < fbProb)
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
                        // ★ 倒计时仍照常递减（驱动圈圈显示 3→2→1→0）；但"释放(火球回归)"改由 dueToRelease(=m_engaged==false) 判定。
                        if (state.counter[r] > 0)
                            state.counter[r] = Math.Max(0, state.counter[r] - 1);
                        // ★ 释放：m_engaged==false（该列已无火球 / 倒计时已归零）即回归滚动队列。
                        //   因 m_engaged 在 OnStartKey 的 CheckEngaged 里、m_num<=0 时清掉，故圈圈显示 0 的那一轮火球仍锁定(空面板)，
                        //   下一轮(engaged 已 false)才回归——即 3→2→1→0(锁一轮)→释放，且显示与行为完全同步。
                        if (!state.released[r] && dueToRelease)
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

        // [cleanup] RespinRowUnlock 已删除：模式A 改为基础轮直线结算（落≥triggerMin 火球直接算分），不再进 Hold&Spin。

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
                    int picked = spCandidates.Count > 0
                        ? spCandidates[rng.Next(spCandidates.Count)]
                        : normalPool[rng.Next(normalPool.Count)];
                    // ★ 百搭(id=10)概率减半（用户 2026-07-25）：落点为百搭时，50% 概率降级为普通符 ID 1-8，
                    //   被砍概率质量分摊回 normalPool(1-8)，与基础旋转口径一致。
                    if (picked == 10 && rng.NextDouble() < 0.5)
                        picked = normalPool[rng.Next(normalPool.Count)];
                    grid[r] = picked;
                    belowSym = picked;
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
