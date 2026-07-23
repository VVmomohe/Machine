using System;
using System.Collections.Generic;
using System.Linq;

namespace SlotMachine.Core
{
    /// <summary>
    /// 转轴结果生成：纯垂直聚类（同一列内上下连续行同符号），水平方向完全随机。
    /// 规则（按从上往下数的绝对行号）：
    ///   第1排(row0)：无聚类，纯随机单格
    ///   第2排(row1)：最大竖连 2
    ///   第3排(row2)：最大竖连 2-3
    ///   第4排(row3)：最大竖连 3-4
    ///   第5排+(row4+)：最大竖连 4-5
    /// 特殊符号(9章鱼/10百搭/11免费/12火球)：不参与聚类，按概率以单格散落。
    /// 返回 grid[reel][row]，row0=顶部，row=rows-1=底部。
    /// </summary>
    public static class OutcomeGenerator
    {
        static readonly List<int> NormalPool = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 };

        static bool IsSpecial(int id) => id >= 9 && id <= 12;

        public static int[][] Spin(ReelConfig cfg, ISlotRng rng)
        {
            int reels = cfg.reelRows.Count;
            var grid = new int[reels][];
            for (int i = 0; i < reels; i++)
                grid[i] = new int[cfg.reelRows[i]];

            var specialBag = BuildSpecialBag(cfg);
            double specialProb = specialBag.Count > 0
                ? Math.Min(0.15, (double)specialBag.Count / cfg.reelStrips.Sum(s => s.Count))
                : 0;

            // ★ 逐列生成：自底向上，按绝对行号控制游程长度
            for (int c = 0; c < reels; c++)
                FillClusteredColumn(grid, c, cfg.reelRows[c], specialBag, specialProb, rng);

            LimitWilds(grid, cfg, rng);
            return grid;
        }

        /// <summary>
        /// 填充一列：从底部往上走。
        /// 每个游程的上限由 GetMaxValidRun 保证不超限；相邻游程强制不同符号防止合并。
        /// </summary>
        static void FillClusteredColumn(int[][] grid, int col, int rows,
            List<int> specialBag, double specialProb, ISlotRng rng)
        {
            if (rows <= 0) return;

            int r = rows - 1;               // 从最底行开始往上
            int belowSym = -1;              // 正下方已填的符号（初始无）

            while (r >= 0)
            {
                // 特殊符号：单格散落，打断普通符号的竖向游程
                bool useSpecial = specialBag.Count > 0 && rng.NextDouble() < specialProb;
                if (useSpecial)
                {
                    // ★ 特殊符号也避开正下方同值，防止两个散落的特殊符合并超限
                    var spCandidates = (belowSym >= 9 && belowSym <= 12)
                        ? specialBag.Where(s => s != belowSym).ToList()
                        : specialBag;
                    grid[col][r] = spCandidates.Count > 0
                        ? spCandidates[rng.Next(spCandidates.Count)]
                        : NormalPool[rng.Next(NormalPool.Count)];   // 兜底：无可用特殊符时用普通符
                    belowSym = grid[col][r];
                    r--;
                    continue;
                }

                int maxRun = GetMaxValidRun(r);
                int runLen = Math.Max(1, 1 + rng.Next(maxRun));

                // ★ 选符号时避开正下方紧邻的符号，防止两个游程合并超限
                int sym;
                var candidates = (belowSym >= 1 && NormalPool.Contains(belowSym))
                    ? NormalPool.Where(s => s != belowSym).ToList()
                    : NormalPool;
                sym = candidates[rng.Next(candidates.Count)];

                for (int k = 0; k < runLen; k++)
                    grid[col][r - k] = sym;

                belowSym = sym;
                r -= runLen;
            }
        }

        /// <summary>
        /// 返回在行 r 及以上能启动的最大竖向游程长度。
        /// 游程向上延伸 [r-len+1, r]，必须保证每行的上限都不被突破。
        /// 由于 GetMaxForRow 随行号递增（顶部最严），只需校验游程顶行即可。
        /// </summary>
        static int GetMaxValidRun(int row)
        {
            int maxPossible = Math.Min(5, row + 1);     // 不超过全局最大5，也不越过 row0
            // 从大到小试，找到第一个满足"顶行允许该长度"的值
            for (int len = maxPossible; len >= 1; len--)
            {
                int topRow = row - len + 1;             // 游程最高到达的行
                if (GetMaxForRow(topRow) >= len)
                    return len;
            }
            return 1;                                   // 兜底至少单格
        }

        /// <summary>返回某绝对行号允许的最大竖向游程长度。</summary>
        static int GetMaxForRow(int row)
        {
            return row switch
            {
                0 => 1,     // 第1排：单格，不聚类
                1 => 2,     // 第2排：最多连 2
                2 => 3,     // 第3排：最多连 3
                3 => 4,     // 第4排：最多连 4
                _ => 5,     // 第5排及以下：最多连 5
            };
        }

        // ─── 符号池 ────────────────────────────────────────────

        static List<int> BuildSpecialBag(ReelConfig cfg)
        {
            var bag = new List<int>();
            foreach (var strip in cfg.reelStrips)
                foreach (var s in strip)
                    if (IsSpecial(s)) bag.Add(s);
            return bag;
        }

        // ─── 百搭限制 ──────────────────────────────────────────

        static void LimitWilds(int[][] grid, ReelConfig cfg, ISlotRng rng)
        {
            int wildId = cfg.WildId();
            if (wildId < 0) return;

            // ★ 第一列(reel0，即 4-4-6-6-8 布局最左那列)永远无百搭：
            //   与视图层 reelIdx==0 拦截双保险，保证显示与连线判定都一致（用户要求）。
            if (grid.Length > 0)
                for (int row = 0; row < grid[0].Length; row++)
                    if (grid[0][row] == wildId)
                        grid[0][row] = NormalPool[rng.Next(NormalPool.Count)];

            // 整盘百搭 ≤1（仅统计第一列以外的列，reel0 已清）
            var wilds = new List<int>();
            for (int r = 1; r < grid.Length; r++)
                for (int row = 0; row < grid[r].Length; row++)
                    if (grid[r][row] == wildId) wilds.Add(r * 1000 + row);

            if (wilds.Count <= 1) return;

            int keepIdx = rng.Next(wilds.Count);
            for (int i = 0; i < wilds.Count; i++)
            {
                if (i == keepIdx) continue;
                int r = wilds[i] / 1000, row = wilds[i] % 1000;
                grid[r][row] = NormalPool[rng.Next(NormalPool.Count)];
            }
        }
    }
}
