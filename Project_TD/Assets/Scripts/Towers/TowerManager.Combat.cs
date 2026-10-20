using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    // TowerManager 전투: 경로 칸 기반 타겟팅, 투사체 (DamageDesign 3장).
    // 재장전이 끝난 타워만 사거리 안 경로 칸의 몬스터를 본다. 전체 몬스터를 훑지 않고, 넥서스 근처 예외 규칙도 없다.
    public partial class TowerManager
    {
        const float HitDistance = 0.1f; // 명중 판정 거리 (칸)

        readonly Dictionary<TowerDef, ObjectPool<Projectile>> projectilePools = new Dictionary<TowerDef, ObjectPool<Projectile>>();
        readonly List<Projectile> flying = new List<Projectile>();

        public int ProjectileCount => flying.Count;
        public int ProjectileViewsCreated { get; private set; }

        // GameLoop 4단계: 타워 상태머신(대기 → 조준 → 발사 → 재장전)과 투사체 이동·명중.
        //   Idle      : 대상 없음. 매 프레임 사거리 안 경로 칸에서 대상을 찾는다
        //   Aiming    : 대상을 잡았고 포탑이 그쪽으로 도는 중. 조준이 맞으면 발사 → Reloading
        //   Reloading : 발사 후 재장전(cooldown). 끝나면 lockMode 규칙으로 대상을 다시 확인 → Aiming / Idle
        // 대상 선택 규칙(DamageDesign 3-0)은 그대로이고, 조준을 기다리는 동안 발사 시점만 늦춘다.
        public void TickCombat(float dt)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                var t = placed[i];
                t.Cooldown -= dt;
                if (t.State == TowerState.Reloading)
                {
                    if (t.Cooldown > 0f)
                        continue;
                    t.State = TowerState.Aiming; // 재장전 끝 → 대상 재확인
                }

                var target = GetTarget(t);
                if (target.IsNone)
                {
                    t.State = TowerState.Idle;
                    t.Cooldown = 0f;              // 후보가 없으면 0에 둔 채 다음 프레임에 다시 본다
                    continue;
                }
                // 포탑이 아직 대상 쪽으로 돌아가는 중이면 기다린다 (총구에서 대상 쪽으로 나가게). 표시가 없으면(테스트) 바로 쏜다.
                if (t.Aim != null && !t.Aim.IsAimedAt(monsters.GetAimPoint(monsters.Get(target))))
                {
                    t.State = TowerState.Aiming;
                    t.Cooldown = 0f;
                    continue;
                }
                Fire(t, target);
                t.Cooldown = t.Def.interval;
                t.State = TowerState.Reloading;
            }

            MoveProjectiles(dt);
        }

        // 표현 단계 (GameLoop 7단계, 모든 상태에서): 포탑 회전과 반동.
        //   Idle이면 제자리, Aiming·Reloading이면 고정 대상 쪽으로 돌린다.
        public void TickViews(float dt)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                var t = placed[i];
                if (t.Aim == null)
                    continue;
                t.Aim.TickRecoil(dt);
                if (t.State != TowerState.Idle && monsters.IsValid(t.Locked))
                    t.Aim.Aim(monsters.GetAimPoint(monsters.Get(t.Locked)), dt);
            }
        }

        // 층 전환·게임오버: 날던 투사체 전부 풀 반납, 모든 타워의 고정 대상 해제 (DamageDesign 6절).
        public void ClearCombat()
        {
            for (int i = flying.Count - 1; i >= 0; i--)
                projectilePools[flying[i].Kind].Release(flying[i]);
            flying.Clear();
            for (int i = 0; i < placed.Count; i++)
            {
                placed[i].Locked = MonsterHandle.None;
                placed[i].State = TowerState.Idle;
            }
        }

        // 재장전이 끝날 때마다: 타워의 lockMode에 따라 고정 유지 또는 새로 선택 (DamageDesign 3-0 그대로).
        MonsterHandle GetTarget(TowerInstance t)
        {
            switch (t.Def.lockMode)
            {
                case TargetLock.Lock:                         // 기본값: 대상 고정
                    if (IsLockValid(t)) return t.Locked;
                    break;
                case TargetLock.PreferType:                   // 고정하되 더 높은 순위 종류가 오면 교체
                    if (IsLockValid(t) && !HasHigherRankInRange(t, t.TypeRank[monsters.Get(t.Locked).Def.Index]))
                        return t.Locked;
                    break;
                case TargetLock.Retarget:                     // 매 공격마다 새로 고름
                    break;
            }
            t.Locked = SelectTarget(t);
            return t.Locked;
        }

        bool IsLockValid(TowerInstance t)
        {
            if (!monsters.IsValid(t.Locked)) return false;    // 죽어서 풀에 반납됐거나 재사용됨
            var m = monsters.Get(t.Locked);
            if (m.State != MonsterState.Alive) return false;  // 같은 프레임에 사망·출구 도착
            return t.CoversPath[m.PathIndex];                 // 사거리(정사각형) 밖으로 나갔으면 해제
        }

        bool HasHigherRankInRange(TowerInstance t, int lockedRank)
        {
            foreach (int pathIdx in t.Covered)
                foreach (var h in board.MonstersAt(pathIdx))
                {
                    var m = monsters.Get(h);
                    if (m.State == MonsterState.Alive && t.TypeRank[m.Def.Index] < lockedRank) return true;
                }
            return false;
        }

        // 새 대상: 후보를 한 번 훑으며 최고 점수를 고름 (정렬·할당 없음).
        // 종류 우선도 → 사거리 우선도(targetRule) → 경로를 더 진행한 쪽.
        MonsterHandle SelectTarget(TowerInstance t)
        {
            MonsterHandle best = MonsterHandle.None;
            int bestRank = int.MaxValue; float bestScore = float.MinValue; float bestProgress = -1f;
            foreach (int pathIdx in t.Covered)
                foreach (var h in board.MonstersAt(pathIdx))
                {
                    var m = monsters.Get(h);
                    if (m.State != MonsterState.Alive) continue;
                    int rank = t.TypeRank[m.Def.Index];
                    float score = Score(t.Def.targetRule, m);
                    bool better = rank < bestRank
                        || (rank == bestRank && score > bestScore)
                        || (rank == bestRank && score == bestScore && m.PathProgress > bestProgress);
                    if (better) { best = h; bestRank = rank; bestScore = score; bestProgress = m.PathProgress; }
                }
            return best;
        }

        static float Score(TargetRule rule, MonsterInstance m)
        {
            switch (rule)
            {
                case TargetRule.First: return m.PathProgress;      // 경로 칸 번호 + 칸 안 진행률
                case TargetRule.Last: return -m.PathProgress;
                case TargetRule.LowestHp: return -m.Hp;
                case TargetRule.HighestHp: return m.Hp;
                default: return 0f;
            }
        }

        // 발사: Projectile이면 투사체에 {핸들, 피해 값 복사본, 속도}, Hitscan이면 즉시 피해.
        void Fire(TowerInstance t, MonsterHandle target)
        {
            var info = new DamageInfo(t.Def.damage, DamageType.Normal);
            if (t.Def.attackType == AttackType.Hitscan)
            {
                monsters.ApplyDamage(target, info);
                return;
            }

            var p = projectilePools[t.Def].Get();
            p.Target = target;
            p.Info = info;
            p.Speed = t.Def.projectileSpeed;
            p.Kind = t.Def;
            p.Active = true;
            p.Age = 0f;
            if (t.Aim != null)
            {
                t.Aim.Fire(out p.Position, out p.Direction); // 총구 끝에서 포신 방향으로
                PlayMuzzleFx(t.Def, p.Position, p.Direction);
            }
            else
            {
                p.Position = board.CellToWorld(t.Cell) + Vector3.up * 0.8f;
                p.Direction = (monsters.GetAimPoint(monsters.Get(target)) - p.Position).normalized;
            }
            if (p.View != null)
            {
                p.View.SetPositionAndRotation(p.Position, ModelRotation(t.Def, p.Direction));
                p.View.gameObject.SetActive(true);
            }
            flying.Add(p);
        }

        // 유도 이동. 거리 ≤ 0.1칸이면 명중(ApplyDamage) 후 반납, 대상이 사라졌으면 피해 없이 반납 (DamageDesign 3-1).
        // 이미 쏜 투사체는 대상이 사거리 밖으로 나가도 따라간다.
        // 포신 방향으로 나간 뒤 대상 쪽으로 휜다. 회전 속도는 날수록 빨라져 맴돌지 않고 반드시 따라붙는다.
        void MoveProjectiles(float dt)
        {
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                var p = flying[i];
                var m = monsters.Get(p.Target);
                if (m == null || m.State != MonsterState.Alive)
                {
                    RemoveProjectile(i);                       // 사망·출구 도착·풀 재사용: 다른 적으로 바꾸지 않음
                    continue;
                }

                var aim = monsters.GetAimPoint(m);
                var delta = aim - p.Position;
                float dist = delta.magnitude;
                float step = p.Speed * dt;
                if (dist <= HitDistance + step)
                {
                    monsters.ApplyDamage(p.Target, p.Info);
                    RemoveProjectile(i);
                    continue;
                }

                p.Age += dt;
                // 처음 0.05초는 거의 포신 방향 그대로 나가다가(발사감), 이후 점점 세게 대상 쪽으로 휜다.
                float launch = Mathf.Clamp01(p.Age / 0.05f);
                float turn = (p.Kind.projectileTurnSpeed * Mathf.Deg2Rad) * launch * (1f + p.Age * 4f) * dt;
                p.Direction = Vector3.RotateTowards(p.Direction, delta / dist, turn, 0f);
                p.Position += p.Direction * step;
                if (p.View != null)
                    p.View.SetPositionAndRotation(p.Position, ModelRotation(p.Kind, p.Direction));
            }
        }

        // 진행 방향 → 투사체 모델 회전 (모델의 탄두 축을 진행 방향에 맞춘다).
        static Quaternion ModelRotation(TowerDef def, Vector3 direction) =>
            Quaternion.LookRotation(direction) * Quaternion.FromToRotation(def.projectileModelForward, Vector3.forward);

        void RemoveProjectile(int index)
        {
            var p = flying[index];
            int last = flying.Count - 1;
            flying[index] = flying[last];
            flying.RemoveAt(last);
            projectilePools[p.Kind].Release(p);
        }

        // --- 발사 이펙트 (총구 섬광·화염·불티·연기) ---
        // 종류별로 effectPoolPrewarm개를 앱 시작 때 만들어 두고 돌려 쓴다 (원형 버퍼: 다 쓰면 가장 오래된 것을 다시 재생).
        // 그래서 웨이브 중 Instantiate/Destroy가 없다. 재생은 파티클 시스템이 하므로 스크립트 Update도 없다.
        sealed class MuzzleFxRing
        {
            public Transform[] Roots;
            public ParticleSystem[] Systems; // 루트 시스템 (Play(true)로 자식까지)
            public int Next;
        }

        readonly Dictionary<TowerDef, MuzzleFxRing> muzzleFx = new Dictionary<TowerDef, MuzzleFxRing>();

        public int EffectViewsCreated { get; private set; }

        void PlayMuzzleFx(TowerDef def, Vector3 position, Vector3 direction)
        {
            if (!muzzleFx.TryGetValue(def, out var ring))
                return;
            int i = ring.Next;
            ring.Next = (i + 1) % ring.Roots.Length;
            ring.Roots[i].SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            var ps = ring.Systems[i];
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }

        void InitMuzzleFx()
        {
            if (viewRoot == null)
                return;
            int count = data.Config != null ? Mathf.Max(1, data.Config.performance.effectPoolPrewarm) : 16;
            foreach (var def in data.Towers)
            {
                if (def.muzzleFxPrefab == null)
                    continue;
                var ring = new MuzzleFxRing { Roots = new Transform[count], Systems = new ParticleSystem[count] };
                for (int i = 0; i < count; i++)
                {
                    var go = Object.Instantiate(def.muzzleFxPrefab, viewRoot, false);
                    go.name = def.id + "_MuzzleFx";
                    ring.Roots[i] = go.transform;
                    ring.Systems[i] = go.GetComponent<ParticleSystem>(); // 생성 때 1번만
                    EffectViewsCreated++;
                }
                muzzleFx.Add(def, ring);
            }
        }

        void InitProjectilePools()
        {
            InitMuzzleFx();
            int prewarm = data.Config != null ? data.Config.performance.projectilePoolPrewarm : 0;
            foreach (var def in data.Towers)
            {
                if (def.attackType != AttackType.Projectile)
                    continue;
                var kind = def;
                var pool = new ObjectPool<Projectile>(() => CreateProjectile(kind), null, ReleaseProjectileView, Mathf.Max(16, prewarm));
                projectilePools.Add(def, pool);
                pool.Prewarm(prewarm);
            }
            if (flying.Capacity < prewarm)
                flying.Capacity = prewarm;
        }

        Projectile CreateProjectile(TowerDef def)
        {
            var p = new Projectile { Kind = def };
            if (viewRoot != null && def.projectilePrefab != null)
            {
                var go = Object.Instantiate(def.projectilePrefab, viewRoot, false);
                go.name = def.id + "_Projectile";
                go.transform.localScale = Vector3.one * def.projectileScale;
                go.SetActive(false);
                p.View = go.transform;
                ProjectileViewsCreated++;
            }
            return p;
        }

        static void ReleaseProjectileView(Projectile p)
        {
            p.Active = false;
            p.Target = MonsterHandle.None;
            if (p.View != null)
                p.View.gameObject.SetActive(false);
        }
    }
}
