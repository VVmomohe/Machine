using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

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
        public MiniLockConfig miniLock;                      // Mini 免费小游戏「行锁定」配置（A/B 共用）

            // ===== 基础旋转符号密度（与转轮条带密度解耦，直接控制每格出什么符号）=====
            // 设计（用户 2026-07-30）：普通ICON≈80% / 章鱼2% / 百搭2% / 免费3% / 火球13%。
            // 实现：baseSpin.specialProb = 目标「每格」是特殊符号(章鱼/免费/火球)的占比 f；
            //   普通ICON = 1 - f。特殊符号内部按 specialWeights=[章鱼,免费,火球] 加权选一种。
            //   生成时按当前列 cap 把 f 折算成「每段触发特殊概率」(抵消普通游程稀释)，保证每列每格特殊率≈f。
            //   百搭(wild)在纯随机架构下与其它符号同权（见 OutcomeGenerator.Spin，每格 1/12 均匀），不再单独规划。
            public BaseSpinConfig baseSpin;

        [JsonIgnore] private Dictionary<int, SymbolPay> _byId;   // 懒构建：symbolId → SymbolPay，避免 GetSymbol 每次 O(n) 线性扫描（评估时每格都查，频率极高）
        [JsonIgnore] private int _scatterId = -2;                // -2=未初始化；EnsureSymbolIndex 首次调用后置 -1（无）或 ≥0
        [JsonIgnore] private int _wildId = -2;

        private void EnsureSymbolIndex()
        {
            if (_byId != null) return;
            _byId = new Dictionary<int, SymbolPay>(paytable.Count);
            _scatterId = -1; _wildId = -1;
            foreach (var s in paytable)
            {
                _byId[s.symbolId] = s;
                if (s.scatter && _scatterId < 0) _scatterId = s.symbolId;
                if (s.wild && _wildId < 0) _wildId = s.symbolId;
            }
        }

        public SymbolPay GetSymbol(int id)
        {
            EnsureSymbolIndex();
            return _byId.TryGetValue(id, out var s) ? s : null;
        }

        public int ScatterId()
        {
            EnsureSymbolIndex();
            return _scatterId;
        }

        /// <summary>百搭(Wild)符号 id（-1=无）。</summary>
        public int WildId()
        {
            EnsureSymbolIndex();
            return _wildId;
        }

        /// <summary>是否为特性符号（火球/FireLink 等特性符，不参与基础连线/ways 判定，按特性符号跳过）。</summary>
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

    /// <summary>
    /// 基础旋转每格的符号密度配置（与转轮条带解耦）。
    /// specialProb = 目标「每格」是特殊符号(章鱼/免费/火球)的占比 f；普通ICON = 1 - f。
    ///   注意：这是 per-cell 目标占比，生成时会按列 cap 自动折算成段触发概率，无需手算稀释。
    /// specialWeights = 特殊符号内部相对权重，顺序固定 [章鱼, 免费, 火球]。
    ///   例：specialProb=0.18 + [2,3,13] → 章鱼2% / 免费3% / 火球13%（普通82%，再减百搭≈80%）。
    /// </summary>
    [Serializable]
    public class BaseSpinConfig
    {
        public float specialProb = 0.18f;
        public List<int> specialWeights = new List<int> { 2, 3, 13 };
    }
}
