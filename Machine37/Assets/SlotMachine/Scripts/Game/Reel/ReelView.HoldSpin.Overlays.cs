using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SlotMachine.Core;

namespace com.slot
{
    /// <summary>ReelView 火球 Overlay 管理：ShowFireballOverlay、满列掉落动画、释放滚走、清理、幽灵克隆。</summary>
    public partial class ReelView
    {
        /// <summary>在 (reel,row) 最上层生成一个火球复制体 overlay（固定不滚），盖住下面滚动的普通格。</summary>
        /// <param name="playSound">true=创建时播放 event:/Sounds/13；初始进入 Hold&Spin 的预置火球传 false 避免连发声。</param>
        public void ShowFireballOverlay(int reel, int row, FireballCell cell, bool playSound = true)
        {
            if (cell == null) return;
            if (playSound && FMODSoundMgr.Instance != null)
                FMODSoundMgr.Instance.PlaySound("event:/Sounds/13");
            if (reel < 0 || reel >= _reels.Count) return;
            var st = _reels[reel];
            if (st.container == null) return;

            // ★ 用户要求"停下的火球放 m_fireNode 下面，肯定在最上层"：主游戏(非持久)优先挂 m_fireNode(顶层容器)；
            //   Mini 持久模式仍按原逻辑挂 m_node[reel](跨 ClearAll 存活)。m_fireNode 未绑定时回退到 m_node[reel]/st.container。
            //   位置统一用世界坐标(TransformPoint)计算，避免挂到不同父节点后 X 偏移错乱。
            bool useFireNode = (!m_persistentFireOverlays && m_fireNode != null);
            Transform parent = useFireNode ? m_fireNode.transform
                : ((m_node != null && reel >= 0 && reel < m_node.Length && m_node[reel] != null) ? m_node[reel].transform : st.container.transform);
            GameObject go;
            if (m_symbolPrefab != null)
            {
                go = Instantiate(m_symbolPrefab, parent);
                go.SetActive(true);
            }
            else
            {
                go = CreateImageGO(parent, $"FBOverlay_{reel}_{row}");
            }
            go.name = $"FBOverlay_{reel}_{row}";
            _fbOverlays.Add(go);

            var rt = go.transform as RectTransform;
            // 世界坐标定位：取该列(row)在 m_node[reel] 下的世界位置，挂到任意父节点(含 m_fireNode)都正确。
            Vector3 worldPos = (m_node != null && reel >= 0 && reel < m_node.Length && m_node[reel] != null)
                ? m_node[reel].transform.TransformPoint(0f, RowToY(row), 0f)
                : this.transform.TransformPoint(0f, RowToY(row), 0f);
            if (rt != null) rt.position = worldPos;
            go.transform.SetAsLastSibling();

            var item = go.GetComponent<ReelItem>();
            if (item != null)
            {
                // ★ 防御：倍数火球 multiplier 不应<=0（配置最小0.5）。若数据异常导致0，给保底倍率并告警，
                //   避免"火球没文字"（ApplyFireballText 对空label会隐藏 text）。
                float safeRate = cell.multiplier;
                if (cell.kind == FireballKind.Multiplier && safeRate <= 0f)
                {
                    safeRate = PickMultiplierFallback();
                    UnityEngine.Debug.LogWarning($"[ShowFireballOverlay] 倍数火球 multiplier<=0(reel={reel} row={row})，强制兜底={safeRate}");
                }
                item.m_type = cell.kind;
                item.m_rate = safeRate;
                // 数据层也同步成安全值，避免后续按 cell.multiplier 读取仍为0
                cell.multiplier = safeRate;
                // ★ 免费外观严格按火球自身 kind 决定：FreeSpins 类型才显示免费火球(m_freeFire)，
                //   倍数/彩金火球一律显示普通火球(m_fire)。不再参考 m_inFreeSpins（该字段全工程从未被置 true，是死代码，
                //   且会错误地让倍数火球在"免费游戏"全局开关下变成免费火球外观）。
                bool freeFire = (cell != null && cell.kind == FireballKind.FreeSpins);
                item.ShowFire(true, freeFire);
                // ★ overlay 的 m_effect 必须关闭——m_effect 只在 ReelItem(卷轴格)上由 SetColumnEffect 管理，
                //   overlay 是克隆体，如果 prefab 上 m_effect 默认 active，ghost 会带着 m_effect 停在原位
                //   否则 m_effect 会随 overlay 一直停在原位、直到 overlay 被销毁才消失 → 视觉上 m_effect "不消失"。
                if (item.m_effect != null) item.m_effect.SetActive(false);
                // ★ 诊断日志：仅对非法 kind（超出 0~5）告警。multiplier 大小不再作为彩金档推断依据。
                if (cell != null && ((int)cell.kind < 0 || (int)cell.kind > 5))
                    Debug.LogWarning($"[ShowFireballOverlay] 非法 kind={(int)cell.kind}({cell.kind}) mult={cell.multiplier} reel={reel} row={row} → label={FireballLabel(cell)}");
            }
            else
            {
                var img = go.GetComponent<Image>();
                if (img != null) { img.enabled = true; img.sprite = GetSymbol(m_fireballSymbolId); }
            }
            ApplyFireballText(go, cell);
        }

        /// <summary>每帧移动待释放列的火球 overlay——随卷轴向下滚走。</summary>
        void MoveReleasingOverlays(Dictionary<int, float> offset)
        {
            float bottomLimit = m_rowBaseY - (m_buf + 1) * m_cellSize;
            for (int i = _fbOverlays.Count - 1; i >= 0; i--)
            {
                var go = _fbOverlays[i];
                if (go == null) { _fbOverlays.RemoveAt(i); continue; }
                int reel, row;
                if (!ParseReelRow(go.name, out reel, out row)) continue;
                if (!_releaseReels.Contains(reel)) continue;
                var rt = go.transform as RectTransform;
                if (rt != null)
                {
                    float off = offset.ContainsKey(reel) ? offset[reel] : 0f;
                    // ★ 世界坐标定位：释放火球随卷轴向下滚走(母节点可能是 m_fireNode 或 m_node[reel])。
                    //   用母列世界原点 + 向下偏移，保证挂到任意父节点都正确；bottomLimit 仍用卷轴局部 Y(RowToY 坐标系)判定。
                    Vector3 baseWorld = (m_node != null && reel >= 0 && reel < m_node.Length && m_node[reel] != null)
                        ? m_node[reel].transform.TransformPoint(0f, RowToY(row), 0f)
                        : this.transform.TransformPoint(0f, RowToY(row), 0f);
                    rt.position = baseWorld - new Vector3(0f, off * m_cellSize, 0f);
                    float yLocal = RowToY(row) - off * m_cellSize;   // 卷轴局部 Y（与 bottomLimit 同坐标系）
                    if (yLocal < bottomLimit)
                    {
                        if (SlotDebug.VerboseLogs)
                            UnityEngine.Debug.Log($"[RELEASE-MOVE] r{reel} row={row} 火球随卷轴滚出→销毁（回归滚动队列）");
                        Destroy(go);
                        _fbOverlays.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>销毁已随卷轴滚走的待释放 overlay（回合末调用）。</summary>
        void DestroyReleasingOverlays()
        {
            for (int i = _fbOverlays.Count - 1; i >= 0; i--)
            {
                var go = _fbOverlays[i];
                if (go == null) continue;
                int reel, row;
                if (!ParseReelRow(go.name, out reel, out row)) continue;
                if (_releaseReels.Contains(reel))
                {
                    Destroy(go);
                    _fbOverlays.RemoveAt(i);
                }
            }
            var remain = new HashSet<int>();
            foreach (var go in _fbOverlays)
                if (go != null && ParseReelRow(go.name, out int r, out _)) remain.Add(r);
            foreach (var r in _collectedReels)
                if (!remain.Contains(r)) _releaseReels.Remove(r);
            RefreshColumnEffects();
        }

        /// <summary>从 overlay 名 "FBOverlay_{reel}_{row}" 解析出 reel/row。</summary>
        public bool ParseReelRow(string name, out int reel, out int row)
        {
            reel = -1; row = -1;
            if (string.IsNullOrEmpty(name)) return false;
            var parts = name.Split('_');
            if (parts.Length < 3) return false;
            if (!int.TryParse(parts[1], out reel)) return false;
            if (!int.TryParse(parts[2], out row)) return false;
            return true;
        }

        /// <summary>销毁全部火球 overlay（含 Mini 持久 overlay）。供 Mini 结束回收时调用，避免跨会话残留。</summary>
        public void ClearFireballOverlays()
        {
            foreach (var go in _fbOverlays)
                if (go != null) Destroy(go);
            _fbOverlays.Clear();
            // ★ 兜底：整盘火球 overlay 清空时，同步清空待释放/已收集集合，避免陈旧 reel 残留——
            //   否则 Mini 结束 ClearFireballOverlays 清了 overlay 却没清 _collectedReels/_releaseReels，
            //   下一局 StartBaseSpin 会把陈旧 reel 误并入 _releaseReels，导致该列"新落火球被当释放滚走、只有部分回归队列"。
            if (_releaseReels != null) _releaseReels.Clear();
            if (_collectedReels != null) _collectedReels.Clear();
            RefreshColumnEffects();
        }

        /// <summary>销毁非释放中的火球 overlay；释放中的保留，随卷轴滚走。Mini 棋盘下跳过清理。</summary>
        public void ClearFireballOverlaysExceptReleasing()
        {
            if (m_persistentFireOverlays) return;
            bool removed = false;
            for (int i = _fbOverlays.Count - 1; i >= 0; i--)
            {
                var go = _fbOverlays[i];
                if (go == null) { _fbOverlays.RemoveAt(i); removed = true; continue; }
                int reel, row;
                if (!ParseReelRow(go.name, out reel, out row)) continue;
                if (_releaseReels.Contains(reel) || _collectedReels.Contains(reel)) continue;
                if (SlotDebug.VerboseLogs)
                    UnityEngine.Debug.Log($"[CLEAR-EXCEPT] 销毁 overlay={go.name} reel={reel} | _releaseReels=[{string.Join(",", _releaseReels)}] _collectedReels=[{string.Join(",", _collectedReels)}] (持有火球→StartBaseSpin 后由 ShowHeldFireballs 重建)");
                Destroy(go);
                _fbOverlays.RemoveAt(i);
                removed = true;
            }
            if (removed) RefreshColumnEffects();
        }

        /// <summary>设置 overlay 及其所有子节点(UI Graphic 与 SpriteRenderer)的透明度——递归改 color.alpha，
        /// 兼容 Image/Text(UI) 与 SpriteRenderer(精灵) 两种视觉，避免根节点 CanvasGroup 对 SpriteRenderer/子物体不变暗的坑（"变暗"不可见）。</summary>
        void SetOverlayAlpha(GameObject go, float alpha)
        {
            if (go == null) return;
            foreach (var g in go.GetComponentsInChildren<Graphic>(true))
            {
                var c = g.color; c.a = alpha; g.color = c;
            }
            foreach (var s in go.GetComponentsInChildren<SpriteRenderer>(true))
            {
                var c = s.color; c.a = alpha; s.color = c;
            }
        }

        // ===== 列预警特效 =====

        /// <summary>统计某列当前火球 overlay 数量（不含幽灵）。</summary>
        int CountFireballsInColumn(int reel)
        {
            int count = 0;
            foreach (var go in _fbOverlays)
            {
                if (go == null) continue;
                if (ParseReelRow(go.name, out int r, out _) && r == reel)
                    count++;
            }
            return count;
        }

        /// <summary>激活/关闭整列的 m_effect 特效。</summary>
        void SetColumnEffect(int reel, bool active)
        {
            if (reel < 0 || reel >= _reels.Count) return;
            var st = _reels[reel];
            foreach (var item in st.cellItems)
            {
                if (item != null && item.m_effect != null)
                    item.m_effect.SetActive(active);
            }
        }

        /// <summary>强制关闭所有列预警特效（新基础局/特性结束兜底）。</summary>
        public void ClearAllColumnEffects()
        {
            for (int reel = 0; reel < _reels.Count; reel++)
                SetColumnEffect(reel, false);
        }

        /// <summary>刷新所有列的预警特效：只差1个火球就满列时激活整列 m_effect。
        /// 优先用 hs.cells（权威数据）统计已落火球数；无 hs 时退回 _fbOverlays 计数（覆盖层清理路径）。</summary>
        public void RefreshColumnEffects(HoldSpinState hs = null, int[] counters = null)
        {
            for (int reel = 0; reel < _reels.Count; reel++)
            {
                int filled;
                if (hs != null && reel < hs.cells.Length)
                {
                    filled = 0;
                    for (int row = 0; row < hs.cells[reel].Length; row++)
                        if (hs.cells[reel][row].filled) filled++;
                }
                else
                {
                    filled = CountFireballsInColumn(reel);
                }
                int rows = _reels[reel].rows;
                bool shouldEffect = (rows > 0 && filled == rows - 1);

                // 已收集/已释放的列不亮特效
                if (hs != null)
                {
                    if (hs.released[reel] || hs.isFull[reel]) shouldEffect = false;
                }
                else
                {
                    if (_collectedReels.Contains(reel) || _releaseReels.Contains(reel)) shouldEffect = false;
                }

                SetColumnEffect(reel, shouldEffect);
            }
        }
    }
}
