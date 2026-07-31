using System.Collections.Generic;
using UnityEngine;

namespace SlotMachine.Core
{
    /// <summary>火球直线结算逻辑（A/B 共用，都走 holdMode=="Direct"），内部按 IsModeB() 区分收集规则：
    ///   · 模式A(China Street)：全局火球收集，triggerMin(=4) 由 CheckFireballHoldSpin 把关（全局火球数≥4 才触发）；
    ///     免费次数来自 Scatter 波动性(freeModeRatio=0，不生成 FREE 火球)，故不按火球派免费次数。
    ///   · 模式B(Cash Falls)：火球倍率之和仍全局计入 featureWin；仅 FREE 火球"免费模式"按【单列收集】数累加免费次数
    ///     (freeballTiers[1,2,3]→[2,5,10])。两者都落定即中彩金(即时清池)。
    ///   与 B 模式的 HoldSpinState.Start 分支完全分离，互不影响。</summary>
    public partial class GameSession
    {
        /// <summary>是否模式B(Cash Falls / 收集盘)：火球"免费模式"按单列收集。与 GameManager.IsModeB 同口径（modeName 含 "ModeB"）。</summary>
        bool IsModeB()
        {
            return _cfg.modeName != null
                && _cfg.modeName.IndexOf("ModeB", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>A/B 共用直线结算：所有火球倍率之和 ×bet 计入 featureWin；彩金火球落定即中 + 即时清池，
        /// 中奖档记 res.wonJackpots 供显示层(GameManager.Flow.ShowDirectJackpotEffects，基础局通用彩金特效)播特效。
        /// 由 CheckFireballHoldSpin 在 holdMode=="Direct" 时调用，调用后不再创建 holdSpinState。
        /// 收集规则按模式区分：A=全局(≥triggerMin 才触发)，B=单列(FREE 火球单列收集数→免费次数)。</summary>
        void SettleFireballsDirect(List<FireballCell> initial, float bet, GameResult res)
        {
            float fbWin = 0f;
            bool modeB = IsModeB();
            // 模式B：FREE 火球按【单列】收集数累加免费次数；模式A：全局 FREE 计数（freeModeRatio=0 实际不会出现）。
            var freeByCol = new Dictionary<int, int>();
            int freeGlobal = 0;
            if (res.wonJackpots == null) res.wonJackpots = new List<string>();
            foreach (var c in initial)
            {
                if (c.kind == FireballKind.FreeSpins)
                {
                    if (modeB)
                    {
                        if (!freeByCol.ContainsKey(c.reel)) freeByCol[c.reel] = 0;
                        freeByCol[c.reel]++;
                    }
                    else
                    {
                        freeGlobal++;
                    }
                    continue;
                }
                fbWin += bet * c.multiplier;
                if (c.jackpotTier >= 0 && c.jackpotTier < HoldSpinState.JackpotTierNames.Length)
                {
                    string t = HoldSpinState.JackpotTierNames[c.jackpotTier];
                    res.wonJackpots.Add(t);
                    ResetJackpot(t);   // A/B 直线结算：彩金火球落定即中 + 即时清池（与基础轮火球同源，落定即中）
                    UnityEngine.Debug.Log($"[JACKPOT-WIN] direct reel={c.reel} row={c.row} tier={t} → ResetJackpot({t})");
                }
            }
            res.featureWin += fbWin;
            // FREE 火球累加免费次数：仅模式B 走单列收集；模式A 不生成 FREE 火球(freeModeRatio=0)，不按火球派免费次数。
            if (_cfg.freeSpins != null)
            {
                if (modeB)
                {
                    if (freeByCol.Count > 0)
                    {
                        int bestCol = 0;
                        foreach (var kv in freeByCol) if (kv.Value > bestCol) bestCol = kv.Value;
                        int award = _cfg.freeSpins.FreeballAwardFor(bestCol);
                        if (award > 0)
                        {
                            res.freeSpinsAwarded += award;
                            UnityEngine.Debug.Log($"[Fireball-B] 单列收集 {bestCol} 颗 FREE 火球 → +{award} 免费局 (freeSpinsAwarded={res.freeSpinsAwarded})");
                        }
                    }
                }
                else if (freeGlobal > 0)
                {
                    // 防御：A 模式若意外出现 FREE 火球（freeModeRatio 应=0），不派免费次数并记录告警。
                    UnityEngine.Debug.LogWarning($"[Fireball-A] 警告：A 模式出现 {freeGlobal} 颗 FREE 火球（freeModeRatio 应为 0），已忽略，不派免费次数");
                }
            }
            UnityEngine.Debug.Log($"[Fireball-{(modeB ? "B" : "A")}] 直线结算：{initial.Count} 火球 → +{fbWin:F2} (featureWin={res.featureWin:F2})");
        }
    }
}
