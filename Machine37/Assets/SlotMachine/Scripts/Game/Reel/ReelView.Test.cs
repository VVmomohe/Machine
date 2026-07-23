using UnityEngine;

namespace com.slot
{
    /// <summary>ReelView 测试部分：Inspector 右键菜单，不依赖引擎即可验证表现。</summary>
    public partial class ReelView
    {
        [ContextMenu("Test 初始化棋盘")]
        public void TestInitGrid() { InitStaticGrid(); }

        [ContextMenu("Test Spin (卷轴滚动)")]
        public void TestSpin()
        {
            int n = (m_node != null && m_node.Length > 0) ? m_node.Length
                  : (m_reelRows.Count > 0 ? m_reelRows.Count : 5);
            int[][] g = new int[n][];
            for (int r = 0; r < n; r++)
            {
                int rows = (r < m_reelRows.Count && m_reelRows[r] > 0) ? m_reelRows[r] : 4;
                g[r] = new int[rows];
                for (int i = 0; i < rows; i++) g[r][i] = RandSymbol();
            }
            ShowGrid(g);
        }

        [ContextMenu("Test Stop (急停)")]
        public void TestStop() { StopNow(); }
    }
}
