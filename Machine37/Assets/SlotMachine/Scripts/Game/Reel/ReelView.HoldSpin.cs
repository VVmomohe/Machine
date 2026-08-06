using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    /// <summary>ReelView 火球 overlay 核心流程：ShowFeatureState（基础局钉持久火球显示）、Release列。</summary>
    public partial class ReelView
    {
        List<GameObject> _fbOverlays = new List<GameObject>();
        HashSet<int> _releaseReels = new HashSet<int>();
        HashSet<int> _collectedReels = new HashSet<int>();
        bool _wasSpinning = false;

        // ===== 基础局火球钉持久 overlay（ShowFeatureState 入口）=====

        public virtual void ShowFeatureState(HoldSpinState s)
        {
            // ★ 诊断（受 SlotDebug.VerboseLogs 控制）：核对 hs.cells 里各 kind 火球的 filled 数，
            //   与下方实际钉住的 overlay([FB-STATE-OUT])对比，区分"某 kind 没固定"是逻辑层(cells 漏加)还是显示层(钉了又没显示)。
            if (SlotDebug.VerboseLogs)
            {
                var inKinds = new System.Collections.Generic.Dictionary<string, int>();
                for (int r = 0; r < s.reels && r < s.cells.Length; r++)
                    for (int row = 0; row < s.cells[r].Length; row++)
                    {
                        var c = s.cells[r][row];
                        if (c != null && c.filled)
                        {
                            string k = c.kind.ToString();
                            if (!inKinds.ContainsKey(k)) inKinds[k] = 0;
                            inKinds[k]++;
                        }
                    }
                var sbIn = new System.Text.StringBuilder("[FB-STATE-IN] hs.cells filled 按kind: ");
                foreach (var kv in inKinds) sbIn.Append($"{kv.Key}={kv.Value} ");
                UnityEngine.Debug.Log(sbIn.ToString());
            }

            ClearFireballOverlays();

            for (int r = 0; r < s.reels && r < _reels.Count; r++)
            {
                var st = _reels[r];
                for (int row = 0; row < s.cells[r].Length; row++)
                {
                    var c = s.cells[r][row];
                    if (c.filled)
                        ShowFireballOverlay(r, row, c, playSound: false);
                }
            }
            RefreshColumnEffects(s);   // 近满列(差1火球)→亮整列 m_effect

            // ★ 诊断（受 SlotDebug.VerboseLogs 控制）：核对本方法实际钉住的 overlay 按 kind 计数，与 [FB-STATE-IN] 对比，
            //   区分 BUG 在逻辑层(加进 cells 但没钉)还是显示层(钉了但被销毁/观察时机)。
            if (SlotDebug.VerboseLogs)
            {
                var outKinds = new System.Collections.Generic.Dictionary<string, int>();
                foreach (var go in _fbOverlays)
                {
                    if (go == null) continue;
                    var it = go.GetComponent<ReelItem>();
                    if (it != null)
                    {
                        string k = it.m_type.ToString();
                        if (!outKinds.ContainsKey(k)) outKinds[k] = 0;
                        outKinds[k]++;
                    }
                }
                var sbOut = new System.Text.StringBuilder("[FB-STATE-OUT] 钉住 overlay 按kind: ");
                foreach (var kv in outKinds) sbOut.Append($"{kv.Key}={kv.Value} ");
                UnityEngine.Debug.Log(sbOut.ToString());
            }
        }

        /// <summary>模式B 旋转期 tray 效果：仅钉住「推进前已跨局持有(preRoundHeldCells)」的火球作为固定 overlay（不随卷轴滚动、定在原位），
        /// 本局新落火球(未持有过的格)【不】钉固——它们由 ShowGrid 底层卷轴滚动显示，满足"老火球定住、新火球滚进来"的诉求。
        /// 停稳后 SettleBaseB→ShowFeatureState 统一重钉全部。
        /// ★ 旋转期由 GameManager.Flow.StartBaseSpin 调用（2026-08-04 修正版：既非"全部预钉"也非"全部滚动"，而是按持有快照区分）。
        /// ★ 已释放(released) 列不钉：火球回归滚动队列，下一局随卷轴滚走。
        /// ★ 碰撞修复：本局新火球恰好落在已持有格上时 AdvanceHoldBoard 保留旧火球(同位置保留旧火球)，
        ///   该格实为 held → 必须用 preRoundHeldCells 钉固；旧逻辑用 skip=baseFireballs 跳过该格会误杀 held 火球
        ///   (开滚即消失、停稳 ShowFeatureState 才重钉)。故不再用 skip，改以"推进前是否持有"为唯一钉固判据。</summary>
        public void ShowHeldFireballs(HoldSpinState s, List<FireballCell> currentGame)
        {
            if (s == null) return;
            // currentGame(=baseFireballs) 仅作兼容入参，不再用于 skip：其中含"落点与已持有格碰撞"的位置，
            // 若据其 skip 会把保留的旧火球误跳（详见类注释"碰撞修复"）。钉固判据统一为 s.preRoundHeldCells。
            for (int r = 0; r < s.reels && r < _reels.Count; r++)
            {
                // 已释放(圈圈归零→回归滚动队列) 列【不】预钉：让其火球随底层卷轴滚动或走释放流程。
                if (s.released != null && s.released[r]) continue;
                for (int row = 0; row < s.cells[r].Length; row++)
                {
                    var c = s.cells[r][row];
                    if (!c.filled) continue;
                    // ★ 仅钉"推进前已持有"的火球；本局新落(未持有过的格，含碰撞格被保留的旧火球视作已持有)才钉，其余(真·新落)不钉、随卷轴滚入。
                    bool wasHeld = (s.preRoundHeldCells != null && s.preRoundHeldCells.Contains(r * 100 + row));
                    if (!wasHeld) continue;
                    ShowFireballOverlay(r, row, c, playSound: false);
                }
            }
            // ★ 诊断（受 SlotDebug.VerboseLogs 控制）：当前火球 overlay 按列/按 kind 计数，核对"某列是否真的被钉住"。
            if (SlotDebug.VerboseLogs)
            {
                var byCol = new System.Collections.Generic.Dictionary<int, int>();
                var byKind = new System.Collections.Generic.Dictionary<string, int>();
                foreach (var go in _fbOverlays)
                {
                    if (go == null) continue;
                    if (ParseReelRow(go.name, out int rcol, out _)) { if (!byCol.ContainsKey(rcol)) byCol[rcol] = 0; byCol[rcol]++; }
                    var it = go.GetComponent<ReelItem>();
                    if (it != null) { string k = it.m_type.ToString(); if (!byKind.ContainsKey(k)) byKind[k] = 0; byKind[k]++; }
                }
                var sbH = new System.Text.StringBuilder("[ShowHeld] 当前火球overlay 按列: ");
                foreach (var kv in byCol) sbH.Append($"r{kv.Key}={kv.Value} ");
                UnityEngine.Debug.Log(sbH.ToString());
                var sbK = new System.Text.StringBuilder("[ShowHeld-KIND] 旋转期钉住火球按kind: ");
                foreach (var kv in byKind) sbK.Append($"{kv.Key}={kv.Value} ");
                UnityEngine.Debug.Log(sbK.ToString());
            }
            RefreshColumnEffects(s);
        }


        /// <summary>返回当前所有火球 overlay（供诊断/外部查询）。返回 List 只读引用，外部不要修改。</summary>
        public List<GameObject> GetFireballOverlays()
        {
            if (_fbOverlays == null) _fbOverlays = new List<GameObject>();
            return _fbOverlays;
        }

        /// <summary>把已收集满列的 overlay 转入待释放集合，下一局卷轴滚动时由 MoveReleasingOverlays 随卷轴滚走。
        /// onlyCollected=true 时只处理 _collectedReels 中的列，不兜底遍历所有 _fbOverlays（用于 StartBaseSpin
        /// 先转 collected 再清非 releasing overlay，避免普通持有火球也被误标为 releasing）。</summary>
        public void ReleaseCollectedForNextSpin(bool onlyCollected = false)
        {
            var movedCols = new List<int>(_collectedReels);
            foreach (var r in _collectedReels) _releaseReels.Add(r);
            _collectedReels.Clear();
            if (movedCols.Count > 0 && SlotDebug.VerboseLogs)
                UnityEngine.Debug.Log($"[RELEASE-PREP] collected[{string.Join(",", movedCols)}] → _releaseReels（下一局随卷轴滚走·回归滚动队列）");

            if (onlyCollected) return;

            // ★ 兜底：直接遍历所有残留火球 overlay，把每个 overlay 的 reel 也并入待释放集合。
            //   原因：CollectFullReelAnimation 在协程末尾才把收集列加回 _collectedReels，而某些路径（如旧 respin 回合末）
            //   会 _releaseReels.Clear()，存在时序竞争。按 _fbOverlays 实际残留兜底，保证任何残留 ghost 都被释放。
            //   ※ Mini 持久 overlay(m_persistentFireOverlays=true) 不参与此释放逻辑，必须跳过，否则会误把 Mini 火球当待释放滚走。
            if (!m_persistentFireOverlays)
            {
                foreach (var go in _fbOverlays)
                {
                    if (go == null) continue;
                    if (ParseReelRow(go.name, out int reel, out _)) _releaseReels.Add(reel);
                }
            }
        }

        // ===== 模式B 收集盘 respin 辅助（轻量，不滚盘）=====

        /// <summary>满列收集演出：复刻旧 HOLD 手感——每颗火球【原地变暗】的同时【新生成一个火球掉入桶(tong)】，
        /// 桶对每个落入的火球播放一次收取反应；顺序与旧 HOLD 一致——【先掉下面(row 小=底部)】，再往上。
        /// 演出结束后原火球 overlay 不删除、而是回归滚动队列：保留在 _fbOverlays 中并加入 _collectedReels，
        /// 由 ReleaseCollectedForNextSpin + MoveReleasingOverlays 在下一局卷轴滚动时随卷轴自然滚走销毁。
        /// 仅清理本列 overlay（其余列持有火球保持钉住，不碰）。</summary>
        public IEnumerator CollectFullReelAnimation(int reel)
        {
            if (reel < 0 || reel >= _reels.Count) yield break;

            // 1) 收集该列所有火球 overlay，按 row 升序（row 小=底部，先掉下面——与旧 HOLD 一致；之前降序写反成顶部先掉）
            var list = new List<GameObject>();
            foreach (var go in _fbOverlays)
            {
                if (go == null) continue;
                if (ParseReelRow(go.name, out int r, out _ ) && r == reel)
                    list.Add(go);
            }
            list.Sort((a, b) =>
            {
                ParseReelRow(a.name, out _, out int ra);
                ParseReelRow(b.name, out _, out int rb);
                return ra.CompareTo(rb);   // row 升序：底部(row 小)在前 → 先掉下面的
            });
            if (list.Count == 0) yield break;

            ReelTong tong = (m_tongs != null && reel < m_tongs.Length) ? m_tongs[reel] : null;
            Vector3 barrelPos = (tong != null) ? tong.transform.position : GetColumnBottomWorld(reel);

            float barrelDur = (tong != null) ? tong.PlayDuration() : 0.5f;
            float fallDur = 0.3f;
            float interval = Mathf.Max(fallDur + 0.1f, barrelDur * 0.9f);   // 相邻两颗落入的起始间隔（>=桶时长则每颗桶动画都能播完）

            if (SlotDebug.VerboseLogs) Debug.Log($"[COLLECT] r{reel} 满列收集：{list.Count} 颗（底部先掉·每颗原地变暗+新火球掉桶·桶对每颗反应一次·原火球回归滚动队列）");

            foreach (var ov in list)
            {
                ParseReelRow(ov.name, out _, out int row);

                // ★ 火球掉落时按类型处理（用户口径 2026-08-04）：
                //   倍数火球 → 累加该列 ReelFireNum 倍数（"掉一个 +X"）；
                //   彩金火球(Mini/Minor/Major/Mega) → 播彩金特效；
                //   免费火球(FreeSpins) → 跳过（播放完动画会进免费小游戏，由 Mini 统一结算）。
                var item = ov.GetComponent<ReelItem>();
                if (item != null)
                {
                    if (item.m_type == FireballKind.FreeSpins)
                    {
                        // 免费火球：不管
                    }
                    else if (item.m_type >= FireballKind.Mini && item.m_type <= FireballKind.Mega)
                    {
                        if (GameManager.Instance != null && GameManager.Instance.m_bonus != null)
                            GameManager.Instance.m_bonus.ShowJackpotEffect(item.m_type, persistent: true);
                        if (SlotDebug.VerboseLogs)
                            Debug.Log($"[COLLECT] r{reel} 彩金火球掉落(kind={item.m_type}) → 播彩金特效");
                    }
                    else if (item.m_type == FireballKind.Multiplier)
                    {
                        // ★ 仅【倍数火球】累加该列倍率（"掉一个 +X"）；
                        //   彩金档(Mini/Minor/Major/Mega)是固定数值、不是倍数，绝不写进 ReelFireNum 倍率累加（已在上方分支单独播特效）；
                        //   免费火球(FreeSpins)已在上方跳过。此处显式判定 ==Multiplier，任何未知类型都不累加。
                        AddFireballMultiplier(reel, item.m_rate);
                        if (SlotDebug.VerboseLogs)
                        {
                            float acc = (m_numObjs != null && reel < m_numObjs.Length && m_numObjs[reel] != null) ? m_numObjs[reel].m_rate : 0f;
                            Debug.Log($"[COLLECT] r{reel} 倍数火球掉落 +{item.m_rate:F2} → ReelFireNum 累计倍率={acc:F2}");
                        }
                    }
                }

                // (a) 新生成一个火球掉入桶（旧 HOLD：新生成一个火球而下落）——克隆原火球、保持原亮度
                var faller = Instantiate(ov, ov.transform.parent);
                faller.name = $"FBFaller_{reel}_{row}";
                faller.transform.SetAsLastSibling();
                SetOverlayAlpha(faller, 1f);                    // 新火球保持原亮度

                // (b) 原火球原地变暗（旧 HOLD：火球会变暗，留在原位）——保留在 _fbOverlays，不删除
                SetOverlayAlpha(ov, 0.8f);                    // 变暗到 80% 不透明（轻微变暗，不过度透明）

                // (c) 新火球掉入桶 + 桶对每个落入火球反应一次（不再“只播一次”）
                yield return AnimateFireballDrop(faller, barrelPos, fallDur);
                if (tong != null) tong.Play();
                Destroy(faller);

                yield return new WaitForSeconds(interval - fallDur);   // 间隔到下一颗开始
            }

            // (d) 本列收集完毕：原火球 overlay 不删除，标记为“已收集列”，下一局卷轴滚动时由 MoveReleasingOverlays 随卷轴滚走
            _collectedReels.Add(reel);

            // 最后一个桶动画播完再返回（避免被进 Mini 截断）
            if (tong != null) yield return tong.WaitDone();
            RefreshColumnEffects();
        }

        /// <summary>取某列最底部世界坐标（桶未绑定时兜底掉落目标）。</summary>
        Vector3 GetColumnBottomWorld(int reel)
        {
            if (reel < 0 || reel >= _reels.Count) return Vector3.zero;
            int rows = _reels[reel].rows;
            return (m_node != null && reel < m_node.Length && m_node[reel] != null)
                ? m_node[reel].transform.TransformPoint(0f, RowToY(rows - 1) - m_cellSize, 0f)
                : this.transform.TransformPoint(0f, RowToY(rows - 1) - m_cellSize, 0f);
        }

        /// <summary>火球从当前位置 ease-in（加速）掉落到目标世界坐标，落点处轻微缩小（“掉进桶”观感）。</summary>
        IEnumerator AnimateFireballDrop(GameObject go, Vector3 targetWorld, float dur)
        {
            var rt = go.transform as RectTransform;
            if (rt == null) yield break;
            Vector3 start = rt.position;
            float t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                float e = t * t;   // ease-in：加速下落（重力感）
                rt.position = Vector3.Lerp(start, targetWorld, Mathf.Clamp01(e));
                float s = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(e));
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            rt.position = targetWorld;
            rt.localScale = new Vector3(0.35f, 0.35f, 1f);
        }

        /// <summary>销毁某列(reel)全部火球 overlay（释放列时调用，使其从屏上消失）。</summary>
        public void ClearColumnFireballs(int reel)
        {
            for (int i = _fbOverlays.Count - 1; i >= 0; i--)
            {
                var go = _fbOverlays[i];
                if (go == null) { _fbOverlays.RemoveAt(i); continue; }
                if (ParseReelRow(go.name, out int r, out _))
                {
                    if (r == reel) { Destroy(go); _fbOverlays.RemoveAt(i); }
                }
            }
            RefreshColumnEffects();
        }

        /// <summary>释放列“回归滚动队列”：该列火球位底层符号换回普通符（不再显示火球图标），计数器由调用方隐藏。
        /// 收集盘 respin 中 counter 归零的列调用——视觉上该列回到正常（下一局自然滚动）。</summary>
        public void ReleaseColumnToSpinQueue(int reel)
        {
            if (reel < 0 || reel >= _reels.Count) return;
            var st = _reels[reel];
            for (int k = m_buf; k < m_buf + st.rows && k < st.cells.Count; k++)
            {
                int logicalRow = k - m_buf;
                int sym = (st.finalSyms != null && logicalRow < st.finalSyms.Length) ? st.finalSyms[logicalRow] : 0;
                if (sym == m_fireballSymbolId) sym = RandNormalSymbol();  // 火球位换普通符（回归滚动队列）
                SetCell(st, k, sym);
            }
        }
    }
}
