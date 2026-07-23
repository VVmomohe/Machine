using System;

namespace SlotMachine.Core
{
    /// <summary>随机源抽象：逻辑层与 Unity 解耦，便于离线仿真/测试用种子。</summary>
    public interface ISlotRng
    {
        int Next(int maxExclusive);
        double NextDouble();
    }

    /// <summary>可种子化随机（测试 / RTP 仿真用，结果可复现）。</summary>
    public class SeedRng : ISlotRng
    {
        private readonly System.Random _r;
        public SeedRng(int seed) { _r = new System.Random(seed); }
        public int Next(int maxExclusive) { return maxExclusive <= 0 ? 0 : _r.Next(maxExclusive); }
        public double NextDouble() { return _r.NextDouble(); }
    }

    /// <summary>真机随机：走 UnityEngine.Random，每次不同。</summary>
    public class UnityRng : ISlotRng
    {
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) return 0;
            return UnityEngine.Random.Range(0, maxExclusive);
        }
        public double NextDouble() { return UnityEngine.Random.value; }
    }
}
