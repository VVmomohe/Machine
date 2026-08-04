using System;
using System.Collections.Generic;

namespace SlotMachine.Core
{
    /// <summary>
    /// 转轴结果生成（客户端本地版）。
    /// ★ 2026-07-31 起改为「纯随机」：每格独立均匀随机取符号 ID（1..12），无聚类、无限制、无概率加权。
    ///   符 9章鱼 / 10Wild / 11Scatter / 12火球 与普通 1..8 同权，均按 1/12 均匀出现。
    /// ★ 算法侧未来会直接下发结果网格（按列给 ID：例如第一列给 rows 个 ID，客户端替换为目标 ID 后滚到对应位置停）。
    ///   届时只需替换本函数体（或在外部拿到算法结果后直接返回该 grid），所有调用方
    ///   （GameSession.Play 基础局 / MiniGame 免费转）无需改动 —— 本函数即唯一的「产出目标网格」接缝。
    /// 返回 grid[reel][row]，row0=底部（视觉下），row=rows-1=顶部（视觉上）。
    /// </summary>
    public static class OutcomeGenerator
    {
        // 符号 ID 范围（与项目约定一致：1..8 普通，9章鱼 / 10Wild / 11Scatter / 12火球）
        const int SymbolMin = 1;
        const int SymbolMax = 12;

        /// <summary>
        /// 产出本局目标网格：每格独立均匀随机（1..12）。
        /// doubleFireball=true 时为测试模式：每格 25% 概率直接落火球(=SymbolMax)，其余 75% 在 1..11 均匀（排除火球）。
        /// </summary>
        public static int[][] Spin(ReelConfig cfg, ISlotRng rng, bool doubleFireball = false)
        {
            int reels = cfg.reelRows.Count;
            var grid = new int[reels][];
            for (int c = 0; c < reels; c++)
            {
                int rows = cfg.reelRows[c];
                grid[c] = new int[rows];
                for (int row = 0; row < rows; row++)
                    grid[c][row] = SymbolMin + rng.Next(SymbolMax - SymbolMin + 1); // [1,12] 均匀
            }

            if (doubleFireball)
            {
                // 测试模式：每格 25% 概率直接落火球(id=SymbolMax)，其余 75% 在 1..11 均匀（排除火球）。
                int fbCount = 0;
                for (int c = 0; c < reels; c++)
                    for (int row = 0; row < grid[c].Length; row++)
                    {
                        if (rng.Next(100) < 25)   // 25% 火球
                        {
                            grid[c][row] = SymbolMax;
                            fbCount++;
                        }
                        else
                        {
                            grid[c][row] = SymbolMin + rng.Next(SymbolMax - SymbolMin); // [1,11] 均匀（排除火球）
                        }
                    }
                UnityEngine.Debug.Log($"[OutcomeGenerator] doubleFireball=true → 25% 火球测试模式，火球数={fbCount}");
            }

            return grid;
        }
    }
}
