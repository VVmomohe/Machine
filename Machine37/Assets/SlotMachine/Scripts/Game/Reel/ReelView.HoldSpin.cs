using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    /// <summary>ReelView 火球 overlay 核心流程：ShowFeatureState（基础局钉持久火球显示）、SpinHoldRound、Release列。</summary>
    public partial class ReelView
    {
        List<GameObject> _fbOverlays = new List<GameObject>();
        HashSet<int> _releaseReels = new HashSet<int>();
        HashSet<int> _collectedReels = new HashSet<int>();
        bool _wasSpinning = false;

        //   故 StopNow 无法通过 st.spinning 命中。用这两个标志让 StopNow 能识别并提前打断 Hold 滚动。

        // ===== 基础局火球钉持久 overlay（原 ShowFeatureState 入口，HOLD respin 已移除）=====

        public virtual void ShowFeatureState(HoldSpinState s)
        {
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
        }

        /// <summary>模式B 旋转期提前钉住「跨局持有」火球（不含本局新落火球；本局火球由 ShowGrid 底层卷轴滚动显示，避免重影）。
        /// 解决"有圈圈时火球没固定"：开新局 ShowGrid→ClearAll 会清掉上局 overlay，若只等停轮后 ShowFeatureState 重钉，
        /// 则旋转期间持有火球不可见，观感像没固定。此处让它整局持续可见（固定 overlay 盖在滚动卷轴之上，不随卷轴漂移）。</summary>
        public void ShowHeldFireballs(HoldSpinState s, List<FireballCell> currentGame)
        {
            if (s == null) return;
            // 本局新落火球已由底层卷轴(finalSyms=baseGrid 含 fbId)显示，跳过其位置，避免"滚动火球 + 固定 overlay"重影。
            var skip = new HashSet<int>();
            if (currentGame != null)
                foreach (var c in currentGame)
                    if (c != null && c.filled) skip.Add(c.reel * 100 + c.row);

            for (int r = 0; r < s.reels && r < _reels.Count; r++)
            {
                for (int row = 0; row < s.cells[r].Length; row++)
                {
                    var c = s.cells[r][row];
                    if (c.filled && !skip.Contains(r * 100 + row))
                        ShowFireballOverlay(r, row, c, playSound: false);
                }
            }
            // ★ 诊断（总是打印）：重建后当前火球 overlay 按列计数，核对"某列火球是否真的被重建=是否真的固定"。
            {
                var byCol = new System.Collections.Generic.Dictionary<int, int>();
                foreach (var go in _fbOverlays)
                {
                    if (go == null) continue;
                    if (ParseReelRow(go.name, out int rcol, out _)) { if (!byCol.ContainsKey(rcol)) byCol[rcol] = 0; byCol[rcol]++; }
                }
                var sbH = new System.Text.StringBuilder("[ShowHeld] 当前火球overlay按列: ");
                foreach (var kv in byCol) sbH.Append($"r{kv.Key}={kv.Value} ");
                UnityEngine.Debug.Log(sbH.ToString());
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
            if (movedCols.Count > 0)
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

        /// <summary>重播某列的 tong（夹子/桶）动画：新火球落入 / 满列收集时调用。</summary>
        public void PlayTong(int reel)
        {
            if (m_tongs != null && reel >= 0 && reel < m_tongs.Length && m_tongs[reel] != null)
                m_tongs[reel].Play();
        }

        /// <summary>播放某列 tong 演出并【等其真正播完】再返回（协程）。
        /// 满列收集演出后阻塞流程，确保动画播完才进 Mini（替换一次性 PlayTong + 估算时长等待）。</summary>
        public IEnumerator PlayTongAndWait(int reel)
        {
            if (m_tongs == null || reel < 0 || reel >= m_tongs.Length || m_tongs[reel] == null) yield break;
            m_tongs[reel].Play();
            yield return m_tongs[reel].WaitDone();
        }

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

            Debug.Log($"[COLLECT] r{reel} 满列收集：{list.Count} 颗（底部先掉·每颗原地变暗+新火球掉桶·桶对每颗反应一次·原火球回归滚动队列）");

            foreach (var ov in list)
            {
                ParseReelRow(ov.name, out _, out int row);

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
