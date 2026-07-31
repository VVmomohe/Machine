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
            int freeCount = 0;
            if (res.wonJackpots == null) res.wonJackpots = new List<string>();
            foreach (var c in initial)
            {
                if (c.kind == FireballKind.FreeSpins) { freeCount++; continue; }
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
            if (freeCount > 0 && _cfg.freeSpins != null)
            {
                int award = _cfg.freeSpins.FreeballAwardFor(freeCount);
                res.freeSpinsAwarded += award;
                UnityEngine.Debug.Log($"[Fireball-A] 直线结算：{freeCount} 颗 FREE 火球 → +{award} 免费局 (freeSpinsAwarded={res.freeSpinsAwarded})");
            }
            UnityEngine.Debug.Log($"[Fireball-A] 直线结算：{initial.Count} 火球 → +{fbWin:F2} (featureWin={res.featureWin:F2})");
        }
    }
}
