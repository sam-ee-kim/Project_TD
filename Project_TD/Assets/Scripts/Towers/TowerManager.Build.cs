using System;
using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    // 타워 건축·판매·철거, 전투, 타워 회수 목록 (GDD 8-1).
    // partial 3파일: Build(공통 + 건축·판매) / Combat(타겟팅·투사체) / Recovery(회수 목록). 맵은 수정하지 않는다.
    //
    // 타워·투사체는 종류(TowerDef)별 풀에서 꺼내 쓴다. 풀은 앱 시작 때 towerPoolPrewarm / projectilePoolPrewarm만큼 채운다.
    // 사거리 안 경로 칸과 종류 우선도 순위표는 설치 때 1번만 계산한다 (DamageDesign 3-0).
    public partial class TowerManager
    {
        public event Action<TowerChange> TowerChanged; // → Board(점유), GameUI
        public event Action<FloorItemGrant> ItemGranted; // → GameUI (층 클리어 아이템 안내)

        GameData data;
        Board board;
        MonsterManager monsters;
        PlayerState player;
        RunState run;

        Transform viewRoot; // null이면 표시 없이 동작 (단위 테스트용)
        readonly Dictionary<TowerDef, ObjectPool<TowerInstance>> towerPools = new Dictionary<TowerDef, ObjectPool<TowerInstance>>();
        readonly List<TowerInstance> placed = new List<TowerInstance>(); // 설치된 타워 (RunState.Towers와 같은 목록)
        readonly List<TowerInstance> demolishing = new List<TowerInstance>(); // 철거 연출 중 (게임에서는 이미 빠짐, 끝나면 풀로)
        readonly Dictionary<TowerDef, TowerViewMerger.Template> mergedViews = new Dictionary<TowerDef, TowerViewMerger.Template>(); // 종류별 합친 메시 (공유)

        public IReadOnlyList<TowerInstance> Towers => placed;
        public int TowerViewsCreated { get; private set; }

        public void Init(GameData data, Board board, MonsterManager monsters, PlayerState player, Transform viewRoot = null)
        {
            this.data = data;
            this.board = board;
            this.monsters = monsters;
            this.player = player;
            this.viewRoot = viewRoot;

            var perf = data.Config != null ? data.Config.performance : null;
            foreach (var def in data.Towers)
            {
                var kind = def;
                var pool = new ObjectPool<TowerInstance>(() => CreateTower(kind), null, ReleaseTowerView, perf != null ? perf.towerPoolPrewarm : 16);
                towerPools.Add(def, pool);
                if (perf != null)
                    pool.Prewarm(perf.towerPoolPrewarm);
            }
            InitProjectilePools();
        }

        // 새 판: 타워·투사체를 모두 풀에 반납, 회수 목록 비우기. 매니저·풀은 다시 만들지 않는다.
        // (Board 점유는 Board.ResetForNewRun이 비운다.)
        public void ResetForNewRun(RunState run)
        {
            for (int i = 0; i < demolishing.Count; i++)
                towerPools[demolishing[i].Def].Release(demolishing[i]);
            demolishing.Clear();
            ClearCombat();
            for (int i = placed.Count - 1; i >= 0; i--)
                towerPools[placed[i].Def].Release(placed[i]);
            placed.Clear();
            this.run = run;
            run.Towers.Clear();
            ResetRecovery(); // 회수 목록은 새 RunState에 비어 있다 (이전 판 목록은 버려짐)
        }

        // 이 칸에 이 타워를 지을 수 있는지 (부지·점유·캐시). 화면은 이 결과로 [설치] 활성 여부를 정한다.
        public bool CanBuild(TowerDef def, Vector2Int cell) =>
            def != null && board.IsBuildable(cell) && player.CanAfford(def.buildCost);

        // 건축: 부지 확인 → 캐시 차감 → 풀에서 타워 → 사거리 칸·순위표 계산 → TowerChanged(Board 점유).
        public bool Build(Vector2Int cell, TowerDef def)
        {
            if (!CanBuild(def, cell) || !player.Spend(def.buildCost, CashReason.Build))
                return false;
            Place(cell, def);
            return true;
        }

        // 판매: 타워만 풀에 반납 (날아가던 투사체는 그대로), 점유 해제, 건축 비용 × sellRefundRate 반환.
        public bool Sell(TowerInstance tower)
        {
            if (tower == null || !placed.Remove(tower))
                return false;
            run.Towers.Remove(tower);
            var def = tower.Def;
            var cell = tower.Cell;
            Retire(tower);
            TowerChanged?.Invoke(new TowerChange(cell, def, false));
            player.Earn(GetSellRefund(def), CashReason.Sell);
            return true;
        }

        // 이동 (드래그): 빈 부지로 옮긴다. 캐시 변화 없음, 재장전 시간은 유지하고 고정 대상만 푼다.
        // 사거리 칸은 새 위치로 다시 계산한다. Board 점유는 TowerChanged(옛 칸 해제 → 새 칸 점유)로 바뀐다.
        public bool Move(TowerInstance tower, Vector2Int cell)
        {
            if (tower == null || !placed.Contains(tower) || cell == tower.Cell || !board.IsBuildable(cell))
                return false;
            var from = tower.Cell;
            TowerChanged?.Invoke(new TowerChange(from, tower.Def, false));

            tower.Cell = cell;
            tower.Locked = MonsterHandle.None;
            if (tower.State == TowerState.Aiming)
                tower.State = TowerState.Idle;
            BuildRangeCache(tower);
            if (tower.View != null)
            {
                tower.View.localPosition = board.CellToWorld(cell);
                if (tower.Aim != null)
                    tower.Aim.ResetPose();
            }

            TowerChanged?.Invoke(new TowerChange(cell, tower.Def, true));
            return true;
        }

        // ---------- 테크트리 (진화) ----------

        // 다음 진화 단계 (테크트리가 꺼져 있거나 마지막 단계면 null).
        public TowerDef GetUpgrade(TowerDef def)
        {
            if (def == null || data.Config == null || !data.Config.upgradeEnabled || def.upgradeTo == null || def.upgradeTo.Length == 0)
                return null;
            return data.TryGetTower(def.upgradeTo[0], out var next) ? next : null;
        }

        // 진화 비용 = 다음 단계 누적 비용 − 지금 단계 누적 비용.
        public int GetUpgradeCost(TowerDef from, TowerDef to) => Mathf.Max(0, to.buildCost - from.buildCost);

        // 이번 층에서 진화할 수 있는 단계인지 (해금 층).
        public bool IsUpgradeUnlocked(TowerDef to) => to != null && run != null && run.Floor >= to.unlockFloor;

        // 진화: 같은 칸에서 다음 단계 타워로 바꾼다 (차액만 낸다). 새 타워는 건축 연출을 거쳐 공격을 시작한다.
        public bool Upgrade(TowerInstance tower)
        {
            if (tower == null || !placed.Contains(tower) || tower.State == TowerState.Building)
                return false;
            var from = tower.Def;
            var next = GetUpgrade(from);
            if (next == null || !IsUpgradeUnlocked(next) || !player.Spend(GetUpgradeCost(from, next), CashReason.Upgrade))
                return false;
            var cell = tower.Cell;
            placed.Remove(tower);
            run.Towers.Remove(tower);
            towerPools[from].Release(tower); // 옛 모델은 바로 치우고 새 모델이 조립된다
            TowerChanged?.Invoke(new TowerChange(cell, from, false));
            Place(cell, next);
            return true;
        }

        // ---------- 층 클리어 아이템 ----------

        // 층을 클리어할 때 1번 (마지막 층 제외): 홀수 층 = 화력, 짝수 층 = 연사.
        void GrantFloorItem(int clearedFloor)
        {
            var items = data.Config != null ? data.Config.floorItems : null;
            if (items == null || !items.enabled)
                return;
            bool damage = clearedFloor % 2 == 1;
            float bonus = damage ? items.damagePerItem : items.speedPerItem;
            if (damage)
                run.ItemDamageMul += bonus;
            else
                run.ItemSpeedMul += bonus;
            run.ItemCount++;
            ItemGranted?.Invoke(new FloorItemGrant(clearedFloor, damage, bonus, run.ItemDamageMul, run.ItemSpeedMul));
        }

        // 아이템을 반영한 1발 피해·공격 간격.
        public float GetDamage(TowerDef def) => def.damage * (run != null ? run.ItemDamageMul : 1f);
        public float GetInterval(TowerDef def) => def.interval / (run != null ? run.ItemSpeedMul : 1f);

        // 이 칸의 타워 (없으면 null).
        public TowerInstance GetTowerAt(Vector2Int cell)
        {
            for (int i = 0; i < placed.Count; i++)
                if (placed[i].Cell == cell)
                    return placed[i];
            return null;
        }

        // 판매 시 돌려받는 캐시 (팝업 표시용): 건축 비용 × sellRefundRate, 반올림.
        public int GetSellRefund(TowerDef def) => Mathf.RoundToInt(def.buildCost * data.Config.sellRefundRate);

        // 풀에서 꺼내 칸에 놓는다 (캐시 처리는 호출한 쪽). 재건축(작업 9)도 이 경로를 쓴다.
        TowerInstance Place(Vector2Int cell, TowerDef def)
        {
            var t = towerPools[def].Get();
            t.Def = def;
            t.Cell = cell;
            t.Cooldown = 0f;
            t.BurstLeft = 0;
            t.ChargeTime = 0f;
            t.State = TowerState.Idle;
            t.Locked = MonsterHandle.None;
            BuildRangeCache(t);

            if (t.View != null)
            {
                t.View.localPosition = board.CellToWorld(cell); // 모델 바닥 = 타일 윗면
                t.View.gameObject.SetActive(true);
                if (t.Aim != null)
                    t.Aim.ResetPose();
                if (t.BuildAnim != null)
                {
                    // 건축 연출: 부품이 모여 조립된 뒤에 공격을 시작한다 (TickViews가 진행)
                    t.State = TowerState.Building;
                    t.BuildTime = 0f;
                    t.Parts?.SetMerged(false);
                    t.BuildAnim.Sample(0f);
                }
            }

            placed.Add(t);
            run.Towers.Add(t);
            TowerChanged?.Invoke(new TowerChange(cell, def, true));
            return t;
        }

        // 설치 때 1번: 정사각형 사거리 안 경로 칸 + 칸 번호별 포함 여부 + 몬스터 종류별 순위표 (DamageDesign 3-0).
        void BuildRangeCache(TowerInstance t)
        {
            int range = t.Def.range;
            int n = board.N;
            // 부채꼴 타워: 불길이 닿을 수 있는 원 (칸 중심 거리 ≤ 길이 + 반 칸). 실제 피해는 발사 때 부채꼴로 다시 판정
            // 전자파 타워: 파동이 닿는 원 (칸 중심 거리 ≤ 반지름 + 반 칸)
            bool cone = t.Def.attackType == AttackType.Cone || t.Def.attackType == AttackType.Pulse;
            float reach = t.Def.attackType == AttackType.Pulse ? t.Def.pulseRadius : t.Def.coneLength;
            float coneReachSq = (reach + 0.5f) * (reach + 0.5f);

            var covered = new List<int>();
            t.CoversCell = new bool[n * n];
            // 모든 스타트 지점 경로의 칸 (합류 뒤 겹치는 칸은 1번만)
            for (int pathId = 0; pathId < board.PathCount; pathId++)
            {
                int pathLength = board.GetPathLength(pathId);
                for (int i = 0; i < pathLength; i++)
                {
                    var c = board.GetPathCell(pathId, i);
                    int dx = c.x - t.Cell.x, dy = c.y - t.Cell.y;
                    bool inRange = cone
                        ? dx * dx + dy * dy <= coneReachSq
                        : Math.Max(Math.Abs(dx), Math.Abs(dy)) <= range; // 정사각형, 원형 아님
                    int id = board.CellId(c);
                    if (inRange && !t.CoversCell[id])
                    {
                        covered.Add(id);
                        t.CoversCell[id] = true;
                    }
                }
            }
            t.Covered = covered.ToArray();
            t.TypeRank = BuildTypeRank(t.Def.typePriority);
        }

        // 몬스터 Def.Index → 순위. typePriority 항목은 몬스터 id 또는 분류 이름("Normal"/"Special"),
        // 앞에 있을수록 먼저(작은 순위). 어느 항목에도 안 맞으면 맨 뒤.
        int[] BuildTypeRank(string[] typePriority)
        {
            var defs = data.Monsters;
            var rank = new int[defs.Count];
            int last = typePriority != null ? typePriority.Length : 0;
            for (int m = 0; m < defs.Count; m++)
            {
                rank[m] = last;
                for (int p = 0; p < last; p++)
                {
                    string entry = typePriority[p];
                    bool matches = entry == defs[m].id
                        || (Enum.TryParse(entry, out MonsterCategory category) && category == defs[m].category);
                    if (matches)
                    {
                        rank[m] = p;
                        break;
                    }
                }
            }
            return rank;
        }

        TowerInstance CreateTower(TowerDef def)
        {
            var t = new TowerInstance { Def = def };
            if (viewRoot != null && def.viewPrefab != null)
            {
                var go = UnityEngine.Object.Instantiate(def.viewPrefab, viewRoot, false);
                go.name = def.id;
                go.transform.localScale = Vector3.one * def.viewScale;
                t.View = go.transform;
                t.Aim = go.GetComponent<TowerAim>(); // 생성 때 1번만
                if (def.buildClip != null)
                    t.BuildAnim = new TowerBuildAnim(go, def.buildClip); // 기본 자세를 합치기 전에 저장
                if (def.chargeClip != null)
                    t.ChargeAnim = new TowerBuildAnim(go, def.chargeClip, t.Aim != null ? t.Aim.animatedParts : null);
                if (def.fireClip != null)
                    t.FireAnim = new TowerBuildAnim(go, def.fireClip, t.Aim != null ? t.Aim.animatedParts : null);
                t.Parts = MergeView(def, go, t.Aim);
                go.SetActive(false);
                TowerViewsCreated++;
            }
            return t;
        }

        // 움직이는 부품 단위로 메시를 합쳐 렌더러 수를 줄인다 (종류별 템플릿은 처음 1번만 만든다).
        TowerViewMerger.Parts MergeView(TowerDef def, GameObject view, TowerAim aim)
        {
            if (!mergedViews.TryGetValue(def, out var template))
            {
                template = TowerViewMerger.Build(view, aim); // 합칠 수 없으면 null → 원래 렌더러 그대로
                mergedViews.Add(def, template);
            }
            return template != null ? TowerViewMerger.Apply(view, template) : null;
        }

        // 게임에서 빠진 타워 (판매·회수): 철거 연출이 있으면 거꾸로 재생한 뒤 풀로, 없으면 바로 풀로.
        // 건축 도중이면 그 자리에서 거꾸로 돌아간다.
        void Retire(TowerInstance tower)
        {
            if (tower.BuildAnim == null || tower.View == null || !tower.View.gameObject.activeInHierarchy)
            {
                towerPools[tower.Def].Release(tower);
                return;
            }
            if (tower.State != TowerState.Building)
            {
                tower.BuildTime = tower.BuildAnim.Length;
                if (tower.Aim != null)
                    tower.Aim.ResetPose(); // 포탑을 정면으로 돌린 자세에서 분해
            }
            tower.State = TowerState.Idle;
            tower.Locked = MonsterHandle.None;
            tower.Parts?.SetMerged(false);
            demolishing.Add(tower);
        }

        static void ReleaseTowerView(TowerInstance t)
        {
            t.Locked = MonsterHandle.None;
            t.BuildTime = 0f;
            t.ChargeTime = 0f;
            t.ChargePosed = false;
            t.ChargeAnim?.Restore();
            t.FireAnimTime = -1f;
            t.FireAnim?.Restore();
            t.BuildAnim?.Restore();      // 다음에 꺼낼 때 기본 자세에서 시작
            t.Parts?.SetMerged(true);
            if (t.View != null)
                t.View.gameObject.SetActive(false);
        }
    }
}
