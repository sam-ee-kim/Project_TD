using System;

namespace TowerDefence
{
    // 몬스터·타워·투사체·이펙트 재사용 풀 (GDD 8-5 #2).
    // Prewarm으로 미리 채워 두면 Get/Release는 할당하지 않는다.
    // 풀이 비었을 때 Get하면 새로 만들며(할당 발생), 이는 Prewarm 부족 신호다.
    public sealed class ObjectPool<T> where T : class
    {
        readonly Func<T> create;
        readonly Action<T> onGet;
        readonly Action<T> onRelease;

        T[] inactive;     // 쉬는 객체 스택
        int inactiveCount;
        int createdCount; // 지금까지 만든 총 개수 = inactive 배열이 담아야 할 최대치

        public int ActiveCount => createdCount - inactiveCount;
        public int InactiveCount => inactiveCount;

        public ObjectPool(Func<T> create, Action<T> onGet = null, Action<T> onRelease = null, int initialCapacity = 16)
        {
            this.create = create ?? throw new ArgumentNullException(nameof(create));
            this.onGet = onGet;
            this.onRelease = onRelease;
            inactive = new T[Math.Max(1, initialCapacity)];
        }

        // 쉬는 객체가 n개 이상이 되도록 미리 만든다. 휴식 시간에 호출.
        public void Prewarm(int n)
        {
            while (inactiveCount < n)
            {
                // CreateNew가 배열을 키울 수 있으므로 먼저 만들고 나서 넣는다
                // (inactive[i] = CreateNew()는 키우기 전 배열에 쓰게 된다).
                var item = CreateNew();
                inactive[inactiveCount++] = item;
            }
        }

        public T Get()
        {
            T item;
            if (inactiveCount > 0)
            {
                item = inactive[--inactiveCount];
                inactive[inactiveCount] = null;
            }
            else
            {
                item = CreateNew();
            }
            onGet?.Invoke(item);
            return item;
        }

        public void Release(T item)
        {
            onRelease?.Invoke(item);
            // createdCount ≤ inactive.Length가 항상 유지되므로 여기서는 배열이 늘지 않는다.
            inactive[inactiveCount++] = item;
        }

        T CreateNew()
        {
            createdCount++;
            if (createdCount > inactive.Length)
                Array.Resize(ref inactive, Math.Max(createdCount, inactive.Length * 2));
            return create();
        }
    }
}
