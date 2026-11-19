using System;

namespace TowerDefence
{
    // 캐시·넥서스 체력. 캐시·체력 증감은 이 모듈 한 곳에서만 한다 (DataArchitecture 경계 4).
    // 판을 넘어 남는 공학연구소 진행(M1반도체·해금 타워, MetaProgress)도 여기서만 바꾸고 저장한다.
    // 값은 RunState에 있고(소유: PlayerState), 새 판이면 새 RunState를 받는다.
    public class PlayerState
    {
        public event Action<CashChange> CashChanged; // → GameUI (HUD, [설치] 활성화), 캐시 로그(보류)
        public event Action<HpChange> HpChanged;     // → GameUI, GameFlow
        public event Action Defeated;                // 체력 0, 판마다 1번만 → GameFlow GameOver (DamageDesign 4-2)
        public event Action<MetaChange> MetaChanged; // → GameUI (공학연구소 화면, 판 결과의 M1반도체 지급)

        GameData data;
        RunState run;
        bool isDefeated;
        MetaProgress meta = new MetaProgress();
        string metaPath;          // 비어 있으면 저장하지 않음 (테스트·시뮬레이터)
        bool chipsGranted;        // 이번 판의 M1반도체를 이미 줬는지

        public int Cash => run != null ? run.Cash : 0;
        public int NexusHp => run != null ? run.NexusHp : 0;
        public bool IsDefeated => isDefeated;
        public int M1Chips => meta.m1Chips;
        public int SmallChips => meta.smallChips;
        bool LabOn => data != null && data.Config != null && data.Config.labUnlock;
        GameConfig.FactorySettings Factory => data != null && data.Config != null ? data.Config.factory : null;
        bool FactoryOn => Factory != null && Factory.enabled;

        // ---------- 반도체 공장 ----------
        public int FactoryLevel(FactoryNode node) => FactoryOn ? meta.FactoryLevel(node) : 0;
        public int FactoryMaxLevel => Factory != null ? Factory.maxLevel : 0;
        public float FactoryBonus(FactoryNode node) => FactoryOn ? meta.FactoryLevel(node) * Factory.perLevel : 0f;
        public float DamageMul => 1f + FactoryBonus(FactoryNode.Damage);        // 모든 타워 공격력
        public float SpeedMul => 1f + FactoryBonus(FactoryNode.Speed);          // 모든 타워 공격 속도 (간격 ÷)
        public int ApplyCost(int baseCost) => Math.Max(0, (int)Math.Round(baseCost * (1f - FactoryBonus(FactoryNode.Cost)), MidpointRounding.AwayFromZero)); // 설치·진화 비용 할인

        // 다음 단계 가격 (최대 단계면 0)
        public int FactoryNextCost(FactoryNode node)
        {
            if (!FactoryOn)
                return 0;
            int level = meta.FactoryLevel(node);
            if (level >= Factory.maxLevel)
                return 0;
            var costs = Factory.levelCosts;
            return costs != null && costs.Length > 0 ? costs[Math.Min(level, costs.Length - 1)] : 1;
        }

        // [강화]: 작은 반도체가 모자라거나 최대 단계면 false
        public bool TryUpgradeFactory(FactoryNode node)
        {
            if (!FactoryOn)
                return false;
            int level = meta.FactoryLevel(node);
            int cost = FactoryNextCost(node);
            if (level >= Factory.maxLevel || meta.smallChips < cost)
                return false;
            meta.smallChips -= cost;
            meta.SetFactoryLevel(node, level + 1);
            meta.Save(metaPath);
            MetaChanged?.Invoke(new MetaChange(meta.m1Chips, 0, null, meta.smallChips, 0));
            return true;
        }

        // [초기화]: 무료. 쓴 작은 반도체를 모두 돌려받고 모든 노드를 0단계로
        public void ResetFactory()
        {
            if (!FactoryOn)
                return;
            int refund = 0;
            var costs = Factory.levelCosts;
            for (int n = 0; n < FactoryNodes.Count; n++)
            {
                int level = meta.FactoryLevel((FactoryNode)n);
                for (int l = 0; l < level; l++)
                    refund += costs != null && costs.Length > 0 ? costs[Math.Min(l, costs.Length - 1)] : 1;
                meta.SetFactoryLevel((FactoryNode)n, 0);
            }
            meta.smallChips += refund;
            meta.Save(metaPath);
            MetaChanged?.Invoke(new MetaChange(meta.m1Chips, 0, null, meta.smallChips, 0));
        }

        // 테스트용 (맵 제작 씬 넥서스 설정): 켜면 넥서스가 피해를 받지 않는다. 판이 바뀌어도 유지.
        public bool Invincible { get; set; }

        public void Init(GameData data)
        {
            this.data = data;
        }

        // 새 판: 시작 캐시·체력이 담긴 새 RunState를 받는다 (값은 RunState.CreateNew가 GameConfig로 채움).
        public void ResetForNewRun(RunState run)
        {
            this.run = run;
            isDefeated = false;
            chipsGranted = false;
            // 반도체 공장: 시작 캐시·넥서스 체력 (새 판 RunState에 한 번)
            if (FactoryOn && data.Config != null)
            {
                run.Cash = (int)Math.Round(run.Cash * (1f + FactoryBonus(FactoryNode.StartCash)), MidpointRounding.AwayFromZero);
                run.NexusHp += (int)Math.Round(data.Config.nexusHp * FactoryBonus(FactoryNode.NexusHp), MidpointRounding.AwayFromZero);
            }
        }

        // 앱 시작 때 1번 (GameLoop): 저장된 공학연구소 진행과 저장 경로.
        public void InitMeta(MetaProgress loaded, string path)
        {
            meta = loaded ?? new MetaProgress();
            metaPath = path;
        }

        // 이 타워로 진화할 수 있게 해금됐는지. 1단계 타워와 공학연구소가 꺼진 설정은 항상 true.
        public bool IsTowerUnlocked(TowerDef def) => def == null || def.tier <= 1 || !LabOn || meta.IsUnlocked(def.id);

        // 공학연구소 [해금]: M1반도체가 모자라거나 이미 해금했으면 false.
        public bool TryUnlockTower(TowerDef def)
        {
            if (def == null || def.tier <= 1 || meta.IsUnlocked(def.id))
                return false;
            int price = Math.Max(0, def.unlockPrice);
            if (meta.m1Chips < price)
                return false;
            meta.m1Chips -= price;
            meta.unlocked.Add(def.id);
            meta.Save(metaPath);
            MetaChanged?.Invoke(new MetaChange(meta.m1Chips, 0, def.id, meta.smallChips, 0));
            return true;
        }

        // GameFlow.StateChanged 구독: 판이 끝나면(게임 오버·클리어) 클리어한 층 수만큼 한 번에 준다.
        //   M1반도체 = 층당 1개 (공학연구소), 작은 반도체 = 층당 chipsPerFloor개 (반도체 공장)
        //   게임 오버 = 도달 층 − 1 (그 층은 못 깸), 게임 클리어 = 마지막 층까지 모두.
        public void HandleStateChanged(StateChange e)
        {
            if (chipsGranted || (!LabOn && !FactoryOn) || (e.State != GameState.GameOver && e.State != GameState.GameClear))
                return;
            chipsGranted = true;
            int cleared = Math.Max(0, e.State == GameState.GameClear ? e.Floor : e.Floor - 1);
            int m1 = LabOn ? cleared : 0;
            int small = FactoryOn ? cleared * Factory.chipsPerFloor : 0;
            meta.m1Chips += m1;
            meta.smallChips += small;
            meta.Save(metaPath);
            MetaChanged?.Invoke(new MetaChange(meta.m1Chips, m1, null, meta.smallChips, small));
        }

        public bool CanAfford(int amount) => amount >= 0 && run.Cash >= amount;

        // 캐시 사용. 부족하거나 음수면 false, 캐시는 그대로.
        public bool Spend(int amount, CashReason reason)
        {
            if (!CanAfford(amount))
                return false;
            run.Cash -= amount;
            CashChanged?.Invoke(new CashChange(run.Cash, -amount, reason));
            return true;
        }

        // 캐시 획득. 0 이하는 무시.
        public void Earn(int amount, CashReason reason)
        {
            if (amount <= 0)
                return;
            run.Cash += amount;
            CashChanged?.Invoke(new CashChange(run.Cash, amount, reason));
        }

        // 몬스터 → 넥서스 피해의 유일한 창구 (DamageDesign 4-2 그대로).
        // 같은 프레임에 여러 번 불려도 피해는 각각 더하고, Defeated는 처음 0이 될 때 1번만 낸다.
        public void TakeNexusDamage(int amount)
        {
            if (isDefeated || Invincible) return;           // 이미 0이면(또는 무적이면) 더 계산하지 않음
            run.NexusHp = Math.Max(0, run.NexusHp - amount);
            HpChanged?.Invoke(new HpChange(run.NexusHp, -amount));
            if (run.NexusHp == 0)
            {
                isDefeated = true;
                Defeated?.Invoke();                         // GameFlow → GameOver (1번만)
            }
        }

        // MonsterManager.Killed 구독.
        public void HandleKilled(MonsterKilled e) => Earn(e.Reward, CashReason.Kill);

        // MonsterManager.ReachedExit 구독.
        public void HandleReachedExit(MonsterReachedExit e) => TakeNexusDamage(e.Damage);

        // TowerManager.RecoveryExpired 구독: 첫 웨이브 끝에 남은 회수 타워 강제 반환.
        public void HandleRecoveryExpired(RecoveryExpire e) => Earn(e.Refund, CashReason.RecoveryExpire);
    }
}
