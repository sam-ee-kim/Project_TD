using System;

namespace TowerDefence
{
    // 풀에서 재사용되는 몬스터를 가리키는 핸들 (DamageDesign 3-3).
    // 몬스터가 풀에서 다시 나올 때마다 Generation이 올라가므로, 옛 핸들은 자동으로 무효가 된다.
    public readonly struct MonsterHandle : IEquatable<MonsterHandle>
    {
        public static readonly MonsterHandle None = new MonsterHandle(-1, 0);

        public readonly int Index;
        public readonly int Generation;

        public MonsterHandle(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsNone => Index < 0;

        public bool Equals(MonsterHandle other) => Index == other.Index && Generation == other.Generation;
        public override bool Equals(object obj) => obj is MonsterHandle other && Equals(other);
        public override int GetHashCode() => (Index * 397) ^ Generation;
        public static bool operator ==(MonsterHandle a, MonsterHandle b) => a.Equals(b);
        public static bool operator !=(MonsterHandle a, MonsterHandle b) => !a.Equals(b);
    }
}
