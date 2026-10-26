#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TowerDefence
{
    // 마지막 층 조건 스트레스 테스트 자동 진행 + 측정 (작업 10, GDD 8-5). MonoBehaviour가 아니다: GameLoop가 프레임마다 부른다.
    //
    //   1) startFloor(9층) 마지막 웨이브를 즉시 끝내 층 전환 → Tier 전환(13×13 → 15×15, 스타트 지점 3개) 프레임 시간 측정
    //   2) 10층 휴식 중 타워 towerCount개를 모든 경로를 따라 부지에 균등 배치 (towerPattern 순서 반복, PreferType 타워 섞음)
    //   3) wavesToMeasure개 웨이브를 실제로 진행하며 프레임 시간·GC Alloc·풀 부족 생성·GameObject 수·Tick 시간 기록
    //   4) 보고서(마크다운 표)를 파일로 쓰고 종료
    //
    // 웨이브 중에는 미리 잡은 배열에만 기록한다 (측정 코드가 GC를 만들지 않게). 문자열은 보고서를 쓸 때만 만든다.
    public sealed class StressTestDriver : IDisposable
    {
        enum Phase { WaitMap, StartGame, ClearFloor, Transition, PlaceTowers, Settle, Baseline, Wave, Report, Done }

        const int MaxFrames = 60000;
        const float AfterLoadWindowSec = 3f; // 맵 교체 뒤 타일 등장 연출을 지켜보는 시간
        const float BaselineSec = 3f;        // 웨이브 전 기준선 (타워 배치, 몬스터 0)

        readonly StressTestSettings settings;
        readonly GameData data;
        readonly GameFlow flow;
        readonly MapProvider maps;
        readonly Board board;
        readonly MonsterManager monsters;
        readonly TowerManager towers;
        RunState run;

        Phase phase;
        float timer;
        int targetFloor;
        int targetTier;

        // 프로파일러 기록기 (LastValue = 직전에 끝난 프레임 값)
        ProfilerRecorder gcRec, gcCountRec, objRec, moveRec, combatRec, spawnRec, viewsRec, loopRec;
        ProfilerRecorder mainRec, renderRec, gpuRec, batchesRec, setPassRec, drawRec, trisRec, shadowRec;
        string objectCounterName;
        long frameStartTicks;

        // Tier 전환
        int mapLoadFrame = -1;
        double mapLoadCpuMs;
        float mapLoadFrameMs = -1f;
        float transitionMaxFrameMs;
        float afterLoadMaxFrameMs;
        float afterLoadTimer;
        int transitionFrames;
        double mapGenMs = -1;

        // 배치
        int mapN, pathLength, siteCount, placedCount, preferCount;

        // 웨이브 기록 (현재 웨이브)
        readonly float[] frameMs = new float[MaxFrames];
        readonly long[] gcBytes = new long[MaxFrames];
        readonly float[] moveMs = new float[MaxFrames];
        readonly float[] combatMs = new float[MaxFrames];
        readonly float[] loopMs = new float[MaxFrames];
        int frames;
        int skipFrames;
        int waveIndex;
        WaveResult current;
        readonly WaveResult[] results;

        // 33ms를 넘은 프레임 (원인 추적용). where는 문자열 상수라 기록 때 할당이 없다.
        struct Spike
        {
            public string Where;
            public int Wave;
            public float Ms;
            public double AtSec;   // 그 구간 시작부터 경과
            public GameState State;
            public int Alive;
        }
        const float SpikeMs = 33f;
        readonly Spike[] spikes = new Spike[40];
        int spikeCount;
        double sectionSec;

        // 프로파일러 기록 (settings.profileCaptureSec > 0일 때, 층 전환 시작부터)
        float captureRemaining;
        string capturePath;

        void StartCapture()
        {
            if (settings.profileCaptureSec <= 0f)
                return;
            string dir = Application.isEditor
                ? Path.Combine(Application.dataPath, "..", "StressReports")
                : Path.Combine(Application.dataPath, "..");
            Directory.CreateDirectory(dir);
            capturePath = Path.GetFullPath(Path.Combine(dir, "stress_profile.raw"));
            UnityEngine.Profiling.Profiler.logFile = capturePath;
            UnityEngine.Profiling.Profiler.enableBinaryLog = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            captureRemaining = settings.profileCaptureSec;
        }

        void TickCapture()
        {
            if (captureRemaining <= 0f)
                return;
            captureRemaining -= Time.unscaledDeltaTime;
            if (captureRemaining > 0f)
                return;
            UnityEngine.Profiling.Profiler.enabled = false;
            UnityEngine.Profiling.Profiler.enableBinaryLog = false;
            UnityEngine.Profiling.Profiler.logFile = "";
            Debug.Log($"[Stress] 프로파일러 기록: {capturePath}");
        }

        void NoteSpike(string where, int wave, float ms)
        {
            if (ms <= SpikeMs || spikeCount >= spikes.Length)
                return;
            spikes[spikeCount++] = new Spike
            {
                Where = where, Wave = wave, Ms = ms, AtSec = sectionSec, State = flow.State, Alive = monsters.AliveCount,
            };
        }
        readonly WaveResult baseline = new WaveResult();

        sealed class WaveResult
        {
            public int Wave;
            public int Frames;
            public double Seconds;
            public double AvgFps, MinFps, Low1Fps;
            public long GcTotal, GcMax, GcAllocCount;
            public int GcFrames;
            public int CreatedDuringWave;  // 몬스터 데이터·표시 풀 부족으로 웨이브 중 새로 만든 수
            public int TowerViews, ProjectileViews, EffectViews; // 웨이브 중 늘어난 표시 수
            public long ObjStart, ObjEnd, ObjMin, ObjMax;
            public double MoveAvg, MoveMax, CombatAvg, CombatMax, LoopAvg, LoopMax;
            public double MainAvg, RenderAvg, GpuAvg, BatchesAvg, SetPassAvg, DrawAvg, TrisAvg, ShadowAvg;
            public int PeakAlive, PeakProjectiles, Spawned;
            // 시작 시점 스냅샷
            public int CreatedStart, ViewsCreatedStart, TowerViewsStart, ProjectileViewsStart, EffectViewsStart;
        }

        public StressTestDriver(StressTestSettings settings, GameData data, GameFlow flow, MapProvider maps,
            Board board, MonsterManager monsters, TowerManager towers)
        {
            this.settings = settings;
            this.data = data;
            this.flow = flow;
            this.maps = maps;
            this.board = board;
            this.monsters = monsters;
            this.towers = towers;
            results = new WaveResult[Mathf.Max(1, settings.wavesToMeasure)];
            for (int i = 0; i < results.Length; i++)
                results[i] = new WaveResult();

            board.MapLoaded += HandleMapLoaded;
            flow.LogStates = false;

            if (settings.uncapFrameRate)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }
            if (!Application.isEditor && Array.IndexOf(Environment.GetCommandLineArgs(), "-stressLite") >= 0)
                ApplyLiteRendering(); // 비교용: 빌드 실행 인자로만 (에디터에서는 에셋이 바뀌므로 하지 않음)

            gcRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            gcCountRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
            foreach (var name in new[] { "Game Object Count", "GameObject Count", "Object Count" })
            {
                objRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, name);
                if (objRec.Valid) { objectCounterName = name; break; }
                objRec.Dispose();
            }
            moveRec = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "TD.TickMove");
            combatRec = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "TD.TickCombat");
            spawnRec = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "TD.TickSpawn");
            viewsRec = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "TD.TickViews");
            loopRec = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "TD.GameLoop");
            mainRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Main Thread Frame Time");
            renderRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Render Thread Frame Time");
            gpuRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GPU Frame Time");
            batchesRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPassRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            drawRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            trisRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            shadowRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
        }

        // 비교용 가벼운 렌더 설정 (빌드 메모리 안에서만 바뀐다): SSAO 끔, HDR 끔, 불투명 텍스처 복사 끔.
        string renderMode = "기본 (프로젝트 PC 설정)";

        void ApplyLiteRendering()
        {
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (asset != null)
            {
                asset.supportsHDR = false;
                asset.supportsCameraOpaqueTexture = false;
            }
            foreach (var feature in Resources.FindObjectsOfTypeAll<UnityEngine.Rendering.Universal.ScriptableRendererFeature>())
                if (feature.GetType().Name.Contains("AmbientOcclusion"))
                    feature.SetActive(false);
            renderMode = "가벼움 (SSAO 끔, HDR 끔, 불투명 텍스처 끔 — 비교용)";
        }

        public void Dispose()
        {
            board.MapLoaded -= HandleMapLoaded;
            gcRec.Dispose(); gcCountRec.Dispose(); objRec.Dispose();
            moveRec.Dispose(); combatRec.Dispose(); spawnRec.Dispose(); viewsRec.Dispose(); loopRec.Dispose();
            mainRec.Dispose(); renderRec.Dispose(); gpuRec.Dispose(); batchesRec.Dispose(); setPassRec.Dispose();
            drawRec.Dispose(); trisRec.Dispose(); shadowRec.Dispose();
        }

        // StartNewRun: 새 RunState를 시작 층 마지막 웨이브 직전으로 맞춘다 (flow가 층 계획을 계산하기 전).
        public void PrepareRun(RunState newRun)
        {
            run = newRun;
            var config = data.Config;
            int start = Mathf.Clamp(settings.startFloor, 1, config.finalFloor - 1);
            int g = 0;
            for (int f = 1; f < start; f++)
                g += LevelRules.GetWaveCount(config, f);
            int wavesHere = LevelRules.GetWaveCount(config, start);
            run.Floor = start;
            run.Wave = wavesHere - 1;          // 다음 웨이브가 이 층의 마지막
            run.TotalWaveIndex = g + wavesHere - 1;
            targetFloor = start + 1;
            targetTier = LevelRules.GetTier(config, targetFloor);
            phase = Phase.WaitMap;
        }

        // StartNewRun 끝: 시작 층 Tier 맵과 다음 Tier 맵 생성을 요청한다 (MapProvider는 새 판에 Tier 1부터 준비하므로).
        public void AfterRunReset()
        {
            maps.EnterTier(flow.CurrentPlan.Tier);
        }

        public void BeginFrame()
        {
            frameStartTicks = Stopwatch.GetTimestamp();
        }

        // GameLoop.Update 끝 (모든 Tick 다음).
        public void EndFrame()
        {
            float dtMs = Time.unscaledDeltaTime * 1000f;
            sectionSec += Time.unscaledDeltaTime;
            TickCapture();
            if (phase == Phase.StartGame || phase == Phase.ClearFloor)
                NoteSpike("준비 (시작 층 즉시 클리어)", 0, dtMs);
            switch (phase)
            {
                case Phase.WaitMap:
                    if (board.Map != null && flow.State == GameState.Ready)
                    {
                        flow.RequestStartGame();          // → Break
                        phase = Phase.StartGame;
                    }
                    break;

                case Phase.StartGame:
                    if (flow.State == GameState.Break)
                    {
                        flow.StartNow();                  // 시작 층 마지막 웨이브
                        phase = Phase.ClearFloor;
                    }
                    break;

                case Phase.ClearFloor:
                    if (flow.State == GameState.Wave)
                    {
                        monsters.DebugFinishWave();       // 즉시 클리어 → 다음 프레임 층 전환
                        phase = Phase.Transition;
                        sectionSec = 0;
                        StartCapture();
                        transitionMaxFrameMs = 0f;
                        transitionFrames = 0;
                    }
                    break;

                case Phase.Transition:
                    TickTransition(dtMs);
                    break;

                case Phase.PlaceTowers:
                    PlaceTowers();
                    timer = settings.settleSec;
                    phase = Phase.Settle;
                    break;

                case Phase.Settle:
                    TrackAfterLoad(dtMs);
                    timer -= Time.unscaledDeltaTime;
                    if (timer <= 0f && flow.State == GameState.Break)
                    {
                        if (waveIndex == 0 && baseline.Frames == 0)
                        {
                            BeginRecord(baseline);           // 웨이브 전 기준선: 맵·타워만, 몬스터 0
                            timer = BaselineSec;
                            phase = Phase.Baseline;
                        }
                        else
                        {
                            StartMeasuredWave();
                        }
                    }
                    break;

                case Phase.Baseline:
                    RecordFrame(dtMs);
                    timer -= Time.unscaledDeltaTime;
                    if (timer <= 0f)
                    {
                        FinishRecord(baseline);
                        StartMeasuredWave();
                    }
                    break;

                case Phase.Wave:
                    TickWave(dtMs);
                    break;

                case Phase.Report:
                    WriteReport();
                    phase = Phase.Done;
                    if (settings.quitWhenDone)
                        Quit();
                    break;
            }
        }

        void HandleMapLoaded(MapData map)
        {
            if (phase != Phase.Transition)
                return;
            mapLoadFrame = Time.frameCount;
            afterLoadTimer = AfterLoadWindowSec;
            afterLoadMaxFrameMs = 0f;
        }

        void TickTransition(float dtMs)
        {
            transitionFrames++;
            if (transitionFrames > 1)
            {
                transitionMaxFrameMs = Mathf.Max(transitionMaxFrameMs, dtMs);
                NoteSpike(mapLoadFrame >= 0 && Time.frameCount == mapLoadFrame + 1 ? "층 전환: 맵 교체 프레임" : "층 전환 연출", 0, dtMs);
            }

            if (Time.frameCount == mapLoadFrame)
                mapLoadCpuMs = (Stopwatch.GetTimestamp() - frameStartTicks) * 1000.0 / Stopwatch.Frequency;
            else if (mapLoadFrame >= 0 && Time.frameCount == mapLoadFrame + 1)
                mapLoadFrameMs = dtMs;                // 맵을 교체한 프레임의 전체 시간 (렌더 포함)
            if (mapLoadFrame >= 0 && Time.frameCount > mapLoadFrame + 1)
                TrackAfterLoad(dtMs);

            bool loadMeasured = mapLoadFrame >= 0 && Time.frameCount > mapLoadFrame + 1;
            if (flow.State == GameState.Break && flow.Floor == targetFloor && loadMeasured)
            {
                mapGenMs = maps.GetGenerationMs(targetTier);
                phase = Phase.PlaceTowers;
            }
        }

        void TrackAfterLoad(float dtMs)
        {
            if (afterLoadTimer <= 0f)
                return;
            afterLoadTimer -= dtMs * 0.001f;
            afterLoadMaxFrameMs = Mathf.Max(afterLoadMaxFrameMs, dtMs);
            if (phase != Phase.Transition)
                NoteSpike("맵 교체 후 타일 등장·타워 배치", 0, dtMs);
        }

        // 경로를 따라가며 만나는 부지를 모은 뒤, 그 순서에서 균등 간격으로 towerCount개를 고른다.
        void PlaceTowers()
        {
            mapN = board.N;
            pathLength = board.GetPathLength(0);
            var seen = new bool[mapN, mapN];
            var sites = new System.Collections.Generic.List<Vector2Int>();
            for (int k = 0; k < board.PathCount; k++)
            for (int i = 0; i < board.GetPathLength(k); i++)
            {
                var c = board.GetPathCell(k, i); // 모든 스타트 지점 경로
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var n = new Vector2Int(c.x + dx, c.y + dy);
                        if (!board.InBounds(n) || seen[n.x, n.y] || !board.IsBuildable(n))
                            continue;
                        seen[n.x, n.y] = true;
                        sites.Add(n);
                    }
            }
            // 경로에 붙지 않은 부지도 있으면 뒤에 붙인다.
            for (int x = 0; x < mapN; x++)
                for (int y = 0; y < mapN; y++)
                    if (!seen[x, y] && board.IsBuildable(new Vector2Int(x, y)))
                        sites.Add(new Vector2Int(x, y));
            siteCount = sites.Count;

            var pattern = settings.towerPattern != null && settings.towerPattern.Length > 0 ? settings.towerPattern : settings.towerDefs;
            int want = Mathf.Min(settings.towerCount, sites.Count);
            for (int k = 0; k < want; k++)
            {
                int idx = (int)((long)k * sites.Count / want);
                var def = pattern[k % pattern.Length];
                if (towers.Build(sites[idx], def))
                {
                    placedCount++;
                    if (def.lockMode == TargetLock.PreferType)
                        preferCount++;
                }
            }
            Debug.Log($"[Stress] 타워 {placedCount}개 배치 (PreferType {preferCount}개, 부지 {siteCount}칸, 경로 {pathLength}칸, 맵 {mapN}×{mapN})");
        }

        void StartMeasuredWave()
        {
            current = results[waveIndex];
            current.Wave = flow.Wave + 1;
            current.CreatedStart = monsters.CreatedDuringWave;
            current.ViewsCreatedStart = monsters.ViewsCreatedDuringWave;
            current.TowerViewsStart = towers.TowerViewsCreated;
            current.ProjectileViewsStart = towers.ProjectileViewsCreated;
            current.EffectViewsStart = towers.EffectViewsCreated;
            BeginRecord(current);
            flow.StartNow();
            current.Spawned = monsters.RemainingToSpawn;
            phase = Phase.Wave;
        }

        void BeginRecord(WaveResult r)
        {
            r.ObjStart = objRec.Valid ? objRec.LastValue : -1;
            r.ObjMin = long.MaxValue;
            r.ObjMax = long.MinValue;
            r.PeakAlive = 0;
            r.PeakProjectiles = 0;
            current = r;
            frames = 0;
            skipFrames = 1; // 첫 기록(LastValue)은 시작 직전 프레임 값
            sectionSec = 0;
        }

        void TickWave(float dtMs)
        {
            if (flow.State != GameState.Wave)
            {
                FinishWave();
                return;
            }
            RecordFrame(dtMs);
        }

        void RecordFrame(float dtMs)
        {
            current.PeakAlive = Math.Max(current.PeakAlive, monsters.AliveCount);
            current.PeakProjectiles = Math.Max(current.PeakProjectiles, towers.ProjectileCount);
            if (objRec.Valid)
            {
                long obj = objRec.LastValue;
                if (skipFrames == 0)
                {
                    if (obj < current.ObjMin) current.ObjMin = obj;
                    if (obj > current.ObjMax) current.ObjMax = obj;
                }
            }

            if (skipFrames > 0)
            {
                skipFrames--;
                return;
            }
            if (frames >= MaxFrames)
                return;
            NoteSpike(current == baseline ? "휴식 기준선" : "웨이브", current == baseline ? 0 : current.Wave, dtMs);
            frameMs[frames] = dtMs;
            gcBytes[frames] = gcRec.Valid ? gcRec.LastValue : -1;
            current.GcAllocCount += gcCountRec.Valid ? gcCountRec.LastValue : 0;
            moveMs[frames] = moveRec.Valid ? moveRec.LastValue * 1e-6f : -1f;
            combatMs[frames] = combatRec.Valid ? combatRec.LastValue * 1e-6f : -1f;
            loopMs[frames] = loopRec.Valid ? loopRec.LastValue * 1e-6f : -1f;
            current.MainAvg += mainRec.Valid ? mainRec.LastValue * 1e-6 : 0;
            current.RenderAvg += renderRec.Valid ? renderRec.LastValue * 1e-6 : 0;
            current.GpuAvg += gpuRec.Valid ? gpuRec.LastValue * 1e-6 : 0;
            current.BatchesAvg += batchesRec.Valid ? batchesRec.LastValue : 0;
            current.SetPassAvg += setPassRec.Valid ? setPassRec.LastValue : 0;
            current.DrawAvg += drawRec.Valid ? drawRec.LastValue : 0;
            current.TrisAvg += trisRec.Valid ? trisRec.LastValue : 0;
            current.ShadowAvg += shadowRec.Valid ? shadowRec.LastValue : 0;
            frames++;
        }

        void FinishWave()
        {
            FinishRecord(current);
            var r = current;
            r.CreatedDuringWave = (monsters.CreatedDuringWave - r.CreatedStart) + (monsters.ViewsCreatedDuringWave - r.ViewsCreatedStart);
            r.TowerViews = towers.TowerViewsCreated - r.TowerViewsStart;
            r.ProjectileViews = towers.ProjectileViewsCreated - r.ProjectileViewsStart;
            r.EffectViews = towers.EffectViewsCreated - r.EffectViewsStart;

            waveIndex++;
            if (waveIndex >= results.Length || flow.State != GameState.Break)
            {
                phase = Phase.Report;
            }
            else
            {
                timer = 1f;
                phase = Phase.Settle;
            }
        }

        void FinishRecord(WaveResult r)
        {
            r.Frames = frames;
            r.ObjEnd = objRec.Valid ? objRec.LastValue : -1;

            double sumMs = 0; float maxMs = 0f;
            for (int i = 0; i < frames; i++)
            {
                sumMs += frameMs[i];
                if (frameMs[i] > maxMs) maxMs = frameMs[i];
                if (gcBytes[i] > 0) { r.GcTotal += gcBytes[i]; r.GcFrames++; }
                if (gcBytes[i] > r.GcMax) r.GcMax = gcBytes[i];
                r.MoveAvg += moveMs[i]; r.MoveMax = Math.Max(r.MoveMax, moveMs[i]);
                r.CombatAvg += combatMs[i]; r.CombatMax = Math.Max(r.CombatMax, combatMs[i]);
                r.LoopAvg += loopMs[i]; r.LoopMax = Math.Max(r.LoopMax, loopMs[i]);
            }
            if (frames > 0)
            {
                r.Seconds = sumMs / 1000.0;
                r.AvgFps = frames / r.Seconds;
                r.MinFps = 1000.0 / maxMs;
                var sorted = new float[frames];
                Array.Copy(frameMs, sorted, frames);
                Array.Sort(sorted);
                r.Low1Fps = 1000.0 / sorted[Math.Min(frames - 1, (int)(frames * 0.99))];
                r.MoveAvg /= frames; r.CombatAvg /= frames; r.LoopAvg /= frames;
                r.MainAvg /= frames; r.RenderAvg /= frames; r.GpuAvg /= frames;
                r.BatchesAvg /= frames; r.SetPassAvg /= frames; r.DrawAvg /= frames; r.TrisAvg /= frames; r.ShadowAvg /= frames;
            }
        }

        // ---------- 보고서 ----------

        string DescribeWave(int wave)
        {
            var waves = data.Config.waves;
            if (waves != null)
                foreach (var w in waves)
                {
                    if (w == null || w.floor != targetFloor || w.wave != wave || w.lines == null)
                        continue;
                    var sb = new StringBuilder();
                    foreach (var line in w.lines)
                    {
                        if (sb.Length > 0) sb.Append(" + ");
                        sb.Append(line.monsterId).Append(" ×").Append(line.count);
                    }
                    if (w.lines.Length > 0)
                        sb.Append($" ({w.lines[0].intervalSec:0.##}초 간격)");
                    return sb.ToString();
                }
            return $"공식 규칙 ×{LevelRules.MonsterCount(data.Config, run.TotalWaveIndex)}";
        }

        static string Pass(bool ok) => ok ? "통과" : "**미달**";

        static void AppendFrameRow(StringBuilder sb, string label, WaveResult r)
        {
            string gpu = r.GpuAvg > 0 ? $"{r.GpuAvg:0.0} ms" : "측정 안 됨";
            sb.AppendLine($"| {label} | {r.AvgFps:0} | {r.MainAvg:0.0} ms | {r.RenderAvg:0.0} ms | {gpu} | {r.LoopAvg:0.00} ms | {r.BatchesAvg:0} | {r.SetPassAvg:0} | {r.DrawAvg:0} | {r.TrisAvg / 1000:0}k | {r.ShadowAvg:0} |");
        }

        void WriteReport()
        {
            var sb = new StringBuilder();
            string env = Application.isEditor ? "에디터 플레이 모드" : (Debug.isDebugBuild ? "개발 빌드(Development)" : "릴리스 빌드");
            sb.AppendLine($"# 스트레스 테스트 결과 ({DateTime.Now:yyyy-MM-dd HH:mm})");
            sb.AppendLine();
            sb.AppendLine($"- 환경: {env}, {Application.platform}, {SystemInfo.processorType}, {SystemInfo.graphicsDeviceName}, 화면 {Screen.width}×{Screen.height}, 수직동기 {(QualitySettings.vSyncCount == 0 ? "끔" : "켬")}");
            sb.AppendLine($"- 렌더 설정: {renderMode}, 그림자 캐스케이드 {(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)?.shadowCascadeCount}");
            sb.AppendLine($"- 조건: {targetFloor}층 (Tier {targetTier}), 맵 {mapN}×{mapN}, 경로 {pathLength}칸, 비어 있던 부지 {siteCount}칸");
            sb.AppendLine($"- 타워: {placedCount}개 배치 (그중 PreferType + typePriority[\"Special\"] {preferCount}개), 경로를 따라 부지에 균등 간격");
            sb.AppendLine($"- GameObject 수 기록기: {objectCounterName ?? "없음"}");
            sb.AppendLine();
            sb.AppendLine($"## Tier 전환 ({targetFloor - 1}층 → {targetFloor}층, {mapN}×{mapN}, 스타트 지점 {board.PathCount}개)");
            sb.AppendLine();
            sb.AppendLine("| 항목 | 측정값 | 기준 | 결과 |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine($"| {mapN}×{mapN} 맵 생성 시간 (백그라운드 스레드, 검사 포함) | {mapGenMs:0.0} ms | 메인 스레드 밖 | {(mapGenMs >= 0 ? "통과" : "측정 실패")} |");
            sb.AppendLine($"| 맵 교체 프레임 시간 (렌더 포함) | {mapLoadFrameMs:0.0} ms | ≤ 33 ms | {Pass(mapLoadFrameMs >= 0 && mapLoadFrameMs <= 33f)} |");
            sb.AppendLine($"| 맵 교체 프레임의 GameLoop CPU (Board.Load + 맵 표시 재구성) | {mapLoadCpuMs:0.00} ms | - | - |");
            sb.AppendLine($"| 층 전환 연출 중 최대 프레임 | {transitionMaxFrameMs:0.0} ms | ≤ 33 ms | {Pass(transitionMaxFrameMs <= 33f)} |");
            sb.AppendLine($"| 교체 후 {AfterLoadWindowSec:0}초 (타일 등장 연출) 최대 프레임 | {afterLoadMaxFrameMs:0.0} ms | ≤ 33 ms | {Pass(afterLoadMaxFrameMs <= 33f)} |");
            sb.AppendLine();
            sb.AppendLine("## 웨이브 (290마리)");
            sb.AppendLine();
            sb.AppendLine("| 웨이브 구성 | 시간 | 평균 FPS | 최저 FPS | 1% Low FPS | GC Alloc 합계 / 발생 프레임 / 최대 | 웨이브 중 Instantiate (풀 부족·표시) | GameObject 수 (시작→끝, 최소~최대) | TickMove 평균/최대 | TickCombat 평균/최대 | GameLoop 평균/최대 | 최대 동시 몬스터 / 투사체 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            for (int i = 0; i < waveIndex; i++)
            {
                var r = results[i];
                int inst = r.CreatedDuringWave + r.TowerViews + r.ProjectileViews + r.EffectViews;
                sb.AppendLine($"| {r.Wave}웨이브: {DescribeWave(r.Wave)} | {r.Seconds:0.0}초 ({r.Frames}프레임) | {r.AvgFps:0} | {r.MinFps:0} | {r.Low1Fps:0} | {r.GcTotal} B / {r.GcFrames} / {r.GcMax} B | {inst} | {r.ObjStart}→{r.ObjEnd}, {r.ObjMin}~{r.ObjMax} | {r.MoveAvg:0.000}/{r.MoveMax:0.000} ms | {r.CombatAvg:0.000}/{r.CombatMax:0.000} ms | {r.LoopAvg:0.000}/{r.LoopMax:0.000} ms | {r.PeakAlive} / {r.PeakProjectiles} |");
            }
            sb.AppendLine();
            sb.AppendLine("## 프레임 구성 (병목 확인)");
            sb.AppendLine();
            sb.AppendLine("| 구간 | 평균 FPS | 메인 스레드 | 렌더 스레드 | GPU | GameLoop | 배치 | SetPass | 드로우 콜 | 삼각형 | 그림자 캐스터 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            AppendFrameRow(sb, "휴식 기준선 (타워만, 몬스터 0)", baseline);
            for (int i = 0; i < waveIndex; i++)
                AppendFrameRow(sb, $"{results[i].Wave}웨이브", results[i]);
            sb.AppendLine();
            sb.AppendLine($"## 33ms 넘은 프레임 ({spikeCount}개{(spikeCount == spikes.Length ? ", 앞쪽만 기록" : "")})");
            sb.AppendLine();
            if (spikeCount > 0)
            {
                sb.AppendLine("| 구간 | 프레임 시간 | 구간 시작 후 | 상태 | 살아있는 몬스터 |");
                sb.AppendLine("|---|---|---|---|---|");
                for (int i = 0; i < spikeCount; i++)
                {
                    var k = spikes[i];
                    string where = k.Wave > 0 ? $"{k.Wave}웨이브" : k.Where;
                    sb.AppendLine($"| {where} | {k.Ms:0.0} ms | {k.AtSec:0.00}초 | {k.State} | {k.Alive} |");
                }
                sb.AppendLine();
            }
            sb.AppendLine("## 기준 판정 (GDD 8-5)");
            sb.AppendLine();
            double minAvg = double.MaxValue, minMin = double.MaxValue; long gcAll = 0; int instAll = 0; bool objStable = true;
            for (int i = 0; i < waveIndex; i++)
            {
                var r = results[i];
                minAvg = Math.Min(minAvg, r.AvgFps);
                minMin = Math.Min(minMin, r.MinFps);
                gcAll += r.GcTotal;
                instAll += r.CreatedDuringWave + r.TowerViews + r.ProjectileViews + r.EffectViews;
                if (r.ObjMin != r.ObjMax || r.ObjStart != r.ObjEnd) objStable = false;
            }
            sb.AppendLine("| 기준 | 측정 | 결과 |");
            sb.AppendLine("|---|---|---|");
            sb.AppendLine($"| 60fps 유지 | 평균 최저 {minAvg:0} / 순간 최저 {minMin:0} FPS | {Pass(minMin >= 60)} |");
            sb.AppendLine($"| 웨이브 중 GC Alloc 0B/프레임 | 합계 {gcAll} B | {Pass(gcAll == 0)} |");
            sb.AppendLine($"| 웨이브 중 Instantiate/Destroy 0회 | 풀 부족 생성 {instAll}회, GameObject 수 {(objStable ? "변화 없음" : "변화 있음")} | {Pass(instAll == 0 && objStable)} |");
            sb.AppendLine($"| Tier 전환 프레임 ≤ 33ms | {mapLoadFrameMs:0.0} ms | {Pass(mapLoadFrameMs >= 0 && mapLoadFrameMs <= 33f)} |");

            string dir = Application.isEditor
                ? Path.Combine(Application.dataPath, "..", "StressReports")
                : Path.Combine(Application.dataPath, "..");
            Directory.CreateDirectory(dir);
            string path = Path.GetFullPath(Path.Combine(dir, $"stress_{DateTime.Now:yyyyMMdd_HHmmss}.md"));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[Stress] 보고서: {path}\n{sb}");
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
#endif
