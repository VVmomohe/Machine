using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using SlotMachine.Core;
using Com.Back;   // DataManager（读取 Setting[1].auto 自动结算开关）

namespace com.slot
{
    /// <summary>GameManager 的 Hold&amp;Spin 子系统（partial 拆分自 GameManager.Flow.cs）：
    ///   进入特性 → 逐轮推进(respin 滚动/停稳/满列派彩) → 收尾/进 Mini。</summary>
    public partial class GameManager
    {
        #region Hold&Spin

        // ★ Hold&Spin 单轮赢分累加器：由 RunRespinRound 写入、主协程/ResolveAfterRound 读取。
        //   抽成协程后无法用 ref/out 参数回传，故提升为实例字段，仅在一轮 AdvanceHoldSpin 生命周期内有效。
        private float _holdRoundWin;

        // ★ 已落账到总分的 Hold 赢分累计（含每轮即时落的 + 收尾补差的）。用于收尾时只补"未加过的差额"，
        //   避免"每轮即时落账"与"收尾 ApplySpinResult(totalPayout)"重复加同一笔赢分。每次进入 Hold&Spin 清零。
        private float _holdAppliedWin;

        /// <summary>进入 Hold&Spin：显示初始锁定状态 + 每列计数器，然后等待玩家按 Start 逐轮推进。</summary>
        void EnterHoldSpin(GameResult r, HoldSpinState hs)
        {
            _activeHold = hs;
            _holdResult = r;
            _holdRolling = false;
            _holdAppliedWin = 0f;        // ★ 新 Hold&Spin 开始：已落账赢分清零
            _holdScatterSpins = r.freeSpinsAwarded;   // ★ 记录 Scatter 触发的原始次数（不含 FREE 火球追加），用于区分 collectedFree

            if (m_reelView != null)
            {
                // ★ 不在此处 ClearWinHighlight：基础旋转的中奖高亮须保留到玩家按 Start 进第一轮 respin 时才清，
                //   否则 HighlightWins 刚播就立刻被清掉（EnterHoldSpin 紧跟 SettleAfterReelsStop 的 HighlightWins 之后调用），
                //   导致用户看不到普通符号(如 J)的中奖动画（2026-07-25 用户报"J没有播放中奖动画"）。
                //   清理已移至 AdvanceHoldSpin 开头（respin 滚动前）。
                m_reelView.ShowFeatureState(hs);   // 火球格锁定 + 倍率文字 + 有火球的列显示计数器3
            }
            // 注意：IsOver 判定已移到 SettleAfterReelsStop 协程（需要先播满列掉落动画再收尾）
        }

        /// <summary>按一次 Start 推进一轮 Hold&Spin：扣压分 → 落火球/减计数器 → 真卷轴滚动 → 停稳结算 → 满列派彩。
        /// 每轮结束若还有活跃列则返回等下次 Start，若全部结束则收尾+统一结算(ShowWin→Confirm→Apply→Mini)。</summary>
        IEnumerator AdvanceHoldSpin()
        {
            if (_activeHold == null) yield break;
            _holdRolling = true;
            var hs = _activeHold;

            try
            {
                // ★ 清掉基础旋转的中奖高亮（原在 EnterHoldSpin 立即清，导致 HighlightWins 刚播就被清——用户看不到 J 等符号的中奖动画）。
                //   移到这里：玩家按 Start 进第一轮 respin 时才清，基础高亮在"等确认"期间持续可见。
                if (m_reelView != null) m_reelView.ClearWinHighlight();

                // 扣本轮流注分（每轮单独押注，余额不足则补 LastBet，仍不足则退出）
                if (!TryDeductRoundBet()) yield break;

                // === 单轮 respin（逻辑推进→滚动→停稳→线奖→满列派彩） ===
                _holdRoundWin = 0f;
                yield return StartCoroutine(RunRespinRound(hs));

                // 结算日志 + 本轮押注消费
                LogSettle("respin", m_machine.totalBet, _holdRoundWin);
                if (m_player != null) m_player.ResetBet();

                // === 判断本轮后如何推进（续轮 / 进 Mini / 收尾） ===
                yield return StartCoroutine(ResolveAfterRound(hs));
            }
            finally
            {
                _holdRolling = false;
            }
        }

        /// <summary>扣本轮流注分：余额不足补 LastBet，仍不足返回 false（主协程据此 yield break）。</summary>
        bool TryDeductRoundBet()
        {
            if (m_player == null) return true;
            if (m_player.m_bet_num <= 0) m_player.LastBet();
            if (m_player.m_bet_num <= 0) return false;
            m_machine.totalBet = m_player.m_bet_num;
            m_machine.session.Contribute(m_player.m_bet_num);
            m_player.ResetWinDisplay();
            return true;
        }

        /// <summary>单轮 respin 核心：推进逻辑→滚动列→真卷轴滚动→停稳结算→普通线奖→满列派彩+列清理。
        /// 写 _holdRoundWin 累加本轮赢分；含 yield（SpinHoldRound / CollectFullReelAnimation）。</summary>
        IEnumerator RunRespinRound(HoldSpinState hs)
        {
            // 1) 推进一轮逻辑（落火球/减计数器/释放列/满列派彩）
            // ★ 释放判定改由显示层 m_engaged 驱动：先把各列 engaged 状态读出来传给逻辑层（在 CheckEngagedAll 之后、本回合滚动之前）。
            bool[] engagedCols = (m_reelView != null) ? m_reelView.GetEngagedColumns() : null;
            var step = GameSession.RespinHoldSpin(hs, m_machine.config, m_machine.rng,
                m_machine.totalBet, m_machine.session.Pots, allowFreeMode: true, engaged: engagedCols);

            // 2) 滚动列 = 未集满列 + 本轮刚集满列(让完成满列的最后一颗火球随卷轴滚入) + 收集满列后"释放滚走"中的幽灵列
            var spun = new List<int>();
            for (int rr = 0; rr < hs.reels; rr++)
            {
                if (!hs.isFull[rr]) { spun.Add(rr); continue; }
                if (m_reelView != null && m_reelView.IsReelReleasing(rr))
                    spun.Add(rr);
            }
            // ★ 修复 BUG：本轮刚集满的列(hs.isFull 在 RespinHoldSpin 内被置 true，但还不是释放态)会被上面循环漏掉 → 该列本回合不滚动，
            //   完成满列的最后一颗火球直接以 overlay 出现（无滚入动画）。显式补进 spun，使其本回合照常滚入、停稳后再走满列收集。
            if (step.fullReels != null)
                foreach (var fr in step.fullReels)
                    if (!spun.Contains(fr.reel)) spun.Add(fr.reel);

            // 3) 真卷轴滚动（新火球作为真实条带符号 id12 随卷轴滚入，停稳后由 ApplyRespinStep 生成锁定 overlay）
            if (m_reelView != null)
            {
                m_reelView.ClearWinHighlight();
                if (step.reelSpun != null)
                    foreach (int reel in step.reelSpun) m_reelView.ReleaseReel(reel);

                var newFireMults = new Dictionary<int, FireballCell>();
                if (step.newFireballs != null)
                    foreach (var c in step.newFireballs) newFireMults[c.reel * 100 + c.row] = c;

                yield return StartCoroutine(m_reelView.SpinHoldRound(spun, 0.75f, newFireMults, step.respinGrid));
            }

            // 4) 停稳后结算：锁新火球 / 释放列 / 计数器-1 / 满列脉冲
            if (m_reelView != null)
                m_reelView.ApplyRespinStep(step, hs);

            // 5) 本轮普通线奖（★ 与基础旋转共用 SettleRoundWins：评估/高亮/音效/诊断同一套口径）
            if (m_machine != null && m_machine.session != null)
            {
                int[][] grid = BuildRespinGrid(hs);
                int sc;
                float win = SettleRoundWins(grid, m_machine.totalBet, out sc);
                if (win > 0)
                {
                    if (_holdResult != null) _holdResult.respinLineWin += win;
                    _holdRoundWin += win;
                }
                // 注：respin 符号池不含 Scatter，sc 恒为 0，不折算免费次数；
                //     Hold&Spin 的免费次数由 FreeSpins 火球（满列）统一负责（CountFreeFireballs/AwardFreeballSpinsFromMain）。
            }

            // 6) 满列派彩 + FREE 火球统计 + 列清理
            if (step.fullReels != null && step.fullReels.Count > 0)
            {
                float collectWin = 0f;
                foreach (var fr in step.fullReels) collectWin += fr.payout;

                // 满列 / 释放列 FREE 火球统一统计
                foreach (var fr in step.fullReels) CountFreeFireballs(hs, fr.reel);
                if (step.reelSpun != null)
                    foreach (int rr in step.reelSpun) CountFreeFireballs(hs, rr, clearAfter: true);

                if (m_reelView != null)
                    foreach (var fr in step.fullReels)
                    {
                        yield return StartCoroutine(m_reelView.CollectFullReelAnimation(fr.reel));
                        // 彩金特效：检查该列是否有彩金火球
                        if (m_bonus != null)
                            ShowJackpotEffectsForReel(hs, fr.reel);
                        // 火球收集成功：按本列总倍数分支播放音效（>8→18，否则→110）
                        if (FMODSoundMgr.Instance != null)
                            FMODSoundMgr.Instance.PlaySound(fr.sum > 8f ? "event:/Sounds/18" : "event:/Sounds/110");

                        // ★ 满列收集后立即清零该列中过的彩金档（不等 FinishHoldSpin）：
                        //   防止后续 respin 继续往已中过的池注水导致彩金变大。
                        if (m_machine?.session != null)
                        {
                            for (int row = 0; row < hs.cells[fr.reel].Length; row++)
                            {
                                var c = hs.cells[fr.reel][row];
                                if (c.filled && c.jackpotTier >= 0 && c.jackpotTier < HoldSpinState.JackpotTierNames.Length)
                                {
                                    string tierName = HoldSpinState.JackpotTierNames[c.jackpotTier];
                                    m_machine.session.ResetJackpot(tierName);
                                }
                            }
                        }
                    }

                if (collectWin > 0)
                {
                    _holdRoundWin += collectWin;   // ★ 本轮赢分已在 ResolveAfterRound 续轮分支统一落账+显示，这里不重复
                }

                foreach (var fr in step.fullReels)
                {
                    int rr = fr.reel;
                    hs.isFull[rr] = false;
                    hs.released[rr] = true;
                    hs.counter[rr] = 0;
                    for (int row = 0; row < hs.cells[rr].Length; row++)
                        hs.cells[rr][row].filled = false;
                    if (m_reelView != null)
                    {
                        m_reelView.ReleaseCollectedReel(rr);
                        // ★ 满列收集后火球已随 CollectFullReelAnimation 滚走；该列累计倍率 X 已通过 AddMultiplier 显示在计数器中。
                        //   不再单独 Freeze——可见性由 ReelFireNum 自管（active 且 rate>0 即显示 X），撑到结算清零/开新局。
                    }
                }
                // ★ 满列收集后刷新特效——收集后该列不再差1个火球，m_effect 应关闭
                if (m_reelView != null) m_reelView.RefreshColumnEffects(hs);
            }
        }

        /// <summary>单轮结束后分流：续轮(防狂按等待) / 中途收集到 FREE 火球进 Mini(保留 _activeHold) / IsOver 收尾。
        /// 各分支最终都结束本协程（主协程其后无业务代码，仅 finally 复位 _holdRolling）。</summary>
        IEnumerator ResolveAfterRound(HoldSpinState hs)
        {
            var holdR = _holdResult;

            // ★ collectedFree: 仅当 HoldSpin 期间「新增」了 FREE 火球奖励（不含 Scatter 触发的原始次数）。
            //   之前这里用 freeSpinsAwarded>0 会误把 Scatter 的 10 次也算进去，导致首次 respin 就进 Mini。
            int freeballAdded = (holdR != null) ? holdR.freeSpinsAwarded - _holdScatterSpins : 0;
            bool collectedFree = freeballAdded > 0;

                // 本轮后尚未结束且未收集 FREE：本轮赢分即时落账→等信用滚动(IsRolling)动画播完才放行下一轮 Start
                if (!hs.IsOver() && !collectedFree)
                {
                    // ★ 本轮赢分立即滚入总分（余额每轮即涨）：根除"收尾一次性落账"在 autoPlay 高速连转下
                    //   偶发丢分（用户 2026-07-28 反馈"赢分没加到总分"）。按 _holdRoundWin 落账并累计到 _holdAppliedWin，
                    //   收尾 ApplyHoldWinToCredit 只补差额，不会重复加。
                    if (_holdRoundWin > 0f && m_player != null)
                    {
                        m_player.ShowWinValue((long)System.Math.Round(_holdRoundWin));   // 显示本轮赢分
                        m_player.AddFeatureWin(_holdRoundWin);                            // 滚入总分（不动押注）
                        _holdAppliedWin += _holdRoundWin;
                    }

                    // ★ 防狂按穿透（用户 2026-07-25 拍板：选"急停+结算完才推进"）：
                    //   本轮赢分信用滚动(IsRolling)动画期间仍保持 _holdRolling=true，忽略所有 Start 输入，
                    //   等动画结束才放行下一轮 Start——避免"结算（赢分滚动）还没播完，狂按就又触发下一轮 respin"（用户反馈的 BUG）。
                    int waitFrames = 0;
                    while (m_player != null && m_player.IsRolling && waitFrames++ < 600)
                        yield return null;
                    yield break;
                }

            // ★ 收集到 FREE 但 HoldSpin 还没结束（counter>0 列仍在）→ 先进 Mini，保留 _activeHold
            //   Mini 结束后在回调里恢复火球/计数器，继续跑剩余列。
            if (collectedFree && !hs.IsOver())
            {
                // 结算当前赢分（不调 FinishHoldSpin——保留 _activeHold 给回调恢复）
                holdR.featureWin = hs.accumulated;
                holdR.totalPayout = holdR.baseWin + holdR.scatterPayout + holdR.respinLineWin + holdR.featureWin + holdR.freeSpinsWin;
                Settle(holdR);
                LogSettle("特性结束(进Mini)", m_machine.totalBet, holdR.featureWin);

                if (m_player != null)
                {
                    long tw = (long)System.Math.Round(holdR.totalPayout);
                    m_player.ShowWinValue(tw);
                    yield return StartCoroutine(WaitForConfirmKey());
                    ApplyHoldWinToCredit(holdR);   // 只补未加过的差额（每轮已即时落账）
                }

                int awardSpins = holdR.freeSpinsAwarded;   // 先保存（下面会清零）

                // ★ 清零 ALL FREE 火球 cells（全列遍历），防止 Mini 回来后 IsOver 时
                //   AwardFreeballSpinsFromMain 把同一批火球重数一遍 → 二次进 Mini → 次数膨胀。
                for (int r = 0; r < hs.reels; r++)
                    for (int row = 0; row < hs.cells[r].Length; row++)
                        if (hs.cells[r][row].filled && hs.cells[r][row].kind == FireballKind.FreeSpins)
                            hs.cells[r][row].filled = false;

                // 清零已结算字段，Mini 后 IsOver 时只算增量（防重复结算）
                holdR.baseWin = 0;
                holdR.scatterPayout = 0;
                holdR.respinLineWin = 0;
                holdR.freeSpinsAwarded = 0;
                _holdScatterSpins = 0;          // Scatter 奖励已随本次 Mini 消耗完毕
                hs.accumulated = 0;
                _holdAppliedWin = 0;            // ★ 已落账赢分清零（Mini 后的赢分从 0 起算补差）

                // 隐藏计数器（Mini 回来后恢复），但不清 _activeHold
                if (m_reelView != null) m_reelView.HideAllCounters();

                // 进入 Mini，回调中恢复 HoldSpin
                var savedHs = hs;
                LogMiniEntry("Hold&Spin中途收集FreeSpins火球", holdR, _holdScatterSpins, freeballAdded, savedHs);
                EnterMiniNow(holdR, () =>
                {
                    _activeHold = savedHs;
                    if (m_reelView != null)
                        m_reelView.ShowFeatureState(savedHs);
                }, awardSpins);
                yield break;
            }

            // === IsOver → 正常收尾（调 FinishHoldSpin） ===
            if (hs.IsOver()) AwardFreeballSpinsFromMain(hs, holdR);
            FinishHoldSpin();
            // ★ 特性结束、结算完成：num 与 rate 【不清零】——保留显示到玩家按确认开新局。
            //   隐藏时机：开新基础局（OnStartKey / ShowGrid → HideAllCounters → ResetAll），
            //   届时每列 num、rate 归 0，(num==0 && rate==0) 成立即隐藏（含满列 X 倍列）。
            LogSettle("特性结束", m_machine.totalBet, holdR != null ? holdR.featureWin : 0f);

            if (m_player != null && holdR != null)
            {
                long tw = (long)System.Math.Round(holdR.totalPayout);
                m_player.ShowWinValue(tw);
                yield return StartCoroutine(WaitForConfirmKey());
                ApplyHoldWinToCredit(holdR);   // 只补未加过的差额（每轮已即时落账）
                // ★ 计数器不在确认时隐藏：保留"收集到多少倍"的显示，直到玩家按确认开新基础局
                //   (OnStartKey → Spin → ShowGrid 入口的 HideAllCounters) 才统一消失。
            }

            int fbEnd = holdR.freeSpinsAwarded - _holdScatterSpins;
            LogMiniEntry("Hold&Spin收尾(IsOver)", holdR, _holdScatterSpins, fbEnd, hs);
            if (WillEnterMini(holdR)) { EnterMiniNow(holdR); yield break; }
            _spinPending = false;
        }

        void FinishHoldSpin()
        {
            var r = _holdResult;
            var hs = _activeHold;
            _activeHold = null;
            _holdResult = null;
            _holdRolling = false;
            _spinPending = false;

            // ★ FinishHoldSpin 本身不直接清火球计数器；清的时机在玩家"按确认开新一局"那一刻：
            //   ① OnStartKey（GameManager.Input.cs:60，IsRolling 守卫前）→ HideAllCounters，按下 Start 开新局瞬间即清；
            //   ② ShowGrid 入口（ReelView.Reels.cs:45，ReleaseCollectedForNextSpin 之后）兜底再清一遍（幂等）。
            //   ★ 结算/确认(respin 收尾)时一律不清——计数器(含 0 圈静止帧、满列 X 文本)撑到开新局才隐藏（用户要求）。
            //   两者都不在 respin 每轮触发（每轮走 AdvanceHoldSpin，计数器须常显）。进 Mini 路径 Flow.cs:368 也会清。

            if (r != null && hs != null)
            {
                r.featureWin = hs.accumulated;
                r.totalPayout = r.baseWin + r.scatterPayout + r.respinLineWin + r.featureWin + r.freeSpinsWin;
            }

            if (r != null) Settle(r);

            // ★ 中过彩金后清零对应档池（渐进池中奖重置）；火球 multiplier 在生成时已锁定，不影响已中金额
            // wonJackpots 已存档名 string（如 "Mini"），直接传给 ResetJackpot
            // ★ 注：满列收集时已即时清过一次（见 RunRespinRound 满列分支），此处为兜底/收尾清零
            if (hs != null && hs.wonJackpots != null && hs.wonJackpots.Count > 0 && m_machine?.session != null)
                foreach (var k in hs.wonJackpots)
                    m_machine.session.ResetJackpot(k);
        }

        /// <summary>把 Hold&Spin 结算赢分补进总分：只加「totalPayout - 已落账(_holdAppliedWin)」的差额，
        /// 不动 m_win_num 显示（调用方已用 ShowWinValue 显示 totalPayout）。每轮已即时落账的部分不重复加。
        /// 用于取代原先的 ApplySpinResult(totalPayout)（那会把每轮已加过的赢分再加重一遍）。</summary>
        void ApplyHoldWinToCredit(GameResult r)
        {
            if (r == null || m_player == null) return;
            float remaining = r.totalPayout - _holdAppliedWin;
            if (remaining > 0.5f)
                m_player.AddWinToCredit((long)System.Math.Round(remaining));
            _holdAppliedWin = r.totalPayout;   // 标记已全部落账，后续再调也不会重复加
        }

        /// <summary>按列统计 FreeSpins 火球，分档追加免费次数（IsOver 兜底路径用）。
        /// ★ 仅统计「已集满(isFull)」的列：未满列即便有 FreeSpins 火球也不计入，须先填满一整列（播满列收集动画）才给免费次数。</summary>
        void AwardFreeballSpinsFromMain(HoldSpinState hs, GameResult r)
        {
            if (hs == null || r == null || m_machine?.config?.freeSpins == null) return;
            var fs = m_machine.config.freeSpins;
            int before = r.freeSpinsAwarded;
            for (int reel = 0; reel < hs.reels; reel++)
            {
                if (!hs.isFull[reel]) continue;   // ★ 门槛：仅满列才统计 FreeSpins 火球
                var col = hs.cells[reel];
                if (col == null) continue;
                int cnt = 0;
                for (int row = 0; row < col.Length; row++)
                    if (col[row].filled && col[row].kind == FireballKind.FreeSpins) cnt++;
                if (cnt > 0) r.freeSpinsAwarded += fs.FreeballAwardFor(cnt);
            }
            if (r.freeSpinsAwarded != before)
                Debug.Log($"[FREE] 兜底统计(满列): {before} → {r.freeSpinsAwarded}");
        }

        /// <summary>统计单列 FREE 火球数并累加到 _holdResult.freeSpinsAwarded。
        /// ★ 仅统计「已集满(isFull)」的列：未满列（含刚转出的散落 FreeSpins 火球）不计入，须先填满一整列才给免费次数。</summary>
        void CountFreeFireballs(HoldSpinState hs, int reel, bool clearAfter = false)
        {
            if (hs == null || reel < 0 || reel >= hs.reels) return;
            if (!hs.isFull[reel]) return;   // ★ 门槛：仅满列才统计 FreeSpins 火球
            if (_holdResult == null || m_machine?.config?.freeSpins == null) return;
            int cnt = 0;
            for (int row = 0; row < hs.cells[reel].Length; row++)
                if (hs.cells[reel][row].filled && hs.cells[reel][row].kind == FireballKind.FreeSpins) cnt++;
            if (cnt > 0) _holdResult.freeSpinsAwarded += m_machine.config.freeSpins.FreeballAwardFor(cnt);
            if (clearAfter)
                for (int row = 0; row < hs.cells[reel].Length; row++)
                    hs.cells[reel][row].filled = false;
        }

        /// <summary>扫描某列中的彩金火球（Mini/Minor/Major/Mega），逐个触发 BonusView 特效。</summary>
        void ShowJackpotEffectsForReel(HoldSpinState hs, int reel)
        {
            if (hs == null || m_bonus == null) return;
            for (int row = 0; row < hs.cells[reel].Length; row++)
            {
                var c = hs.cells[reel][row];
                // ★ 用 jackpotTier 做权威判定（避免枚举偏移）
                if (c.filled && c.jackpotTier >= 0 && c.jackpotTier < HoldSpinState.JackpotTierNames.Length)
                {
                    if (System.Enum.TryParse<FireballKind>(HoldSpinState.JackpotTierNames[c.jackpotTier], out var fk))
                        m_bonus.ShowJackpotEffect(fk);
                }
            }
        }
        #endregion
    }
}
