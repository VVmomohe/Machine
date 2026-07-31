using System.Collections.Generic;
using UnityEngine;

namespace SlotMachine.Core
{
    /// <summary>模式A(China Street / 直线结算 holdMode="Direct") 专属逻辑：
    ///   基础旋转落 ≥triggerMin 火球即直接算分（不进 Hold&amp;Spin、不锁定、不 respin）。
    ///   与 B 模式(GameSession.cs 中 CheckFireballHoldSpin 的 HoldSpinState.Start 分支) 完全分离，互不影响。</summary>
    public partial class GameSession
    {
        /// <summary>A 直线结算：所有火球倍率之和 ×bet 计入 featureWin；彩金火球落定即中 + 即时清池，
        /// 中奖档记 res.wonJackpots 供显示层(GameManager.Flow.A.ShowDirectJackpotEffects)播特效。
        /// 由 CheckFireballHoldSpin 在 holdMode=="Direct" 时调用，调用后不再创建 holdSpinState。</summary>
        void SettleFireballsDirect(List<FireballCell> initial, float bet, GameResult res)
        {
            float fbWin = 0f;
            // ★ "一列收集"：FREE 火球按【单列】收集数累加免费次数（而非全盘总数）。
            //   记录每列(c.reel)落下的 FREE 火球数，取"收集最多的一列"作为档位依据，
            //   对应 freeballTiers[1,2,3]→freeballAwards[2,5,10]（freeModeRatio 由 JSON 控制出现概率）。
            var freeByCol = new Dictionary<int, int>();
            if (res.wonJackpots == null) res.wonJackpots = new List<string>();
            foreach (var c in initial)
            {
                if (c.kind == FireballKind.FreeSpins)
                {
                    if (!freeByCol.ContainsKey(c.reel)) freeByCol[c.reel] = 0;
                    freeByCol[c.reel]++;
                    continue;
                }
                fbWin += bet * c.multiplier;
                if (c.jackpotTier >= 0 && c.jackpotTier < HoldSpinState.JackpotTierNames.Length)
                {
                    string t = HoldSpinState.JackpotTierNames[c.jackpotTier];
                    res.wonJackpots.Add(t);
                    ResetJackpot(t);   // ★ A 直线结算：彩金火球落定即中 + 即时清池（与基础轮火球同源，落定即中）
                    UnityEngine.Debug.Log($"[JACKPOT-WIN] A-direct reel={c.reel} row={c.row} tier={t} → ResetJackpot({t})");
                }
            }
            res.featureWin += fbWin;
            // ★ FREE 火球累加免费次数（B 模式 base-spin 火球可出现 FREE 类型，触发 Mini）。
            //   仅当某列至少收集到 1 颗 FREE 火球才计入；档位按"单列收集数"取最高列（freeballTiers:1/2/3 → 2/5/10）。
            if (_cfg.freeSpins != null && freeByCol.Count > 0)
            {
                int bestCol = 0;
                foreach (var kv in freeByCol) if (kv.Value > bestCol) bestCol = kv.Value;
                int award = _cfg.freeSpins.FreeballAwardFor(bestCol);
                if (award > 0)
                {
                    res.freeSpinsAwarded += award;
                    UnityEngine.Debug.Log($"[Fireball-A] 直线结算：单列收集 {bestCol} 颗 FREE 火球 → +{award} 免费局 (freeSpinsAwarded={res.freeSpinsAwarded})");
                }
            }
            UnityEngine.Debug.Log($"[Fireball-A] 直线结算：{initial.Count} 火球 → +{fbWin:F2} (featureWin={res.featureWin:F2})");
        }
    }
}
