using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SlotMachine.Core
{
    /// <summary>A/B 双模式显式标记。作为模式唯一真值源，替代从 config.modeName 字符串推断（避免命名改动导致 IsModeB 误判）。</summary>
    public enum SlotGameMode { ModeA, ModeB }

    /// <summary>
    /// 一次完整游戏动作编排（逻辑层，不依赖 UnityEngine）：
    ///   基础旋转 → 火球 Hold&amp;Spin（≥1颗火球触发） → Scatter 免费旋转。
    /// </summary>
    public partial class GameSession
    {
        private readonly ReelConfig _cfg;
        private readonly ISlotRng _rng;
        private readonly SlotGameMode _mode;   // ★ 显式模式标记，替代从 modeName 字符串推断 IsModeB()

        // 渐进奖池
        private Dictionary<string,float> _pots = new Dictionary<string,float>();
        private bool _potsInit;
        // 每档各自累计局数（中该档时清零回 0，未中的档继续累计）
        private Dictionary<string,int> _tierSpinCount = new Dictionary<string,int>();
        // 最近一次用于计算的压分（Contribute/RefreshPots 写入，ResetJackpot 清局数后重算用）
        private float _lastBet = 0f;

        // 模式B 跨局收集盘（持久，直到进 Mini 或全释放/全满后下一局清空）
        public HoldSpinState holdBoard;
        private bool _holdEnded;   // 上一局触发了 Mini（进 Mini 后清空收集盘）

        /// <summary>彩金池变化通知（注水/清零后自动触发）。由表现层(GameManager)挂接 BonusView.ShowPots，
        /// 逻辑层不直接引用 View。挂 null 即关闭自动刷新。</summary>
        public Action<IReadOnlyDictionary<string,float>> OnPotsChanged;

        public GameSession(ReelConfig cfg, ISlotRng rng, SlotGameMode mode)
        {
            _cfg = cfg;
            _rng = rng;
            _mode = mode;
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
            //UnityEngine.Debug.Log($"[{(_tierSpinCount[j.tier] == cnt ? "Refresh" : "Contribute")}] bet={bet}(eff={effBet}) tier={j.tier} ={val:F4} (effBet×betMult={effBet*j.betMult:F4}+potRate×局数={j.potRate*cnt:F4}[局数={cnt}])  {before:F2}→{_pots[j.tier]:F2}{(capped ? " [CAPPED@"+j.potCap+"]" : "")}");
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

        public GameResult Play(float bet, bool doubleFireball = false)
        {
            var res = new GameResult();
            EnsurePots();

            // 1) 基础旋转
            int[][] grid = OutcomeGenerator.Spin(_cfg, _rng, doubleFireball);

            // ★ 持有火球格排除掩码：显示层已把跨局持有格钉成火球，其底层新鲜卷轴可能藏有 Scatter，
            //   但该位置已被火球占据 → 不计 Scatter（否则"r2 全火球"却仍进免费小游戏、且白拿 scatter 赔付）。
            //   排除 = holdBoard.cells 中 filled 且未释放(released) 的位置；此刻 holdBoard 为本局推进(AdvanceHoldBoard)前的
            //   上一局持有态 = 玩家当前看到的全火球列。AdvanceHoldBoard 在下方 CheckFireballHoldSpin 才更新 holdBoard。
            bool[][] heldMask = null;
            if (holdBoard != null && holdBoard.cells != null)
            {
                heldMask = new bool[holdBoard.cells.Length][];
                for (int r = 0; r < holdBoard.cells.Length; r++)
                {
                    var col = holdBoard.cells[r];
                    int h = (col != null) ? col.Length : 0;
                    heldMask[r] = new bool[h];
                    bool released = (holdBoard.released != null && r < holdBoard.released.Length) ? holdBoard.released[r] : false;
                    for (int row = 0; row < h; row++)
                        heldMask[r][row] = !released && col[row] != null && col[row].filled;
                }
            }

            var wins = EvaluateBase(grid, bet, heldMask);
            res.baseWins = wins;
            float baseWin = 0;
            for (int i = 0; i < wins.Count; i++) baseWin += wins[i].payout;
            int sc = ScatterUtil.Count(grid, _cfg, heldMask);
            float sp = ScatterUtil.Payout(sc, _cfg, bet);
            res.baseWin = baseWin;
            res.scatterPayout = sp;
            res.scatterCount = sc;

            // 注：scatter(免费游戏) 不加入 baseWins 高亮。scatter 走独立计数/赔付(res.scatterPayout)，
            // 不参与普通连线高亮动画，避免与连线高亮混淆。

            // 2) grid 交给显示层
            res.baseGrid = grid;

            // 3) Scatter 免费旋转 → 只记奖励次数，交由 Mini（MiniGame）统一运行与结算。
            //    ★ 旧逻辑（在此处内部模拟 while 循环旋转、累加 res.freeSpinsWin）已删除，
            //      避免与 Mini 重复结算；免费游戏的实际旋转/连线加转/火球统计全部在 MiniGame 内进行。
            int fsAward = 0;
            if (_cfg.freeSpins != null)
            {
                // A 模式(useVolatility)：左起连续 ≥triggerScatter(默认3) 列、每列≥1 个 Free Games 符号即触发 → 波动性选局数+倍率；
                //   同列多个 Scatter 只算 1 列（按列去重），必须从最左列(reel0)起连续（左起口径）；奖励仍随机选 局数×倍率。
                // B 模式：Scatter 触发改为「左到右连续相邻」口径（reel0 起连续列每列≥1个，长度≥triggerScatter=3）。
                //   两者都只记次数，免费局由 MiniGame 运行。
                if (_cfg.freeSpins.useVolatility)
                {
                    fsAward = PickVolatilityFreeSpins(grid);
                }
                else
                {
                    int scL2R = ScatterUtil.CountLeftToRight(grid, _cfg, heldMask);
                    res.scatterL2R = scL2R;
                    fsAward = _cfg.freeSpins.SpinsFor(scL2R);
                }
            }
            res.freeSpinsAwarded = fsAward;
            res.freeSpinsFromScatter = fsAward;   // ★ 仅 Scatter 授予的部分（火球追加在 CheckFireballHoldSpin 内累加）
            // 注：res.freeSpinsWin 恒为 0（免费游戏赢分由 Mini 统计火球后经回调 AddFeatureWin 入账）。

            // 4) 火球检测：基础旋转落了火球？→ 直线结算（holdMode="Direct"，A/B 共用）。
            //    ★ 必须在 res.freeSpinsAwarded 赋值之后调用：SettleFireballsDirect 会把 FREE 火球累加进
            //      res.freeSpinsAwarded（+=），若先于此处赋值会被覆盖归零。
            if (_cfg.fireballSymbolId > 0)
                CheckFireballHoldSpin(grid, bet, res);
            // ★ 火球追加后，反推火球授予部分 = 总数 − Scatter 部分（≥0 保护）。
            res.freeSpinsFromFireball = System.Math.Max(0, res.freeSpinsAwarded - res.freeSpinsFromScatter);

            res.totalPayout = res.baseWin + res.scatterPayout + res.featureWin + res.freeSpinsWin;
            return res;
        }

        /// <summary>A 模式波动性免费转触发+选择：左起连续 ≥triggerScatter(默认3) 列、每列≥1 个 Scatter(=Free Games 符号) 即触发
        /// （同一列多个 Scatter 只算 1 列，必须从 reel0 起连续，即"左起连续3列"口径；reel0 无 Scatter 则整局不触发）；
        /// 触发后随机选一档波动性( volatilitySpins[i] 局 + 对应 volatilityMultipliers[i] 倍 )，并把倍率写入 cfg.freeSpins.multiplier
        /// （供 MiniGame 结算免费局赢分时使用；当前 MiniGame 未消费该倍率，已标记 TODO）。
        /// 不足 triggerScatter 列则不触发(返回 0)。</summary>
        int PickVolatilityFreeSpins(int[][] grid)
        {
            var fs = _cfg.freeSpins;
            // ★ 触发口径：左起连续列数（每列≥1个Scatter算1列，断列即停；同列多个只算1个 → "1列4个当一个处理"）。
            //   必须从 reel0 起连续 ≥ triggerScatter(默认3) 列才触发；reel0 无 Scatter 则整局不触发。
            int cols = ScatterUtil.CountLeftToRight(grid, _cfg);
            if (cols < fs.triggerScatter) return 0;
            int n = Math.Min(fs.volatilitySpins.Count, fs.volatilityMultipliers.Count);
            if (n <= 0) return 0;
            int idx = _rng.Next(n);
            int spins = fs.volatilitySpins[idx];
            int mult = fs.volatilityMultipliers[idx];
            fs.multiplier = mult;   // ★ 写入倍率供 MiniGame 结算用（当前 MiniGame 未消费，标记 TODO）
            UnityEngine.Debug.Log($"[FreeSpins-A] 波动性触发：左起连续 {cols} 列含 Scatter(≥{fs.triggerScatter}) → {spins} 局 ×{mult}");
            return spins;
        }

        // ---- 内部 ----

        List<Win> EvaluateBase(int[][] grid, float bet, bool[][] exclude = null)
        {
            switch (_cfg.winEval)
            {
                case WinEvalType.Rows:    return new RowEvaluator().Evaluate(grid, _cfg, bet, exclude);
                case WinEvalType.Paylines: return new PaylineEvaluator().Evaluate(grid, _cfg, bet, exclude);
                default:                  return new WaysEvaluator().Evaluate(grid, _cfg, bet, exclude);
            }
        }

        /// <summary>公开：在任意网格上按当前 winEval 评估线奖（供 Hold&amp;Spin 每轮 respin 结算普通连线用）。
        /// exclude!=null 时排除指定格（持有火球格），使"中间火球切断"对所有符号生效、不产生 phantom 赢分。</summary>
        public List<Win> EvaluateGrid(int[][] grid, float bet, bool[][] exclude = null)
        {
            return EvaluateBase(grid, bet, exclude);
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

            // 火球"免费模式"按模式区分：B 模式 base-spin 传 true（火球可生成 FREE 类型累加免费局），
            // A 模式传 false（设计：火球不生成 FREE 类型，直接禁止，不依赖 freeModeRatio 配置）。
            bool allowFree = IsModeB();

            for (int r = 0; r < grid.Length; r++)
                for (int row = 0; row < grid[r].Length; row++)
                    if (grid[r][row] == fbId)
                    {
                        var c = HoldSpinState.RollFireball(_cfg, _rng, bet, _pots, allowFreeMode: allowFree);
                        c.reel = r; c.row = row; c.filled = true;
                        initial.Add(c);
                        if (res.baseFireballs == null) res.baseFireballs = new List<FireballCell>();
                        res.baseFireballs.Add(c);
                    }

            // ★ 模式B(Cash Falls 收集盘)：跨局持有，每局推进一个步（合并新火球 + 减一个圈圈 + 满列/释放）。
            //   不在此处重建 holdSpinState（收集盘跨局持久，见 GameSession.HoldB.cs AdvanceHoldBoard）。
            if (IsModeB())
            {
                AdvanceHoldBoard(initial, bet, res);
                return;
            }

            if (initial.Count == 0) return;  // 没火球 → 不触发

            // triggerMin: 最少几颗才触发（默认1=有就触发）
            int minTrigger = (_cfg.holdSpin.triggerMin > 0) ? _cfg.holdSpin.triggerMin : 1;
            if (initial.Count < minTrigger) return;

            // ★ A 模式(直线结算：holdMode="Direct")：落 ≥triggerMin 火球直接在基础轮算分，不进 Hold&Spin、不锁定、不收集盘。
            //   所有火球倍率之和 ×bet 计入 featureWin；彩金火球落定即中(即时清池)，中奖档记 res.wonJackpots 供显示层播特效。
            //   ★ 逻辑抽到 GameSession.A.cs 的 SettleFireballsDirect（A/B 共用，内部按 IsModeB() 区分：A=全局≥triggerMin，B=单列收集 FREE）。
            if (_cfg.holdSpin.holdMode == "Direct")
            {
                SettleFireballsDirect(initial, bet, res);
                return;  // 不创建 holdSpinState
            }

        }


    }
}
