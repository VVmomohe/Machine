using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlotMachine.Core
{
    /// <summary>
    /// 一种棋盘模式的完整配置。两种模式（4x5 / 4-4-6-6-8）就是两份此对象。
    /// 由 JSON 经 JsonUtility 反序列化得到，可直接在 inspector 里改。
    /// </summary>
    [Serializable]
    public class ReelConfig
    {
        public string modeName;
        public int reelCount = 5;
        public List<int> reelRows = new List<int>();          // 每列可见行数
        public List<List<int>> reelStrips = new List<List<int>>(); // 每列符号带（Newtonsoft.Json 原生支持嵌套列表）

        public WinEvalType winEval;                            // 0=连线 1=Ways
        public List<List<int>> paylines = new List<List<int>>();   // 每条: 每列行号
        public List<SymbolPay> paytable = new List<SymbolPay>();
        public List<int> scatterPays = new List<int>();       // index=scatter个数
        public int minMatch = 3;
        public int totalWays = 1;                             // 变行模式 = product(reelRows)
        public int lines = 0;                                 // 连线模式活跃线数

        // ===== 百搭(Wild)生成控制（数据驱动，缺省保守）=====
        public int maxWildsPerSpin = 1;               // 整盘百搭上限（0=禁用百搭；默认 1=「只生成一个」）
        public bool wildAllowedInFirstReel = false;   // 百搭是否允许出现在第一列(reel0)；默认 false（不在第一列生成）
        public float wildSpawnChance = 0.5f;          // 单次旋转实际投放百搭的概率（整体出现率；1=必出，0.5=约半数出）

        // ===== 火球 / Fire Link / 奖池 / 免费转 特性（数据驱动，缺省=关闭）=====
        public int fireballSymbolId = -1;                    // 火球符号 id（-1=不启用）
        public int fireLinkSymbolId = -1;                    // FireLink 大奖符 id（-1=不启用）
        public int maxRows = 8;                              // Fire Link 大奖回合解锁到的行数
        public HoldSpinConfig holdSpin;                      // 火球锁定参数
        public List<JackpotTier> jackpots = new List<JackpotTier>(); // Mini/Minor/Major/Mega
        public FreeSpinsConfig freeSpins;                    // Scatter 免费转

        public SymbolPay GetSymbol(int id)
        {
            for (int i = 0; i < paytable.Count; i++)
                if (paytable[i].symbolId == id) return paytable[i];
            return null;
        }

        public int ScatterId()
        {
            for (int i = 0; i < paytable.Count; i++)
                if (paytable[i].scatter) return paytable[i].symbolId;
            return -1;
        }

        /// <summary>百搭(Wild)符号 id（-1=无）。</summary>
        public int WildId()
        {
            for (int i = 0; i < paytable.Count; i++)
                if (paytable[i].wild) return paytable[i].symbolId;
            return -1;
        }

        /// <summary>是否为特性符号（仅火球；FireLink 已废弃，不参与基础连线/ways 判定）。</summary>
        public bool IsFeatureSymbol(int id)
        {
            return id == fireballSymbolId;
        }

        /// <summary>某符号的最低连数：优先用该符号自身的 minMatch，否则全局 minMatch。</summary>
        public int MinMatchFor(int id)
        {
            var s = GetSymbol(id);
            if (s != null && s.minMatch > 0) return s.minMatch;
            return minMatch;
        }

        /// <summary>count 连(>=最低连数)的赔付倍数，含 wild 自身。pays[count-start] 索引（start 为该符号实际最低连数）。</summary>
        public float PayMult(int id, int count)
        {
            var s = GetSymbol(id);
            if (s == null) return 0f;
            int start = (s.minMatch > 0) ? s.minMatch : minMatch;
            if (count < start) return 0f;
            int idx = count - start;
            if (idx < 0) return 0f;
            if (idx >= s.pays.Count) idx = s.pays.Count - 1;   // 超出连击上限 → 封顶最高赔付（如 6~8 连按 5 连赔）
            return s.pays[idx];
        }

        /// <summary>符号总数（= paytable.Count），用于 fallback 随机符号生成。</summary>
        public int GetSymbolCount() => paytable.Count;
    }
}
