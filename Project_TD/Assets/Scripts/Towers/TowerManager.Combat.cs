using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    // TowerManager 전투: 경로 칸 기반 타겟팅, 투사체 (DamageDesign 3장).
    // 재장전이 끝난 타워만 사거리 안 경로 칸의 몬스터를 본다. 전체 몬스터를 훑지 않고, 넥서스 근처 예외 규칙도 없다.
    public partial class TowerManager
    {
        const float HitDistance = 0.1f; // 명중 판정 거리 (칸)
        public const float ConeHitRadius = 0.25f; // 부채꼴 히트박스: 몬스터 몸통 반지름만큼 여유 (가장자리에 걸치면 맞음)

        readonly Dictionary<TowerDef, ObjectPool<Projectile>> projectilePools = new Dictionary<TowerDef, ObjectPool<Projectile>>();
        readonly List<Projectile> flying = new List<Projectile>();

        public int ProjectileCount => flying.Count;
        public int ProjectileViewsCreated { get; private set; }

        // GameLoop 4단계: 타워 상태머신(대기 → 조준 → 발사 → 재장전)과 투사체 이동·명중.
        //   Idle      : 대상 없음. 매 프레임 사거리 안 경로 칸에서 대상을 찾는다
        //   Aiming    : 대상을 잡았고 포탑이 그쪽으로 도는 중. 조준이 맞으면 발사 → Reloading
        //   Reloading : 발사 후 재장전(cooldown). 끝나면 lockMode 규칙으로 대상을 다시 확인 → Aiming / Idle
        //   Charging  : (chargeTime > 0인 타워) 조준이 맞으면 차지 → 끝나면 발사. 차지 중 대상이 사라지면 사거리 안 다른 대상, 없으면 Idle
        // 대상 선택 규칙(DamageDesign 3-0)은 그대로이고, 조준을 기다리는 동안 발사 시점만 늦춘다.
        public void TickCombat(float dt)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                var t = placed[i];
                if (t.State == TowerState.Building)
                    continue;                     // 건축 연출이 끝나야 공격 (TickViews가 끝낸다)
                if (t.Def.spinUpTime > 0f)
                    TickSpinUp(t, dt);
                t.Cooldown -= dt;
                if (t.BurstLeft > 0)
                    TickBurst(t, dt);
                if (t.State == TowerState.Charging)
                {
                    TickCharge(t, dt);
                    continue;
                }
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
                if (t.Def.chargeTime > 0f)
                {
                    // 차지 시작: 발사점에서 전기가 모인다. 발사는 TickCharge가 한다
                    t.State = TowerState.Charging;
                    t.ChargeTime = 0f;
                    if (t.FireAnim != null)
                        t.FireAnimTime = 0f; // 발사 연출이 차지부터 시작하는 타워 (EMP: 모았다가 0.42초에 방출)
                    if (t.Aim != null && t.Aim.firePoints != null && t.Aim.firePoints.Length > 0)
                        PlayFx(chargeFx, t.Def, t.Aim.firePoints[0].position, Vector3.up);
                    continue;
                }
                Fire(t, target);
                t.Cooldown = GetInterval(t); // 연사 아이템·예열 반영
                t.State = TowerState.Reloading;
                t.BurstLeft = t.Def.burstCount - 1; // 이점사: 남은 1발은 burstGap 뒤
                t.BurstTimer = t.Def.burstGap;
            }

            MoveProjectiles(dt);
            TickPulses(dt);
        }

        // --- 전자파 (Pulse) ---
        // 퍼지는 파동 하나 = 중심·현재 반지름. 매 틱 (이전 반지름, 지금 반지름] 사이에 몸이 걸친 몬스터에게 피해 → 파면이 지나갈 때 1번.
        // 대상 후보는 쏜 타워가 설치 때 계산한 범위 칸만 본다 (타워가 팔려도 배열은 그대로 남는다). 고정 크기 배열이라 할당 없음.
        const int MaxPulses = 64;
        readonly Vector3[] pulseCenter = new Vector3[MaxPulses];
        readonly float[] pulseRadius = new float[MaxPulses];
        readonly float[] pulseMax = new float[MaxPulses];
        readonly float[] pulseSpeed = new float[MaxPulses];
        readonly DamageInfo[] pulseDamage = new DamageInfo[MaxPulses];
        readonly int[][] pulseCovered = new int[MaxPulses][];
        readonly TowerDef[] pulseDef = new TowerDef[MaxPulses];
        int pulseCount;

        public int PulseCount => pulseCount;

        void StartPulse(TowerInstance t, in DamageInfo info)
        {
            if (pulseCount >= MaxPulses)
                return;
            int i = pulseCount++;
            pulseCenter[i] = board.CellToWorld(t.Cell);
            pulseRadius[i] = -1f; // 첫 틱에 타워 바로 옆 몬스터도 맞게
            pulseMax[i] = t.Def.pulseRadius;
            pulseSpeed[i] = Mathf.Max(0.1f, t.Def.pulseSpeed);
            pulseDamage[i] = info;
            pulseCovered[i] = t.Covered;
            pulseDef[i] = t.Def;
        }

        void TickPulses(float dt)
        {
            for (int i = pulseCount - 1; i >= 0; i--)
            {
                float from = pulseRadius[i];
                float to = Mathf.Max(0f, from) + pulseSpeed[i] * dt;
                if (to > pulseMax[i]) to = pulseMax[i];
                pulseRadius[i] = to;
                var center = pulseCenter[i];
                var covered = pulseCovered[i];
                for (int c = 0; c < covered.Length; c++)
                {
                    var bucket = board.MonstersAt(covered[c]);
                    for (int k = bucket.Count - 1; k >= 0; k--)
                    {
                        if (k >= bucket.Count)
                            continue;
                        var h = bucket[k];
                        var m = monsters.Get(h);
                        if (m == null || m.State != MonsterState.Alive)
                            continue;
                        var p = monsters.GetGroundPoint(m);
                        float dx = p.x - center.x, dz = p.z - center.z;
                        float d = Mathf.Max(0f, Mathf.Sqrt(dx * dx + dz * dz) - ConeHitRadius); // 몸 가장자리에 닿으면
                        if (d > from && d <= to)
                        {
                            PlayFx(hitFx, pulseDef[i], monsters.GetAimPoint(m), Vector3.up);
                            monsters.ApplyDamage(h, pulseDamage[i]);
                        }
                    }
                }
                if (to >= pulseMax[i])
                {
                    int last = --pulseCount; // 다 퍼진 파동은 맨 뒤와 자리를 바꿔 뺀다
                    pulseCenter[i] = pulseCenter[last];
                    pulseRadius[i] = pulseRadius[last];
                    pulseMax[i] = pulseMax[last];
                    pulseSpeed[i] = pulseSpeed[last];
                    pulseDamage[i] = pulseDamage[last];
                    pulseCovered[i] = pulseCovered[last];
                    pulseDef[i] = pulseDef[last];
                    pulseCovered[last] = null;
                    pulseDef[last] = null;
                }
            }
        }

        // 예열 (미니건): 대상을 잡고 있는 동안 spinUpTime에 걸쳐 0 → 1, 대상이 없으면 그 절반 시간에 걸쳐 식는다.
        void TickSpinUp(TowerInstance t, float dt)
        {
            bool firing = t.State != TowerState.Idle && !t.Locked.IsNone;
            float rate = 1f / t.Def.spinUpTime;
            t.SpinUp = Mathf.Clamp01(t.SpinUp + (firing ? rate : -2f * rate) * dt);
        }

        // 차지 진행: 대상이 사라지면 사거리 안 다른 대상으로 이어 가고(차지 유지), 없으면 Idle. 다 차면 발사.
        void TickCharge(TowerInstance t, float dt)
        {
            t.ChargeTime += dt;
            var target = IsLockValid(t) ? t.Locked : (t.Locked = SelectTarget(t));
            if (target.IsNone)
            {
                t.State = TowerState.Idle;
                t.ChargeTime = 0f;
                t.Cooldown = 0f;
                return;
            }
            if (t.ChargeTime < t.Def.chargeTime)
                return;
            Fire(t, target);
            t.Cooldown = GetInterval(t.Def);
            t.State = TowerState.Reloading;
            t.ChargeTime = 0f;
            t.BurstLeft = t.Def.burstCount - 1;
            t.BurstTimer = t.Def.burstGap;
        }

        // 연발 (이점사): 같은 대상에게 burstGap마다 1발씩 (발마다 피해 damage, 따로 명중).
        // 대상이 죽었거나 사거리를 벗어났으면 남은 발은 쏘지 않는다.
        void TickBurst(TowerInstance t, float dt)
        {
            t.BurstTimer -= dt;
            while (t.BurstLeft > 0 && t.BurstTimer <= 0f)
            {
                if (!IsLockValid(t))
                {
                    t.BurstLeft = 0;
                    return;
                }
                Fire(t, t.Locked);
                t.BurstLeft--;
                t.BurstTimer += t.Def.burstGap;
            }
        }

        // 표현 단계 (GameLoop 7단계, 모든 상태에서): 포탑 회전과 반동.
        //   Idle이면 제자리, Aiming·Reloading이면 고정 대상 쪽으로 돌린다.
        public void TickViews(float dt)
        {
            TickDemolish(dt);
            TickFieldViews(dt);
            for (int i = 0; i < placed.Count; i++)
            {
                var t = placed[i];
                if (t.State == TowerState.Building)
                {
                    TickBuild(t, dt);
                    continue;
                }
                if (t.ChargeAnim != null)
                {
                    // 차지 연출: 차지 중이면 진행 시점의 자세, 끝나면 차지 부품만 기본 자세로
                    if (t.State == TowerState.Charging)
                    {
                        t.ChargeAnim.Sample(t.ChargeTime);
                        t.ChargePosed = true;
                    }
                    else if (t.ChargePosed)
                    {
                        t.ChargeAnim.Restore();
                        t.ChargePosed = false;
                    }
                }
                if (t.SpinUpAnim != null || t.SpinLoopAnim != null)
                {
                    // 예열 연출: 예열 중이면 예열 클립을 예열 정도에 맞춰, 다 돌면 반복 클립. 식으면 기본 자세로
                    if (t.SpinUp > 0f)
                    {
                        if (t.SpinUp < 1f || t.SpinLoopAnim == null)
                        {
                            t.SpinUpAnim?.Sample(t.SpinUp * t.SpinUpAnim.Length);
                            t.SpinLoopTime = 0f;
                        }
                        else
                        {
                            t.SpinLoopTime += dt;
                            if (t.SpinLoopTime >= t.SpinLoopAnim.Length)
                                t.SpinLoopTime -= t.SpinLoopAnim.Length;
                            t.SpinLoopAnim.Sample(t.SpinLoopTime);
                        }
                        t.SpinPosed = true;
                    }
                    else if (t.SpinPosed)
                    {
                        (t.SpinLoopAnim ?? t.SpinUpAnim).Restore();
                        t.SpinPosed = false;
                    }
                }
                if (t.FireAnim != null && t.FireAnimTime >= 0f)
                {
                    // 발사 연출 (나노: 발사구 셀이 차례로 열리고 발사대가 밀린다)
                    t.FireAnimTime += dt;
                    if (t.FireAnimTime < t.FireAnim.Length)
                        t.FireAnim.Sample(t.FireAnimTime);
                    else
                    {
                        t.FireAnim.Restore();
                        t.FireAnimTime = -1f;
                    }
                }
                if (t.Aim == null)
                    continue;
                t.Aim.TickRecoil(dt);
                if (t.State != TowerState.Idle && monsters.IsValid(t.Locked))
                    t.Aim.Aim(monsters.GetAimPoint(monsters.Get(t.Locked)), dt);
            }
        }

        // --- 나노필드 표시 (감속 판정은 MonsterManager) ---
        // 종류별로 effectPoolPrewarm개를 미리 만들어 원형 버퍼로 돌려 쓴다. 유지 시간이 지나면 끈다.
        sealed class FieldViewRing
        {
            public Transform[] Roots;
            public float[] End;
            public int Next;
        }

        readonly Dictionary<TowerDef, FieldViewRing> fieldViews = new Dictionary<TowerDef, FieldViewRing>();
        float viewTime;

        void InitFieldViews(int count)
        {
            foreach (var def in data.Towers)
            {
                if (def.fieldFxPrefab == null || def.fieldRadius <= 0f)
                    continue;
                var ring = new FieldViewRing { Roots = new Transform[count], End = new float[count] };
                float scale = def.fieldFxRadius > 0f ? def.fieldRadius / def.fieldFxRadius : 1f;
                for (int i = 0; i < count; i++)
                {
                    var go = Object.Instantiate(def.fieldFxPrefab, viewRoot, false);
                    go.name = def.id + "_Field";
                    go.transform.localScale = Vector3.one * scale;
                    go.SetActive(false);
                    ring.Roots[i] = go.transform;
                    EffectViewsCreated++;
                }
                fieldViews.Add(def, ring);
            }
        }

        // 첫 발이 명중한 자리: 감속 영역(MonsterManager) + 표시.
        void SpawnField(TowerDef def, Vector3 ground)
        {
            ground.y = 0f;
            monsters.AddSlowField(ground, def.fieldRadius, def.fieldSlow, def.fieldDuration);
            if (!fieldViews.TryGetValue(def, out var ring))
                return;
            int i = ring.Next;
            ring.Next = (i + 1) % ring.Roots.Length;
            var root = ring.Roots[i];
            root.position = ground;
            root.gameObject.SetActive(false); // 다시 켜서 애니메이션을 처음부터
            root.gameObject.SetActive(true);
            ring.End[i] = viewTime + def.fieldDuration;
        }

        void TickFieldViews(float dt)
        {
            viewTime += dt;
            foreach (var ring in fieldViews.Values)
                for (int i = 0; i < ring.Roots.Length; i++)
                    if (ring.End[i] > 0f && ring.End[i] <= viewTime)
                    {
                        ring.End[i] = 0f;
                        ring.Roots[i].gameObject.SetActive(false);
                    }
        }

        // 층 전환·새 판: 깔린 필드 표시를 모두 끈다.
        void ClearFieldViews()
        {
            foreach (var ring in fieldViews.Values)
                for (int i = 0; i < ring.Roots.Length; i++)
                {
                    ring.End[i] = 0f;
                    ring.Roots[i].gameObject.SetActive(false);
                }
        }

        // 건축 연출 진행. 끝나면 기본 자세·합친 메시로 돌아가 대기(Idle) → 다음 프레임부터 대상을 찾는다.
        void TickBuild(TowerInstance t, float dt)
        {
            t.BuildTime += dt;
            if (t.BuildTime < t.BuildAnim.Length)
            {
                t.BuildAnim.Sample(t.BuildTime);
                return;
            }
            t.BuildAnim.Restore();
            t.Parts?.SetMerged(true);
            if (t.Aim != null)
                t.Aim.ResetPose();
            t.Cooldown = 0f;
            t.State = TowerState.Idle;
        }

        // 철거 연출 (건축을 거꾸로). 끝나면 풀에 반납.
        void TickDemolish(float dt)
        {
            for (int i = demolishing.Count - 1; i >= 0; i--)
            {
                var t = demolishing[i];
                t.BuildTime -= dt;
                if (t.BuildTime > 0f)
                {
                    t.BuildAnim.Sample(t.BuildTime);
                    continue;
                }
                int last = demolishing.Count - 1;
                demolishing[i] = demolishing[last];
                demolishing.RemoveAt(last);
                towerPools[t.Def].Release(t);
            }
        }

        // 층 전환·게임오버: 날던 투사체 전부 풀 반납, 모든 타워의 고정 대상 해제 (DamageDesign 6절).
        public void ClearCombat()
        {
            for (int i = flying.Count - 1; i >= 0; i--)
                projectilePools[flying[i].Kind].Release(flying[i]);
            flying.Clear();
            ClearFieldViews();
            for (int i = 0; i < pulseCount; i++)
            {
                pulseCovered[i] = null;
                pulseDef[i] = null;
            }
            pulseCount = 0;
            for (int i = 0; i < placed.Count; i++)
            {
                placed[i].Locked = MonsterHandle.None;
                placed[i].BurstLeft = 0;
                if (placed[i].State != TowerState.Building) // 조립 중인 타워는 연출을 이어 간다
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
            return t.CoversCell[m.CellId];                    // 사거리 밖으로 나갔으면 해제
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
        // 종류 우선도 → 사거리 우선도(targetRule) → 출구에 더 가까운 쪽.
        MonsterHandle SelectTarget(TowerInstance t)
        {
            MonsterHandle best = MonsterHandle.None;
            int bestRank = int.MaxValue; float bestScore = float.MinValue; float bestProgress = float.MinValue;
            foreach (int pathIdx in t.Covered)
                foreach (var h in board.MonstersAt(pathIdx))
                {
                    var m = monsters.Get(h);
                    if (m.State != MonsterState.Alive) continue;
                    int rank = t.TypeRank[m.Def.Index];
                    float score = Score(t.Def.targetRule, m);
                    bool better = rank < bestRank
                        || (rank == bestRank && score > bestScore)
                        || (rank == bestRank && score == bestScore && -m.Remaining > bestProgress);
                    if (better) { best = h; bestRank = rank; bestScore = score; bestProgress = -m.Remaining; }
                }
            return best;
        }

        static float Score(TargetRule rule, MonsterInstance m)
        {
            switch (rule)
            {
                case TargetRule.First: return -m.Remaining;        // 출구까지 가장 적게 남은 몬스터 (경로가 달라도 비교)
                case TargetRule.Last: return m.Remaining;
                case TargetRule.LowestHp: return -m.Hp;
                case TargetRule.HighestHp: return m.Hp;
                default: return 0f;
            }
        }

        // 발사: Projectile이면 투사체에 {핸들, 피해 값 복사본, 속도}, Hitscan이면 즉시 피해.
        void Fire(TowerInstance t, MonsterHandle target)
        {
            var info = new DamageInfo(GetDamage(t.Def), DamageType.Normal, t.Def.bonusTrait, t.Def.bonusMultiplier); // 화력 아이템 반영, 속성 상성
            if (t.Def.attackType == AttackType.Pulse)
            {
                // 전자파 방출: 타워 발밑에서 원형으로 퍼진다 (이펙트도 바닥 가까이)
                if (t.FireAnim != null && t.FireAnimTime < 0f)
                    t.FireAnimTime = 0f;
                StartPulse(t, info);
                PlayMuzzleFx(t.Def, board.CellToWorld(t.Cell) + Vector3.up * 0.4f, Vector3.forward); // 풀·흙 위로 살짝 띄워 가려지지 않게
                return;
            }
            if (t.Def.attackType == AttackType.Cone)
            {
                // 부채꼴 범위: 타워 중심에서 대상 쪽(바닥 평면)으로 히트박스. 이펙트는 노즐에서 같은 방향으로 뿜는다
                var center = board.CellToWorld(t.Cell);
                var toTarget = monsters.GetGroundPoint(monsters.Get(target)) - center;
                toTarget.y = 0f;
                var forward = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : Vector3.forward;
                if (t.Aim != null)
                {
                    t.Aim.Fire(out var origin, out var direction);
                    PlayMuzzleFx(t.Def, origin, direction);
                }
                ApplyCone(t, center, forward, info);
                return;
            }
            if (t.Def.attackType == AttackType.Hitscan)
            {
                // 즉시 명중 (화염방사 등): 피해는 바로, 표현은 노즐에서 대상 쪽으로 뿜는 이펙트
                if (t.Aim != null)
                {
                    t.Aim.Fire(out var origin, out var direction);
                    PlayMuzzleFx(t.Def, origin, direction);
                }
                monsters.ApplyDamage(target, info);
                return;
            }

            if (t.FireAnim != null && t.BurstLeft == 0 && t.FireAnimTime < 0f)
                t.FireAnimTime = 0f; // 공격 시작: 발사 연출 (연발 둘째 발부터는 이어서, 차지 때 이미 시작했으면 그대로)
            var p = projectilePools[t.Def].Get();
            p.SpawnsField = t.Def.fieldRadius > 0f && t.BurstLeft == 0; // 연발의 첫 발만 필드를 깐다
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
                if (p.Fx != null)
                {
                    p.Fx.Clear(true); // 지난 비행의 꼬리를 지우고 발사점에서 새로
                    p.Fx.Play(true);
                }
            }
            flying.Add(p);
        }

        // 부채꼴 히트박스 안의 살아 있는 몬스터 모두에게 피해. 후보는 설치 때 계산한 범위 칸의 몬스터만 본다.
        // 죽은 몬스터는 그 칸 목록에서 바로 빠지므로(맨 뒤와 자리 바꿈) 칸 목록은 뒤에서부터 돈다.
        void ApplyCone(TowerInstance t, Vector3 center, Vector3 forward, in DamageInfo info)
        {
            float length = t.Def.coneLength;
            float halfAngle = t.Def.coneAngle * 0.5f;
            var covered = t.Covered;
            for (int c = 0; c < covered.Length; c++)
            {
                var bucket = board.MonstersAt(covered[c]);
                for (int k = bucket.Count - 1; k >= 0; k--)
                {
                    if (k >= bucket.Count)
                        continue;
                    var h = bucket[k];
                    var m = monsters.Get(h);
                    if (m == null || m.State != MonsterState.Alive)
                        continue;
                    if (InCone(monsters.GetGroundPoint(m) - center, forward, length, halfAngle, ConeHitRadius))
                        monsters.ApplyDamage(h, info);
                }
            }
        }

        // 바닥 평면 부채꼴 판정: 거리 ≤ length + radius, 방향과의 각도 ≤ halfAngle + (몸통이 걸치는 각도).
        public static bool InCone(Vector3 offset, Vector3 forward, float length, float halfAngleDeg, float radius)
        {
            offset.y = 0f;
            float dist = offset.magnitude;
            if (dist > length + radius)
                return false;
            if (dist <= radius)
                return true; // 타워 바로 앞에 겹쳐 있음
            float cos = Vector3.Dot(offset / dist, forward);
            float allowed = halfAngleDeg + Mathf.Asin(Mathf.Min(1f, radius / dist)) * Mathf.Rad2Deg;
            return cos >= Mathf.Cos(Mathf.Min(180f, allowed) * Mathf.Deg2Rad);
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
                    PlayFx(hitFx, p.Kind, aim, delta / Mathf.Max(dist, 1e-4f)); // 명중 이펙트 (있을 때)
                    if (p.SpawnsField)
                        SpawnField(p.Kind, monsters.GetGroundPoint(m)); // 피해는 맞은 몬스터만, 필드는 감속만
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
        readonly Dictionary<TowerDef, MuzzleFxRing> chargeFx = new Dictionary<TowerDef, MuzzleFxRing>(); // 차지 (TowerDef.chargeFxPrefab)
        readonly Dictionary<TowerDef, MuzzleFxRing> hitFx = new Dictionary<TowerDef, MuzzleFxRing>();    // 명중 (TowerDef.hitFxPrefab)

        public int EffectViewsCreated { get; private set; }

        void PlayMuzzleFx(TowerDef def, Vector3 position, Vector3 direction) => PlayFx(muzzleFx, def, position, direction);

        void PlayFx(Dictionary<TowerDef, MuzzleFxRing> rings, TowerDef def, Vector3 position, Vector3 direction)
        {
            if (!rings.TryGetValue(def, out var ring))
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
                if (def.muzzleFxPrefab != null)
                {
                    // 불길 끝 = 히트박스 끝
                    float scale = def.attackType == AttackType.Cone && def.coneFxReach > 0f ? def.coneLength / def.coneFxReach : 1f;
                    muzzleFx.Add(def, CreateFxRing(def.muzzleFxPrefab, def.id + "_MuzzleFx", count, scale));
                }
                if (def.chargeFxPrefab != null)
                    chargeFx.Add(def, CreateFxRing(def.chargeFxPrefab, def.id + "_ChargeFx", count, 1f));
                if (def.hitFxPrefab != null)
                    hitFx.Add(def, CreateFxRing(def.hitFxPrefab, def.id + "_HitFx", count, 1f));
            }
        }

        MuzzleFxRing CreateFxRing(GameObject prefab, string name, int count, float scale)
        {
            var ring = new MuzzleFxRing { Roots = new Transform[count], Systems = new ParticleSystem[count] };
            for (int i = 0; i < count; i++)
            {
                var go = Object.Instantiate(prefab, viewRoot, false);
                go.name = name;
                go.transform.localScale = Vector3.one * scale;
                ring.Roots[i] = go.transform;
                ring.Systems[i] = go.GetComponent<ParticleSystem>(); // 생성 때 1번만
                EffectViewsCreated++;
            }
            return ring;
        }

        void InitProjectilePools()
        {
            InitMuzzleFx();
            if (viewRoot != null)
                InitFieldViews(data.Config != null ? Mathf.Max(1, data.Config.performance.effectPoolPrewarm) : 16);
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
                p.Fx = go.GetComponent<ParticleSystem>(); // 생성 때 1번만 (없으면 null)
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
