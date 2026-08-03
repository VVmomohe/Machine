using System.Collections.Generic;

namespace SlotMachine.Core
{
    /// <summary>模式B(Cash Falls / 收集盘) 跨局持有逻辑：
    ///   holdBoard 跨基础局持久保留（GameSession.holdBoard），每开一局(Play)推进【一个】步：
    ///   合并本局基础火球入盘 + 每列"有新火球→重置3 / 无新火球→−1" + 归零释放 + 整列集满→进 Mini。
    ///   不再有单局内循环 respin（那会让圈圈在停轮后慢慢减完）；圈圈只在"开新一局"时减一。
    ///   显示与动画（钉 overlay / tong / 计数器 / 满列演出）由 GameManager.Flow.B 在停轮后按盘当前态展示。</summary>
    public partial class GameSession
    {
        /// <summary>模式B 收集盘推进（跨局持有，纯逻辑，不依赖 Unity）：
        /// 把本局基础火球(initial)合并进持久 holdBoard；每列倒计时按"新火球→3 / 否则−1"推进；
        /// 归零→释放(火球回归滚动队列)；整列集满→enterMiniByColumnFill（进 Mini）。
        /// 进 Mini 后下一局清空收集盘(_holdEnded)。FREE 火球单列累计仅在进 Mini 时并入 freeSpinsAwarded。</summary>
        void AdvanceHoldBoard(List<FireballCell> initial, float bet, GameResult res)
        {
            // 进 Mini 后：清空收集盘，下一局从零开始
            if (_holdEnded) { holdBoard = null; _holdEnded = false; }

            bool hasNew = initial != null && initial.Count > 0;

            // 无盘且无新火球 → 不触发（res.baseFireballs 仍保留，供显示层兜底钉出）
            if (holdBoard == null && !hasNew) return;

            // 板子已死（无活跃列且无满列）→ 重置，等同无盘
            if (holdBoard != null && !HoldSpinState.AnyActive(holdBoard) && !HoldSpinState.AnyFull(holdBoard))
                holdBoard = null;

            if (holdBoard == null)
            {
                int minTrigger = (_cfg.holdSpin.triggerMin > 0) ? _cfg.holdSpin.triggerMin : 1;
                if (initial.Count < minTrigger) return;   // 新局火球不足，不新建盘
                holdBoard = HoldSpinState.Start(_cfg, _rng, bet, initial, _pots, allowFreeMode: true, payOnStart: false);
                var newJ = new List<string>();
                foreach (var f in initial) PayFireball(f, bet, holdBoard, newJ);
                res.featureWin = holdBoard.accumulated;   // 首局：本局收集即全部
                res.wonJackpots = newJ;
                res.holdSpinState = holdBoard;
                UnityEngine.Debug.Log($"[Fireball-B] 新建收集盘：{initial.Count} 颗 → featureWin={res.featureWin:F2}");
                return;
            }

            // 已有收集盘：合并本局新火球 + 每列减一个圈圈（有新火球则重置为 respinCount）
            float before = holdBoard.accumulated;
            var newInCol = new bool[holdBoard.reels];
            var newJ = new List<string>();
            if (hasNew)
                foreach (var f in initial)
                {
                    if (!f.filled) continue;
                    if (!holdBoard.cells[f.reel][f.row].filled)
                    {
                        holdBoard.cells[f.reel][f.row] = f;   // 入盘（f 已定倍率/档/FREE）
                        newInCol[f.reel] = true;
                        PayFireball(f, bet, holdBoard, newJ);
                    }
                }

            int respinCount = (_cfg.holdSpin != null) ? _cfg.holdSpin.respinCount : 3;
            for (int r = 0; r < holdBoard.reels; r++)
            {
                if (holdBoard.isFull[r] || holdBoard.released[r]) continue;
                if (newInCol[r])
                {
                    holdBoard.counter[r] = respinCount;       // 新火球 → 重置圈圈为 3
                }
                else
                {
                    holdBoard.counter[r] -= 1;                 // 无新火球 → 减一个圈圈
                    if (holdBoard.counter[r] <= 0)
                    {
                        // 倒计时归零且未集满 → 释放：清掉该列火球，回归滚动队列
                        holdBoard.counter[r] = 0;
                        holdBoard.released[r] = true;
                        for (int row = 0; row < holdBoard.cells[r].Length; row++)
                            holdBoard.cells[r][row] = new FireballCell { reel = r, row = row };
                        continue;
                    }
                }
                // 满列判定（优先于释放）：某列集满所有格 → 进 Mini
                if (!holdBoard.isFull[r] && HoldSpinState.ReelFull(holdBoard, r))
                {
                    holdBoard.isFull[r] = true;
                    holdBoard.counter[r] = 0;
                    res.enterMiniByColumnFill = true;
                    _holdEnded = true;
                }
            }

            // FREE 火球免费次数：单列累计，仅「整列集满」开 Mini 时才授予（防单颗 FREE 就进小游戏）
            int freeAdded = 0;
            if (_cfg.freeSpins != null)
                foreach (var kv in holdBoard.freeCountByCol)
                {
                    int award = _cfg.freeSpins.FreeballAwardFor(kv.Value);
                    int prev = holdBoard.prevFreeAward.ContainsKey(kv.Key) ? holdBoard.prevFreeAward[kv.Key] : 0;
                    if (award > prev) freeAdded += (award - prev);
                }
            if (res.enterMiniByColumnFill)
            {
                res.freeSpinsAwarded += freeAdded;
                if (_cfg.freeSpins != null)
                    foreach (var kv in holdBoard.freeCountByCol)
                        holdBoard.prevFreeAward[kv.Key] = _cfg.freeSpins.FreeballAwardFor(kv.Value);
            }

            res.featureWin = holdBoard.accumulated - before;   // ★ 本局增量（避免跨局累计重复付）
            res.wonJackpots = newJ;                             // 仅本局新中彩金（旧档已由持久特效/上一局处理）
            res.holdSpinState = holdBoard;
            UnityEngine.Debug.Log($"[Fireball-B] 收集盘推进：新火球={hasNew} 本局featureWin={res.featureWin:F2} enterMini={res.enterMiniByColumnFill}");
        }

        /// <summary>逐颗结算一颗火球：倍数火球→×bet 累加；彩金火球→记档+即时清池；FREE 火球→单列计数（不派彩）。
        /// 供「新建盘」与「合并新火球」复用；outJackpots 收集本局新中彩金档（供显示层播特效）。</summary>
        void PayFireball(FireballCell f, float bet, HoldSpinState hs, List<string> outJackpots)
        {
            if (f.kind == FireballKind.FreeSpins)
            {
                if (!hs.freeCountByCol.ContainsKey(f.reel)) hs.freeCountByCol[f.reel] = 0;
                hs.freeCountByCol[f.reel]++;
                return;
            }
            hs.accumulated += bet * f.multiplier;
            if (f.jackpotTier >= 0 && f.jackpotTier < HoldSpinState.JackpotTierNames.Length)
            {
                string t = HoldSpinState.JackpotTierNames[f.jackpotTier];
                hs.wonJackpots.Add(t);
                outJackpots.Add(t);
                ResetJackpot(t);   // 彩金火球落定即中 + 即时清池
            }
        }
    }
}
