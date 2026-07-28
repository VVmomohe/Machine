using System;
using System.Collections.Generic;
using System.Linq;

namespace SlotMachine.Core
{
    /// <summary>
    /// 转轴结果生成：纯垂直聚类（同一列内上下连续行同符号），水平方向完全随机。
    /// ★ 新架构（用户 2026-07-25）：每列先从一个预构建的「符号 List」里取一段连续窗口作为结果，
    ///   所有 ID 在生成阶段一次性算定，停轮/视图层不再中途更换 ID 或 ICON。
    /// 规则（按【列/转轮索引】reel，从 0 起算，对应列高 4/4/6/6/8）：
    ///   reel0（最左，高4）：无垂直聚类（每格独立随机，竖连上限 = 1）
    ///   reel1（高4）：竖连上限 = 2
    ///   reel2（高6）：竖连上限 = 3
    ///   reel3（高6）：竖连上限 = 4
    ///   reel4（高8）：竖连上限 = 5
    /// ★ 相邻游程强制不同号（去重），游程不能跨段拼接 → 严格保证每列竖连 ≤ 该列上限。
    /// 特殊符号(9章鱼/11免费/12火球)：不参与聚类，按概率以单格散落。
    /// 百搭(10)：由 DecideWildPlan 预先决定（最多 maxWildsPerSpin 颗、排除第一列 reel0 与顶行、整体 wildSpawnChance 概率），写一次。
    /// 返回 grid[reel][row]，row0=顶部，row=rows-1=底部。
    /// </summary>
    public static class OutcomeGenerator
    {
        static readonly List<int> NormalPool = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8 };

        // 每列预构建 List 长度 = 可见行数 × 此倍数（用户示例：re0=4*10 / re3=6*10 / re4=8*10）。
        // 列表比可见窗口长，窗口外作 padding；最终只取其中连续 rows 个作为该列结果（"从 List 拿窗口"）。
        const int StripBaseMultiplier = 10;

        static bool IsSpecial(int id) => id >= 9 && id <= 12;

        public static int[][] Spin(ReelConfig cfg, ISlotRng rng)
        {
            int reels = cfg.reelRows.Count;
            var grid = new int[reels][];
            for (int i = 0; i < reels; i++)
                grid[i] = new int[cfg.reelRows[i]];

            // 特殊符号袋：排除百搭(由 wildPlan 预先决定)，仅含 9章鱼/11免费/12火球
            var specialBag = BuildSpecialBag(cfg);
            double specialProb = specialBag.Count > 0
                ? Math.Min(0.15, (double)specialBag.Count / cfg.reelStrips.Sum(s => s.Count))
                : 0;

            // ★ 1) 预先决定百搭方案（写一次，绝不事后清理/替换）
            var wildCells = DecideWildPlan(cfg, rng);

            // ★ 2) 逐列：先从 List 算好整列 ID 队列（含聚类），再取一段连续窗口作为该列结果
            for (int c = 0; c < reels; c++)
            {
                int rows = cfg.reelRows[c];
                int[] colResult = BuildColumnFromList(c, rows, specialBag, specialProb, rng);
                for (int row = 0; row < rows; row++)
                    grid[c][row] = colResult[row];
            }

            // ★ 3) 注入预先决定的百搭（仅写一次，覆盖该格原本算出的符号）
            int wildId = cfg.WildId();
            foreach (var (col, row) in wildCells)
                if (col >= 0 && col < grid.Length && row >= 0 && row < grid[col].Length)
                    grid[col][row] = wildId;

            return grid;
        }

        /// <summary>
        /// 从预构建 List 取该列结果：先按列索引上限生成长度为 rows×StripBaseMultiplier 的聚类序列（List），
        /// 再随机取其中连续 rows 个作为本列可见结果（窗口外作 padding，绝不中途换）。
        /// 不含百搭（百搭由 DecideWildPlan 统一注入）。
        /// </summary>
        static int[] BuildColumnFromList(int col, int rows, List<int> specialBag, double specialProb, ISlotRng rng)
        {
            int listLen = rows * StripBaseMultiplier;
            var queue = BuildRunSequence(listLen, GetReelCap(col), specialBag, specialProb, rng);

            // 随机取一段连续窗口（窗口来自已算好的 List，而非滚动中临时决定）
            int maxStart = listLen - rows;
            int start = (maxStart > 0) ? rng.Next(maxStart + 1) : 0;
            var result = new int[rows];
            for (int i = 0; i < rows; i++)
                result[i] = queue[start + i];
            return result;
        }

        /// <summary>生成长度为 len 的聚类序列（自底向上，按 cap 给定竖连上限）。返回 int[]。</summary>
        static int[] BuildRunSequence(int len, int cap, List<int> specialBag, double specialProb, ISlotRng rng)
        {
            var queue = new int[len];
            if (len <= 0) return queue;

            int r = len - 1;                    // 从最底行开始往上
            int belowSym = -1;                  // 正下方已填符号（初始无）

            while (r >= 0)
            {
                // 特殊符号：单格散落，打断普通符号的竖向游程
                bool useSpecial = specialBag.Count > 0 && rng.NextDouble() < specialProb;
                if (useSpecial)
                {
                    var spCandidates = (belowSym >= 9 && belowSym <= 12)
                        ? specialBag.Where(s => s != belowSym).ToList()
                        : specialBag;
                    queue[r] = spCandidates.Count > 0
                        ? spCandidates[rng.Next(spCandidates.Count)]
                        : NormalPool[rng.Next(NormalPool.Count)];   // 兜底：无可用特殊符时用普通符
                    belowSym = queue[r];
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
                    queue[r - k] = sym;

                belowSym = sym;
                r -= runLen;
            }
            return queue;
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
            int wildId = cfg.WildId();
            var bag = new List<int>();
            foreach (var strip in cfg.reelStrips)
                foreach (var s in strip)
                    if (IsSpecial(s) && s != wildId) bag.Add(s);   // ★ 排除百搭，百搭走 DecideWildPlan
            return bag;
        }

        // ─── 百搭预先决定（写一次，不事后清理）────────────────────

        /// <summary>
        /// 预先决定本局百搭落点：最多 maxWildsPerSpin 颗，排除第一列(reel0, 除非 wildAllowedInFirstReel)
        /// 与顶行(data row0, 与视图层 toprow 拦截一致，避免显示/结算脱节)，整体以 wildSpawnChance 概率实际投放。
        /// 返回 (col,row) 列表；写一次，生成层据此注入，视图层无需再替换。
        /// </summary>
        static List<(int col, int row)> DecideWildPlan(ReelConfig cfg, ISlotRng rng)
        {
            var result = new List<(int col, int row)>();
            int wildId = cfg.WildId();
            if (wildId < 0) return result;
            if (cfg.maxWildsPerSpin <= 0) return result;
            if (rng.NextDouble() >= cfg.wildSpawnChance) return result;   // 整体出现率

            // 候选(列,行)：排除第一列(除非允许) 与 顶行(data row0)
            var cells = new List<(int col, int row)>();
            for (int c = 0; c < cfg.reelRows.Count; c++)
            {
                if (!cfg.wildAllowedInFirstReel && c == 0) continue;
                int rows = cfg.reelRows[c];
                for (int row = 1; row < rows; row++)   // row0=顶行，排除（与视图 toprow 拦截一致）
                    cells.Add((c, row));
            }
            if (cells.Count == 0) return result;

            RandomHelper.Shuffle(cells, rng);
            int place = Math.Min(cfg.maxWildsPerSpin, cells.Count);
            for (int i = 0; i < place; i++)
                result.Add(cells[i]);
            return result;
        }
    }
}
