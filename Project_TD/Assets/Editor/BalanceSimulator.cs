using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.EditorTools
{
    // 레벨디자인 검토용 헤드리스 시뮬레이터 (에디터 전용, 게임에는 들어가지 않는다).
    // 실제 게임 코드(Board·MonsterManager·TowerManager·PlayerState·LevelRules·맵 생성기)로 1~10층을 끝까지 돌리고
    // 웨이브마다 처치·누수·캐시·타워 수·화력을 집계한다. 넥서스가 0이 돼도 끝까지 진행해 추세를 본다.
    //
    // 플레이어 AI (모든 시나리오 공통, Casual이면 휴식 때만 짓고 자리를 "최고 자리의 절반 이상 덮는 칸" 중 무작위로 고른다):
    //   - 0.5초마다(휴식·웨이브 모두) 할 수 있는 행동 중 "늘어나는 화력 × 덮는 경로 칸 ÷ 비용"이 가장 큰 것을 산다.
    //   - 더 좋은 행동이 아직 비싸면(가치가 1.3배 이상) 모아 둔다.
    //   - 행동: 총 타워 / 화염방사 타워 새로 짓기, (테크트리) 다음 단계로 진화.
    //   - 맵이 바뀌면 회수 목록 타워를 새 맵의 가장 좋은 자리에 무료로 재건축한다 (게임 규칙과 같음).
    //   - 타워 모델이 없어 포탑 회전 대기 없이 바로 쏜다 (실제보다 약간 유리).
    public static class BalanceSimulator
    {
        public class Tier
        {
            public string Id, Name;
            public float Damage, Interval, ConeLength;
            public int Burst = 1, Range = 2, TotalCost, UnlockFloor;
        }

        // 시나리오: 테크트리(진화 단계), 아이템(층 클리어 보상, 영구 스탯).
        public class Scenario
        {
            public string Name;
            public bool TechTree;
            public bool Items;
            public Tier[] GunTiers;    // [0] = 기본 총 타워
            public Tier[] FlameTiers;  // [0] = 기본 화염방사
            public float ItemDamagePerPick = 0.15f; // 화력 아이템 1개 = 공격력 +15% (합산)
            public float ItemSpeedPerPick = 0.10f;  // 연사 아이템 1개 = 공격 속도 +10% (합산)
            public float HpMul = 1f;   // 몬스터 체력 추가 배율 (여유도 측정용)
            public int HpMulFromFloor = 1; // 이 층부터 HpMul 적용 (초반 1~2층과 중후반을 따로 재기 위해)
            public bool Casual;        // 일반 플레이어: 휴식 때만 짓고, 자리는 좋은 자리 중 무작위

            public Scenario Copy() => (Scenario)MemberwiseClone();
        }

        public class WaveRow
        {
            public int Floor, Wave, G, Monsters, Kills, Leaks, CashEarned, CashEnd, Guns, Flames, Upgrades, NexusHp;
            public float Hp, TotalHp, TeamDps, DmgMul, SpdMul;
            public string Tiers;
        }

        public class RunResult
        {
            public string Scenario;
            public int Seed;
            public List<WaveRow> Rows = new List<WaveRow>();
        }

        const float Dt = 0.05f;
        const float ShopEvery = 0.5f;

        public static Scenario Baseline() => new Scenario
        {
            Name = "현재",
            GunTiers = new[] { new Tier { Id = "gun1", Name = "총 타워", Damage = 1f, Interval = 1f, Burst = 2, Range = 2, TotalCost = 50, UnlockFloor = 1 } },
            FlameTiers = new[] { new Tier { Id = "flame1", Name = "화염방사", Damage = 0.5f, Interval = 0.2f, ConeLength = 1.6f, Range = 2, TotalCost = 70, UnlockFloor = 1 } },
        };

        // 1번: 테크트리 (진화). 진화 비용 대비 화력 증가가 새로 짓기(총 0.040 화력/캐시)보다 조금씩 좋아진다 (0.043 → 0.047):
        // 후반에 부지가 꽉 차면 "같은 자리에서 강해지는" 것이 유일한 성장 수단이므로 보상이 더 커야 한다.
        // 해금 층은 맵 단계가 바뀌는 층(3층·7층)에 맞춰, 새 맵마다 새 목표가 생기게 한다.
        public static Scenario TechTree() => new Scenario
        {
            Name = "테크트리",
            TechTree = true,
            GunTiers = new[]
            {
                new Tier { Id = "gun1", Name = "총 타워", Damage = 1f, Interval = 1f, Burst = 2, Range = 2, TotalCost = 50, UnlockFloor = 1 },
                new Tier { Id = "gun2", Name = "쌍열 기관총", Damage = 1.5f, Interval = 0.6f, Burst = 2, Range = 2, TotalCost = 120, UnlockFloor = 3 },
                new Tier { Id = "gun3", Name = "레일 캐논", Damage = 3f, Interval = 0.5f, Burst = 2, Range = 3, TotalCost = 270, UnlockFloor = 7 },
            },
            FlameTiers = new[]
            {
                new Tier { Id = "flame1", Name = "화염방사", Damage = 0.5f, Interval = 0.2f, ConeLength = 1.6f, Range = 2, TotalCost = 70, UnlockFloor = 1 },
                new Tier { Id = "flame2", Name = "화염포", Damage = 1.4f, Interval = 0.2f, ConeLength = 2.0f, Range = 2, TotalCost = 170, UnlockFloor = 3 },
                new Tier { Id = "flame3", Name = "플라즈마 방사기", Damage = 3.2f, Interval = 0.2f, ConeLength = 2.4f, Range = 3, TotalCost = 370, UnlockFloor = 7 },
            },
        };

        // 2번: 층 클리어 아이템 (영구). 1~9층 클리어 때 1개씩: 홀수 층 화력 +10%, 짝수 층 연사 +8% (합산).
        // 10층 도착 때 공격력 ×1.5, 공격 속도 ×1.32 → 화력 약 ×2.0. 부지가 꽉 차는 9~10층의 성장 공백을 메우는 크기.
        public static Scenario Items() => new Scenario
        {
            Name = "아이템",
            Items = true,
            GunTiers = Baseline().GunTiers,
            FlameTiers = Baseline().FlameTiers,
            ItemDamagePerPick = 0.10f,
            ItemSpeedPerPick = 0.08f,
        };

        public static Scenario Both()
        {
            var s = TechTree();
            s.Name = "테크트리+아이템";
            s.Items = true;
            s.ItemDamagePerPick = 0.10f;
            s.ItemSpeedPerPick = 0.08f;
            return s;
        }

        // ---------- 실행 ----------

        public static List<RunResult> RunAll(Scenario[] scenarios, int[] seeds)
        {
            var results = new List<RunResult>();
            foreach (var s in scenarios)
                foreach (int seed in seeds)
                    results.Add(Run(s, seed));
            return results;
        }

        public static RunResult Run(Scenario sc, int seed)
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            var mapRules = AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset");
            var monsterDefs = new[]
            {
                Object.Instantiate(AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/normal.asset")),
                Object.Instantiate(AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/fast.asset")),
                Object.Instantiate(AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/tank.asset")),
            };
            var baseHp = new float[monsterDefs.Length];
            for (int i = 0; i < monsterDefs.Length; i++)
                baseHp[i] = monsterDefs[i].hp; // 복사본만 바꾼다 (에셋은 그대로)
            var rng = new System.Random(seed * 7919);
            var gunAsset = AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/basic.asset");
            var flameAsset = AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/flamethrower.asset");

            // 시뮬레이션 전용 TowerDef 복사본 (에셋은 건드리지 않는다)
            var gunDefs = MakeDefs(gunAsset, sc.TechTree ? sc.GunTiers : new[] { sc.GunTiers[0] });
            var flameDefs = MakeDefs(flameAsset, sc.TechTree ? sc.FlameTiers : new[] { sc.FlameTiers[0] });
            var allDefs = new List<TowerDef>();
            allDefs.AddRange(gunDefs);
            allDefs.AddRange(flameDefs);
            var tierOf = new Dictionary<TowerDef, Tier>();
            var next = new Dictionary<TowerDef, TowerDef>();
            var tiers = new List<Tier>();
            tiers.AddRange(sc.TechTree ? sc.GunTiers : new[] { sc.GunTiers[0] });
            tiers.AddRange(sc.TechTree ? sc.FlameTiers : new[] { sc.FlameTiers[0] });
            for (int i = 0; i < allDefs.Count; i++)
                tierOf[allDefs[i]] = tiers[i];
            for (int i = 0; i + 1 < gunDefs.Length; i++) next[gunDefs[i]] = gunDefs[i + 1];
            for (int i = 0; i + 1 < flameDefs.Length; i++) next[flameDefs[i]] = flameDefs[i + 1];

            var data = new GameData();
            data.Init(config, mapRules, monsterDefs, allDefs.ToArray());
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

            var result = new RunResult { Scenario = sc.Name, Seed = seed };
            var buffer = new WaveSpawn[8];
            int g = 0, lastTier = 0, upgrades = 0;
            float dmgMul = 1f, spdMul = 1f;
            int itemsTaken = 0;

            for (int floor = 1; floor <= config.finalFloor; floor++)
            {
                var plan = LevelRules.GetFloorPlan(config, floor);
                for (int i = 0; i < monsterDefs.Length; i++)
                    monsterDefs[i].hp = baseHp[i] * (floor >= sc.HpMulFromFloor ? sc.HpMul : 1f);
                if (plan.Tier != lastTier)
                {
                    if (lastTier != 0)
                        towers.MoveAllTowersToRecovery(floor);
                    var map = new LayoutMapGenerator().Generate(new MapSpec(plan.N, plan.Complexity, seed * 100 + floor, mapRules));
                    board.Load(map);
                    // 회수 목록 → 새 맵의 좋은 자리에 무료 재건축 (비싼 타워부터)
                    var recovered = new List<TowerDef>(run.RecoveryList);
                    recovered.Sort((a, b) => b.buildCost.CompareTo(a.buildCost));
                    foreach (var def in recovered)
                    {
                        if (BestSite(board, def, null, out var cell, out _))
                            towers.RequestRebuild(def, cell);
                    }
                    run.RecoveryList.Clear(); // 자리가 없어 남은 것은 버린다 (실제로는 70% 반환, 시뮬레이션에선 드묾)
                    lastTier = plan.Tier;
                }

                for (int w = 1; w <= plan.Waves; w++)
                {
                    g++;
                    run.Floor = floor;
                    run.Wave = w;
                    int cashBefore = player.Cash;
                    int k0 = kills, l0 = leaks, u0 = upgrades;
                    int earnedBefore = kills;

                    // 휴식: 쇼핑
                    upgrades += Shop(sc, floor, board, towers, player, run, gunDefs, flameDefs, next, tierOf, rng, false);

                    monsters.HandleStateChanged(new StateChange(GameState.Wave, floor, w));
                    int lines = LevelRules.GetWaveComposition(config, floor, w, g, buffer);
                    monsters.BeginWave(buffer, lines);
                    int count = 0;
                    for (int i = 0; i < lines; i++) count += buffer[i].Count;
                    float hp = LevelRules.ScaleHp(config, monsterDefs[0].hp, monsters.HpScale);

                    float shopTimer = 0f;
                    int guard = 0;
                    while ((!monsters.SpawnFinished || monsters.AliveCount > 0) && guard++ < 200000)
                    {
                        monsters.TickSpawn(Dt);
                        monsters.TickMove(Dt);
                        towers.TickCombat(Dt);
                        monsters.Cleanup();
                        shopTimer += Dt;
                        if (shopTimer >= ShopEvery)
                        {
                            shopTimer = 0f;
                            upgrades += Shop(sc, floor, board, towers, player, run, gunDefs, flameDefs, next, tierOf, rng, true);
                        }
                    }
                    monsters.HandleStateChanged(new StateChange(GameState.Break, floor, w));
                    towers.ClearCombat();

                    var row = new WaveRow
                    {
                        Floor = floor, Wave = w, G = g, Monsters = count, Hp = hp, TotalHp = hp * count,
                        Kills = kills - k0, Leaks = leaks - l0, CashEarned = (kills - k0) * monsterDefs[0].reward,
                        CashEnd = player.Cash, Upgrades = upgrades - u0,
                        NexusHp = config.nexusHp - nexusLost, DmgMul = dmgMul, SpdMul = spdMul,
                    };
                    float dps = 0f;
                    var tierCount = new SortedDictionary<string, int>();
                    foreach (var t in run.Towers)
                    {
                        dps += t.Def.damage * Mathf.Max(1, t.Def.burstCount) / t.Def.interval;
                        if (t.Def.attackType == AttackType.Cone) row.Flames++; else row.Guns++;
                        tierCount.TryGetValue(t.Def.id, out int c);
                        tierCount[t.Def.id] = c + 1;
                    }
                    row.TeamDps = dps;
                    var sb = new StringBuilder();
                    foreach (var kv in tierCount) sb.Append(kv.Key).Append('×').Append(kv.Value).Append(' ');
                    row.Tiers = sb.ToString().Trim();
                    result.Rows.Add(row);
                }

                // 층 클리어 보상 아이템 (마지막 층 제외): 홀수 층 = 화력, 짝수 층 = 연사
                if (sc.Items && floor < config.finalFloor)
                {
                    itemsTaken++;
                    if (floor % 2 == 1) dmgMul += sc.ItemDamagePerPick; else spdMul += sc.ItemSpeedPerPick;
                    ApplyItems(allDefs, tierOf, dmgMul, spdMul);
                }
            }

            foreach (var d in allDefs)
                Object.DestroyImmediate(d);
            foreach (var m in monsterDefs)
                Object.DestroyImmediate(m);
            return result;
        }

        static TowerDef[] MakeDefs(TowerDef source, Tier[] tiers)
        {
            var defs = new TowerDef[tiers.Length];
            for (int i = 0; i < tiers.Length; i++)
            {
                var d = Object.Instantiate(source);
                d.name = tiers[i].Id;
                d.id = tiers[i].Id;
                d.displayName = tiers[i].Name;
                d.damage = tiers[i].Damage;
                d.interval = tiers[i].Interval;
                d.burstCount = tiers[i].Burst;
                d.range = tiers[i].Range;
                if (tiers[i].ConeLength > 0f) d.coneLength = tiers[i].ConeLength;
                d.buildCost = tiers[i].TotalCost;
                d.viewPrefab = null;
                d.projectilePrefab = null;
                d.muzzleFxPrefab = null;
                d.buildClip = null;
                defs[i] = d;
            }
            return defs;
        }

        static void ApplyItems(List<TowerDef> defs, Dictionary<TowerDef, Tier> tierOf, float dmgMul, float spdMul)
        {
            foreach (var d in defs)
            {
                var t = tierOf[d];
                d.damage = t.Damage * dmgMul;
                d.interval = t.Interval / spdMul;
            }
        }

        // ---------- 플레이어 AI ----------

        static int Shop(Scenario sc, int floor, Board board, TowerManager towers, PlayerState player, RunState run,
            TowerDef[] gunDefs, TowerDef[] flameDefs, Dictionary<TowerDef, TowerDef> next, Dictionary<TowerDef, Tier> tierOf,
            System.Random rng, bool inWave)
        {
            int upgrades = 0;
            if (sc.Casual && inWave)
                return 0;
            for (int guard = 0; guard < 50; guard++)
            {
                float bestValue = 0f, bestAffordable = 0f;
                int bestCost = 0;
                System.Action buy = null;

                void Consider(float value, int cost, System.Action action)
                {
                    if (value > bestValue) { bestValue = value; bestCost = cost; }
                    if (cost <= player.Cash && value > bestAffordable) { bestAffordable = value; buy = action; }
                }

                // 새로 짓기 (1단계)
                foreach (var def in new[] { gunDefs[0], flameDefs[0] })
                {
                    if (!BestSite(board, def, sc.Casual ? rng : null, out var cell, out int cover)) continue;
                    float value = Dps(def) * Coverage(def, cover) / def.buildCost;
                    var d = def; var c = cell;
                    Consider(value, def.buildCost, () => towers.Build(c, d));
                }

                // 진화
                if (sc.TechTree)
                {
                    foreach (var t in run.Towers)
                    {
                        if (!next.TryGetValue(t.Def, out var up) || tierOf[up].UnlockFloor > floor) continue;
                        int cost = up.buildCost - t.Def.buildCost;
                        int cover = CountCover(board, up, t.Cell);
                        float value = (Dps(up) * Coverage(up, cover) - Dps(t.Def) * Coverage(t.Def, CountCover(board, t.Def, t.Cell))) / cost;
                        var tower = t; var upDef = up;
                        Consider(value, cost, () =>
                        {
                            var cell = tower.Cell;
                            var from = tower.Def;
                            player.Earn(from.buildCost - towers.GetSellRefund(from), CashReason.Sell); // 판매 손실 없이 차액만 내도록
                            towers.Sell(tower);
                            towers.Build(cell, upDef);
                            upgrades++;
                        });
                    }
                }

                if (buy == null || bestValue > bestAffordable * 1.3f)
                    break; // 살 게 없거나, 더 좋은 행동을 위해 모은다
                buy();
            }
            return upgrades;
        }

        static float Dps(TowerDef d) => d.damage * Mathf.Max(1, d.burstCount) / d.interval;

        // 덮는 경로 칸 → 실제로 때리는 양. 화염방사는 범위 안 여러 마리를 동시에 맞혀 1.5배로 본다.
        static float Coverage(TowerDef d, int cover) => d.attackType == AttackType.Cone ? cover * 1.5f : cover;

        // rng가 있으면(Casual) 최고 자리의 절반 이상 덮는 칸 중 무작위.
        static bool BestSite(Board board, TowerDef def, System.Random rng, out Vector2Int best, out int bestCover)
        {
            best = default;
            bestCover = 0;
            int n = board.N;
            var covers = new List<KeyValuePair<Vector2Int, int>>();
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (!board.IsBuildable(c)) continue;
                    int cover = CountCover(board, def, c);
                    if (cover <= 0) continue;
                    covers.Add(new KeyValuePair<Vector2Int, int>(c, cover));
                    if (cover > bestCover) { bestCover = cover; best = c; }
                }
            if (bestCover == 0 || rng == null)
                return bestCover > 0;
            int top = bestCover;
            var good = covers.FindAll(kv => kv.Value * 2 >= top);
            var pick = good[rng.Next(good.Count)];
            best = pick.Key;
            bestCover = pick.Value;
            return true;
        }

        // 몬스터 체력을 몇 배까지 올려도 모든 시드에서 클리어(넥서스 > 0)하는지: 이분 탐색.
        public static float FindHpMargin(Scenario sc, int[] seeds, float lo = 0.25f, float hi = 8f, int steps = 9, int fromFloor = 1)
        {
            sc = sc.Copy();
            sc.HpMulFromFloor = fromFloor;
            if (!ClearsAll(sc, seeds, lo)) return 0f;
            for (int i = 0; i < steps; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (ClearsAll(sc, seeds, mid)) lo = mid; else hi = mid;
            }
            return lo;
        }

        static bool ClearsAll(Scenario sc, int[] seeds, float hpMul)
        {
            var s = sc.Copy();
            s.HpMul = hpMul;
            foreach (int seed in seeds)
            {
                var r = Run(s, seed);
                if (r.Rows[r.Rows.Count - 1].NexusHp <= 0) return false;
            }
            return true;
        }

        static int CountCover(Board board, TowerDef def, Vector2Int site)
        {
            int count = 0;
            int n = board.N;
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (!board.IsPathCell(c)) continue;
                    if (def.attackType == AttackType.Cone)
                    {
                        if ((c - site).magnitude <= def.coneLength + TowerManager.ConeHitRadius) count++;
                    }
                    else if (Mathf.Max(Mathf.Abs(c.x - site.x), Mathf.Abs(c.y - site.y)) <= def.range) count++;
                }
            return count;
        }

        // ---------- 메뉴 ----------

        [MenuItem("TowerDefence/Balance/Run Current Design (3 seeds)")]
        public static void RunCurrentMenu()
        {
            var r = RunAll(new[] { Baseline() }, new[] { 1, 2, 3 });
            Debug.Log(Summary(r));
        }

        public static string Summary(List<RunResult> results)
        {
            var sb = new StringBuilder();
            foreach (var r in results)
            {
                sb.AppendLine($"## {r.Scenario} seed {r.Seed}");
                foreach (var w in r.Rows)
                    sb.AppendLine($"{w.Floor}-{w.Wave} g{w.G} n{w.Monsters} hp{w.Hp:0.#} kill{w.Kills} leak{w.Leaks} cash+{w.CashEarned} cash{w.CashEnd} gun{w.Guns} fl{w.Flames} up{w.Upgrades} dps{w.TeamDps:0.#} nexus{w.NexusHp} [{w.Tiers}]");
            }
            return sb.ToString();
        }

        public static string ToJson(List<RunResult> results)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"scenario\":\"").Append(r.Scenario).Append("\",\"seed\":").Append(r.Seed).Append(",\"rows\":[");
                for (int j = 0; j < r.Rows.Count; j++)
                {
                    var w = r.Rows[j];
                    if (j > 0) sb.Append(',');
                    sb.Append(JsonUtility.ToJson(new RowJson(w)));
                }
                sb.Append("]}");
            }
            return sb.Append(']').ToString();
        }

        [System.Serializable]
        class RowJson
        {
            public int floor, wave, g, monsters, kills, leaks, cashEarned, cashEnd, guns, flames, upgrades, nexusHp;
            public float hp, totalHp, teamDps, dmgMul, spdMul;
            public string tiers;

            public RowJson(WaveRow w)
            {
                floor = w.Floor; wave = w.Wave; g = w.G; monsters = w.Monsters; kills = w.Kills; leaks = w.Leaks;
                cashEarned = w.CashEarned; cashEnd = w.CashEnd; guns = w.Guns; flames = w.Flames; upgrades = w.Upgrades;
                nexusHp = w.NexusHp; hp = w.Hp; totalHp = w.TotalHp; teamDps = w.TeamDps; dmgMul = w.DmgMul; spdMul = w.SpdMul;
                tiers = w.Tiers;
            }
        }
    }
}
