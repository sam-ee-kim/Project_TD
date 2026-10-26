using System;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    // 몬스터 스폰·이동·피격·사망·출구 도착 (GDD 8-1).
    // 캐시·체력은 직접 바꾸지 않고 Killed / ReachedExit 이벤트만 발행한다.
    //
    // 몬스터는 풀(ObjectPool)에서 꺼내 쓰고, 배열 인덱스 + Generation 핸들로 가리킨다 (DamageDesign 3-3).
    // 표시(MonsterView: 모델 + Idle/Walk/Die 상태머신)는 종류별 풀을 따로 두고, 스크립트 Update 없이
    // TickMove(위치)와 TickViews(상태·애니메이션)에서 구동한다.
    // 게임 데이터는 사망 프레임의 5단계에서 바로 풀로 돌아가고(DamageDesign), 표시만 Die 애니메이션이 끝날 때까지 남는다.
    // 풀은 앱 시작 때 monsterPoolPrewarm만큼, 휴식 때 다음 웨이브 수만큼 채우므로 웨이브 중에는 만들지도 지우지도 않는다.
    public class MonsterManager
    {
        public event Action<MonsterKilled> Killed;           // → PlayerState.Earn(Kill)
        public event Action<MonsterReachedExit> ReachedExit; // → PlayerState.TakeNexusDamage

        const float CapsuleScale = 0.4f; // 모델이 없는 몬스터의 기본 캡슐 크기 (높이 0.8)

        GameData data;
        Board board;
        RunState run;

        ObjectPool<MonsterInstance> pool;
        readonly List<MonsterInstance> all = new List<MonsterInstance>();    // 인덱스 = 핸들 Index
        readonly List<MonsterInstance> active = new List<MonsterInstance>(); // Alive·Dying·Exited (정리 전)
        int aliveCount;

        // 표시 (viewRoot가 null이면 표시 없이 동작: 단위 테스트용)
        Transform viewRoot;
        Mesh capsuleMesh;
        Material capsuleMaterial;
        readonly Dictionary<MonsterDef, ObjectPool<MonsterView>> viewPools = new Dictionary<MonsterDef, ObjectPool<MonsterView>>();
        readonly List<MonsterView> dyingViews = new List<MonsterView>(); // 데이터는 반납됐고 Die 애니메이션만 남은 표시
        bool movementPaused = true; // Wave가 아니면 남은 몬스터는 제자리 대기(Idle)

        // 이번 웨이브 구성표 (GameFlow가 넘긴 버퍼를 복사해 보관, 할당 없이 재사용).
        WaveSpawn[] waveSpawns = new WaveSpawn[8];
        MonsterDef[] waveDefs = new MonsterDef[8];
        readonly WaveSpawn[] prewarmBuffer = new WaveSpawn[8];
        int waveSpawnCount;
        int spawnLine;          // 지금 내보내는 구성표 줄
        int spawnedInLine;      // 그 줄에서 내보낸 수
        float spawnTimer;       // 다음 스폰까지 남은 시간
        int remainingToSpawn;   // 이번 웨이브에서 아직 내보내지 않은 몬스터 수
        bool inWave;
        int nextSpawn;          // 다음 몬스터의 스타트 지점 (번갈아)

        // 이번 웨이브 스폰이 모두 끝났는지 (웨이브 클리어 판정용).
        public bool IsSpawnFinished => remainingToSpawn == 0;
        public bool SpawnFinished => IsSpawnFinished;

        // 맵 위 Alive 몬스터 수.
        public int AliveCount => aliveCount;

        // 이번 웨이브에서 아직 내보내지 않은 몬스터 수.
        public int RemainingToSpawn => remainingToSpawn;

        // 풀이 만든 몬스터·표시 수 / 웨이브 중에 새로 만든 수 (0이어야 한다, GDD 8-5 #2 확인용).
        public int CreatedCount => all.Count;
        public int CreatedDuringWave { get; private set; }
        public int ViewsCreated { get; private set; }
        public int ViewsCreatedDuringWave { get; private set; }
        public int DyingViewCount => dyingViews.Count;

        // 테스트용(임시): 0보다 크면 스폰하는 몬스터 체력을 이 값으로 (정의 데이터는 바꾸지 않는다).
        const float HpEpsilon = 0.0001f;

        public int DebugHpOverride { get; set; }

        // 이번 웨이브 체력 배율 (LevelRules.GetHpScale, 웨이브 시작 때 1번 계산).
        float hpScale = 1f;
        public float HpScale => hpScale;

        public void Init(GameData data, Board board, Transform viewRoot = null)
        {
            this.data = data;
            this.board = board;
            this.viewRoot = viewRoot;

            pool = new ObjectPool<MonsterInstance>(CreateInstance, OnGet, OnRelease,
                data.Config != null ? data.Config.performance.monsterPoolPrewarm : 16);
            Prewarm(data.Config != null ? data.Config.performance.monsterPoolPrewarm : 0);
        }

        // 새 판: 모든 몬스터와 표시를 풀에 반납, 스폰 상태 초기화. 매니저·풀은 다시 만들지 않는다.
        // (Board 칸 목록은 Board.ResetForNewRun이 비운다.)
        public void ResetForNewRun(RunState run)
        {
            this.run = run;
            hpScale = 1f;
            ClearSlowFields();
            for (int i = active.Count - 1; i >= 0; i--)
            {
                ReleaseView(active[i]);
                pool.Release(active[i]);
            }
            active.Clear();
            for (int i = 0; i < dyingViews.Count; i++)
                viewPools[dyingViews[i].Def].Release(dyingViews[i]);
            dyingViews.Clear();
            aliveCount = 0;
            waveSpawnCount = 0;
            remainingToSpawn = 0;
            inWave = false;
            movementPaused = true;
            nextSpawn = 0;
        }

        // 쉬는 몬스터가 count마리 이상 되도록 채운다 (앱 시작, 휴식 시간). 웨이브 중에는 부르지 않는다.
        public void Prewarm(int count)
        {
            pool.Prewarm(count);
            if (active.Capacity < all.Count)
                active.Capacity = all.Count;
        }

        // 이 종류의 표시를 count개 이상 미리 만든다 (휴식 시간).
        public void PrewarmViews(MonsterDef def, int count)
        {
            if (viewRoot == null || def == null)
                return;
            GetViewPool(def).Prewarm(count);
            if (dyingViews.Capacity < count)
                dyingViews.Capacity = count;
        }

        // GameFlow가 Wave에 들어갈 때 이번 웨이브 구성표를 넘긴다 (LevelRules.GetWaveComposition 결과).
        // 몬스터 id → Def 조회는 여기서 웨이브당 1번만 한다.
        public void BeginWave(WaveSpawn[] spawns, int count)
        {
            if (waveSpawns.Length < count)
            {
                waveSpawns = new WaveSpawn[count];
                waveDefs = new MonsterDef[count];
            }
            Array.Copy(spawns, waveSpawns, count);
            waveSpawnCount = count;

            remainingToSpawn = 0;
            for (int i = 0; i < count; i++)
            {
                if (!data.TryGetMonster(spawns[i].MonsterId, out waveDefs[i]))
                {
                    Debug.LogError($"[MonsterManager] 웨이브 구성표의 몬스터 id '{spawns[i].MonsterId}'가 없습니다.");
                    waveSpawns[i] = new WaveSpawn(spawns[i].MonsterId, 0, spawns[i].IntervalSec, spawns[i].DelaySec);
                }
                remainingToSpawn += waveSpawns[i].Count;
            }

            spawnLine = 0;
            spawnedInLine = 0;
            spawnTimer = count > 0 ? waveSpawns[0].DelaySec : 0f; // 첫 마리는 지연 뒤 바로
            inWave = true;
        }

        // GameFlow.StateChanged 구독.
        public void HandleStateChanged(StateChange change)
        {
            movementPaused = change.State != GameState.Wave;
            switch (change.State)
            {
                case GameState.Wave:
                    hpScale = data.Config != null ? LevelRules.GetHpScale(data.Config, change.Floor, change.Wave) : 1f;
                    break;
                case GameState.Break:
                    inWave = false;
                    PrewarmForNextWave();
                    break;
                default:
                    // 게임오버·클리어·층 전환: 남은 스폰을 버린다. 남은 몬스터는 제자리 대기, 새 판(ResetForNewRun)에서 반납.
                    inWave = false;
                    remainingToSpawn = 0;
                    ClearSlowFields(); // 맵이 바뀌거나 판이 끝나면 감속 영역도 사라진다
                    break;
            }
        }

        // GameLoop 2단계: 구성표대로 spawnIntervalSec 간격으로 풀에서 꺼내 스폰.
        public void TickSpawn(float dt)
        {
            if (remainingToSpawn == 0 || board.Map == null)
                return;

            spawnTimer -= dt;
            while (spawnTimer <= 0f && remainingToSpawn > 0)
            {
                // 다 내보낸 줄은 건너뛰고 다음 줄의 지연을 더한다.
                while (spawnedInLine >= waveSpawns[spawnLine].Count)
                {
                    spawnLine++;
                    spawnedInLine = 0;
                    spawnTimer += waveSpawns[spawnLine].DelaySec;
                }
                if (spawnTimer > 0f)
                    break;

                Spawn(waveDefs[spawnLine]);
                spawnedInLine++;
                remainingToSpawn--;
                spawnTimer += waveSpawns[spawnLine].IntervalSec;
            }
        }

        // --- 감속 영역 (나노필드) ---
        // 원 안(바닥 평면)에 있는 몬스터는 이동 속도가 (1 − slow)배. 여러 개가 겹치면 가장 센 것 하나만.
        // 고정 크기 원형 버퍼라 할당이 없고, 다 차면 가장 오래된 것을 덮어쓴다.
        public const int MaxSlowFields = 64;
        readonly Vector3[] fieldCenter = new Vector3[MaxSlowFields];
        readonly float[] fieldRadiusSq = new float[MaxSlowFields];
        readonly float[] fieldSlow = new float[MaxSlowFields];
        readonly float[] fieldEnd = new float[MaxSlowFields];
        int fieldNext;
        float fieldLatestEnd;
        float moveTime; // TickMove 누적 시간 (영역 만료 기준)

        // 감속 영역 추가 (TowerManager가 나노 탄 명중 때 요청).
        public void AddSlowField(Vector3 center, float radius, float slow, float duration)
        {
            int i = fieldNext;
            fieldNext = (i + 1) % MaxSlowFields;
            fieldCenter[i] = center;
            fieldRadiusSq[i] = radius * radius;
            fieldSlow[i] = Mathf.Clamp01(slow);
            fieldEnd[i] = moveTime + duration;
            if (fieldEnd[i] > fieldLatestEnd)
                fieldLatestEnd = fieldEnd[i];
        }

        public int ActiveSlowFields
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxSlowFields; i++)
                    if (fieldEnd[i] > moveTime) n++;
                return n;
            }
        }

        // 이 몬스터의 이동 속도 배율 (1 = 정상).
        public float GetSpeedMultiplier(MonsterInstance m)
        {
            if (fieldLatestEnd <= moveTime)
                return 1f;
            var p = GetGroundPoint(m);
            float slow = 0f;
            for (int i = 0; i < MaxSlowFields; i++)
            {
                if (fieldEnd[i] <= moveTime || fieldSlow[i] <= slow)
                    continue;
                float dx = p.x - fieldCenter[i].x, dz = p.z - fieldCenter[i].z;
                if (dx * dx + dz * dz <= fieldRadiusSq[i])
                    slow = fieldSlow[i];
            }
            return 1f - slow;
        }

        void ClearSlowFields()
        {
            for (int i = 0; i < MaxSlowFields; i++)
                fieldEnd[i] = 0f;
            fieldLatestEnd = 0f;
            moveTime = 0f;
            fieldNext = 0;
        }

        // GameLoop 3단계: 경로를 따라 speed(칸/초)로 이동. 칸이 바뀐 몬스터만 Board 칸 목록 갱신, 출구 도착 처리.
        // 스폰 직후 대기 시간(IdleTime) 동안은 움직이지 않는다.
        public void TickMove(float dt)
        {
            moveTime += dt;
            for (int i = 0; i < active.Count; i++)
            {
                var m = active[i];
                if (m.State != MonsterState.Alive)
                    continue;

                float moveDt = dt;
                if (m.IdleTime > 0f)
                {
                    m.IdleTime -= dt;
                    if (m.IdleTime > 0f)
                        continue;
                    moveDt = -m.IdleTime; // 대기가 끝난 뒤 남은 시간만큼 이동
                    m.IdleTime = 0f;
                }

                int lastIndex = board.GetPathLength(m.PathId) - 1;
                float speedMul = GetSpeedMultiplier(m); // 나노필드 안이면 느려진다
                if (m.View != null && speedMul != m.SpeedMul)
                    m.View.SetMoveScale(speedMul);      // 걷기 애니메이션도 같은 비율로
                m.SpeedMul = speedMul;
                m.PathProgress += m.Def.speed * speedMul * moveDt;
                m.Remaining = lastIndex - m.PathProgress;

                if (m.PathProgress >= lastIndex)
                {
                    // 출구 칸 도착 (DamageDesign 4-1): Exited → Board 목록에서 제거 → 넥서스 피해. 보상 없음.
                    m.PathProgress = lastIndex;
                    m.Remaining = 0f;
                    m.State = MonsterState.Exited;
                    aliveCount--;
                    board.RemoveFromCell(m.CellId, m.Handle);
                    PlaceView(m, false);
                    ReachedExit?.Invoke(new MonsterReachedExit(m.Def.nexusDamage));
                    continue;
                }

                int cellIndex = (int)m.PathProgress;
                if (cellIndex != m.PathIndex)
                {
                    int cellId = board.CellId(board.GetPathCell(m.PathId, cellIndex));
                    board.RemoveFromCell(m.CellId, m.Handle);
                    board.AddToCell(cellId, m.Handle);
                    m.PathIndex = cellIndex;
                    m.CellId = cellId;
                }
                PlaceView(m, false);
            }
        }

        // GameLoop 5단계: Dying·Exited 몬스터를 풀에 반납 (State = Inactive). 다음에 꺼낼 때 Generation +1.
        // 죽은 몬스터의 표시는 떼어 내 Die 애니메이션을 재생하고, 출구에 들어간 몬스터의 표시는 바로 숨긴다.
        public void Cleanup()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var m = active[i];
                if (m.State != MonsterState.Dying && m.State != MonsterState.Exited)
                    continue;
                if (m.View != null)
                {
                    if (m.State == MonsterState.Dying)
                    {
                        m.View.ChangeState(MonsterViewState.Die);
                        dyingViews.Add(m.View);
                        m.View = null;
                    }
                    else
                    {
                        ReleaseView(m);
                    }
                }
                int last = active.Count - 1;
                active[i] = active[last];
                active.RemoveAt(last);
                pool.Release(m);
            }
        }

        // 표현 단계 (GameLoop 7단계, 모든 상태에서): 몬스터 표시 상태머신과 Die 애니메이션.
        //   스폰 직후 대기·Wave가 아님 → Idle / 경로 이동 중 → Walk / 사망 → Die → 끝나면 숨김
        public void TickViews(float dt)
        {
            for (int i = 0; i < active.Count; i++)
            {
                var m = active[i];
                if (m.View == null)
                    continue;
                bool walking = m.State == MonsterState.Alive && !movementPaused && m.IdleTime <= 0f;
                m.View.ChangeState(walking ? MonsterViewState.Walk : MonsterViewState.Idle);
                m.View.Tick(dt);
            }

            for (int i = dyingViews.Count - 1; i >= 0; i--)
            {
                var view = dyingViews[i];
                if (!view.Tick(dt))
                    continue;
                int last = dyingViews.Count - 1;
                dyingViews[i] = dyingViews[last];
                dyingViews.RemoveAt(last);
                viewPools[view.Def].Release(view); // → Hidden
            }
        }

        // 타워 → 몬스터 피해의 유일한 창구 (DamageDesign 3-2 그대로).
        public DamageResult ApplyDamage(MonsterHandle target, in DamageInfo info)
        {
            if (!IsValid(target)) return DamageResult.Ignored;              // 이미 정리됐거나 재사용됨
            var m = all[target.Index];
            if (m.State != MonsterState.Alive) return DamageResult.Ignored; // 같은 프레임에 이미 사망·도착

            float dmg = DamageCalculator.Compute(info, m.Def);               // 지금은 info.Amount 그대로
            m.Hp -= dmg;
            if (m.Hp > HpEpsilon) return DamageResult.Damaged;               // 소수 피해 누적 오차 (0.5 × 8 = 4) 흡수

            m.State = MonsterState.Dying;                                    // 사망 판정은 여기서 딱 1번
            aliveCount--;
            board.RemoveFromCell(m.CellId, m.Handle);                        // 즉시 공격 대상 후보에서 제외
            Killed?.Invoke(new MonsterKilled(m.Def.reward));                 // PlayerState.Earn(amount, Kill)
            return DamageResult.Killed;
        }

        // 핸들이 아직 같은 몬스터를 가리키는지 (DamageDesign 3-3).
        public bool IsValid(MonsterHandle handle)
        {
            if (handle.Index < 0 || handle.Index >= all.Count)
                return false;
            var m = all[handle.Index];
            return m.Generation == handle.Generation && m.State != MonsterState.Inactive;
        }

        // 핸들로 몬스터 조회. 호출 전에 IsValid로 확인한다 (무효면 null).
        public MonsterInstance Get(MonsterHandle handle) => IsValid(handle) ? all[handle.Index] : null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 디버그(F3): 남은 스폰을 버리고 살아있는 몬스터를 보상 없이 정리해 웨이브를 끝낸다.
        public void DebugFinishWave()
        {
            remainingToSpawn = 0;
            for (int i = 0; i < active.Count; i++)
            {
                var m = active[i];
                if (m.State != MonsterState.Alive)
                    continue;
                m.State = MonsterState.Dying;
                aliveCount--;
                board.RemoveFromCell(m.CellId, m.Handle);
            }
        }
#endif

        void Spawn(MonsterDef def)
        {
            var m = pool.Get(); // OnGet: Generation +1, State = Alive
            m.Def = def;
            m.DefIndex = def.Index;
            m.Hp = DebugHpOverride > 0 ? DebugHpOverride : LevelRules.ScaleHp(data.Config, def.hp, hpScale);
            m.PathId = ChooseSpawn(def);
            m.PathIndex = 0;
            m.PathProgress = 0f;
            m.Remaining = board.GetPathLength(m.PathId) - 1;
            m.CellId = board.CellId(board.GetPathCell(m.PathId, 0));
            m.IdleTime = def.spawnIdleSec;
            m.SpeedMul = 1f;
            m.View?.SetMoveScale(1f);
            active.Add(m);
            aliveCount++;
            board.AddToCell(m.CellId, m.Handle);

            if (viewRoot != null)
            {
                m.View = GetViewPool(def).Get();
                PlaceView(m, true);
                m.View.ChangeState(m.IdleTime > 0f ? MonsterViewState.Idle : MonsterViewState.Walk);
            }
        }

        // 스타트 지점 고르기: 지금은 순서대로 번갈아 (1 → 2 → 3 → 1 …).
        // 나중에 몬스터 티어가 나뉘면 스타트 지점마다 랜덤 가중치를 두어 높은 티어의 배치율을 높이는 자리.
        int ChooseSpawn(MonsterDef def)
        {
            int count = board.PathCount;
            if (count <= 1)
                return 0;
            int k = nextSpawn % count;
            nextSpawn++;
            return k;
        }

        // 경로 칸 사이를 진행률로 보간한 바닥 위치 (타일 윗면 y = 0).
        public Vector3 GetGroundPoint(MonsterInstance m)
        {
            int i = (int)m.PathProgress;
            int last = board.GetPathLength(m.PathId) - 1;
            var from = board.CellToWorld(board.GetPathCell(m.PathId, i));
            var to = board.CellToWorld(board.GetPathCell(m.PathId, i < last ? i + 1 : i));
            return Vector3.LerpUnclamped(from, to, m.PathProgress - i);
        }

        // 몬스터 몸통 중앙의 월드 좌표 (조준·투사체 유도용, 표시가 없어도 계산된다).
        public Vector3 GetAimPoint(MonsterInstance m)
        {
            var pos = GetGroundPoint(m);
            pos.y += m.Def.aimHeight;
            return pos;
        }

        // 표시 위치와 바라볼 방향(지금 걷는 경로 구간 방향).
        void PlaceView(MonsterInstance m, bool snap)
        {
            if (m.View == null)
                return;
            int i = (int)m.PathProgress;
            int last = board.GetPathLength(m.PathId) - 1;
            int a = i < last ? i : last - 1;
            var dir = board.CellToWorld(board.GetPathCell(m.PathId, a + 1)) - board.CellToWorld(board.GetPathCell(m.PathId, a));
            m.View.Place(GetGroundPoint(m), dir, snap);
        }

        void ReleaseView(MonsterInstance m)
        {
            if (m.View == null)
                return;
            viewPools[m.View.Def].Release(m.View);
            m.View = null;
        }

        // 휴식 시간: 다음 웨이브 몬스터 수만큼 데이터 풀과 종류별 표시 풀을 채운다 (부족할 때만 만든다).
        void PrewarmForNextWave()
        {
            if (data.Config == null || run == null)
                return;
            int g = run.TotalWaveIndex + 1;
            int lines = LevelRules.GetWaveComposition(data.Config, run.Floor, run.Wave + 1, g, prewarmBuffer);
            int total = 0;
            for (int i = 0; i < lines; i++)
            {
                total += prewarmBuffer[i].Count; // 웨이브 테이블 구성도 반영 (공식 마리 수와 다를 수 있음)

                // 같은 종류가 여러 줄에 나뉘어 있으면 그 종류의 첫 줄에서 합계만큼 표시를 채운다.
                bool seenBefore = false;
                for (int k = 0; k < i && !seenBefore; k++)
                    seenBefore = prewarmBuffer[k].MonsterId == prewarmBuffer[i].MonsterId;
                if (seenBefore || !data.TryGetMonster(prewarmBuffer[i].MonsterId, out var def))
                    continue;
                int sameKind = 0;
                for (int k = i; k < lines; k++)
                    if (prewarmBuffer[k].MonsterId == prewarmBuffer[i].MonsterId)
                        sameKind += prewarmBuffer[k].Count;
                PrewarmViews(def, sameKind);
            }
            Prewarm(total);
        }

        ObjectPool<MonsterView> GetViewPool(MonsterDef def)
        {
            if (!viewPools.TryGetValue(def, out var viewPool))
            {
                var kind = def;
                viewPool = new ObjectPool<MonsterView>(() => CreateView(kind), null,
                    v => v.ChangeState(MonsterViewState.Hidden), 32);
                viewPools.Add(def, viewPool);
            }
            return viewPool;
        }

        MonsterView CreateView(MonsterDef def)
        {
            if (inWave)
            {
                ViewsCreatedDuringWave++;
                Debug.LogWarning($"[MonsterManager] 웨이브 중 표시 풀이 부족해 '{def.id}' 표시를 새로 만들었습니다.");
            }
            ViewsCreated++;

            if (def.viewPrefab != null)
            {
                var go = UnityEngine.Object.Instantiate(def.viewPrefab, viewRoot, false);
                go.name = def.id;
                go.transform.localScale = Vector3.one * def.viewScale;
                return new MonsterView(def, go.transform, go.GetComponent<Animator>()); // 생성 때 1번만
            }

            // 모델이 없는 종류: 기본 캡슐 (애니메이션 없음)
            if (capsuleMesh == null)
                CreateCapsuleAssets();
            var root = new GameObject(def.id);
            root.transform.SetParent(viewRoot, false);
            root.transform.localScale = Vector3.one * CapsuleScale;
            var body = new GameObject("Capsule");
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.up; // 캡슐 바닥이 땅에 닿게 (높이 2의 절반)
            body.AddComponent<MeshFilter>().sharedMesh = capsuleMesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = capsuleMaterial;
            return new MonsterView(def, root.transform, null);
        }

        MonsterInstance CreateInstance()
        {
            if (inWave)
            {
                CreatedDuringWave++;
                Debug.LogWarning($"[MonsterManager] 웨이브 중 풀이 부족해 몬스터를 새로 만들었습니다 (총 {all.Count + 1}).");
            }
            var m = new MonsterInstance { Index = all.Count, State = MonsterState.Inactive };
            all.Add(m);
            return m;
        }

        static void OnGet(MonsterInstance m)
        {
            m.Generation++;
            m.State = MonsterState.Alive;
        }

        static void OnRelease(MonsterInstance m)
        {
            m.State = MonsterState.Inactive;
            m.Def = null;
            m.View = null;
        }

        // 기본 몬스터 표시: 캡슐 메시 + 단색 머티리얼 (모델이 없는 종류용). 콜라이더는 쓰지 않는다.
        void CreateCapsuleAssets()
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsuleMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(temp); // 메시만 가져오고 콜라이더째 버림
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            capsuleMaterial = new Material(shader) { name = "MonsterCapsule", color = new Color(0.85f, 0.25f, 0.25f) };
        }
    }
}
