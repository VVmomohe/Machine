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
        /// 归零→释放(火球回归滚动队列)；整列集满→对该列所有火球统一派彩(倍数/彩金/FREE)+enterMiniByColumnFill（进 Mini）。
        /// ★ 收集模式语义：火球入盘不付，整列集满才付。进 Mini 后下一局清空收集盘(_holdEnded)。
        /// FREE 火球单列累计仅在进 Mini 时并入 freeSpinsAwarded。</summary>
        /// <summary>把收集盘各列倒计时压成一行（r0=3 r1=- r2=F...），供 [Fireball-countdown] 直观确认"扣几次"。</summary>
        static string CountdownStr(HoldSpinState hs)
        {
            var sb = new System.Text.StringBuilder();
            for (int r = 0; r < hs.reels; r++)
            {
                if (r > 0) sb.Append(' ');
                if (hs.isFull[r]) sb.Append($"r{r}=F");
                else if (hs.released[r]) sb.Append($"r{r}=-");
                else sb.Append($"r{r}={hs.counter[r]}");
            }
            return sb.ToString();
        }

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

            var newJ = new List<string>();   // 本局新中彩金档（供显示层播特效），整个方法仅声明一次
            int freeAdded = 0;               // 本局 FREE 火球免费次数增量（新盘分支与方法级共用，仅声明一次避免 CS0136）
            var filledCols = new List<int>(); // ★ 本局「整列集满」的列（仅这些列才授予 FREE 火球免费次数，避免其它未集满列累计的 FREE 被误加）

            if (holdBoard == null)
            {
                int minTrigger = (_cfg.holdSpin.triggerMin > 0) ? _cfg.holdSpin.triggerMin : 1;
                if (initial.Count < minTrigger) return;   // 新局火球不足，不新建盘
                holdBoard = HoldSpinState.Start(_cfg, _rng, bet, initial, _pots, allowFreeMode: true, payOnStart: false);
                // ★ 收集模式：火球入盘不付，整列集满才付。检查新建盘是否有初始即满列（罕见但需处理）。
                for (int r = 0; r < holdBoard.reels; r++)
                {
                    if (!holdBoard.isFull[r] && !holdBoard.released[r] && HoldSpinState.ReelFull(holdBoard, r))
                    {
                        holdBoard.isFull[r] = true;
                        holdBoard.counter[r] = 0;
                        filledCols.Add(r);   // ★ 记录集满列（仅此列授予 FREE 火球免费次数）
                        for (int row = 0; row < holdBoard.cells[r].Length; row++)
                            PayFireball(holdBoard.cells[r][row], bet, holdBoard, newJ);
                        res.enterMiniByColumnFill = true;
                        _holdEnded = true;
                    }
                }
                // FREE 火球免费次数：仅「本局集满的列」才授予（避免其它未集满列累计的 FREE 被误加 → 表现"进 Mini 莫名多了 5 次"）
                if (res.enterMiniByColumnFill && _cfg.freeSpins != null)
                {
                    freeAdded = 0;
                    foreach (var col in filledCols)
                    {
                        int cnt = holdBoard.freeCountByCol.ContainsKey(col) ? holdBoard.freeCountByCol[col] : 0;
                        int award = _cfg.freeSpins.FreeballAwardFor(cnt);
                        int prev = holdBoard.prevFreeAward.ContainsKey(col) ? holdBoard.prevFreeAward[col] : 0;
                        if (award > prev) freeAdded += (award - prev);
                    }
                    res.freeSpinsAwarded += freeAdded;
                    foreach (var col in filledCols)
                        holdBoard.prevFreeAward[col] = _cfg.freeSpins.FreeballAwardFor(holdBoard.freeCountByCol.ContainsKey(col) ? holdBoard.freeCountByCol[col] : 0);
                }
                res.featureWin = holdBoard.accumulated;   // 首局：仅满列派彩（无满列则为0）
                res.wonJackpots = newJ;
                res.holdSpinState = holdBoard;
                if (SlotDebug.VerboseLogs) UnityEngine.Debug.Log($"[Fireball-B] 新建收集盘：{initial.Count} 颗 → featureWin={res.featureWin:F2} enterMini={res.enterMiniByColumnFill}");
                UnityEngine.Debug.Log($"[Fireball-countdown] 新建盘: {CountdownStr(holdBoard)}");
                return;
            }

            // 已有收集盘：合并本局新火球 + 每列减一个圈圈（有新火球则重置为 respinCount）
            float before = holdBoard.accumulated;
            // ★ 关键修复：按【列】统计本局是否落了火球（任何位置），而不是按【格子】。
            //   旧逻辑用 !holdBoard.cells[reel][row].filled 判定入盘：若同位置重复落入，cells[reel][row] 已 filled，
            //   不再入盘也不置 newInCol[r]=true → 该列被当作"无新火球"减圈到 0 释放 → released=true → cells 清空。
            //   但 baseGrid 该位置仍是火球符号（r.baseFireballs 含），屏上仍显示火球，且 SettleBaseB 因 released 隐藏圈圈——
            //   表现为"r1 火球固定了但没有圈圈"。修正：只要本局有火球落到该列（任意位置），就视为该列有新火球→counter 重置 3。
            var newInCol = new bool[holdBoard.reels];
            var fbReels = new HashSet<int>();
            if (hasNew)
                foreach (var f in initial)
                {
                    if (!f.filled) continue;
                    fbReels.Add(f.reel);
                    if (!holdBoard.cells[f.reel][f.row].filled)
                        holdBoard.cells[f.reel][f.row] = f;   // 入盘（f 已定倍率/档/FREE）—— 不派彩，整列集满才付
                                                                //   同位置重复落入：保留已有（避免重复派彩）；仍标记该列有新火球
                }
            for (int r = 0; r < holdBoard.reels; r++)
                if (fbReels.Contains(r)) newInCol[r] = true;

            int respinCount = (_cfg.holdSpin != null) ? _cfg.holdSpin.respinCount : 3;
            for (int r = 0; r < holdBoard.reels; r++)
            {
                if (holdBoard.isFull[r]) continue;
                // ★ 本局有新火球落到该列：清掉上一局留下的 released 标记（cells 已被 released 清空，本局新火球会入盘）
                if (newInCol[r] && holdBoard.released[r])
                {
                    holdBoard.released[r] = false;
                }
                if (holdBoard.released[r]) continue;
                // ★ 上一局已扣到 -1（本局仍无新火球）→ 本局彻底释放隐藏（圈圈 0 已显示过一局，再下一局才消失）
                if (!newInCol[r] && holdBoard.counter[r] < 0)
                {
                    holdBoard.released[r] = true;
                    continue;
                }
        if (newInCol[r])
        {
            holdBoard.counter[r] = respinCount;       // 新火球 → 重置圈圈为 3
        }
        else
        {
            holdBoard.counter[r] -= 1;                 // 无新火球 → 减一个圈圈（允许 3→2→1→0→-1）
            if (holdBoard.counter[r] <= 0)
            {
                // 倒计时归零：清掉该列火球，回归滚动队列（火球离场）；
                // 圈圈仍显示 0（见 SettleBaseB / ReelFireNum.showZero → 文本"0"），不立即隐藏。
                for (int row = 0; row < holdBoard.cells[r].Length; row++)
                    holdBoard.cells[r][row] = new FireballCell { reel = r, row = row };
            }
            if (holdBoard.counter[r] < 0)
            {
                // ★ 用户口径：扣到 -1 才真正释放隐藏（保证 0 被显示一局，再下一局 -1 消失）
                holdBoard.released[r] = true;
                holdBoard.counter[r] = 0;             // 归零，避免 SettleBaseB 显示负数
            }
        }
                // 满列判定（优先于释放）：某列集满所有格 → 对该列所有火球统一派彩 + 进 Mini
                if (!holdBoard.isFull[r] && HoldSpinState.ReelFull(holdBoard, r))
                {
                    holdBoard.isFull[r] = true;
                    holdBoard.counter[r] = 0;
                    filledCols.Add(r);   // ★ 记录集满列（仅此列授予 FREE 火球免费次数）
                    // ★ 收集模式：整列集满才对该列所有火球派彩（倍数/彩金/FREE 统一生效）
                    for (int row = 0; row < holdBoard.cells[r].Length; row++)
                        PayFireball(holdBoard.cells[r][row], bet, holdBoard, newJ);
                    res.enterMiniByColumnFill = true;
                    _holdEnded = true;
                }
            }

            // FREE 火球免费次数：仅「本局集满的列」才授予（防其它未集满列累计的 FREE 被误加 → "进 Mini 莫名多了 5 次"）
            freeAdded = 0;
            if (_cfg.freeSpins != null)
                foreach (var col in filledCols)
                {
                    int cnt = holdBoard.freeCountByCol.ContainsKey(col) ? holdBoard.freeCountByCol[col] : 0;
                    int award = _cfg.freeSpins.FreeballAwardFor(cnt);
                    int prev = holdBoard.prevFreeAward.ContainsKey(col) ? holdBoard.prevFreeAward[col] : 0;
                    if (award > prev) freeAdded += (award - prev);
                }
            if (res.enterMiniByColumnFill)
            {
                res.freeSpinsAwarded += freeAdded;
                if (_cfg.freeSpins != null)
                    foreach (var col in filledCols)
                        holdBoard.prevFreeAward[col] = _cfg.freeSpins.FreeballAwardFor(holdBoard.freeCountByCol.ContainsKey(col) ? holdBoard.freeCountByCol[col] : 0);
            }

            res.featureWin = holdBoard.accumulated - before;   // ★ 本局增量（避免跨局累计重复付）
            res.wonJackpots = newJ;                             // 仅本局新中彩金（旧档已由持久特效/上一局处理）
            res.holdSpinState = holdBoard;
            if (SlotDebug.VerboseLogs) UnityEngine.Debug.Log($"[Fireball-B] 收集盘推进：新火球={hasNew} 本局featureWin={res.featureWin:F2} enterMini={res.enterMiniByColumnFill}");
            UnityEngine.Debug.Log($"[Fireball-countdown] 推进: {CountdownStr(holdBoard)}");
            // ★ 诊断：按列打印 newInCol / counter / released / isFull / filled 数（核对"r1 有火球无圈圈"是否 newInCol 漏标记）；受 SlotDebug.VerboseLogs 开关控制。
            if (SlotDebug.VerboseLogs)
            {
                var sbDiag = new System.Text.StringBuilder("[Fireball-B-cols]");
                for (int r = 0; r < holdBoard.reels; r++)
                {
                    int filled = 0;
                    for (int row = 0; row < holdBoard.cells[r].Length; row++) if (holdBoard.cells[r][row].filled) filled++;
                    sbDiag.Append($" r{r}[new={newInCol[r]} cnt={holdBoard.counter[r]} rel={holdBoard.released[r]} full={holdBoard.isFull[r]} filled={filled}]");
                }
                UnityEngine.Debug.Log(sbDiag.ToString());
            }
        }

        /// <summary>逐颗结算一颗火球：倍数火球→×bet 累加；彩金火球→记档+即时清池；FREE 火球→单列计数（不派彩）。
        /// ★ 收集模式：仅在「整列集满」时对该列所有火球调用，火球入盘时不调。
        /// outJackpots 收集本局新中彩金档（供显示层播特效）。</summary>
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
