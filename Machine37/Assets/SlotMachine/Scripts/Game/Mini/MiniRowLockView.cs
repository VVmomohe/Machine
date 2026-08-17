using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace com.slot
{
    /// <summary>
    /// Mini 免费小游戏「行锁定」视觉层。
    ///
    /// 【挂载】由你手动挂到 MINIGame 的 "Excel" 节点上。
    ///
    /// 【两种用法】
    ///   1) 美术已摆：prefab 里 Excel 下已摆好每行的 Box（下标=行号，0=最上行，按兄弟顺序对应）。
    ///      进 Mini 时本类【直接接管这些已有子对象】作为各行锁位（m_box 可留空，不必手填）；锁数文本留空则自动在该 Box 子级找 Text。
    ///   2) 美术没摆齐 / 想先测：对于 Excel 下【仍缺失】的行，本类直接【克隆已有的 Box 模板】（Instantiate，
    ///      结构/字体/位置基线全继承美术，只按行改 y 定位）补到 totalRows；已有的 Box 绝不重复生成/覆盖；
    ///      美术把缺的 Box 摆好后，下次进 Mini 这些克隆行即消失（由真 Box 接管）。
    ///
    /// 【结构约定（手动模式）】
    ///   Excel  ← 挂本组件
    ///    ├─ Box     ← 第 0 行（最上行）整行对象：内含 Lock 图标 + NumText（锁数文本）
    ///    ├─ Box_2   ← 第 1 行
    ///    └─ ...     ← 依次到第 (totalRows-1) 行（最下行）
    ///   m_box 数组【下标即行号】，0 = 最上行，与 MiniGame 的 row 索引（0-7）完全一致。
    ///
    /// 【职责边界】本类只管显示：显示/隐藏行锁、刷新锁数文本、播放解锁特效。
    ///   行锁定的逻辑（锁哪些行、何时解锁、锁定行火球不计派彩）全在 MiniGame.cs。
    /// </summary>
    public class MiniRowLockView : MonoBehaviour
    {
        [Header("每行一个 Box 对象：下标=行号（0=最上行）。留空则运行时自动生成占位行供测试")]
        public GameObject[] m_box = new GameObject[0];

        [Header("可选：显式指定各行锁数文本（下标=行号）；留空则自动在对应 Box 子级查找 Text")]
        public Text[] m_numText = new Text[0];

        [Header("可选：各行解锁特效（下标=行号）。美术未做时留空；填了则解锁瞬间 SetActive(true)")]
        public GameObject[] m_unlockFx = new GameObject[0];

        // 自动生成/克隆锁行时按行定位用（由 MiniGame 传入棋盘格高；美术把 Box 摆齐后该值仅用于补缺克隆行的 y 定位）
        [Header("锁行定位用：单行高（≈棋盘格高），由 MiniGame 传入")]
        public float m_cellSize = 135f;     // 单行高（≈棋盘格高）

        // 行号 → 锁数文本（含"该行没有文本"的负缓存，避免每轮重复 GetComponentInChildren）
        readonly Dictionary<int, Text> _numCache = new Dictionary<int, Text>();

        // 本局为补缺而克隆出来的 Box（离开 Mini / 重进时销毁，避免跨局累积）
        readonly List<GameObject> _generatedClones = new List<GameObject>();

        /// <summary>初始化：只显示被锁的行，其余行的 Box 全部隐藏。
        /// totalRows=总行数(用于自动生成占位行与定位)；cellSize/boardWidth 供自动生成定位用。</summary>
        public void Init(IEnumerable<int> lockedRows, int totalRows, float cellSize, float boardWidth)
        {
            if (cellSize > 0) m_cellSize = cellSize;
            EnsureBoxes(Mathf.Max(totalRows, 1));
            HideAllBoxes();
            if (lockedRows == null) return;
            foreach (var r in lockedRows) SetRowActive(r, true);
        }

        /// <summary>刷新某一行的锁数文本（该行还需转几轮才解锁）。</summary>
        public void SetRowCount(int row, int spinsLeft)
        {
            var t = GetNumText(row);
            if (t != null) t.text = Mathf.Max(0, spinsLeft).ToString();
        }

        /// <summary>解锁某一行：播放解锁特效并隐藏该行 Box。</summary>
        public void RemoveLock(int row)
        {
            PlayUnlockEffect(row);
            SetRowActive(row, false);
        }

        /// <summary>★ 解锁特效：美术未做时 m_unlockFx 留空，此处仅 DEBUG 日志（桩）。
        /// 美术就绪后把特效对象填进 m_unlockFx[row]，或在此改成播 Animator/粒子。</summary>
        void PlayUnlockEffect(int row)
        {
            if (m_unlockFx != null && row >= 0 && row < m_unlockFx.Length && m_unlockFx[row] != null)
            {
                m_unlockFx[row].SetActive(false);   // 重置，保证同一特效可重复触发
                m_unlockFx[row].SetActive(true);
                return;
            }
            Debug.Log($"[MiniRowLockView] 解锁行 row={row}（Unlock 特效待美术接入）");
        }

        /// <summary>清空：彻底拆除本局锁视觉状态（离开 Mini 时调，避免残留进下一局）。
        /// 关键：销毁上一局克隆出的占位行并清空 m_box/m_numText 引用，下次进 Mini 时 EnsureBoxes 才会重新从 Excel 节点接管美术已摆的 Box。
        /// 否则 m_box 仍持有上一局已被 Destroy 的克隆引用（Unity 下 == null），EnsureBoxes 见 m_box 非空会跳过接管、template 取不到 → 锁定行视觉不还原。</summary>
        public void Clear()
        {
            HideAllBoxes();                                   // 先隐藏当前所有行 Box（美术摆的 + 克隆的）
            foreach (var g in _generatedClones) if (g != null) Destroy(g);   // 销毁上一局克隆的占位行，避免跨局累积
            _generatedClones.Clear();
            _numCache.Clear();
            m_box = new GameObject[0];                        // ★ 清空引用：下一局 EnsureBoxes 重新接管 Excel 下美术已摆的 Box
            m_numText = new Text[0];
            if (m_unlockFx != null)
                foreach (var fx in m_unlockFx)
                    if (fx != null) fx.SetActive(false);
        }

        /// <summary>确保每行都有 Box 引用：
        ///   ① 若 inspector 未手填 m_box，则【接管 Excel 节点下已有的 Box 子对象】（美术摆的 Box/Box_1/Box_2…）按兄弟顺序作为各行引用；
        ///   ② 之后只为仍缺失的行（m_box[r]==null）【克隆已有的 Box 模板】补到 totalRows（不再新建占位对象）。
        /// 美术已摆的 Box 不会被覆盖/重复生成，代码只补缺口；克隆行在离开 Mini / 重进时销毁，避免跨局累积。</summary>
        void EnsureBoxes(int totalRows)
        {
            // ★ 清掉上一局克隆出来的占位行，避免跨局累积/重复接管
            foreach (var g in _generatedClones) if (g != null) Destroy(g);
            _generatedClones.Clear();
            _numCache.Clear();

            // ① 接管 Excel 下已有的 Box 子对象（美术摆的，按兄弟顺序 = 行号；只认名字以 "Box" 开头的，避免误接管其它节点）
            if (m_box == null || m_box.Length == 0)
            {
                var adopted = new List<GameObject>();
                for (int i = 0; i < transform.childCount; i++)
                {
                    var c = transform.GetChild(i);
                    if (c != null && c.name.StartsWith("Box", System.StringComparison.OrdinalIgnoreCase))
                        adopted.Add(c.gameObject);
                }
                m_box = adopted.ToArray();
            }
            if (m_box.Length < totalRows)
            {
                var grown = new GameObject[totalRows];
                System.Array.Copy(m_box, grown, m_box.Length);
                m_box = grown;
            }

            if (m_numText == null || m_numText.Length < totalRows)
            {
                var grown = new Text[totalRows];
                if (m_numText != null) System.Array.Copy(m_numText, grown, m_numText.Length);
                m_numText = grown;
            }

            // ② 仍为 null 的行 → 直接克隆已有的 Box 模板（结构/字体/位置基线全继承美术，只按行改 y）
            int adoptedCount = 0, cloned = 0;
            GameObject template = null;
            foreach (var b in m_box) if (b != null) { template = b; break; }
            if (template == null)
            {
                // ★ 美术未摆 Box（m_rowLockView 已挂但 Excel 下还没有任何 Box 子对象）→ 仅跑逻辑、无锁视觉，不报错。
                Debug.LogWarning("[MiniRowLockView] Excel 下没有任何 Box 可作克隆模板（美术尚未摆放锁行）。本局仅跑逻辑，无锁视觉；美术把 Box 摆齐后自动接管。");
                return;
            }
            float mid = (totalRows - 1) / 2f;
            for (int r = 0; r < totalRows; r++)
            {
                if (m_box[r] != null) { adoptedCount++; continue; }   // 已有（美术摆的或手填的）保留

                var go = Instantiate(template, transform, false);
                go.name = $"Box_clone_{r}";
                var rt = go.GetComponent<RectTransform>();
                if (rt != null) rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, (mid - r) * m_cellSize);
                m_box[r] = go;
                _generatedClones.Add(go);
                cloned++;
            }
            Debug.Log($"[MiniRowLockView] 接管已有 Box={adoptedCount} 个 + 克隆 {cloned} 个，共 {totalRows} 行锁位");
        }

        void HideAllBoxes()
        {
            if (m_box == null) return;
            for (int i = 0; i < m_box.Length; i++)
                if (m_box[i] != null) m_box[i].SetActive(false);
        }

        void SetRowActive(int row, bool on)
        {
            if (m_box == null || row < 0 || row >= m_box.Length) return;
            if (m_box[row] != null) m_box[row].SetActive(on);
        }

        Text GetNumText(int row)
        {
            if (_numCache.TryGetValue(row, out var cached)) return cached;

            Text t = null;
            if (m_numText != null && row >= 0 && row < m_numText.Length) t = m_numText[row];
            if (t == null && m_box != null && row >= 0 && row < m_box.Length && m_box[row] != null)
                t = m_box[row].GetComponentInChildren<Text>(true);   // includeInactive：Box 此刻可能还没显示

            _numCache[row] = t;                                       // 负缓存也存，避免每轮重复查找
            return t;
        }
    }
}
