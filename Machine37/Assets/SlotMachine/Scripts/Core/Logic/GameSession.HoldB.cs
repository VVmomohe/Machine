using System.Collections.Generic;

namespace SlotMachine.Core
{
    /// <summary>模式B(Cash Falls / 收集盘) 跨局持有逻辑：
    ///   holdBoard 跨基础局持久保留（GameSession.holdBoard），每开一局(Play)推进【一个】步：
    ///   合并本局基础火球入盘 + 每列"真·新增火球(落新格)→重置3 / 否则−1" + 归零释放 + 整列集满→进 Mini。
    ///   不再有单局内循环 respin（那会让圈圈在停轮后慢慢减完）；圈圈只在"开新一局"时减一。
    ///   显示与动画（钉 overlay / tong / 计数器 / 满列演出）由 GameManager.Flow.B 在停轮后按盘当前态展示。</summary>
    public partial class GameSession
    {
        /// <summary>模式B 收集盘推进（跨局持有，纯逻辑，不依赖 Unity）：
        /// 把本局基础火球(initial)合并进持久 holdBoard；每列倒计时按"真·新增火球(落新格)→3 / 否则−1"推进；
        /// 归零→释放(火球回归滚动队列)；整列集满→对该列所有火球统一派彩(倍数/彩金/FREE)+enterMiniByColumnFill（进 Mini）。
        /// ★ 收集模式语义：火球入盘不付，整列集满才付。进 Mini 后下一局仅清空「集满的那一列」(_holdEnded)，其余持有列继续保留。
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
            // 进 Mini 后：仅清空「整列集满」的 THAT 列（已派彩 + 火球滚回卷轴），其余持有列跨 Mini 继续保留、圈圈不被清零。
            // 旧实现 holdBoard = null 会连其它列一起清空 → 用户反馈「小游戏出来后全部列圈圈都清零了」。
            if (_holdEnded && holdBoard != null)
            {
                for (int r = 0; r < holdBoard.reels; r++)
                {
                    if (!holdBoard.isFull[r]) continue;          // 只动集满的那一列
                    for (int row = 0; row < holdBoard.cells[r].Length; row++)
                        holdBoard.cells[r][row] = new FireballCell { reel = r, row = row };   // 清空本列火球
                    holdBoard.isFull[r] = false;
                    holdBoard.released[r] = false;
                    holdBoard.counter[r] = 0;
                    if (holdBoard.freeCountByCol.ContainsKey(r)) holdBoard.freeCountByCol[r] = 0;   // FREE 计数重置，便于重新收集
                    if (holdBoard.prevFreeAward.ContainsKey(r)) holdBoard.prevFreeAward[r] = 0;
                }
                _holdEnded = false;
            }

            bool hasNew = initial != null && initial.Count > 0;

            // 无盘且无新火球 → 不触发（res.baseFireballs 仍保留，供显示层兜底钉出）
            if (holdBoard == null && !hasNew) return;

            // 板子已死（无活跃列且无满列）→ 重置，等同无盘
            if (holdBoard != null && !HoldSpinState.AnyActive(holdBoard) && !HoldSpinState.AnyFull(holdBoard))
                holdBoard = null;

            // ★ 快照「本局推进前」各列是否已跨局持有火球（供展示层旋转期只显示老持有列圈圈、不剧透本局新落列）。
            //   必须在合并本局新火球之前抓拍；holdBoard 下方会被原地改写，preHeld 是独立副本不受影响。
            bool[] preHeld = null;
            int[] preCnt = null;   // ★ 推进前每列圈数快照（供旋转期显示旧值；真正的递减在停稳后由 SettleBaseB 显示）
            HashSet<int> preHeldCells = null;   // ★ 推进前逐格持有快照(reel*100+row)，供旋转期只钉"真正持有过的火球"
            if (holdBoard != null)
            {
                preHeld = new bool[holdBoard.reels];
                preCnt = new int[holdBoard.reels];
                preHeldCells = new HashSet<int>();
                for (int r = 0; r < holdBoard.reels; r++)
                {
                    bool hadFilled = false;
                    for (int row = 0; row < holdBoard.cells[r].Length; row++)
                        if (holdBoard.cells[r][row].filled) { hadFilled = true; preHeldCells.Add(CellKey.Encode(r, row)); }
                    preHeld[r] = !holdBoard.released[r] && !holdBoard.isFull[r] && (hadFilled || holdBoard.counter[r] > 0);
                    preCnt[r] = holdBoard.counter[r];   // ★ 推进前快照（此时尚未递减/重置，= 上一局结束值）
                }
            }

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
                        if (SlotDebug.VerboseLogs) UnityEngine.Debug.Log($"[COLLECT-B] r{r} 整列集满→收集(火球回归队列) [新盘分支]");
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
                holdBoard.preRoundHeldCols = new bool[holdBoard.reels];   // 全 false：本局全为新落，旋转期不剧透
                holdBoard.preRoundCounter = new int[holdBoard.reels];      // 新盘：无推进前旧值，旋转期本就不显圈（preRoundHeldCols 全 false）
                holdBoard.preRoundHeldCells = new System.Collections.Generic.HashSet<int>();   // 新盘：无推进前持有格，旋转期不钉任何火球
                if (SlotDebug.VerboseLogs)
                {
                    UnityEngine.Debug.Log($"[Fireball-B] 新建收集盘：{initial.Count} 颗 → featureWin={res.featureWin:F2} enterMini={res.enterMiniByColumnFill}");
                    UnityEngine.Debug.Log($"[Fireball-countdown] 新建盘: {CountdownStr(holdBoard)}");
                }
                return;
            }

            // 已有收集盘：合并本局新火球 + 每列减一个圈圈（有新火球则重置为 respinCount）
            float before = holdBoard.accumulated;
            // ★ 按【列】统计本局是否落了火球（任何位置）。
            var newInCol = new bool[holdBoard.reels];
            var fbByReel = new Dictionary<int, List<FireballCell>>();   // 本局新火球按列分组（释放旧火球后用于重新入盘）
            if (hasNew)
                foreach (var f in initial)
                {
                    if (!f.filled) continue;
                    newInCol[f.reel] = true;
                    if (!fbByReel.ContainsKey(f.reel)) fbByReel[f.reel] = new List<FireballCell>();
                    fbByReel[f.reel].Add(f);
                }

            // ★ 修正(2026-08-04)：newInCol 只判断"本局该列是否落了火球(含砸到已持有格的碰撞)"。
            //   碰撞(落点在推进前已持有)时，底层 cells 因"同位置保留旧火球"未新增格子 → 屏上无可见新火球，
            //   但 newInCol[r]=true 仍把圈圈重置为 3，造成"没看到新火球、圈圈却没减"的错觉(用户实测)。
            //   realNewInCol = 落点推进前为空(真·新增一格)才重置为 3；碰撞(落点已持有)→ false → 正常递减，与玩家所见一致。
            var realNewInCol = new bool[holdBoard.reels];
            if (hasNew)
                foreach (var f in initial)
                {
                    if (!f.filled) continue;
                    if (!preHeldCells.Contains(CellKey.Encode(f.reel, f.row)))   // 推进前该格为空 → 本局真·新增一颗火球
                        realNewInCol[f.reel] = true;
                }

            int respinCount = (_cfg.holdSpin != null) ? _cfg.holdSpin.respinCount : 3;
            for (int r = 0; r < holdBoard.reels; r++)
            {
                if (holdBoard.isFull[r]) continue;
                // 本局有新火球落到该列：清掉上一局留下的 released 标记（cells 已空，本局新火球会重新入盘）
                if (realNewInCol[r] && holdBoard.released[r])
                    holdBoard.released[r] = false;
                if (holdBoard.released[r]) continue;

                // 释放判定：counter==0 即已显示过"0"帧、本应回归滚动队列。
                // 此时无论本局是否落新火球，旧火球都必须释放（修复：旧逻辑"落新火球→重置3"会把已到0的列
                // 误判为刷新、旧火球永不回归）。若本局落了新火球，旧火球释放后新火球作为全新捕获重新入盘，
                // 二者独立计数。
                bool releasePending = holdBoard.counter[r] == 0;

                if (releasePending)
                {
                    if (SlotDebug.VerboseLogs) UnityEngine.Debug.Log($"[RELEASE-B] r{r} 圈圈归零→释放旧火球回队列 counter前={holdBoard.counter[r]} newInCol={newInCol[r]}{(newInCol[r] ? " (本局新火球将重新捕获)" : "")}");
                    // 释放旧火球（清空本列 cells = 回归滚动队列）
                    for (int row = 0; row < holdBoard.cells[r].Length; row++)
                        holdBoard.cells[r][row] = new FireballCell { reel = r, row = row };
                    holdBoard.released[r] = true;
                    holdBoard.counter[r] = 0;
                    if (realNewInCol[r])
                    {
                        // 旧火球已释放；新火球作为全新捕获重新入盘（刚清空必为空，入盘不派彩）
                        holdBoard.released[r] = false;
                        if (fbByReel.ContainsKey(r))
                            foreach (var f in fbByReel[r])
                                if (!holdBoard.cells[f.reel][f.row].filled)
                                    holdBoard.cells[f.reel][f.row] = f;
                        holdBoard.counter[r] = respinCount;
                    }
                    // 满列判定（重新入盘后可能集满）
                    if (!holdBoard.isFull[r] && HoldSpinState.ReelFull(holdBoard, r))
                    {
                        holdBoard.isFull[r] = true;
                        holdBoard.counter[r] = 0;
                        filledCols.Add(r);
                        if (SlotDebug.VerboseLogs) UnityEngine.Debug.Log($"[COLLECT-B] r{r} 整列集满→收集(火球回归队列)");
                        for (int row = 0; row < holdBoard.cells[r].Length; row++)
                            PayFireball(holdBoard.cells[r][row], bet, holdBoard, newJ);
                        res.enterMiniByColumnFill = true;
                        _holdEnded = true;
                    }
                    continue;
                }

                // ★ 正常推进：counter>0
                if (realNewInCol[r])
                {
                    // 新火球(真·新增一格) → 重置圈圈为 3（旧火球仍在持有窗口内，新火球并入同列 cells）
                    if (fbByReel.ContainsKey(r))
                        foreach (var f in fbByReel[r])
                            if (!holdBoard.cells[f.reel][f.row].filled)
                                holdBoard.cells[f.reel][f.row] = f;
                    holdBoard.counter[r] = respinCount;
                }
                else
                {
                    holdBoard.counter[r] -= 1;                 // 无真·新增火球(碰撞落已持有格不算) → 减一个圈圈（3→2→1→0，0 为"待释放"下一局回归）
                }

                // 满列判定
                if (!holdBoard.isFull[r] && HoldSpinState.ReelFull(holdBoard, r))
                {
                    holdBoard.isFull[r] = true;
                    holdBoard.counter[r] = 0;
                    filledCols.Add(r);
                    if (SlotDebug.VerboseLogs) UnityEngine.Debug.Log($"[COLLECT-B] r{r} 整列集满→收集(火球回归队列)");
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
            if (preHeld != null) holdBoard.preRoundHeldCols = preHeld;   // ★ 推进前快照（老持有列 vs 本局新落列）供展示层用
            if (preCnt != null) holdBoard.preRoundCounter = preCnt;       // ★ 推进前圈数快照（旋转期显示旧值，递减留到停稳后）
            if (preHeldCells != null) holdBoard.preRoundHeldCells = preHeldCells;   // ★ 推进前逐格持有快照（旋转期只钉真正持有的火球，修复碰撞格漏钉）
            if (SlotDebug.VerboseLogs)
            {
                UnityEngine.Debug.Log($"[Fireball-B] 收集盘推进：新火球={hasNew} 本局featureWin={res.featureWin:F2} enterMini={res.enterMiniByColumnFill}");
                UnityEngine.Debug.Log($"[Fireball-countdown] 推进: {CountdownStr(holdBoard)}");
            }
            // 按列诊断（受 SlotDebug.VerboseLogs 开关控制）：newInCol / counter / released / isFull / filled 数。
            if (SlotDebug.VerboseLogs)
            {
                var sbDiag = new System.Text.StringBuilder("[Fireball-B-cols]");
                for (int r = 0; r < holdBoard.reels; r++)
                {
                    int filled = 0;
                    for (int row = 0; row < holdBoard.cells[r].Length; row++) if (holdBoard.cells[r][row].filled) filled++;
                    sbDiag.Append($" r{r}[new={newInCol[r]} realNew={realNewInCol[r]} cnt={holdBoard.counter[r]} rel={holdBoard.released[r]} full={holdBoard.isFull[r]} filled={filled}]");
                }
                UnityEngine.Debug.Log(sbDiag.ToString());
            }
            // ★ 诊断（受 SlotDebug.VerboseLogs 控制）：推进后每列状态一览，便于核对"哪列释放/集满/持有几圈"，
            //   与展示层 [RELEASE-MOVE]/[CLEAR-EXCEPT] 对照，定位"有圈圈却火球回归队列"。
            if (SlotDebug.VerboseLogs)
            {
                var sbSum = new System.Text.StringBuilder("[Fireball-B-SUM] ");
                for (int r = 0; r < holdBoard.reels; r++)
                {
                    int f = 0; for (int row = 0; row < holdBoard.cells[r].Length; row++) if (holdBoard.cells[r][row].filled) f++;
                    sbSum.Append($"r{r}[cnt={holdBoard.counter[r]} rel={holdBoard.released[r]} full={holdBoard.isFull[r]} filled={f}] ");
                }
                UnityEngine.Debug.Log(sbSum.ToString() + $"| enterMini={res.enterMiniByColumnFill}");
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
