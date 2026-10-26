using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TowerDefence
{
    // MapProvider.TierReady 인자 (GDD 8-4).
    public readonly struct TierReadyInfo
    {
        public readonly int Tier;

        public TierReadyInfo(int tier) => Tier = tier;
    }

    // MapSpec → IMapGenerator → IMapValidator. Tier마다 맵 1장, 다음 Tier 맵 1장을 미리 만들어 대기 (GDD 8-2).
    // 생성은 백그라운드 스레드(Task.Run). 스레드를 못 쓰면 Poll에서 프레임당 1장씩 메인 스레드로 만든다.
    // 결과 확인과 TierReady 발행은 메인 스레드의 Poll(GameFlow.Tick이 매 프레임 호출)에서만 한다.
    public class MapProvider
    {
        public event Action<TierReadyInfo> TierReady; // → GameFlow (전환 대기 해제)

        // 맵 1장 생성 작업. 워커 스레드는 Result/Error/ElapsedMs만 쓰고, 메인 스레드는 완료 뒤에만 읽는다.
        sealed class Job
        {
            public int Tier;
            public MapSpec Spec;
            public Task Task;          // 스레드 모드
            public bool Done;          // 메인 스레드 기준 완료 처리 끝
            public MapData Result;
            public string Error;
            public double ElapsedMs;
        }

        readonly Dictionary<int, Job> jobs = new Dictionary<int, Job>();
        readonly List<Job> waiting = new List<Job>(); // 아직 완료 처리 안 된 작업

        GameData data;
        IMapGenerator generator;
        IMapValidator validator;
        RunState run;
        bool useThread;
        int runSeed;

        // 0이 아니면 판 시드를 이 값으로 고정 (버그 재현용). 0이면 매 판 무작위.
        public int SeedOverride { get; set; }
        public int RunSeed => runSeed;

        // 생성기가 연결돼 있는지 (단위 테스트처럼 생성기 없이 흐름만 돌릴 때는 맵 교체를 건너뛴다).
        public bool HasGenerator => generator != null;

        public void Init(GameData data, IMapGenerator generator, IMapValidator validator)
        {
            this.data = data;
            this.generator = generator;
            this.validator = validator;
            useThread = data.Config != null && data.Config.performance.mapGenUseThread
                        && Application.platform != RuntimePlatform.WebGLPlayer;
        }

        // 새 판: 맵 풀과 시드를 비우고 Tier 1(+ 다음 Tier)을 준비한다.
        // 진행 중이던 백그라운드 작업은 멈출 수 없으므로 결과를 버린다 (목록에서 빼면 아무도 읽지 않음).
        public void ResetForNewRun(RunState run)
        {
            this.run = run;
            jobs.Clear();
            waiting.Clear();
            runSeed = SeedOverride != 0 ? SeedOverride : Environment.TickCount;
            Debug.Log($"[MapProvider] 새 판 시드 {runSeed} ({(useThread ? "백그라운드 스레드" : "메인 스레드 분할")})");
            EnterTier(1);
        }

        // Tier의 첫 층에 들어설 때: 이 Tier 맵이 없으면 만들고, 다음 Tier 맵을 미리 만든다.
        public void EnterTier(int tier)
        {
            Prepare(tier);
            var config = data.Config;
            if (config != null && config.mapPool.prefetchNextTier && tier < LevelRules.GetTier(config, config.finalFloor))
                Prepare(tier + 1);
        }

        // 이 Tier 맵 생성을 시작한다 (이미 있거나 진행 중이면 무시).
        public void Prepare(int tier)
        {
            if (jobs.ContainsKey(tier) || data.Config == null || generator == null)
                return;

            var spec = new MapSpec(LevelRules.GetMapSize(data.Config, tier), LevelRules.GetComplexity(data.Config, tier), SeedFor(tier), data.MapGenRules);
            var job = new Job { Tier = tier, Spec = spec };
            jobs.Add(tier, job);
            waiting.Add(job);

            if (useThread)
            {
                var gen = generator;
                var val = validator;
                job.Task = Task.Run(() => Run(job, gen, val));
            }
            // 스레드를 못 쓰면 Poll에서 메인 스레드로 1장씩 만든다.
        }

        // 메인 스레드에서 매 프레임 호출: 끝난 작업을 확인하고 TierReady를 발행한다.
        // 스레드를 못 쓰는 경우 프레임당 최대 1장만 만든다 (정본 생성기는 한 번에 끝나는 함수라 더 잘게 나눌 수 없다).
        public void Poll()
        {
            bool ranOnMainThread = false;
            for (int i = 0; i < waiting.Count; i++)
            {
                var job = waiting[i];
                if (job.Task == null)
                {
                    if (ranOnMainThread)
                        continue;
                    Run(job, generator, validator);
                    ranOnMainThread = true;
                }
                else if (!job.Task.IsCompleted)
                {
                    continue;
                }
                else if (job.Task.IsFaulted)
                {
                    job.Error = job.Task.Exception?.GetBaseException().ToString();
                }

                waiting.RemoveAt(i--);
                Complete(job);
            }
        }

        public bool IsReady(int tier) => jobs.TryGetValue(tier, out var job) && job.Done && job.Result != null;

        // 준비된 맵을 꺼낸다 (GDD 8-3 "MapProvider.Take(tier)"). 준비 전이면 null.
        // 같은 Tier의 3개 층은 같은 맵을 쓰므로 꺼낸 맵은 남겨 두고, 지난 Tier 맵만 버린다.
        public MapData Take(int tier)
        {
            if (!IsReady(tier))
                return null;
            DropTiersBelow(tier);
            return jobs[tier].Result;
        }

        // 이 Tier 맵의 시드 (버그 재현용).
        public int GetSeed(int tier) => SeedFor(tier);

        // 이 Tier 맵 생성에 걸린 시간 (검사 포함, ms). 준비 전이면 -1.
        public double GetGenerationMs(int tier) => jobs.TryGetValue(tier, out var job) && job.Done ? job.ElapsedMs : -1;

        int SeedFor(int tier) => unchecked(runSeed * 486187739 + tier * 16777619);

        void DropTiersBelow(int tier)
        {
            var old = new List<int>();
            foreach (var key in jobs.Keys)
                if (key < tier)
                    old.Add(key);
            foreach (var key in old)
                jobs.Remove(key);
        }

        void Complete(Job job)
        {
            job.Done = true;
            var spec = job.Spec;
            if (job.Result == null)
            {
                // 정본 방식: 생성기 안에서 재시도하고, 끝내 실패하면 맵 없음으로 보고만 한다.
                Debug.LogError($"[MapProvider] Tier {job.Tier} 맵 생성 실패 (N={spec.N}, seed={spec.Seed}): {job.Error}");
                return;
            }
            Debug.Log($"[MapProvider] Tier {job.Tier} 맵 준비 (N={spec.N}, seed={spec.Seed}, {job.ElapsedMs:F2}ms)");
            TierReady?.Invoke(new TierReadyInfo(job.Tier));
        }

        // 생성 + 검사. 워커 스레드에서도 불리므로 Unity 객체에 손대지 않는다.
        static void Run(Job job, IMapGenerator generator, IMapValidator validator)
        {
            var sw = Stopwatch.StartNew();
            var map = generator.Generate(job.Spec);
            if (map == null)
            {
                job.Error = "생성기가 맵을 만들지 못함";
            }
            else if (validator != null)
            {
                var report = validator.Validate(map, job.Spec.Rules);
                if (!report.IsValid)
                {
                    var first = report.Violations[0];
                    job.Error = $"검사 실패 {report.Violations.Count}건, 첫 위반 [{first.RuleId}] {first.Detail}";
                    map = null;
                }
            }
            sw.Stop();
            job.ElapsedMs = sw.Elapsed.TotalMilliseconds;
            job.Result = map;
        }
    }
}
