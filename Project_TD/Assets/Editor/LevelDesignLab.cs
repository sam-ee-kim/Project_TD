using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Tile = TowerDefence.MapBuilderScene.Tile;

namespace TowerDefence.EditorTools
{
    // 레벨디자인 실험실 (에디터 전용, Docs/LevelDesign_Stage1.md · Stage2.md). 맵 제작 씬(Test_MapBuilder)과 같은 맵 파일을 만든다.
    // 단계(Stage): 1단계 = 7×7 · 타워 4개, 2단계 = 9×9 · 타워 6개 (UseStage로 바꾼다).
    //
    // 1) 컨셉 5개 × 변형 10개 = 맵 50종 (1단계 = 7×7). 컨셉마다 경로 모양과 "잔디(설치) / 나무(장애물)" 배치가 특정 타워의 고점을 열어 준다.
    //    LevelDesign/Stage1/*.json 으로 저장 → 맵 제작 씬 상단 ◀ ▶ 로 불러와 직접 플레이할 수 있다.
    // 2) 맵마다 타워 4개를 세 가지 방식으로 깔아 실제 게임 코드(Board·MonsterManager·TowerManager)로 웨이브를 돌린다.
    //      컨셉: 컨셉이 의도한 조합 그대로 / 비컨셉: 컨셉 타워를 뺀 나머지 타워 중에서 / 전체: 모든 타워 중에서 (AI가 고름)
    //      비컨셉·전체는 컨셉 조합의 칸마다 같은 단계(1·2단계) 타워로만 바꿔 끼운다 (단계 차이가 결과를 덮지 않게)
    //    + 단일 타워: 타워 11종을 각각 4개씩 깔아, 어떤 타워가 어느 컨셉 맵에서 고점을 보는지 본다.
    //    몬스터 수에 배율(×1, ×1.5, ×2 …)을 곱해 올려 가며, 넥서스 피해가 20(넥서스 체력)에 닿기 전까지 버틴 가장 큰 배율을 점수로 본다.
    //    타워 수를 같게 두는 이유: 지금 1단계 타워 비용이 임시로 모두 50이라 예산으로 맞추면 비용 차이가 결과를 덮는다. 맵 구조가 주는 차이만 본다.
    public static class LevelDesignLab
    {
        public static int Stage = 1;
        static int W = 7, H = 7;                // 맵 가로 × 세로 (1단계 7×7, 2단계 9×9, 3단계부터 13×9 고정)
        static int Size => W;                   // 1·2단계(정사각형) 템플릿 대칭용
        static Vector2Int Offset => new Vector2Int((MapBuilderScene.EditSize - W) / 2, (MapBuilderScene.EditSize - H) / 2); // 편집 격자 가운데
        static int TowerCount = 4;              // 깔아 보는 타워 수 (1단계 4, 2단계 6)
        const int NexusHp = 20;
        const float Dt = 0.05f;
        static readonly float[] Multipliers = { 1f, 1.5f, 2f, 3f, 4f, 6f, 8f, 12f, 16f, 24f, 32f };
        public static float HpScale = 2.5f; // 2단계 3.5 (UseStage).       // 테스트 체력 = 기본 체력 × 이 값 (기본 체력이면 타워 4개가 ×16까지 모두 버텨 차이가 안 보인다)
        static readonly string[] AllTowers = { "basic", "flamethrower", "emp", "nano", "lightning", "cryo", "electro", "minigun", "plasma", "dust", "phosphorus" };
        static readonly string[] MonsterIds = { "normal", "fast", "tank", "urantank" };

        public static readonly Dictionary<string, string> TowerName = new Dictionary<string, string>
        {
            { "basic", "듀얼샷" }, { "flamethrower", "화염방사" }, { "emp", "EMP" }, { "nano", "나노봇" }, { "lightning", "번개" },
            { "cryo", "냉기" }, { "electro", "전기" }, { "minigun", "미니건" }, { "plasma", "플라즈마" }, { "dust", "분진" }, { "phosphorus", "백린" },
        };
        static readonly Dictionary<string, string> MonsterName = new Dictionary<string, string>
        {
            { "normal", "일반" }, { "fast", "해머헤드(빠름)" }, { "tank", "바스티온(탱커)" }, { "urantank", "우란탱크(기계 정예)" },
        };

        // ---------- 컨셉 ----------

        public class SpawnLine
        {
            public int Wave, Spawn;
            public string Monster;
            public int Count;
            public float Interval, Delay;
            public SpawnLine(int wave, int spawn, string monster, int count, float interval, float delay = 0f)
            { Wave = wave; Spawn = spawn; Monster = monster; Count = count; Interval = interval; Delay = delay; }
        }

        public class Concept
        {
            public string Code, Name, Idea, Waves;
            public string[] Towers;       // 컨셉 타워 (비컨셉 조합에서는 이 타워들을 뺀다)
            public string[] Composition;  // 컨셉 조합 4개 (놓는 순서)
            public int[][] Templates;     // 경로 꺾임점 (x, y, x, y, …). 여러 경로는 Multi
            public int[][][] Multi;       // 스타트 2곳 이상인 템플릿: [템플릿][경로][좌표]
            public List<int[][]> Layouts; // 3단계(13×9): 후보 경로 묶음 (이미 방향이 정해짐, 규칙 검사를 통과한 것만 쓴다)
            public Func<List<Vector2Int>[], int, HashSet<Vector2Int>> Grass; // 잔디 칸 정하기
            public List<SpawnLine> Lines;
        }

        static readonly Concept[] Stage1Concepts = BuildConcepts();
        static readonly Concept[] Stage2Concepts = BuildStage2(Stage1Concepts);
        static readonly Concept[] Stage3Concepts = BuildStage3(Stage1Concepts);
        public static Concept[] Concepts => Stage >= 3 ? Stage3Concepts : Stage == 2 ? Stage2Concepts : Stage1Concepts;

        public static void UseStage(int stage)
        {
            Stage = stage;
            W = H = stage == 2 ? 9 : 7;
            TowerCount = stage == 2 ? 6 : 4;
            HpScale = stage == 2 ? 3.5f : 2.5f; // 실제 게임 3~4층 체력(1단계의 약 1.4배) × 2 (1단계와 같은 기준)
            WaveCount = 3;
            if (stage >= 3)
            {
                W = 13; H = 9;           // 맵 크기 고정. 레벨디자인은 체력 배율 · 몬스터 양 · 웨이브 수로 한다
                TowerCount = 8;
                HpScale = Stage3HpScale;
            }
        }

        public static float Stage3HpScale = 5f;   // 13×9 기준 체력 배율 (조합 비교용 한 점)
        public static int WaveCount = 3;           // 돌릴 웨이브 수 (3 넘으면 3웨이브 구성을 늘려 만든다)
        public static readonly float[] HpGrid = { 0.5f, 0.75f, 1f, 1.5f, 2f };   // 난이도 표: 기준 체력 배율에 곱하는 값
        public static readonly int[] WaveGrid = { 1, 2, 3, 4, 5 };

        // 2단계 (9×9): 컨셉 설명·컨셉 타워는 1단계와 같고, 경로·조합(6개)·웨이브(약 1.5배)를 키운다.
        static Concept[] BuildStage2(Concept[] s1)
        {
            Concept C(int i, string waves, string[] comp, int[][] templates, int[][][] multi, List<SpawnLine> lines) => new Concept
            {
                Code = s1[i].Code, Name = s1[i].Name, Idea = s1[i].Idea, Waves = waves, Towers = s1[i].Towers, Grass = s1[i].Grass,
                Composition = comp, Templates = templates, Multi = multi, Lines = lines,
            };
            return new[]
            {
                C(0, s1[0].Waves, new[] { "plasma", "plasma", "plasma", "nano", "nano", "nano" },
                    new[] { new[] { 1, 0, 1, 5, 7, 5, 7, 8 }, new[] { 1, 0, 1, 3, 7, 3, 7, 8 }, new[] { 2, 0, 2, 6, 6, 6, 6, 8 },
                            new[] { 1, 0, 1, 4, 4, 4, 4, 7, 7, 7, 7, 8 }, new[] { 1, 0, 1, 2, 7, 2, 7, 8 } }, null,
                    new List<SpawnLine> { new SpawnLine(1, 0, "normal", 12, 0.7f), new SpawnLine(2, 0, "normal", 15, 0.7f), new SpawnLine(2, 0, "fast", 5, 0.5f, 5f),
                                          new SpawnLine(3, 0, "normal", 18, 0.6f), new SpawnLine(3, 0, "fast", 6, 0.45f, 4f) }),
                C(1, s1[1].Waves, new[] { "flamethrower", "flamethrower", "flamethrower", "emp", "nano", "nano" },
                    new[] { new[] { 1, 0, 1, 6, 3, 6, 3, 1, 5, 1, 5, 6, 7, 6, 7, 8 }, new[] { 0, 1, 6, 1, 6, 3, 1, 3, 1, 5, 6, 5, 6, 7, 8, 7 },
                            new[] { 1, 0, 1, 4, 4, 4, 4, 1, 7, 1, 7, 8 }, new[] { 1, 0, 1, 7, 4, 7, 4, 2, 7, 2, 7, 8 }, new[] { 2, 0, 2, 3, 6, 3, 6, 5, 2, 5, 2, 8 } }, null,
                    new List<SpawnLine> { new SpawnLine(1, 0, "tank", 3, 2.5f), new SpawnLine(1, 0, "normal", 9, 1f, 1f), new SpawnLine(2, 0, "tank", 4, 2.5f),
                                          new SpawnLine(2, 0, "normal", 12, 0.9f, 1f), new SpawnLine(3, 0, "tank", 6, 2f), new SpawnLine(3, 0, "normal", 15, 0.8f, 1f) }),
                C(2, s1[2].Waves, new[] { "flamethrower", "flamethrower", "flamethrower", "nano", "nano", "emp" },
                    new[] { new[] { 0, 1, 8, 1 }, new[] { 0, 4, 8, 4 }, new[] { 1, 0, 1, 4, 7, 4, 7, 8 }, new[] { 0, 2, 5, 2, 5, 6, 8, 6 }, new[] { 1, 0, 1, 2, 7, 2, 7, 8 } }, null,
                    new List<SpawnLine> { new SpawnLine(1, 0, "normal", 15, 0.6f), new SpawnLine(2, 0, "normal", 18, 0.6f), new SpawnLine(2, 0, "tank", 2, 1.5f, 6f),
                                          new SpawnLine(3, 0, "normal", 21, 0.5f), new SpawnLine(3, 0, "tank", 3, 2f, 5f) }),
                C(3, s1[3].Waves, new[] { "electro", "electro", "lightning", "lightning", "cryo", "cryo" }, null,
                    new[]
                    {
                        new[] { new[] { 4, 0, 4, 5 } },
                        new[] { new[] { 0, 4, 4, 4, 4, 6 } },
                        new[] { new[] { 0, 4, 4, 4 }, new[] { 4, 0, 4, 4 } },
                        new[] { new[] { 1, 0, 1, 3, 5, 3 } },
                        new[] { new[] { 0, 2, 4, 2, 4, 5 }, new[] { 8, 5, 4, 5 } },
                    },
                    new List<SpawnLine> { new SpawnLine(1, 0, "fast", 9, 0.45f), new SpawnLine(2, 0, "fast", 12, 0.4f), new SpawnLine(2, 0, "normal", 6, 0.9f, 2f),
                                          new SpawnLine(3, 0, "fast", 18, 0.35f), new SpawnLine(3, 1, "fast", 9, 0.45f, 1f) }),
                C(4, s1[4].Waves, new[] { "minigun", "minigun", "minigun", "cryo", "cryo", "basic" },
                    new[] { new[] { 1, 0, 1, 2, 7, 2, 7, 4, 1, 4, 1, 6, 7, 6, 7, 8 }, new[] { 0, 1, 7, 1, 7, 3, 1, 3, 1, 5, 7, 5, 7, 7, 1, 7 },
                            new[] { 1, 0, 1, 7, 3, 7, 3, 1, 5, 1, 5, 7, 7, 7, 7, 0 }, new[] { 7, 0, 7, 2, 1, 2, 1, 4, 7, 4, 7, 6, 1, 6, 1, 8 },
                            new[] { 0, 7, 6, 7, 6, 5, 1, 5, 1, 3, 6, 3, 6, 1, 8, 1 } }, null,
                    new List<SpawnLine> { new SpawnLine(1, 0, "tank", 3, 3.5f), new SpawnLine(2, 0, "tank", 3, 3.5f), new SpawnLine(2, 0, "urantank", 2, 4f, 6f),
                                          new SpawnLine(3, 0, "urantank", 3, 4f), new SpawnLine(3, 0, "tank", 3, 3.5f, 2f) }),
            };
        }

        // ---------- 3단계 이후: 13×9 고정 맵 ----------
        // 경로 후보를 규칙대로 많이 만든 뒤(가로 방향·세로 방향, 뒤집기 4가지), ValidateLayout을 통과한 것만 쓴다.
        // 컨셉마다 최대 10개. 규칙을 지키는 후보가 모자라면 모자란 채로 둔다 (규칙을 어겨 수를 채우지 않는다).
        const int W3 = 13, H3 = 9;

        static Concept[] BuildStage3(Concept[] s1)
        {
            Concept C(int i, string idea, string[] comp, List<int[][]> layouts, List<SpawnLine> lines) => new Concept
            {
                Code = s1[i].Code, Name = s1[i].Name, Idea = idea ?? s1[i].Idea, Waves = s1[i].Waves, Towers = s1[i].Towers, Grass = s1[i].Grass,
                Composition = comp, Layouts = layouts, Lines = lines,
            };
            return new[]
            {
                C(0, null, new[] { "plasma", "plasma", "plasma", "plasma", "nano", "nano", "nano", "nano" },
                    Mix(1, Straights(), TwoTurns(3)),
                    new List<SpawnLine> { new SpawnLine(1, 0, "normal", 16, 0.6f), new SpawnLine(2, 0, "normal", 20, 0.6f), new SpawnLine(2, 0, "fast", 6, 0.45f, 5f),
                                          new SpawnLine(3, 0, "normal", 24, 0.5f), new SpawnLine(3, 0, "fast", 8, 0.4f, 4f) }),
                C(1, null, new[] { "flamethrower", "flamethrower", "flamethrower", "flamethrower", "emp", "emp", "nano", "nano" },
                    Mix(2, Serpentines(2)),
                    new List<SpawnLine> { new SpawnLine(1, 0, "tank", 4, 2.2f), new SpawnLine(1, 0, "normal", 12, 0.9f, 1f), new SpawnLine(2, 0, "tank", 5, 2.2f),
                                          new SpawnLine(2, 0, "normal", 16, 0.8f, 1f), new SpawnLine(3, 0, "tank", 8, 1.8f), new SpawnLine(3, 0, "normal", 20, 0.7f, 1f) }),
                C(2, null, new[] { "flamethrower", "flamethrower", "flamethrower", "flamethrower", "nano", "nano", "emp", "emp" },
                    Mix(3, TwoTurns(3), FourTurns()),
                    new List<SpawnLine> { new SpawnLine(1, 0, "normal", 20, 0.5f), new SpawnLine(2, 0, "normal", 24, 0.5f), new SpawnLine(2, 0, "tank", 3, 1.5f, 6f),
                                          new SpawnLine(3, 0, "normal", 28, 0.45f), new SpawnLine(3, 0, "tank", 4, 1.8f, 5f) }),
                C(3, "본선은 넥서스 반대 변에서 곧게 들어오지만(끝과 끝), 스타트 2~3곳 중 지선 스타트가 넥서스와 가까운 옆 변에 있어 빠른 몬스터가 짧은 길로 급습한다. " +
                     "사거리 3으로 멀리서 때리는 전기·번개와, 맞히면 멈춰 세우는 냉기·전기 정지로 짧은 지선을 막는다. 잔디는 경로 둘레 두 줄.",
                    new[] { "electro", "electro", "electro", "lightning", "lightning", "lightning", "cryo", "cryo" },
                    Mix(4, Raids()),
                    new List<SpawnLine> { new SpawnLine(1, 0, "fast", 8, 0.5f), new SpawnLine(1, 1, "fast", 6, 0.5f, 1f),
                                          new SpawnLine(2, 0, "fast", 10, 0.45f), new SpawnLine(2, 1, "fast", 8, 0.45f, 1f), new SpawnLine(2, 2, "normal", 6, 0.9f, 2f),
                                          new SpawnLine(3, 0, "fast", 14, 0.4f), new SpawnLine(3, 1, "fast", 10, 0.4f, 1f), new SpawnLine(3, 2, "fast", 8, 0.45f, 1f) }),
                C(4, null, new[] { "minigun", "minigun", "minigun", "minigun", "cryo", "cryo", "cryo", "basic" },
                    Mix(5, Serpentines(3)),
                    new List<SpawnLine> { new SpawnLine(1, 0, "tank", 4, 3f), new SpawnLine(2, 0, "tank", 4, 3f), new SpawnLine(2, 0, "urantank", 3, 3.5f, 6f),
                                          new SpawnLine(3, 0, "urantank", 4, 3.5f), new SpawnLine(3, 0, "tank", 4, 3f, 2f) }),
            };
        }

        // 기본 방향: 가로형 = 스타트 오른쪽 변(x=12) → 넥서스 왼쪽 변(x=0), 세로형 = 스타트 위 변(y=8) → 넥서스 아래 변(y=0).
        // 후보마다 뒤집기(그대로·좌우·상하·180°) 하나를 정해 네 방향이 고루 나오게 한다.
        static List<int[][]> Mix(int seed, params List<int[][]>[] groups)
        {
            var rng = new System.Random(seed * 7919);
            var horizontal = new List<int[][]>();
            var vertical = new List<int[][]>();
            foreach (var g in groups)
                foreach (var l in g)
                    (l[0][0] == W3 - 1 || (l[0][0] != 0 && l[0][1] != H3 - 1 && l[0][1] != 0) ? horizontal : vertical).Add(l);
            Shuffle(horizontal, rng);
            Shuffle(vertical, rng);
            var list = new List<int[][]>();
            for (int i = 0; i < Math.Max(horizontal.Count, vertical.Count); i++)
            {
                if (i < horizontal.Count) list.Add(Flip(horizontal[i], rng.Next(4)));
                if (i < vertical.Count) list.Add(Flip(vertical[i], rng.Next(4)));
            }
            return list;
        }

        static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // 0 = 그대로, 1 = 좌우 반전, 2 = 상하 반전, 3 = 180° (13×9는 90° 회전이 안 되므로 4가지)
        static int[][] Flip(int[][] paths, int f)
        {
            var o = new int[paths.Length][];
            for (int k = 0; k < paths.Length; k++)
            {
                o[k] = (int[])paths[k].Clone();
                for (int i = 0; i < o[k].Length; i += 2)
                {
                    if ((f & 1) != 0) o[k][i] = W3 - 1 - o[k][i];
                    if ((f & 2) != 0) o[k][i + 1] = H3 - 1 - o[k][i + 1];
                }
            }
            return o;
        }

        static int[][] One(params int[] xy) => new[] { xy };

        // 직선 (꺾임 0): 끝과 끝
        static List<int[][]> Straights()
        {
            var list = new List<int[][]>();
            for (int r = 1; r <= H3 - 2; r++) list.Add(One(W3 - 1, r, 0, r));
            for (int r = 1; r <= W3 - 2; r++) list.Add(One(r, H3 - 1, r, 0));
            return list;
        }

        // 꺾임 2번 (계단): 직선 구간이 길다 (구간 minRun칸 이상)
        static List<int[][]> TwoTurns(int minRun)
        {
            var list = new List<int[][]>();
            for (int a = 1; a <= H3 - 2; a++)
                for (int c = 1; c <= H3 - 2; c++)
                    for (int b = minRun; b <= W3 - 1 - minRun; b++)
                        if (Math.Abs(a - c) >= 2)
                            list.Add(One(W3 - 1, a, b, a, b, c, 0, c));
            for (int a = 1; a <= W3 - 2; a++)
                for (int c = 1; c <= W3 - 2; c++)
                    for (int b = 2; b <= H3 - 3; b++)
                        if (Math.Abs(a - c) >= minRun)
                            list.Add(One(a, H3 - 1, a, b, c, b, c, 0));
            return list;
        }

        // 꺾임 4번: 두 번 비켜 가는 계단 (2열 진지)
        static List<int[][]> FourTurns()
        {
            var list = new List<int[][]>();
            int[] rows = { 1, 3, 5, 7 };
            foreach (int a in rows) foreach (int c in rows) foreach (int d in rows)
                        if (a != c && c != d)
                            for (int b1 = 7; b1 <= 9; b1++)
                                for (int b2 = 3; b2 <= b1 - 3; b2++)
                                    list.Add(One(W3 - 1, a, b1, a, b1, c, b2, c, b2, d, 0, d));
            int[] cols = { 1, 3, 5, 7, 9, 11 };
            foreach (int a in cols) foreach (int c in cols) foreach (int d in cols)
                        if (Math.Abs(a - c) >= 3 && Math.Abs(c - d) >= 3)
                            list.Add(One(a, H3 - 1, a, 6, c, 6, c, 3, d, 3, d, 0));
            return list;
        }

        // 지그재그 (gap 2 = 접힌 구간 사이에 한 칸 = 두 구간에 닿는 명당, gap 3 = 두 칸 띄운 뱀 모양)
        static List<int[][]> Serpentines(int gap)
        {
            var list = new List<int[][]>();
            var hPairs = new[] { (1, 7), (2, 6), (1, 6), (2, 7), (1, 5), (3, 7) };
            var vPairs = new[] { (1, 11), (2, 10), (1, 9), (3, 11), (2, 8), (4, 10) };
            foreach (int start in new[] { W3 - 3, W3 - 4 })
                for (int n = 2; n <= 5; n++)
                {
                    int last = start - gap * (n - 1);
                    if (last < 2) break;
                    foreach (var (lo, hi) in hPairs)
                        foreach (bool up in new[] { true, false })
                        {
                            var xy = new List<int>();
                            int y = up ? lo : hi;
                            xy.Add(W3 - 1); xy.Add(y);
                            for (int k = 0; k < n; k++)
                            {
                                int x = start - gap * k;
                                xy.Add(x); xy.Add(y);
                                y = y == lo ? hi : lo;
                                xy.Add(x); xy.Add(y);
                            }
                            xy.Add(0); xy.Add(y);
                            list.Add(new[] { xy.ToArray() });
                        }
                }
            for (int n = 2; n <= 3; n++)
            {
                int last = H3 - 3 - gap * (n - 1);
                if (last < 2) break;
                foreach (var (lo, hi) in vPairs)
                    foreach (bool right in new[] { true, false })
                    {
                        var xy = new List<int>();
                        int x = right ? lo : hi;
                        xy.Add(x); xy.Add(H3 - 1);
                        for (int k = 0; k < n; k++)
                        {
                            int y = H3 - 3 - gap * k;
                            xy.Add(x); xy.Add(y);
                            x = x == lo ? hi : lo;
                            xy.Add(x); xy.Add(y);
                        }
                        xy.Add(x); xy.Add(0);
                        list.Add(new[] { xy.ToArray() });
                    }
            }
            return list;
        }

        // 급습: 본선은 끝과 끝 직선, 지선(스타트 2·3)은 넥서스 가까운 옆 변에서 들어와 본선에 수직으로 합류
        static List<int[][]> Raids()
        {
            var list = new List<int[][]>();
            // 세로형: 본선 (m,8) → (m,0), 지선 왼쪽 (0,r) / 오른쪽 (12,r)
            for (int m = 2; m <= W3 - 3; m++)
                for (int r1 = 2; r1 <= 3; r1++)
                {
                    var main = new[] { m, H3 - 1, m, 0 };
                    var left = new[] { 0, r1, m, r1, m, 0 };
                    var right = new[] { W3 - 1, r1, m, r1, m, 0 };
                    list.Add(new[] { main, left });
                    list.Add(new[] { main, right });
                    for (int r2 = 2; r2 <= 3; r2++)
                        list.Add(new[] { main, left, new[] { W3 - 1, r2, m, r2, m, 0 } });
                }
            // 가로형: 본선 (12,m) → (0,m), 지선 위 (c,8) / 아래 (c,0)
            for (int m = 2; m <= H3 - 3; m++)
                for (int c1 = 2; c1 <= 4; c1++)
                {
                    var main = new[] { W3 - 1, m, 0, m };
                    var top = new[] { c1, H3 - 1, c1, m, 0, m };
                    var bottom = new[] { c1, 0, c1, m, 0, m };
                    list.Add(new[] { main, top });
                    list.Add(new[] { main, bottom });
                    for (int c2 = 2; c2 <= 4; c2++)
                        list.Add(new[] { main, top, new[] { c2, 0, c2, m, 0, m } });
                }
            return list;
        }

        // ---------- 맵 규칙 검사 (13×9) ----------
        //   가장자리: 넥서스·모든 스타트는 맵 가장자리(꼭짓점 제외). 경로의 나머지 칸은 가장자리를 따라가지 않는다
        //   본선(스타트 1): 넥서스의 반대 변에서 시작 (스타트가 1곳이면 무조건 끝과 끝)
        //   지선(스타트 2·3): 넥서스 변에는 못 둔다. 넥서스와 가까워도 되는 건 스타트가 2곳 이상일 때(지선)뿐. 스타트끼리 맨해튼 3 이상
        //   모양: 끊김 없음, 직진 2칸 이상 후 꺾기, 본선은 넥서스 쪽으로만(역행 없음), 지선은 합류 지점 쪽으로만
        //   합류: 본선 직선 구간(양 끝 2칸 제외)에 수직으로 들어와 그 뒤로 본선을 그대로 따라감
        //   접촉: 이어지지 않은 경로 칸끼리 상하좌우로 맞닿지 않는다 (지름길 금지)
        static bool Border(Vector2Int p) => p.x == 0 || p.y == 0 || p.x == W - 1 || p.y == H - 1;
        static bool Corner(Vector2Int p) => (p.x == 0 || p.x == W - 1) && (p.y == 0 || p.y == H - 1);
        static bool Opposite(Vector2Int a, Vector2Int b) =>
            (a.x == 0 && b.x == W - 1) || (a.x == W - 1 && b.x == 0) || (a.y == 0 && b.y == H - 1) || (a.y == H - 1 && b.y == 0);
        static bool SameEdge(Vector2Int a, Vector2Int b) =>
            (a.x == b.x && (a.x == 0 || a.x == W - 1)) || (a.y == b.y && (a.y == 0 || a.y == H - 1));
        static int Manhattan(Vector2Int a, Vector2Int b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

        public static bool ValidateLayout(List<Vector2Int>[] paths, out string reason)
        {
            reason = null;
            var main = paths[0];
            var nexus = main[main.Count - 1];
            if (!Border(nexus) || Corner(nexus)) { reason = "넥서스가 가장자리가 아님"; return false; }
            var spawns = new List<Vector2Int>();
            for (int k = 0; k < paths.Length; k++)
            {
                var p = paths[k];
                var s = p[0];
                if (p[p.Count - 1] != nexus) { reason = "경로 끝이 넥서스가 아님"; return false; }
                if (!Border(s) || Corner(s)) { reason = "스타트가 가장자리가 아님"; return false; }
                if (k == 0 && !Opposite(s, nexus)) { reason = "본선 스타트가 넥서스 반대 변이 아님"; return false; }
                if (k > 0 && SameEdge(s, nexus)) { reason = "지선 스타트가 넥서스와 같은 변"; return false; }
                foreach (var o in spawns)
                    if (Manhattan(o, s) < 3) { reason = "스타트끼리 가까움"; return false; }
                spawns.Add(s);
                for (int i = 1; i < p.Count - 1; i++)
                    if (Border(p[i])) { reason = "경로가 가장자리를 따라감"; return false; }
            }
            // 본선: 넥서스 변 쪽 축으로 역행 없음
            bool xAxis = nexus.x == 0 || nexus.x == W - 1;
            if (!CheckShape(main, i => xAxis ? Math.Abs(main[i].x - nexus.x) : Math.Abs(main[i].y - nexus.y), out reason, "본선"))
                return false;

            var mainIndex = new Dictionary<Vector2Int, int>();
            for (int i = 0; i < main.Count; i++)
                mainIndex[main[i]] = i;
            for (int k = 1; k < paths.Length; k++)
            {
                var p = paths[k];
                int q = 0;
                while (q < p.Count && !mainIndex.ContainsKey(p[q]))
                    q++;
                if (q == 0 || q >= p.Count) { reason = "지선이 본선에 합류하지 않음"; return false; }
                int j = mainIndex[p[q]];
                if (p.Count - q != main.Count - j) { reason = "지선이 합류 뒤 본선을 벗어남"; return false; }
                for (int i = 0; i < p.Count - q; i++)
                    if (p[q + i] != main[j + i]) { reason = "지선이 합류 뒤 본선을 벗어남"; return false; }
                if (j < 2 || j > main.Count - 3) { reason = "합류 지점이 본선 끝에 너무 가까움"; return false; }
                var dir = main[j + 1] - main[j];
                if (main[j] - main[j - 1] != dir) { reason = "꺾이는 칸에 합류"; return false; }
                var inDir = p[q] - p[q - 1];
                if (inDir.x * dir.x + inDir.y * dir.y != 0) { reason = "수직으로 합류하지 않음"; return false; }
                var prefix = p.GetRange(0, q + 1);
                var junction = p[q];
                if (!CheckShape(prefix, i => Manhattan(prefix[i], junction), out reason, "지선"))
                    return false;
            }
            // 접촉 금지
            var edges = new HashSet<(Vector2Int, Vector2Int)>();
            var cells = new HashSet<Vector2Int>();
            foreach (var p in paths)
                for (int i = 0; i < p.Count; i++)
                {
                    cells.Add(p[i]);
                    if (i > 0) { edges.Add((p[i - 1], p[i])); edges.Add((p[i], p[i - 1])); }
                }
            foreach (var c in cells)
                foreach (var d in Dirs)
                    if (cells.Contains(c + d) && !edges.Contains((c, c + d))) { reason = "경로끼리 맞닿음 (지름길)"; return false; }
            return true;
        }

        // 끊김 없음 · 직진 2칸 이상 후 꺾기 · dist(i)가 늘지 않음(역행 없음) · 마지막 직진 2칸 이상
        static bool CheckShape(List<Vector2Int> path, Func<int, int> dist, out string reason, string label)
        {
            reason = null;
            int run = 0;
            var prev = Vector2Int.zero;
            for (int i = 1; i < path.Count; i++)
            {
                var d = path[i] - path[i - 1];
                if (Math.Abs(d.x) + Math.Abs(d.y) != 1) { reason = $"{label} 끊김"; return false; }
                if (!In(path[i])) { reason = $"{label} 맵 밖"; return false; }
                if (dist(i) > dist(i - 1)) { reason = $"{label} 역행"; return false; }
                if (prev != Vector2Int.zero && d != prev)
                {
                    if (run < 2) { reason = $"{label} 직진 2칸 전에 꺾임"; return false; }
                    run = 0;
                }
                run++;
                prev = d;
            }
            if (run < 2) { reason = $"{label} 마지막 직진이 2칸 미만"; return false; }
            return true;
        }


        static Concept[] BuildConcepts()
        {
            return new[]
            {
                new Concept
                {
                    Code = "A", Name = "플라즈마 회랑",
                    Idea = "긴 직선 구간 끝(꺾이는 칸의 연장선)에만 잔디를 둔다. 그 자리에서 직선을 따라 쏘는 플라즈마가 경로 3칸을 모두 관통하고, 나노봇 감속으로 직선 위에 오래 머물게 한다. 옆으로 붙은 잔디는 띄엄띄엄이라 일반 타워의 사거리 겹치기가 어렵다.",
                    Waves = "일반 몬스터 물량 + 마지막에 빠른 몬스터",
                    Towers = new[] { "plasma", "nano" },
                    Composition = new[] { "plasma", "plasma", "nano", "nano" },
                    Templates = new[]
                    {
                        new[] { 1, 0, 1, 4, 5, 4, 5, 6 }, new[] { 1, 0, 1, 3, 5, 3, 5, 6 }, new[] { 1, 0, 1, 2, 5, 2, 5, 6 },
                        new[] { 2, 0, 2, 4, 4, 4, 4, 6 }, new[] { 1, 0, 1, 5, 5, 5, 5, 6 },
                    },
                    Grass = (paths, v) => Union(Caps(paths), SideEvery(paths, 3, v)),
                    Lines = new List<SpawnLine>
                    {
                        new SpawnLine(1, 0, "normal", 8, 0.8f), new SpawnLine(2, 0, "normal", 10, 0.8f), new SpawnLine(2, 0, "fast", 3, 0.6f, 5f),
                        new SpawnLine(3, 0, "normal", 12, 0.7f), new SpawnLine(3, 0, "fast", 4, 0.5f, 4f),
                    },
                },
                new Concept
                {
                    Code = "B", Name = "화염·CC 소용돌이",
                    Idea = "경로가 지그재그로 접히며 사이 칸(경로 두 구간에 닿는 명당)에만 잔디를 준다. 느린 탱커가 같은 곳을 두 번 지나가므로 부채꼴 화염과 EMP 파동이 두 구간을 함께 태우고, 나노봇 감속이 그 시간을 늘린다.",
                    Waves = "바스티온(느린 탱커) 위주 + 일반",
                    Towers = new[] { "flamethrower", "dust", "phosphorus", "emp", "nano", "cryo" },
                    Composition = new[] { "flamethrower", "flamethrower", "emp", "nano" },
                    Templates = new[]
                    {
                        new[] { 1, 0, 1, 4, 3, 4, 3, 1, 5, 1, 5, 6 }, new[] { 1, 0, 1, 5, 3, 5, 3, 2, 5, 2, 5, 6 }, new[] { 0, 1, 4, 1, 4, 3, 1, 3, 1, 5, 6, 5 },
                        new[] { 1, 0, 1, 3, 4, 3, 4, 1, 6, 1 }, new[] { 2, 0, 2, 2, 5, 2, 5, 4, 1, 4, 1, 6 },
                    },
                    Grass = (paths, v) => Union(Pockets(paths), SideEvery(paths, 4, v)),
                    Lines = new List<SpawnLine>
                    {
                        new SpawnLine(1, 0, "tank", 2, 2.5f), new SpawnLine(1, 0, "normal", 6, 1f, 1f),
                        new SpawnLine(2, 0, "tank", 3, 2.5f), new SpawnLine(2, 0, "normal", 8, 1f, 1f),
                        new SpawnLine(3, 0, "tank", 4, 2f), new SpawnLine(3, 0, "normal", 10, 0.9f, 1f),
                    },
                },
                new Concept
                {
                    Code = "C", Name = "2열 진지",
                    Idea = "나무를 경로에서 두 칸 뒤로 물려 잔디를 2줄로 깐다. 앞 줄(경로 바로 옆)에는 바로 앞 한 줄만 공격하는 화염방사를, 뒷줄에는 사거리 2의 나노봇·EMP를 세워 화염이 닿는 칸에 감속·파동을 겹친다.",
                    Waves = "일반 몬스터 대량 + 탱커 약간",
                    Towers = new[] { "flamethrower", "dust", "phosphorus", "nano", "emp" },
                    Composition = new[] { "flamethrower", "flamethrower", "nano", "emp" },
                    Templates = new[]
                    {
                        new[] { 0, 1, 6, 1 }, new[] { 0, 3, 6, 3 }, new[] { 1, 0, 1, 3, 5, 3, 5, 6 },
                        new[] { 1, 0, 1, 2, 5, 2, 5, 6 }, new[] { 0, 1, 5, 1, 5, 5, 0, 5 },
                    },
                    Grass = (paths, v) => Ring(paths, 2),
                    Lines = new List<SpawnLine>
                    {
                        new SpawnLine(1, 0, "normal", 10, 0.7f), new SpawnLine(2, 0, "normal", 12, 0.7f), new SpawnLine(2, 0, "tank", 1, 1f, 6f),
                        new SpawnLine(3, 0, "normal", 14, 0.6f), new SpawnLine(3, 0, "tank", 2, 2f, 5f),
                    },
                },
                new Concept
                {
                    Code = "D", Name = "급습 단거리",
                    Idea = "스타트에서 넥서스까지 4~6칸뿐이다. 빠른 몬스터가 몇 초 만에 도착하므로 사거리 3으로 멀리서부터 때리는 전기·번개와, 맞히면 멈춰 세우는 냉기·전기 정지가 필요하다. 잔디는 넉넉하게 두 줄.",
                    Waves = "해머헤드(빠른 몬스터) 떼",
                    Towers = new[] { "electro", "lightning", "cryo" },
                    Composition = new[] { "electro", "lightning", "electro", "cryo" },
                    Multi = new[]
                    {
                        new[] { new[] { 3, 0, 3, 4 } },
                        new[] { new[] { 0, 3, 3, 3, 3, 5 } },
                        new[] { new[] { 0, 3, 3, 3 }, new[] { 3, 0, 3, 3 } },
                        new[] { new[] { 1, 0, 1, 2, 4, 2 } },
                        new[] { new[] { 0, 2, 3, 2, 3, 4 }, new[] { 6, 4, 3, 4 } },
                    },
                    Grass = (paths, v) => Ring(paths, 2),
                    Lines = new List<SpawnLine>
                    {
                        new SpawnLine(1, 0, "fast", 6, 0.5f), new SpawnLine(2, 0, "fast", 8, 0.45f), new SpawnLine(2, 0, "normal", 4, 1f, 2f),
                        new SpawnLine(3, 0, "fast", 12, 0.4f), new SpawnLine(3, 1, "fast", 6, 0.5f, 1f),
                    },
                },
                new Concept
                {
                    Code = "E", Name = "정예 사냥",
                    Idea = "경로를 뱀처럼 길게 감아 몬스터가 오래 머물지만, 잔디는 띄엄띄엄 몇 칸뿐이다. 체력이 큰 정예 몇 마리를 오래 붙잡고 때리는 미니건(예열 뒤 초당 16발)과 냉기 정지가 강하고, 넓게 뿌리는 범위 타워는 대상이 적어 손해다.",
                    Waves = "바스티온·우란탱크(정예) 소수",
                    Towers = new[] { "minigun", "cryo", "basic" },
                    Composition = new[] { "minigun", "minigun", "cryo", "basic" },
                    Templates = new[]
                    {
                        new[] { 1, 0, 1, 2, 5, 2, 5, 4, 1, 4, 1, 6 }, new[] { 0, 1, 5, 1, 5, 3, 1, 3, 1, 5, 6, 5 }, new[] { 1, 0, 1, 5, 3, 5, 3, 1, 5, 1, 5, 6 },
                        new[] { 5, 0, 5, 2, 1, 2, 1, 4, 5, 4, 5, 6 }, new[] { 0, 5, 4, 5, 4, 3, 1, 3, 1, 1, 6, 1 },
                    },
                    Grass = (paths, v) => SideEvery(paths, 4, v),
                    Lines = new List<SpawnLine>
                    {
                        new SpawnLine(1, 0, "tank", 2, 4f), new SpawnLine(2, 0, "tank", 2, 4f), new SpawnLine(2, 0, "urantank", 1, 1f, 6f),
                        new SpawnLine(3, 0, "urantank", 2, 5f), new SpawnLine(3, 0, "tank", 2, 4f, 2f),
                    },
                },
            };
        }

        // ---------- 맵 만들기 ----------

        public class Variant
        {
            public string Id, Title;
            public Concept Concept;
            public Dictionary<Vector2Int, Tile> Tiles;           // 편집 격자 좌표
            public List<MapBuilderScene.SpawnSetting> Spawns;    // 배율 1
            public MapData Map;
            public string[] Ascii;
        }

        public static List<Variant> BuildStage1() => BuildStage();

        public static List<Variant> BuildStage()
        {
            var list = new List<Variant>();
            Rejected.Clear();
            foreach (var c in Concepts)
            {
                if (c.Layouts != null)
                {
                    int ok = 0;
                    var seen = new HashSet<string>();
                    for (int t = 0; t < c.Layouts.Count && ok < 10; t++)
                    {
                        var v = MakeVariant(c, t, 0, ok + 1, out string why);
                        if (v == null)
                        {
                            Rejected[why] = (Rejected.TryGetValue(why, out int r) ? r : 0) + 1;
                            continue;
                        }
                        if (!seen.Add(string.Join("/", v.Ascii)))
                            continue;
                        list.Add(v);
                        ok++;
                    }
                    if (ok < 10)
                        Debug.LogWarning($"[LevelDesign] {c.Code}: 규칙을 지키는 맵이 {ok}개뿐 (채우려고 규칙을 어기지 않음)");
                    continue;
                }
                int templates = c.Multi != null ? c.Multi.Length : c.Templates.Length;
                int made = 0;
                // 템플릿 × 대칭(원본 → 좌우 반전 → 180° → …)에서 10개. 경로가 맞닿아 지름길이 생기는 조합은 건너뛴다
                foreach (int sym in new[] { 0, 4, 2, 6, 1, 5, 3, 7 })
                    for (int t = 0; t < templates && made < 10; t++)
                    {
                        var v = MakeVariant(c, t, sym, made + 1, out _);
                        if (v == null)
                            continue;
                        list.Add(v);
                        made++;
                    }
                if (made < 10)
                    Debug.LogWarning($"[LevelDesign] {c.Code} 변형 {made}개만 만듦");
            }
            return list;
        }

        public static readonly Dictionary<string, int> Rejected = new Dictionary<string, int>(); // 규칙 위반으로 버린 후보 (이유별)

        static Variant MakeVariant(Concept c, int template, int sym, int number, out string why)
        {
            why = null;
            int[][] raw = c.Layouts != null ? c.Layouts[template] : c.Multi != null ? c.Multi[template] : new[] { c.Templates[template] };
            var paths = new List<Vector2Int>[raw.Length];
            for (int k = 0; k < raw.Length; k++)
            {
                paths[k] = Walk(raw[k]);
                for (int i = 0; i < paths[k].Count; i++)
                    paths[k][i] = Sym(paths[k][i], sym);
            }
            if (Stage >= 3 && !ValidateLayout(paths, out why))
                return null;
            var pathSet = new HashSet<Vector2Int>(paths.SelectMany(p => p));
            var grass = c.Grass(paths, number);
            var local = new Dictionary<Vector2Int, Tile>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    var p = new Vector2Int(x, y);
                    local[p] = pathSet.Contains(p) ? Tile.Path : grass.Contains(p) ? Tile.Grass : Tile.Tree;
                }
            var nexus = paths[0][paths[0].Count - 1];
            local[nexus] = Tile.Nexus;
            var spawns = new List<MapBuilderScene.SpawnSetting>();
            for (int k = 0; k < paths.Length; k++)
            {
                local[paths[k][0]] = Tile.Spawn;
                spawns.Add(new MapBuilderScene.SpawnSetting { cell = paths[k][0] + Offset, weight = 1f });
            }
            var tiles = local.ToDictionary(kv => kv.Key + Offset, kv => kv.Value);
            foreach (var l in c.Lines)
            {
                int s = Mathf.Min(l.Spawn, spawns.Count - 1);
                spawns[s].lines.Add(new MapBuilderScene.Line { wave = l.Wave, monsterId = l.Monster, count = l.Count, interval = l.Interval, delay = l.Delay });
            }
            var map = MapBuilderScene.BuildMap(tiles, spawns, out string error);
            if (map == null)
            {
                why = "맵 제작 씬 검사: " + error;
                return null;
            }
            // 경로 계산(최단 경로)이 설계한 경로와 같아야 한다 (경로끼리 맞닿으면 지름길이 생긴다)
            for (int k = 0; k < paths.Length; k++)
                if (map.MonsterPaths[k].Count != paths[k].Count)
                {
                    why = "지름길이 생김";
                    return null;
                }
            var v = new Variant
            {
                Id = $"S{Stage}_{c.Code}{number:00}", Concept = c, Tiles = tiles, Spawns = spawns, Map = map,
                Title = $"{Stage}단계 {c.Code}{number:00} {c.Name}",
            };
            v.Ascii = Ascii(local);
            return v;
        }

        static List<Vector2Int> Walk(int[] xy)
        {
            var path = new List<Vector2Int> { new Vector2Int(xy[0], xy[1]) };
            for (int i = 2; i < xy.Length; i += 2)
            {
                var target = new Vector2Int(xy[i], xy[i + 1]);
                var cur = path[path.Count - 1];
                var step = new Vector2Int(Math.Sign(target.x - cur.x), Math.Sign(target.y - cur.y));
                while (cur != target)
                {
                    cur += step;
                    path.Add(cur);
                }
            }
            return path;
        }

        // 7×7 대칭 8가지: 0~3 = 90° 회전 횟수, 4~7 = 좌우 반전 뒤 회전
        static Vector2Int Sym(Vector2Int p, int k)
        {
            int n = Size - 1;
            if (k >= 4)
                p = new Vector2Int(n - p.x, p.y);
            for (int r = 0; r < (k & 3); r++)
                p = new Vector2Int(p.y, n - p.x);
            return p;
        }

        static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        static bool In(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < W && p.y < H;

        static HashSet<Vector2Int> Union(params HashSet<Vector2Int>[] sets)
        {
            var u = new HashSet<Vector2Int>();
            foreach (var s in sets) u.UnionWith(s);
            return u;
        }

        // 직선 구간의 연장선 칸 (구간 앞·뒤): 플라즈마가 직선을 따라 쏠 수 있는 자리
        static HashSet<Vector2Int> Caps(List<Vector2Int>[] paths)
        {
            var all = new HashSet<Vector2Int>(paths.SelectMany(p => p));
            var caps = new HashSet<Vector2Int>();
            foreach (var p in paths)
            {
                int start = 0;
                for (int i = 1; i <= p.Count; i++)
                {
                    bool end = i == p.Count || (i >= 2 && p[i] - p[i - 1] != p[i - 1] - p[i - 2]);
                    if (!end)
                        continue;
                    var a = p[start];
                    var b = p[i - 1];
                    if (b != a)
                    {
                        var d = new Vector2Int(Math.Sign(b.x - a.x), Math.Sign(b.y - a.y));
                        foreach (var c in new[] { b + d, a - d })
                            if (In(c) && !all.Contains(c))
                                caps.Add(c);
                    }
                    start = i - 1;
                }
            }
            return caps;
        }

        // 경로 n칸마다 옆 칸 하나 (띄엄띄엄 부지). v로 시작 위치를 바꿔 변형마다 다르게
        static HashSet<Vector2Int> SideEvery(List<Vector2Int>[] paths, int every, int v)
        {
            var all = new HashSet<Vector2Int>(paths.SelectMany(p => p));
            var set = new HashSet<Vector2Int>();
            foreach (var p in paths)
                for (int i = 1 + v % every; i < p.Count - 1; i += every)
                {
                    var dir = p[i + 1] - p[i];
                    var side = (v / 2) % 2 == 0 ? new Vector2Int(-dir.y, dir.x) : new Vector2Int(dir.y, -dir.x);
                    foreach (var c in new[] { p[i] + side, p[i] - side })
                        if (In(c) && !all.Contains(c))
                        {
                            set.Add(c);
                            break;
                        }
                }
            return set;
        }

        // 경로 두 구간 이상에 닿는 칸 (지그재그 사이 명당)
        static HashSet<Vector2Int> Pockets(List<Vector2Int>[] paths)
        {
            var seg = new Dictionary<Vector2Int, int>();
            int s = 0;
            foreach (var p in paths)
            {
                s++;
                for (int i = 0; i < p.Count; i++)
                {
                    if (i >= 2 && p[i] - p[i - 1] != p[i - 1] - p[i - 2])
                        s++;
                    if (!seg.ContainsKey(p[i]))
                        seg[p[i]] = s;
                }
            }
            var set = new HashSet<Vector2Int>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (seg.ContainsKey(c))
                        continue;
                    var touched = new HashSet<int>();
                    foreach (var d in Dirs)
                        if (seg.TryGetValue(c + d, out int id))
                            touched.Add(id);
                    if (touched.Count >= 2)
                        set.Add(c);
                }
            return set;
        }

        // 경로에서 depth칸 안(체비쇼프 거리)의 모든 칸 = 나무를 depth칸 뒤로 물린 잔디 띠
        static HashSet<Vector2Int> Ring(List<Vector2Int>[] paths, int depth)
        {
            var all = new HashSet<Vector2Int>(paths.SelectMany(p => p));
            var set = new HashSet<Vector2Int>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (all.Contains(c))
                        continue;
                    foreach (var p in all)
                        if (Mathf.Max(Mathf.Abs(p.x - x), Mathf.Abs(p.y - y)) <= depth)
                        {
                            set.Add(c);
                            break;
                        }
                }
            return set;
        }

        static string[] Ascii(Dictionary<Vector2Int, Tile> local)
        {
            var rows = new string[H];
            for (int y = H - 1; y >= 0; y--)
            {
                var sb = new StringBuilder();
                for (int x = 0; x < W; x++)
                {
                    switch (local[new Vector2Int(x, y)])
                    {
                        case Tile.Path: sb.Append('■'); break;
                        case Tile.Spawn: sb.Append('S'); break;
                        case Tile.Nexus: sb.Append('N'); break;
                        case Tile.Grass: sb.Append('□'); break;
                        default: sb.Append('♣'); break;
                    }
                }
                rows[H - 1 - y] = sb.ToString();
            }
            return rows;
        }

        // ---------- 시뮬레이션 ----------

        // 조정 전 수치로 돌리기 (비교용): 시뮬레이터 안의 타워 사본만 예전 값으로 되돌린다. 에셋은 그대로.
        public static bool Legacy;

        static void ApplyLegacy(TowerDef d)
        {
            // 2차 조정 (2단계 시작 전): EMP 피해 1 → 2, 나노필드 안 받는 피해 +20%
            if (d.id == "emp") d.damage = 1f;
            if (d.id == "nano") d.fieldDamageBonus = 0f;
            if (Stage >= 2)
                return; // 2단계의 조정 전 = 1차 조정 뒤 수치
            switch (d.id)
            {
                case "basic": d.damage = 1f; break;
                case "lightning": d.damage = 4f; break;
                case "cryo": d.damage = 3f; d.status.procStacks = 3; break;
                case "electro": d.damage = 4f; d.status.procStacks = 3; break;
                case "minigun": d.damage = 1f; d.spinUpTime = 1.5f; d.overheatAfter = 0f; d.focusBonusPerSec = 0f; break;
            }
            d.coneWholeArea = false; // 예전: 인식 칸 안 부채꼴, 대상 수 제한 없음, 번짐 없음
            d.maxTargets = 0;
            d.status.splashRadius = 0f;
        }

        public struct RunStats
        {
            public int Leak, Kills, Spawned;
            public float Damage;
        }

        public class ComboResult
        {
            public string Key;                         // 예: "basic+cryo"
            public List<string> Picked = new List<string>();
            public float Score;                        // 버틴 가장 큰 배율 (×1도 못 버티면 0)
            public RunStats AtOne;                     // ×1 한 판: 넥서스 피해 · 처치 · 몬스터 수 · 준 피해 (타워 파워량)
        }

        public class GridCell
        {
            public string Set;
            public float Hp;
            public int Waves;
            public ComboResult Result;
        }

        public class MapResult
        {
            public Variant Variant;
            public ComboResult Concept, Off, Free;
            public List<GridCell> Grid = new List<GridCell>(); // 3단계: 체력 배율 × 웨이브 수 → 버틴 몬스터 양 배율
            public List<ComboResult> Combos = new List<ComboResult>();
        }

        // 시뮬레이터: 매니저와 풀은 한 번만 만들고 판마다 ResetForNewRun으로 돌려 쓴다 (게임과 같은 방식, 빠르다)
        class Sim
        {
            public GameConfig Config;
            public MonsterDef[] Monsters;
            public TowerDef[] Towers;
            public Dictionary<string, TowerDef> TowerById = new Dictionary<string, TowerDef>();
            public Board Board;
            public MonsterManager MonsterManager;
            public TowerManager TowerManager;
            public PlayerState Player;
            public int Leak;
            public float[] BaseHp;   // 기본 체력 (체력 배율을 바꿀 때 기준)
            public readonly List<WaveSpawn> Buffer = new List<WaveSpawn>();
        }

        static void SetHpScale(Sim sim, float scale)
        {
            for (int i = 0; i < sim.Monsters.Length; i++)
                sim.Monsters[i].hp = sim.BaseHp[i] * scale;
        }

        static Sim CreateSim()
        {
            var sim = new Sim();
            sim.Config = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset"));
            var r = sim.Config.rules;
            r.hpMapScale = new[] { 1f };
            r.hpComplexityScale = new[] { 1f };
            r.hpSecondFloorScale = 1f;
            r.hpPerWave = 0f;
            sim.Config.performance.monsterPoolPrewarm = 128;
            sim.Config.performance.projectilePoolPrewarm = 64;
            sim.Config.performance.towerPoolPrewarm = 4;
            sim.Config.performance.effectPoolPrewarm = 1;
            sim.Config.nexusHp = 99999;
            sim.Monsters = MonsterIds.Select(id =>
            {
                var m = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<MonsterDef>("Assets/Data/Monsters/" + id + ".asset"));
                m.name = id;
                m.hp *= HpScale;
                m.viewPrefab = null;
                m.spawnIdleSec = 0f;
                return m;
            }).ToArray();
            sim.Towers = AllTowers.Select(id =>
            {
                var d = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/" + id + ".asset"));
                d.name = id;
                d.viewPrefab = null;
                d.projectilePrefab = null;
                d.muzzleFxPrefab = null;
                d.chargeFxPrefab = null;
                d.hitFxPrefab = null;
                d.fieldFxPrefab = null;
                d.status = JsonUtility.FromJson<StatusEffect>(JsonUtility.ToJson(d.status)); // 사본끼리 공유하지 않게
                d.status.fxPrefab = null;
                if (Legacy)
                    ApplyLegacy(d);
                return d;
            }).ToArray();
            foreach (var d in sim.Towers)
                sim.TowerById[d.id] = d;
            sim.BaseHp = sim.Monsters.Select(m => m.hp / HpScale).ToArray();

            var data = new GameData();
            data.Init(sim.Config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"), sim.Monsters, sim.Towers);
            sim.Board = new Board();
            sim.MonsterManager = new MonsterManager();
            sim.TowerManager = new TowerManager();
            sim.Player = new PlayerState();
            sim.Board.Init(data);
            sim.Player.Init(data);
            sim.MonsterManager.Init(data, sim.Board);
            sim.TowerManager.Init(data, sim.Board, sim.MonsterManager, sim.Player);
            sim.TowerManager.TowerChanged += sim.Board.HandleTowerChanged;
            sim.MonsterManager.ReachedExit += e => sim.Leak += e.Damage;
            return sim;
        }

        static void DestroySim(Sim sim)
        {
            foreach (var d in sim.Towers) UnityEngine.Object.DestroyImmediate(d);
            foreach (var m in sim.Monsters) UnityEngine.Object.DestroyImmediate(m);
            UnityEngine.Object.DestroyImmediate(sim.Config);
        }

        // 모든 경우의 수: 타워 11종에서 1~4종을 고른 조합 (11 + 55 + 165 + 330 = 561가지).
        // 조합의 타워를 1개씩 먼저 놓고, 남은 칸(4개까지)은 그 조합 안에서 AI가 가치가 큰 타워로 채운다 (1종 = 같은 타워 4개, 2종 = 보통 2 + 2).
        public static List<string[]> Combos(int maxKinds = 4)
        {
            var list = new List<string[]>();
            int n = AllTowers.Length;
            for (int a = 0; a < n; a++)
            {
                list.Add(new[] { AllTowers[a] });
                if (maxKinds < 2) continue;
                for (int b = a + 1; b < n; b++)
                {
                    list.Add(new[] { AllTowers[a], AllTowers[b] });
                    if (maxKinds < 3) continue;
                    for (int c = b + 1; c < n; c++)
                    {
                        list.Add(new[] { AllTowers[a], AllTowers[b], AllTowers[c] });
                        if (maxKinds < 4) continue;
                        for (int d = c + 1; d < n; d++)
                            list.Add(new[] { AllTowers[a], AllTowers[b], AllTowers[c], AllTowers[d] });
                    }
                }
            }
            return list;
        }

        static Sim shared;

        public static MapResult Evaluate(Variant v, int maxKinds = 4)
        {
            var sim = shared ?? CreateSim();
            try
            {
                var conceptSet = new HashSet<string>(v.Concept.Towers);
                var off = new string[TowerCount][];
                var free = new string[TowerCount][];
                for (int i = 0; i < TowerCount; i++)
                {
                    int tier = sim.TowerById[v.Concept.Composition[i]].tier;
                    free[i] = AllTowers.Where(t => sim.TowerById[t].tier == tier).ToArray();
                    off[i] = free[i].Where(t => !conceptSet.Contains(t)).ToArray();
                }
                var result = new MapResult
                {
                    Variant = v,
                    Concept = RunSet(sim, v, "컨셉", v.Concept.Composition, null, null),
                    Off = RunSet(sim, v, "비컨셉", null, off, null),
                    Free = RunSet(sim, v, "전체", null, free, null),
                };
                foreach (var combo in Combos(maxKinds))
                    result.Combos.Add(RunSet(sim, v, string.Join("+", combo), null, null, combo));
                if (Stage >= 3)
                {
                    // 난이도 표: 체력 배율 × 웨이브 수마다 버틴 몬스터 양 배율 (컨셉·비컨셉·전체)
                    int savedWaves = WaveCount;
                    try
                    {
                        foreach (float h in HpGrid)
                            foreach (int w in WaveGrid)
                            {
                                SetHpScale(sim, HpScale * h);
                                WaveCount = w;
                                result.Grid.Add(new GridCell { Set = "컨셉", Hp = HpScale * h, Waves = w, Result = RunSet(sim, v, "컨셉", v.Concept.Composition, null, null) });
                                result.Grid.Add(new GridCell { Set = "비컨셉", Hp = HpScale * h, Waves = w, Result = RunSet(sim, v, "비컨셉", null, off, null) });
                                result.Grid.Add(new GridCell { Set = "전체", Hp = HpScale * h, Waves = w, Result = RunSet(sim, v, "전체", null, free, null) });
                            }
                    }
                    finally
                    {
                        SetHpScale(sim, HpScale);
                        WaveCount = savedWaves;
                    }
                }
                return result;
            }
            finally
            {
                if (shared == null)
                    DestroySim(sim);
            }
        }

        // 배치 방식: composition(정해진 4개) / slotPools(칸마다 후보) / combo(조합 1개씩 + 남은 칸은 조합 안에서 AI)
        static ComboResult RunSet(Sim sim, Variant v, string key, string[] composition, string[][] slotPools, string[] combo)
        {
            var res = new ComboResult { Key = key };
            res.AtOne = RunOnce(sim, v, 1f, composition, slotPools, combo, res.Picked);
            if (res.AtOne.Leak >= NexusHp)
                return res; // ×1도 못 버팀
            // 배율은 올릴수록 어려워진다고 보고 이분 탐색 (×1은 통과)
            int lo = 0, hi = Multipliers.Length - 1;
            if (RunOnce(sim, v, Multipliers[hi], composition, slotPools, combo, null).Leak < NexusHp)
                lo = hi;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (RunOnce(sim, v, Multipliers[mid], composition, slotPools, combo, null).Leak < NexusHp)
                    lo = mid;
                else
                    hi = mid;
            }
            res.Score = Multipliers[lo];
            return res;
        }

        static RunStats RunOnce(Sim sim, Variant v, float mul, string[] composition, string[][] slotPools, string[] combo, List<string> picked)
        {
            var run = RunState.CreateNew(sim.Config);
            run.Cash = 999999; // 타워 수를 맞춰 비교 (비용 무시)
            sim.Board.ResetForNewRun(run);
            sim.Player.ResetForNewRun(run);
            sim.MonsterManager.ResetForNewRun(run);
            sim.TowerManager.ResetForNewRun(run);
            sim.Board.Load(v.Map);
            sim.Leak = 0;
            var board = sim.Board;
            var monsters = sim.MonsterManager;
            var towers = sim.TowerManager;

            // 타워 배치
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < TowerCount; i++)
            {
                string id;
                if (composition != null) id = composition[i];
                else if (combo != null) id = i < combo.Length ? combo[i] : PickByValue(sim, board, towers, combo, counts);
                else id = PickByValue(sim, board, towers, slotPools[i], counts);
                if (id == null)
                    break;
                var def = sim.TowerById[id];
                if (!BestSite(board, towers, def, out var cell, out _))
                    continue;
                towers.Build(cell, def);
                counts[id] = (counts.TryGetValue(id, out int c) ? c : 0) + 1;
                picked?.Add(id);
            }

            // 웨이브 (스타트 지점별 줄 동시 진행, 수량 × 배율, 간격 ÷ √배율)
            // 정해 둔 웨이브(3개)보다 많이 돌리면 마지막 웨이브 구성을 웨이브마다 +30%씩 늘려 쓴다
            int defined = v.Concept.Lines.Max(l => l.Wave);
            int waves = Stage >= 3 ? WaveCount : defined;
            int spawned = 0;
            for (int w = 1; w <= waves; w++)
            {
                sim.Buffer.Clear();
                int src = Mathf.Min(w, defined);
                float grow = 1f + 0.3f * Mathf.Max(0, w - defined);
                foreach (var l in v.Concept.Lines)
                    if (l.Wave == src)
                    {
                        int count = Mathf.CeilToInt(l.Count * mul * grow);
                        spawned += count;
                        sim.Buffer.Add(new WaveSpawn(l.Monster, count, l.Interval / Mathf.Sqrt(mul), l.Delay, Mathf.Min(l.Spawn, v.Map.MonsterPaths.Count - 1), true));
                    }
                run.Wave = w;
                monsters.HandleStateChanged(new StateChange(GameState.Wave, 1, w));
                monsters.BeginWave(sim.Buffer.ToArray(), sim.Buffer.Count);
                int guard = 0;
                while ((!monsters.SpawnFinished || monsters.AliveCount > 0) && guard++ < 200000)
                {
                    monsters.TickSpawn(Dt);
                    monsters.TickMove(Dt);
                    towers.TickCombat(Dt);
                    monsters.Cleanup();
                }
                monsters.HandleStateChanged(new StateChange(GameState.Break, 1, w));
                towers.ClearCombat();
                if (sim.Leak >= NexusHp && picked == null)
                    break; // 점수 찾기 중에는 무너지면 바로 끝 (×1 측정 판은 끝까지)
            }
            return new RunStats { Leak = sim.Leak, Kills = monsters.KillCount, Spawned = spawned, Damage = monsters.DamageTaken };
        }

        // ---------- 타워 고르기 (비컨셉·전체 조합) ----------

        // 가치 = 대상 1마리 DPS × 동시에 맞히는 마리 수 × 가동률 + 감속·정지 보정 (LevelTestSimulator와 같은 식), 같은 종류는 조금씩 감쇠
        static string PickByValue(Sim sim, Board board, TowerManager towers, string[] pool, Dictionary<string, int> counts)
        {
            string best = null;
            float bestValue = 0f;
            foreach (var id in pool)
            {
                var def = sim.TowerById[id];
                if (!BestSite(board, towers, def, out _, out int cover))
                    continue;
                float value = Value(def, cover) * Mathf.Pow(0.85f, counts.TryGetValue(id, out int c) ? c : 0);
                if (value > bestValue)
                {
                    bestValue = value;
                    best = id;
                }
            }
            return best;
        }

        static float Value(TowerDef d, int cover)
        {
            float dps = d.damage * Mathf.Max(1, d.burstCount) / (d.interval + d.chargeTime);
            if (d.spinUpTime > 0f)
                dps *= 0.6f; // 예열: 대상이 바뀔 때마다 느리게 시작
            int maxTargets = d.attackType == AttackType.Pulse ? 6 : d.attackType == AttackType.Cone || d.attackType == AttackType.Line ? 3 : 1;
            float targets = Mathf.Clamp(cover * 0.7f, 1f, maxTargets);
            float uptime = Mathf.Min(1f, cover / 3f);
            float cc = d.fieldRadius > 0f ? 1.2f : d.status != null && (d.status.kind == StatusKind.Frost || d.status.kind == StatusKind.Static) ? 1f : 0f;
            return (dps * targets + cc) * uptime;
        }

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

        // ---------- 실행 · 저장 ----------

        public static string LibraryFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LevelDesign", "Stage" + Stage));
        static string TempFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp"));

        public static void SaveMaps(List<Variant> variants)
        {
            Directory.CreateDirectory(LibraryFolder);
            foreach (var f in Directory.GetFiles(LibraryFolder, "*.json"))
                File.Delete(f);
            foreach (var v in variants)
            {
                var file = MapBuilderScene.ToFile(v.Tiles, v.Spawns, true, NexusHp);
                file.title = v.Title;
                file.concept = v.Concept.Idea;
                file.towers = string.Join(", ", v.Concept.Composition.Select(t => TowerName[t]));
                MapBuilderScene.WriteFile(Path.Combine(LibraryFolder, v.Id + ".json"), file);
            }
        }

        // 오래 걸리므로 에디터 업데이트마다 조금씩 나눠 돌린다 (에디터가 멈추지 않게). 상태는 JobStatus로 본다.
        //   1) 조정 전 수치: 1종·2종 조합 + 컨셉·비컨셉·전체   2) 조정 후 수치: 1~4종 모든 조합 + 컨셉·비컨셉·전체
        // 결과: Temp/LevelDesign_Stage1_before.json, _after.json (보고서는 Tools/LevelDesign/make_report.py가 만든다)
        public static string JobStatus = "대기";
        static List<Variant> jobVariants;
        static int jobIndex;
        static bool jobLegacy;
        static List<MapResult> jobResults;
        static System.Diagnostics.Stopwatch jobClock;

        public static void StartJob(int stage = 1)
        {
            StopJob();
            UseStage(stage);
            jobVariants = BuildStage();
            SaveMaps(jobVariants);
            jobIndex = 0;
            jobLegacy = stage < 3;
            jobResults = new List<MapResult>();
            jobClock = System.Diagnostics.Stopwatch.StartNew();
            Legacy = jobLegacy;
            shared = CreateSim();
            EditorApplication.update += StepJob;
            JobStatus = "조정 전 0/" + jobVariants.Count;
        }

        public static void StopJob()
        {
            EditorApplication.update -= StepJob;
            if (shared != null)
                DestroySim(shared);
            shared = null;
            Legacy = false;
        }

        static void StepJob()
        {
            try
            {
                var slice = System.Diagnostics.Stopwatch.StartNew();
                while (slice.ElapsedMilliseconds < 400 && jobIndex < jobVariants.Count)
                {
                    jobResults.Add(Evaluate(jobVariants[jobIndex], jobLegacy ? 2 : 4));
                    jobIndex++;
                }
                JobStatus = $"{(jobLegacy ? "조정 전" : "조정 후")} {jobIndex}/{jobVariants.Count} ({jobClock.Elapsed.TotalSeconds:0}초)";
                if (jobIndex < jobVariants.Count)
                    return;
                File.WriteAllText(Path.Combine(TempFolder, $"LevelDesign_Stage{Stage}_{(jobLegacy ? "before" : "after")}.json"), ToJson(jobResults));
                if (jobLegacy)
                {
                    // 조정 후로 이어서
                    DestroySim(shared);
                    Legacy = false;
                    shared = CreateSim();
                    jobLegacy = false;
                    jobIndex = 0;
                    jobResults = new List<MapResult>();
                    return;
                }
                StopJob();
                JobStatus = $"완료 ({jobClock.Elapsed.TotalSeconds:0}초)";
            }
            catch (Exception e)
            {
                StopJob();
                JobStatus = "오류: " + e;
                Debug.LogException(e);
            }
        }

        [MenuItem("TowerDefence/Balance/Level Design Stage 1 (50 maps, all combos)")]
        public static void RunMenu() => StartJob(1);

        [MenuItem("TowerDefence/Balance/Level Design Stage 2 (50 maps, all combos)")]
        public static void RunMenu2() => StartJob(2);

        [MenuItem("TowerDefence/Balance/Level Design 13x9 (rule-checked maps, all combos + HP x waves)")]
        public static void RunMenu3() => StartJob(3);

        static string Fmt(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        static void AppendCombo(StringBuilder sb, ComboResult c)
        {
            sb.Append("{\"key\":\"").Append(c.Key).Append("\",\"score\":").Append(Fmt(c.Score))
              .Append(",\"towers\":[").Append(string.Join(",", c.Picked.Select(t => "\"" + t + "\""))).Append("]")
              .Append(",\"leak\":").Append(c.AtOne.Leak).Append(",\"kills\":").Append(c.AtOne.Kills)
              .Append(",\"spawned\":").Append(c.AtOne.Spawned).Append(",\"damage\":").Append(Fmt(c.AtOne.Damage)).Append('}');
        }

        static string ToJson(List<MapResult> results)
        {
            var sb = new StringBuilder();
            sb.Append("{\"stage\":").Append(Stage).Append(",\"size\":").Append(W).Append(",\"width\":").Append(W).Append(",\"height\":").Append(H)
              .Append(",\"waveCount\":").Append(WaveCount)
              .Append(",\"rejected\":{").Append(string.Join(",", Rejected.Select(kv => "\"" + kv.Key.Replace("\"", "'") + "\":" + kv.Value))).Append('}')
              .Append(",\"hpScale\":").Append(Fmt(HpScale)).Append(",\"towerCount\":").Append(TowerCount)
              .Append(",\"multipliers\":[").Append(string.Join(",", Multipliers.Select(Fmt))).Append("],\"concepts\":[");
            for (int i = 0; i < Concepts.Length; i++)
            {
                var c = Concepts[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"code\":\"").Append(c.Code).Append("\",\"name\":\"").Append(c.Name).Append("\",\"idea\":\"").Append(c.Idea.Replace("\"", "'"))
                  .Append("\",\"waves\":\"").Append(WaveText(c)).Append("\",\"towers\":[").Append(string.Join(",", c.Towers.Select(t => "\"" + t + "\"")))
                  .Append("],\"composition\":[").Append(string.Join(",", c.Composition.Select(t => "\"" + t + "\""))).Append("]}");
            }
            sb.Append("],\"maps\":[");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (i > 0) sb.Append(',');
                int sites = r.Variant.Tiles.Values.Count(t => t == Tile.Grass);
                sb.Append("{\"id\":\"").Append(r.Variant.Id).Append("\",\"concept\":\"").Append(r.Variant.Concept.Code)
                  .Append("\",\"paths\":[").Append(string.Join(",", r.Variant.Map.MonsterPaths.Select(p => p.Count))).Append("],\"sites\":").Append(sites)
                  .Append(",\"ascii\":[").Append(string.Join(",", r.Variant.Ascii.Select(a => "\"" + a + "\""))).Append("],\"sets\":[");
                AppendCombo(sb, r.Concept); sb.Append(',');
                AppendCombo(sb, r.Off); sb.Append(',');
                AppendCombo(sb, r.Free);
                sb.Append("],\"grid\":[");
                for (int k = 0; k < r.Grid.Count; k++)
                {
                    var g = r.Grid[k];
                    if (k > 0) sb.Append(',');
                    sb.Append("{\"set\":\"").Append(g.Set).Append("\",\"hp\":").Append(Fmt(g.Hp)).Append(",\"waves\":").Append(g.Waves)
                      .Append(",\"score\":").Append(Fmt(g.Result.Score)).Append(",\"leak\":").Append(g.Result.AtOne.Leak)
                      .Append(",\"spawned\":").Append(g.Result.AtOne.Spawned).Append('}');
                }
                sb.Append("],\"spawnCount\":").Append(r.Variant.Map.MonsterPaths.Count);
                sb.Append(",\"combos\":[");
                for (int k = 0; k < r.Combos.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    AppendCombo(sb, r.Combos[k]);
                }
                sb.Append("]}");
            }
            return sb.Append("]}").ToString();
        }

        static string WaveText(Concept c)
        {
            var sb = new StringBuilder();
            foreach (var g in c.Lines.GroupBy(l => l.Wave))
            {
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(g.Key).Append("웨이브 ").Append(string.Join(" + ", g.Select(l => $"{MonsterName[l.Monster]} {l.Count}" + (l.Spawn > 0 ? "(스타트 2)" : ""))));
            }
            return sb.ToString();
        }
    }
}
