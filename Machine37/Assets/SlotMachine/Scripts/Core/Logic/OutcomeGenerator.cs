using System;
using System.Collections.Generic;
using System.Linq;

namespace SlotMachine.Core
{
    /// <summary>
    /// 转轴结果生成：纯垂直聚类（同一列内上下连续行同符号），水平方向完全随机。
    /// 规则（按【列/转轮索引】reel，从 0 起算，对应列高 4/4/6/6/8）：
    ///   reel0（最左，高4）：无垂直聚类（每格独立随机，竖连上限 = 1）
    ///   reel1（高4）：竖连上限 = 2
    ///   reel2（高6）：竖连上限 = 3
    ///   reel3（高6）：竖连上限 = 4
    ///   reel4（高8）：竖连上限 = 5
    /// ★ 相邻游程强制不同号（去重），游程不能跨行拼接 → 严格保证每列竖连 ≤ 该列上限。
    ///   这样既能按列给出确定的最大连号，又不会因"拼接"突破上限（对齐原游戏"超高"连号率）。
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
        /// 填充一列：自底向上，按【列索引】给定竖连上限逐段填游程。
        /// 相邻游程强制不同号，游程不能跨段拼接 → 单列竖连严格 ≤ 该列上限。
        /// </summary>
        static void FillClusteredColumn(int[][] grid, int col, int rows,
            List<int> specialBag, double specialProb, ISlotRng rng)
        {
            if (rows <= 0) return;

            int cap = GetReelCap(col);          // 该列竖连上限（reel0=1 无聚类 … reel4=5）
            int r = rows - 1;                    // 从最底行开始往上
            int belowSym = -1;                   // 正下方已填符号（初始无）

            while (r >= 0)
            {
                // 特殊符号：单格散落，打断普通符号的竖向游程
                bool useSpecial = specialBag.Count > 0 && rng.NextDouble() < specialProb;
                if (useSpecial)
                {
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

                // ★ 普通游程：长度 1..cap（同时不超过剩余行数 r+1）
                int maxRun = Math.Min(cap, r + 1);
                int runLen = 1 + rng.Next(maxRun);

                // ★ 相邻游程强制不同号：新游程符号排除正下方符号，
                //   防止两段同号拼接突破该列上限（保证竖连严格 ≤ cap）。
                var candidates = (belowSym >= 1 && belowSym <= 8)
                    ? NormalPool.Where(s => s != belowSym).ToList()
                    : NormalPool;
                int sym = candidates[rng.Next(candidates.Count)];

                for (int k = 0; k < runLen; k++)
                    grid[col][r - k] = sym;

                belowSym = sym;
                r -= runLen;
            }
        }

        /// <summary>返回某列(reel)允许的最大竖向游程长度（按列索引，对应布局 4/4/6/6/8）：
        /// reel0=1（无聚类），reel1=2，reel2=3，reel3=4，reel4=5，更靠右的列类推（封顶 5）。</summary>
        static int GetReelCap(int reel)
        {
            return Math.Min(reel + 1, 5);
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
