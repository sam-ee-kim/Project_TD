using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // 레벨디자인 테스트 (Docs/LevelTest_Tier1.md): 1단계 타워 4종만으로 10층까지 어디까지 버티는지 (에디터 전용).
    // 실제 게임 코드(맵 생성기·Board·MonsterManager·TowerManager·LevelRules)로 끝까지 돌린다. 에셋은 건드리지 않고 복사본만 바꾼다.
    //
    // 테스트 규칙:
    //   - 타워: 듀얼샷(basic) · 화염방사 · 나노봇(3층 해금) · EMP(5층 해금). 스탯은 끝까지 그대로 (진화·아이템 없음)
    //   - 크레딧: 시작 50, 웨이브 중 2초마다 1, 처치 1마리당 1. 휴식 중에는 없음
    //   - 몬스터 체력: 테스트 기본 체력(BaseHp) × 층당 성장률^(층−1) × 웨이브 보정(+8%/웨이브). 맵 단계 배율 표는 쓰지 않는다
    //   - 웨이브 구성·넥서스 체력(20)·몬스터 속도·넥서스 피해는 게임 규칙 그대로
    //
    // 플레이어 AI (0.5초마다, 휴식·웨이브 모두): "실제 효과 화력(EffectiveDps) × 같은 종류 감쇠 ÷ 비용"이 가장 큰 타워를 가장 좋은 자리·방향에 짓는다.
    // 더 좋은 타워(가치 1.3배 이상)가 20 크레딧 안쪽으로 모자라면 모아 두고, 더 모자라면 지금 살 수 있는 것을 산다 (타워 2개 미만이면 모으지 않음). 맵이 바뀌면 회수 타워를 새 맵에 무료로 재건축한다 (게임 규칙).
    // 넥서스가 0이 돼도 끝까지 진행해 "몇 층에서 무너졌는지"와 그 뒤 추세를 함께 본다.
    public static class LevelTestSimulator
    {
        public class TowerSpec
        {
            public string Id, Name;
            public int Cost, UnlockFloor;
            public int MaxTargets;   // 한 번에 맞히는 최대 마리 수 (단일 = 1, 범위 = 덮는 칸에 비례)
            public float SlowBonus;  // 감속으로 다른 타워가 더 오래 때리게 되는 몫을 DPS로 환산 (나노필드)
        }

        public static readonly TowerSpec[] Towers =
        {
            new TowerSpec { Id = "basic", Name = "듀얼샷", Cost = 50, UnlockFloor = 1, MaxTargets = 1 },
            new TowerSpec { Id = "flamethrower", Name = "화염방사", Cost = 70, UnlockFloor = 1, MaxTargets = 3 },
            new TowerSpec { Id = "nano", Name = "나노봇", Cost = 80, UnlockFloor = 3, MaxTargets = 1, SlowBonus = 1.2f },
            new TowerSpec { Id = "emp", Name = "EMP", Cost = 100, UnlockFloor = 5, MaxTargets = 6 },
        };

        public const int StartCredit = 50;
        public const float IncomeEverySec = 2f;
        public const int KillReward = 1;
        public const float HpPerWave = 0.08f;

        [System.Serializable]
        public class WaveRow
        {
            public int Floor, Wave, Monsters, Kills, Leaks, Income, KillIncome, CreditEnd, NexusHp;
            public int Basic, Flame, Nano, Emp;
            public float NormalHp, TankHp, FastHp, Seconds, TeamDps;
        }

        public class RunResult
        {
            public float Growth;
            public int Seed;
            public int FailFloor; // 넥서스가 처음 0 이하가 된 층 (끝까지 버티면 0)
            public List<WaveRow> Rows = new List<WaveRow>();
        }

        const float Dt = 0.05f;
        const float ShopEvery = 0.5f;

        // 몬스터 기본 체력 (테스트용, 1층 1웨이브 값). 넥서스 피해는 게임 값 그대로 (일반 1 · 해머헤드 1 · 바스티온 2)
        public static readonly Dictionary<string, float> BaseHp = new Dictionary<string, float> { { "normal", 2f }, { "fast", 1f }, { "tank", 6f } };

        public static RunResult Run(float growth, int seed)
        {
            var srcConfig = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            var config = Object.Instantiate(srcConfig);
            config.startCash = StartCredit;
            var r = config.rules;
            r.hpMapScale = new[] { 1f };
            r.hpComplexityScale = new[] { 1f };
            r.hpSecondFloorScale = 1f;
            r.hpPerWave = HpPerWave;
            var mapRules = AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset");

            string[] monsterIds = { "normal", "fast", "tank" };
            var monsterDefs = new MonsterDef[monsterIds.Length];
            var baseHp = new float[monsterIds.Length];
            for (int i = 0; i < monsterIds.Length; i++)
            {
                monsterDefs[i] = Object.Instantiate(AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/" + monsterIds[i] + ".asset"));
                monsterDefs[i].reward = KillReward;
                monsterDefs[i].viewPrefab = null;
                baseHp[i] = BaseHp[monsterIds[i]];
            }

            var towerDefs = new TowerDef[Towers.Length];
            for (int i = 0; i < Towers.Length; i++)
            {
                var d = Object.Instantiate(AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/" + Towers[i].Id + ".asset"));
                d.name = Towers[i].Id;
                d.buildCost = Towers[i].Cost;
                d.viewPrefab = null;
                d.projectilePrefab = null;
                d.muzzleFxPrefab = null;
                d.chargeFxPrefab = null;
                d.hitFxPrefab = null;
                d.fieldFxPrefab = null;
                towerDefs[i] = d;
            }

            var data = new GameData();
            data.Init(config, mapRules, monsterDefs, towerDefs);
            var board = new Board();
            var monsters = new MonsterManager();
            var towers = new TowerManager();
            var player = new PlayerState();
            board.Init(data);
            player.Init(data);
            monsters.Init(data, board);
            towers.Init(data, board, monsters, player);
            towers.TowerChanged += board.HandleTowerChanged;
            var run = RunState.CreateNew(config);
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);

            int kills = 0, leaks = 0, nexusLost = 0;
            monsters.Killed += e => { kills++; player.HandleKilled(e); };
            monsters.ReachedExit += e => { leaks++; nexusLost += e.Damage; };

            var result = new RunResult { Growth = growth, Seed = seed };
            var buffer = new WaveSpawn[8];
            int g = 0, lastTier = 0;

            for (int floor = 1; floor <= config.finalFloor; floor++)
            {
                var plan = LevelRules.GetFloorPlan(config, floor);
                float floorMul = Mathf.Pow(growth, floor - 1);
                for (int i = 0; i < monsterDefs.Length; i++)
                    monsterDefs[i].hp = baseHp[i] * floorMul;
                if (plan.Tier != lastTier)
                {
                    if (lastTier != 0)
                        towers.MoveAllTowersToRecovery(floor);
                    board.Load(new LayoutMapGenerator().Generate(new MapSpec(plan.N, plan.Complexity, seed * 100 + floor, mapRules)));
                    var recovered = new List<TowerDef>(run.RecoveryList);
                    recovered.Sort((a, b) => b.buildCost.CompareTo(a.buildCost));
                    foreach (var def in recovered)
                        if (BestSite(board, towers, def, out var cell, out _))
                            towers.RequestRebuild(def, cell);
                    run.RecoveryList.Clear();
                    lastTier = plan.Tier;
                }

                for (int w = 1; w <= plan.Waves; w++)
                {
                    g++;
                    run.Floor = floor;
                    run.Wave = w;
                    int k0 = kills, l0 = leaks;

                    Shop(floor, board, towers, player, towerDefs); // 휴식: 짓기 (휴식 중 크레딧 지급은 없음)

                    monsters.HandleStateChanged(new StateChange(GameState.Wave, floor, w));
                    int lines = LevelRules.GetWaveComposition(config, floor, w, g, buffer);
                    monsters.BeginWave(buffer, lines);
                    int count = 0;
                    for (int i = 0; i < lines; i++) count += buffer[i].Count;

                    float shopTimer = 0f, incomeTimer = 0f, time = 0f;
                    int income = 0, guard = 0;
                    while ((!monsters.SpawnFinished || monsters.AliveCount > 0) && guard++ < 400000)
                    {
                        monsters.TickSpawn(Dt);
                        monsters.TickMove(Dt);
                        towers.TickCombat(Dt);
                        monsters.Cleanup();
                        time += Dt;
                        incomeTimer += Dt;
                        if (incomeTimer >= IncomeEverySec)
                        {
                            incomeTimer -= IncomeEverySec;
                            player.Earn(1, CashReason.Kill); // 웨이브 중 시간 지급 (시뮬레이션 전용, 사유 구분 없음)
                            income++;
                        }
                        shopTimer += Dt;
                        if (shopTimer >= ShopEvery)
                        {
                            shopTimer = 0f;
                            Shop(floor, board, towers, player, towerDefs);
                        }
                    }
                    monsters.HandleStateChanged(new StateChange(GameState.Break, floor, w));
                    towers.ClearCombat();

                    float scale = monsters.HpScale;
                    var row = new WaveRow
                    {
                        Floor = floor, Wave = w, Monsters = count, Kills = kills - k0, Leaks = leaks - l0,
                        Income = income, KillIncome = (kills - k0) * KillReward, CreditEnd = player.Cash,
                        NexusHp = config.nexusHp - nexusLost, Seconds = time,
                        NormalHp = LevelRules.ScaleHp(config, monsterDefs[0].hp, scale),
                        FastHp = LevelRules.ScaleHp(config, monsterDefs[1].hp, scale),
                        TankHp = LevelRules.ScaleHp(config, monsterDefs[2].hp, scale),
                    };
                    foreach (var t in run.Towers)
                    {
                        row.TeamDps += Dps(t.Def);
                        switch (t.Def.name)
                        {
                            case "basic": row.Basic++; break;
                            case "flamethrower": row.Flame++; break;
                            case "nano": row.Nano++; break;
                            case "emp": row.Emp++; break;
                        }
                    }
                    if (result.FailFloor == 0 && row.NexusHp <= 0)
                        result.FailFloor = floor;
                    result.Rows.Add(row);
                }
            }

            foreach (var d in towerDefs) Object.DestroyImmediate(d);
            foreach (var m in monsterDefs) Object.DestroyImmediate(m);
            Object.DestroyImmediate(config);
            return result;
        }

        // ---------- 플레이어 AI ----------

        // 혼합 배치 시나리오: 해금된 나노봇·EMP를 각각 이만큼은 먼저 갖춘다 (0 = AI 가치 계산대로).
        // AI 가치 계산으로는 둘 다 한 번도 사지 않아서, 섞어 지었을 때 차이를 따로 본다.
        public static int ForceMixCount;

        static void Shop(int floor, Board board, TowerManager towers, PlayerState player, TowerDef[] defs)
        {
            for (int guard = 0; guard < 30; guard++)
            {
                if (ForceMixCount > 0)
                {
                    bool waiting = false;
                    for (int i = 2; i < defs.Length; i++) // 나노봇, EMP
                    {
                        if (floor < Towers[i].UnlockFloor || CountOf(towers, defs[i]) >= ForceMixCount)
                            continue;
                        if (!BestSite(board, towers, defs[i], out var site, out _))
                            continue;
                        if (player.Cash >= defs[i].buildCost)
                        {
                            towers.Build(site, defs[i]);
                            waiting = true; // 지었으면 처음부터 다시 본다
                            break;
                        }
                        if (towers.Towers.Count >= 2)
                            return; // 모은다
                    }
                    if (waiting)
                        continue;
                }
                float bestValue = 0f, bestAffordable = 0f;
                int bestCost = 0;
                TowerDef buyDef = null;
                Vector2Int buyCell = default;
                for (int i = 0; i < defs.Length; i++)
                {
                    if (floor < Towers[i].UnlockFloor)
                        continue;
                    var def = defs[i];
                    if (!BestSite(board, towers, def, out var cell, out int cover))
                        continue;
                    float value = EffectiveDps(i, def, cover) * Mathf.Pow(VarietyDecay, CountOf(towers, def)) / def.buildCost;
                    if (value > bestValue) { bestValue = value; bestCost = def.buildCost; }
                    if (def.buildCost <= player.Cash && value > bestAffordable)
                    {
                        bestAffordable = value;
                        buyDef = def;
                        buyCell = cell;
                    }
                }
                // 살 게 없거나, 더 좋은 타워가 조금만(SaveGap 이하) 모으면 되면 모은다. 한참 모아야 하면 지금 살 수 있는 걸 산다
                // 타워가 2개 미만이면 모으지 않는다 (막을 수단부터)
                if (buyDef == null || (towers.Towers.Count >= 2 && bestValue > bestAffordable * 1.3f && bestCost - player.Cash <= SaveGap))
                    return;
                towers.Build(buyCell, buyDef); // 방향 = 경로를 가장 많이 덮는 쪽 (BestFacing)
            }
        }

        // 실제 효과 화력: 대상 1마리 DPS × 동시에 맞히는 마리 수(덮는 칸의 70%, 최대 MaxTargets) × 가동률 + 감속 보정.
        // 몬스터는 1초 간격·1칸/초로 와서 경로 칸마다 약 1마리. 덮는 칸이 3칸 미만이면 대상이 없는 시간이 생긴다.
        static float EffectiveDps(int spec, TowerDef def, int cover)
        {
            var s = Towers[spec];
            float uptime = Mathf.Min(1f, cover / 3f);
            float targets = Mathf.Clamp(cover * 0.7f, 1f, s.MaxTargets);
            return (Dps(def) * targets + s.SlowBonus) * uptime;
        }

        // 같은 종류를 많이 지을수록 가치를 조금씩 낮춰 섞어 짓게 한다 (한 종류만 쌓으면 그 종류의 약점이 그대로 드러남).
        const float VarietyDecay = 0.92f;

        static int CountOf(TowerManager towers, TowerDef def)
        {
            int n = 0;
            for (int i = 0; i < towers.Towers.Count; i++)
                if (towers.Towers[i].Def == def) n++;
            return n;
        }

        const int SaveGap = 20;

        // 1회 공격 피해 ÷ 주기 (차지 포함, 대상 1마리 기준)
        static float Dps(TowerDef d) => d.damage * Mathf.Max(1, d.burstCount) / (d.interval + d.chargeTime);

        static bool BestSite(Board board, TowerManager towers, TowerDef def, out Vector2Int best, out int bestCover)
        {
            best = default;
            bestCover = 0;
            int n = board.N;
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (!board.IsBuildable(c))
                        continue;
                    int facing = towers.BestFacing(def, c);
                    int cover = 0;
                    for (int px = 0; px < n; px++)
                        for (int py = 0; py < n; py++)
                            if (board.IsPathCell(new Vector2Int(px, py)) && TowerFacing.InArea(def, facing, px - x, py - y))
                                cover++;
                    if (cover > bestCover)
                    {
                        bestCover = cover;
                        best = c;
                    }
                }
            return bestCover > 0;
        }

        // ---------- 출력 ----------

        public static string ToJson(List<RunResult> results)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < results.Count; i++)
            {
                var res = results[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"growth\":").Append(res.Growth.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(",\"seed\":").Append(res.Seed).Append(",\"failFloor\":").Append(res.FailFloor).Append(",\"rows\":[");
                for (int j = 0; j < res.Rows.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append(JsonUtility.ToJson(res.Rows[j]));
                }
                sb.Append("]}");
            }
            return sb.Append(']').ToString();
        }

        [MenuItem("TowerDefence/Balance/Level Test Tier1 (10F)")]
        public static void RunMenu()
        {
            var results = new List<RunResult>();
            foreach (float growth in new[] { 1.15f, 1.25f, 1.35f, 1.45f })
                foreach (int seed in new[] { 1, 2, 3 })
                    results.Add(Run(growth, seed));
            System.IO.File.WriteAllText("Temp/LevelTest_Tier1.json", ToJson(results));
            Debug.Log("[LevelTest] Temp/LevelTest_Tier1.json");
        }
    }
}
