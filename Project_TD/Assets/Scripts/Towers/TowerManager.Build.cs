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

        GameData data;
        Board board;
        MonsterManager monsters;
        PlayerState player;
        RunState run;

        Transform viewRoot; // null이면 표시 없이 동작 (단위 테스트용)
        readonly Dictionary<TowerDef, ObjectPool<TowerInstance>> towerPools = new Dictionary<TowerDef, ObjectPool<TowerInstance>>();
        readonly List<TowerInstance> placed = new List<TowerInstance>(); // 설치된 타워 (RunState.Towers와 같은 목록)

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
            towerPools[def].Release(tower);
            TowerChanged?.Invoke(new TowerChange(cell, def, false));
            player.Earn(GetSellRefund(def), CashReason.Sell);
            return true;
        }

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
            t.State = TowerState.Idle;
            t.Locked = MonsterHandle.None;
            BuildRangeCache(t);

            if (t.View != null)
            {
                t.View.localPosition = board.CellToWorld(cell); // 모델 바닥 = 타일 윗면
                t.View.gameObject.SetActive(true);
                if (t.Aim != null)
                    t.Aim.ResetPose();
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
            int pathLength = board.GetPathLength(0);

            var covered = new List<int>();
            t.CoversPath = new bool[pathLength];
            // 큰 칸 번호(출구에 가까운 칸)부터 담는다.
            for (int i = pathLength - 1; i >= 0; i--)
            {
                var c = board.GetPathCell(0, i);
                if (Math.Max(Math.Abs(c.x - t.Cell.x), Math.Abs(c.y - t.Cell.y)) <= range) // 정사각형, 원형 아님
                {
                    covered.Add(i);
                    t.CoversPath[i] = true;
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
                go.SetActive(false);
                TowerViewsCreated++;
            }
            return t;
        }

        static void ReleaseTowerView(TowerInstance t)
        {
            t.Locked = MonsterHandle.None;
            if (t.View != null)
                t.View.gameObject.SetActive(false);
        }
    }
}
