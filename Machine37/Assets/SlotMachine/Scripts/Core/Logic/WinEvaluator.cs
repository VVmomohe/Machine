using System;
using System.Collections.Generic;

namespace SlotMachine.Core
{
    public interface IPayEvaluator
    {
        List<Win> Evaluate(int[][] grid, ReelConfig cfg, float totalBet);
    }

    public static class ScatterUtil
    {
        public static int Count(int[][] grid, ReelConfig cfg)
        {
            int sid = cfg.ScatterId();
            if (sid < 0) return 0;
            int c = 0;
            for (int r = 0; r < grid.Length; r++)
                for (int row = 0; row < grid[r].Length; row++)
                    if (grid[r][row] == sid) c++;
            return c;
        }

        public static float Payout(int count, ReelConfig cfg, float totalBet)
        {
            if (count >= 0 && count < cfg.scatterPays.Count)
                return cfg.scatterPays[count] * totalBet;
            return 0f;
        }
    }

    /// <summary>
    /// 经典连线（payline）判定：每条蛇形线从 reel0 起连续相同符号(含 wild 替代)。
    /// </summary>
    public class PaylineEvaluator : IPayEvaluator
    {
        public List<Win> Evaluate(int[][] grid, ReelConfig cfg, float totalBet)
        {
            var wins = new List<Win>();
            int lines = cfg.paylines.Count;
            if (lines == 0) return wins;
            float betPerLine = totalBet / lines;

            for (int li = 0; li < lines; li++)
            {
                var line = cfg.paylines[li];
                // 基准符号：取线上第一个非 wild、非特性符号；全 wild 则取 wild
                int baseSym = -1;
                for (int reel = 0; reel < cfg.reelCount; reel++)
                {
                    int s = grid[reel][line[reel]];
                    var sp = cfg.GetSymbol(s);
                    if (sp != null && !sp.wild && !cfg.IsFeatureSymbol(s)) { baseSym = s; break; }
                }
                if (baseSym < 0) baseSym = grid[0][line[0]];

                int cnt = 0;
                for (int reel = 0; reel < cfg.reelCount; reel++)
                {
                    int s = grid[reel][line[reel]];
                    var sp = cfg.GetSymbol(s);
                    if (s == baseSym || (sp != null && sp.wild)) cnt++;
                    else break;
                }

                var bsp = cfg.GetSymbol(baseSym);
                if ((bsp != null && bsp.scatter) || cfg.IsFeatureSymbol(baseSym)) continue;
                if (cnt >= cfg.MinMatchFor(baseSym))
                {
                    float mult = cfg.PayMult(baseSym, cnt);
                    if (mult > 0)
                    {
                        var w = new Win
                        {
                            lineIndex = li,
                            symbolId = baseSym,
                            count = cnt,
                            ways = 0,
                            payout = mult * betPerLine
                        };
                        for (int reel = 0; reel < cnt; reel++)
                            w.positions.Add(reel * 100 + line[reel]);
                        wins.Add(w);
                    }
                }
            }
            return wins;
        }
    }

    /// <summary>
    /// Ways 判定：某符号从 reel0 起连续出现即算，ways=各列出现数乘积。
    /// 自动适配变行(4-4-6-6-8)，无需维护连线表。
    /// </summary>
    public class WaysEvaluator : IPayEvaluator
    {
        public List<Win> Evaluate(int[][] grid, ReelConfig cfg, float totalBet)
        {
            var wins = new List<Win>();
            float perWay = totalBet / cfg.totalWays;

            for (int pi = 0; pi < cfg.paytable.Count; pi++)
            {
                var sp = cfg.paytable[pi];
                // 特性符号(火球/FireLink)不参与基础连线/ways 判定
                if (sp.scatter || sp.fireball || sp.firelink) continue;
                int sym = sp.symbolId;

                int[] counts = new int[cfg.reelCount];
                for (int r = 0; r < cfg.reelCount; r++)
                {
                    int c = 0;
                    for (int row = 0; row < grid[r].Length; row++)
                    {
                        int s = grid[r][row];
                        var sp2 = cfg.GetSymbol(s);
                        if (s == sym || (sp2 != null && sp2.wild)) c++;
                    }
                    counts[r] = c;
                }

                int k = 0;
                while (k < cfg.reelCount && counts[k] > 0) k++;
                int matched = k;
                if (matched >= cfg.MinMatchFor(sym))
                {
                    long ways = 1;
                    for (int i = 0; i < matched; i++) ways *= counts[i];
                    float mult = cfg.PayMult(sym, matched);
                    if (mult > 0)
                    {
                        var w = new Win
                        {
                            lineIndex = -1,
                            symbolId = sym,
                            count = matched,
                            ways = (int)ways,
                            payout = ways * mult * perWay
                        };
                        // 记录本 symbol 在各列实际中奖的格子坐标(reel*100+row)，供视图高亮
                        for (int r = 0; r < matched; r++)
                            for (int row = 0; row < grid[r].Length; row++)
                            {
                                int s = grid[r][row];
                                var cell = cfg.GetSymbol(s);
                                if (s == sym || (cell != null && cell.wild))
                                    w.positions.Add(r * 100 + row);
                            }
                        wins.Add(w);
                    }
                }
            }
            return wins;
        }
    }

    /// <summary>
    /// 逐列匹配（Rows）判定：从第 1 列开始，从左到右逐列检查。
    /// 只要该列包含 ≥1 个目标符号（或 wild），就算该列匹配；连续匹配的列数 = match，≥minMatch 即赢。
    /// 一旦某一列不包含该符号（且无 wild）立刻断掉，后面不再算。
    /// 高亮所有该符号（含 wild）的位置。
    /// scatter / fireball 不参与（scatter 全局算、fireball 走特性）。
    /// </summary>
    public class RowEvaluator : IPayEvaluator
    {
        public List<Win> Evaluate(int[][] grid, ReelConfig cfg, float totalBet)
        {
            var wins = new List<Win>();

            foreach (var s in cfg.paytable)
            {
                if (s.scatter || s.fireball || s.firelink) continue;
                if (s.pays == null || s.pays.Count == 0) continue;
                int sym = s.symbolId;

                int match = 0;
                var positions = new System.Collections.Generic.List<int>();
                for (int reel = 0; reel < cfg.reelCount; reel++)
                {
                    bool has = false;
                    for (int row = 0; row < grid[reel].Length; row++)
                    {
                        int gid = grid[reel][row];
                        var sp = cfg.GetSymbol(gid);
                        if (gid == sym || (sp != null && sp.wild))
                        {
                            has = true;
                            positions.Add(reel * 100 + row);
                        }
                    }
                    if (!has) break;
                    match++;
                }

                if (match < cfg.MinMatchFor(sym)) continue;

                float mult = cfg.PayMult(sym, match);
                if (mult <= 0f) continue;

                var w = new Win
                {
                    lineIndex = -1,
                    symbolId = sym,
                    count = match,
                    ways = 0,
                    payout = mult * totalBet
                };
                w.positions.AddRange(positions);
                wins.Add(w);
            }
            return wins;
        }
    }
}
