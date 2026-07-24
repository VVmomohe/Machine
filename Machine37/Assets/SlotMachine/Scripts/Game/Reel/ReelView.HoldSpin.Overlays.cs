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

            Transform parent = m_persistentFireOverlays && m_node != null && reel < m_node.Length && m_node[reel] != null
                ? m_node[reel].transform : st.container.transform;
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
            if (rt != null) rt.anchoredPosition = new Vector2(0f, RowToY(row));
            go.transform.SetAsLastSibling();

            if (rt != null)
                Debug.Log($"[FBOverlay] {go.name} Y={rt.anchoredPosition.y:F1} parent={parent.name} active={go.activeSelf} (reel{reel} row{row})");

            var item = go.GetComponent<ReelItem>();
            if (item != null)
            {
                item.m_type = cell.kind;
                item.m_rate = cell.multiplier;
                bool freeFire = m_inFreeSpins || (cell != null && cell.kind == FireballKind.FreeSpins);
                item.ShowFire(true, freeFire);
                // ★ overlay 的 m_effect 必须关闭——m_effect 只在 ReelItem(卷轴格)上由 SetColumnEffect 管理，
                //   overlay 是克隆体，如果 prefab 上 m_effect 默认 active，ghost 会带着 m_effect 停在原位
                //   直到下一轮 SpinHoldRound 才销毁 → 视觉上 m_effect "不消失"。
                if (item.m_effect != null) item.m_effect.SetActive(false);
                // ★ 诊断日志：若 kind 非法或 multiplier 与 kind 不匹配，输出详细值供定位
                if (cell != null && ((int)cell.kind < 0 || (int)cell.kind > 5 || (cell.kind == FireballKind.Multiplier && cell.multiplier > 10f)))
                    Debug.LogWarning($"[ShowFireballOverlay] kind={(int)cell.kind}({cell.kind}) mult={cell.multiplier} reel={reel} row={row} → label={FireballLabel(cell)}");
            }
            else
            {
                var img = go.GetComponent<Image>();
                if (img != null) { img.enabled = true; img.sprite = GetSymbol(m_fireballSymbolId); }
            }
            ApplyFireballText(go, cell);
        }

        /// <summary>重播某列的 tong（夹子/桶）动画。</summary>
        void PlayTong(int reel)
        {
            if (m_tongs != null && reel >= 0 && reel < m_tongs.Length && m_tongs[reel] != null)
                m_tongs[reel].Play();
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
                    float y = RowToY(row) - off * m_cellSize;
                    rt.anchoredPosition = new Vector2(0f, y);
                    if (y < bottomLimit)
                    {
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
        bool ParseReelRow(string name, out int reel, out int row)
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
            RefreshColumnEffects();
        }

        /// <summary>销毁非释放中的火球 overlay；释放中的保留，随卷轴滚走。Mini 棋盘下跳过清理。</summary>
        void ClearFireballOverlaysExceptReleasing()
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
                Destroy(go);
                _fbOverlays.RemoveAt(i);
                removed = true;
            }
            if (removed) RefreshColumnEffects();
        }

        /// <summary>满列结算：从最下格开始，火球向下掉落（留 80% 幽灵），落入桶时播 tong 动画并销毁。</summary>
        public IEnumerator CollectFullReelAnimation(int reel)
        {
            var list = new List<GameObject>();
            for (int i = _fbOverlays.Count - 1; i >= 0; i--)
            {
                var go = _fbOverlays[i];
                if (go == null) { _fbOverlays.RemoveAt(i); continue; }
                if (ParseReelRow(go.name, out int rr, out _) && rr == reel)
                    list.Add(go);
            }
            if (list.Count == 0) yield break;

            Debug.Log($"[CollectFullReel] reel{reel} 找到 {list.Count} 个火球 overlay");

            list.Sort((a, b) =>
            {
                ParseReelRow(a.name, out _, out int ra);
                ParseReelRow(b.name, out _, out int rb);
                return RowToY(ra).CompareTo(RowToY(rb));
            });

            // ★ 满列收集演出：所有火球先统一慢慢放大到 ~1.1 倍，再开始逐个下落（不放大的话掉落太突兀）
            {
                float popDur = 0.25f;
                float pt = 0f;
                while (pt < popDur)
                {
                    pt += Time.deltaTime;
                    float k = Mathf.Clamp01(pt / popDur);
                    float s = Mathf.Lerp(1f, 1.1f, k * k * (3f - 2f * k));   // SmoothStep 缓动，放大更柔和
                    foreach (var ov in list)
                    {
                        if (ov == null) continue;
                        var rt0 = ov.transform as RectTransform;
                        if (rt0 != null) rt0.localScale = new Vector3(s, s, 1f);
                    }
                    yield return null;
                }
            }

            float tongY = float.MinValue;
            if (m_tongs != null && reel >= 0 && reel < m_tongs.Length && m_tongs[reel] != null)
                tongY = this.transform.InverseTransformPoint(m_tongs[reel].transform.position).y;
            float catchGap = m_cellSize * 0.6f;

            var ghosts = new List<GameObject>();

            foreach (var ov in list)
            {
                ParseReelRow(ov.name, out _, out int row);
                float fbMult = GetFireballMult(ov);

                Transform ghostParent = (m_node != null && reel >= 0 && reel < m_node.Length && m_node[reel] != null)
                    ? m_node[reel].transform : this.transform;
                var ghost = Instantiate(ov, ghostParent);
                ghost.name = $"FBGhost_{reel}_{row}";
                var grt = ghost.transform as RectTransform;
                if (grt != null) grt.anchoredPosition = new Vector2(0f, RowToY(row));
                grt.localScale = Vector3.one;   // 残留幽灵保持原大小，不跟随放大（ov 此刻已是 ~1.1）
                ghost.transform.SetAsLastSibling();
                SetOverlayAlpha(ghost, 0.8f);
                // ★ ghost 的 m_effect 也必须关闭——ghost 会停在原位直到下一轮 SpinHoldRound 才滚走销毁，
                //   如果 m_effect 开着，玩家会看到 m_effect 在火球收集后仍不消失（要等到下一轮确认才消失）。
                var ghostItem = ghost.GetComponent<ReelItem>();
                if (ghostItem != null && ghostItem.m_effect != null) ghostItem.m_effect.SetActive(false);
                ghosts.Add(ghost);

                var rt = ov.transform as RectTransform;
                if (rt != null)
                {
                    rt.SetParent(this.transform, true);
                    float startLocalY = rt.localPosition.y;
                    float targetLocalY = (tongY > float.MinValue)
                        ? (tongY + catchGap)
                        : (m_rowBaseY - (m_buf + 1) * m_cellSize);
                    float dropDur = 0.35f;
                    float t = 0f;
                    bool caught = false;
                    while (t < dropDur)
                    {
                        t += Time.deltaTime;
                        float y = Mathf.Lerp(startLocalY, targetLocalY, t / dropDur);
                        rt.localPosition = new Vector2(rt.localPosition.x, y);

                        if (tongY > float.MinValue && y <= tongY + catchGap * 0.5f)
                        {
                            caught = true;
                            ov.SetActive(false);
                            PlayTong(reel);
                            break;
                        }
                        yield return null;
                    }
                    if (!caught)
                    {
                        ov.SetActive(false);
                        PlayTong(reel);
                    }
                }
                else
                {
                    ov.SetActive(false);
                    PlayTong(reel);
                }

                var fbItem = ov.GetComponent<ReelItem>();
                FireballKind fbKind = (fbItem != null) ? fbItem.m_type : FireballKind.Multiplier;
                // 免费模式火球(FreeSpins)不派彩、不入计数器；倍数火球→X数字，彩金火球→档名 MINOR/MAJOR…
                if (fbKind != FireballKind.FreeSpins)
                    AddFireballToCounter(reel, fbMult, fbKind);

                _fbOverlays.Remove(ov);
                Destroy(ov);

                yield return new WaitForSeconds(0.06f);
            }

            // 火球收集完成：event:/Sounds/23
            if (FMODSoundMgr.Instance != null)
                FMODSoundMgr.Instance.PlaySound("event:/Sounds/23");

            foreach (var g in ghosts)
            {
                if (g == null) continue;
                if (!ParseReelRow(g.name, out _, out int grow)) continue;
                g.name = $"FBOverlay_{reel}_{grow}";
                _fbOverlays.Add(g);
            }
            _collectedReels.Add(reel);
            _releaseReels.Add(reel);

            // ★ 全部火球下落完成后，停顿约 0.3s，再把幽灵从 80% 平滑恢复为 100%（满列残留视觉更干净）
            yield return new WaitForSeconds(0.3f);
            {
                float fadeDur = 0.15f;
                float ft = 0f;
                while (ft < fadeDur)
                {
                    ft += Time.deltaTime;
                    float k = Mathf.Clamp01(ft / fadeDur);
                    float a = Mathf.Lerp(0.8f, 1f, k);
                    foreach (var g in ghosts)
                        if (g != null) SetOverlayAlpha(g, a);
                    yield return null;
                }
            }
            foreach (var g in ghosts)
                if (g != null) SetOverlayAlpha(g, 1f);
        }

        /// <summary>设置 overlay 及其子节点的透明度（CanvasGroup）。</summary>
        void SetOverlayAlpha(GameObject go, float alpha)
        {
            if (go == null) return;
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            cg.alpha = alpha;
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
