using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 작업 9: 층 전환(같은 Tier / Tier 변경 맵 교체) + 타워 회수 목록(재건축·철거·만료).
    // 실제 맵 생성기(백그라운드)로 7×7 → 9×9 교체까지 확인한다. 표시 없이 동작.
    public class FloorTransitionTests
    {
        GameConfig config;
        GameData data;
        MapProvider maps;
        Board board;
        MonsterManager monsters;
        TowerManager towers;
        PlayerState player;
        GameFlow flow;
        BuildController build;
        TowerDef basic;

        readonly List<RecoveryChange> recoveryChanges = new List<RecoveryChange>();
        readonly List<RecoveryExpire> recoveryExpires = new List<RecoveryExpire>();
        int mapWaits;

        [SetUp]
        public void SetUp()
        {
            config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            basic = AssetDatabase.LoadAssetAtPath<TowerDef>("Assets/Data/Towers/basic.asset");
            data = new GameData();
            var rules = AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset");
            data.Init(config, rules, new[] { TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset") }, new[] { basic });

            maps = new MapProvider();
            board = new Board();
            monsters = new MonsterManager();
            towers = new TowerManager();
            player = new PlayerState();
            flow = new GameFlow { LogStates = false };
            build = new BuildController();

            maps.Init(data, new LayoutMapGenerator(), new LayoutMapValidator());
            maps.SeedOverride = 12345;
            board.Init(data);
            player.Init(data);
            monsters.Init(data, board);
            towers.Init(data, board, monsters, player);
            flow.Init(data, maps, board, monsters, towers);
            build.Init(data, flow, board, towers, player, null);

            // GameLoop와 같은 구독 (화면 제외)
            flow.StateChanged += monsters.HandleStateChanged;
            flow.StateChanged += towers.HandleStateChanged;
            flow.StateChanged += build.HandleStateChanged;
            flow.MapWaitChanged += w => { if (w) mapWaits++; };
            maps.TierReady += flow.HandleTierReady;
            monsters.Killed += player.HandleKilled;
            monsters.ReachedExit += player.HandleReachedExit;
            player.Defeated += flow.HandleDefeated;
            towers.TowerChanged += board.HandleTowerChanged;
            towers.RecoveryExpired += player.HandleRecoveryExpired;
            towers.RecoveryExpired += build.HandleRecoveryExpired;
            towers.RecoveryChanged += e => recoveryChanges.Add(e);
            towers.RecoveryExpired += e => recoveryExpires.Add(e);
            recoveryChanges.Clear();
            recoveryExpires.Clear();
            mapWaits = 0;

            var run = RunState.CreateNew(config);
            maps.ResetForNewRun(run);
            board.ResetForNewRun(run);
            player.ResetForNewRun(run);
            monsters.ResetForNewRun(run);
            towers.ResetForNewRun(run);
            build.ResetForNewRun(run);
            flow.ResetForNewRun(run);

            WaitUntil(() => board.Map != null, "첫 맵");
            player.Earn(10000, CashReason.Kill);
            flow.RequestStartGame();
        }

        // 백그라운드 생성 결과는 flow.Tick(→ MapProvider.Poll)에서 받는다.
        void WaitUntil(System.Func<bool> condition, string what)
        {
            for (int i = 0; i < 1000 && !condition(); i++)
            {
                Thread.Sleep(2);
                flow.Tick(0f);
            }
            Assert.IsTrue(condition(), what + " 대기 시간 초과");
        }

        void ClearWave()
        {
            flow.StartNow();
            monsters.DebugFinishWave();
            flow.CheckWaveClear();
        }

        // 지금 층의 웨이브를 모두 끝내고 층 전환 상태로.
        void FinishFloor()
        {
            int floor = flow.Floor;
            while (flow.State == GameState.Break && flow.Floor == floor)
                ClearWave();
            Assert.AreEqual(GameState.FloorTransition, flow.State);
        }

        // 층 전환 연출 시간을 흘려 다음 층 Break로.
        void FinishTransition()
        {
            flow.Tick(config.floorTransitionSec + 0.01f);
            WaitUntil(() => flow.State == GameState.Break, "층 전환 완료");
        }

        Vector2Int BuildableCell(int skip = 0)
        {
            for (int x = 0; x < board.N; x++)
                for (int y = 0; y < board.N; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (board.IsBuildable(c) && skip-- <= 0)
                        return c;
                }
            Assert.Fail("설치 가능 칸 없음");
            return default;
        }

        void GoToFloor4WithTowers(int towerCount)
        {
            for (int i = 0; i < towerCount; i++)
                Assert.IsTrue(towers.Build(BuildableCell(), basic));
            for (int f = 1; f <= 3; f++)
            {
                FinishFloor();
                if (f < 3)
                    FinishTransition();
            }
        }

        [Test]
        public void SameTier_1to2_KeepsMapAndTowers_NoRecovery()
        {
            var map = board.Map;
            var cell = BuildableCell();
            Assert.IsTrue(towers.Build(cell, basic));

            FinishFloor();
            Assert.AreEqual(1, towers.Towers.Count, "전환 중에도 타워 그대로");
            FinishTransition();

            Assert.AreEqual(2, flow.Floor);
            Assert.AreSame(map, board.Map, "같은 Tier: 맵 그대로");
            Assert.IsNotNull(towers.GetTowerAt(cell), "타워 그대로");
            Assert.AreEqual(0, towers.RecoveryTotalCount, "회수 목록 없음");
            Assert.AreEqual(0, recoveryChanges.Count);
        }

        [Test]
        public void TierChange_3to4_Swaps7x7To9x9_TowersToRecovery_NoWait()
        {
            Assert.AreEqual(7, board.N);
            GoToFloor4WithTowers(3);

            // 3층 마지막 웨이브 클리어 → 연출 시작: 타워 전부 회수 목록으로
            Assert.AreEqual(GameState.FloorTransition, flow.State);
            Assert.AreEqual(0, towers.Towers.Count, "맵 위 타워 없음");
            Assert.AreEqual(3, towers.RecoveryTotalCount);
            Assert.AreEqual(3, towers.GetRecoveryCount(basic));
            Assert.AreEqual(1, recoveryChanges.Count, "종류별 1번: 기본 타워 ×3");
            Assert.AreEqual((basic, 3, 3), (recoveryChanges[0].Def, recoveryChanges[0].Count, recoveryChanges[0].TotalCount));
            Assert.IsTrue(maps.IsReady(2), "다음 Tier 맵이 이미 준비됨 (미리 생성)");

            flow.Tick(config.floorTransitionSec + 0.01f);
            Assert.AreEqual(GameState.Break, flow.State, "생성 대기 없이 바로 Break");
            Assert.AreEqual(0, mapWaits, "로딩 대기 없음");
            Assert.AreEqual(4, flow.Floor);
            Assert.AreEqual(9, board.N, "9×9 맵으로 교체");
            Assert.AreEqual(9, board.Map.N);
        }

        [Test]
        public void Rebuild_IsFree_DecrementsList_AutoExitsWhenUsedUp()
        {
            GoToFloor4WithTowers(2);
            FinishTransition();
            int cash = player.Cash;

            build.EnterRebuildMode(basic);
            Assert.AreEqual(BuildMode.Rebuild, build.Mode);

            var first = BuildableCell();
            build.Click(first);
            Assert.AreEqual(PopupKind.Build, build.Popup);
            build.ConfirmPopup();
            Assert.IsNotNull(towers.GetTowerAt(first));
            Assert.AreEqual(cash, player.Cash, "재건축은 캐시가 들지 않음");
            Assert.AreEqual(1, towers.RecoveryTotalCount);
            Assert.AreEqual(BuildMode.Rebuild, build.Mode, "남아 있으면 모드 유지");

            build.Click(BuildableCell());
            build.ConfirmPopup();
            Assert.AreEqual(0, towers.RecoveryTotalCount);
            Assert.AreEqual(cash, player.Cash);
            Assert.AreEqual(BuildMode.None, build.Mode, "다 쓰면 자동 종료");
            Assert.AreEqual(2, towers.Towers.Count);
        }

        [Test]
        public void Rebuild_EscExitsAnytime()
        {
            GoToFloor4WithTowers(2);
            FinishTransition();
            build.EnterRebuildMode(basic);
            build.ExitMode(); // ESC
            Assert.AreEqual(BuildMode.None, build.Mode);
            Assert.AreEqual(2, towers.RecoveryTotalCount, "목록은 그대로");
        }

        [Test]
        public void Scrap_RefundsExactly70Percent()
        {
            GoToFloor4WithTowers(2);
            FinishTransition();
            int cash = player.Cash;

            build.OpenRecoveryPopup(basic); // 항목 클릭
            Assert.AreEqual(PopupKind.Recovery, build.Popup);
            build.CancelPopup();            // 오른쪽 [철거]: 확인 없이 바로
            Assert.AreEqual(cash + 35, player.Cash, "50 × 0.7 = 35");
            Assert.AreEqual(1, towers.RecoveryTotalCount);
            Assert.AreEqual(PopupKind.None, build.Popup);
        }

        [Test]
        public void Expire_AfterFirstWaveOfNewMap_Refunds70Percent_ListGone()
        {
            GoToFloor4WithTowers(3);
            FinishTransition();
            int cash = player.Cash;

            ClearWave(); // 4층 첫 웨이브 끝나는 순간
            Assert.AreEqual(1, recoveryExpires.Count);
            Assert.AreEqual((3, 105), (recoveryExpires[0].TowerCount, recoveryExpires[0].Refund), "3 × 35");
            Assert.AreEqual(cash + 105, player.Cash, "만료 = 정확히 70%");
            Assert.AreEqual(0, towers.RecoveryTotalCount, "목록 제거");

            ClearWave(); // 두 번째 웨이브: 다시 만료되지 않음
            Assert.AreEqual(1, recoveryExpires.Count);
        }

        [Test]
        public void GameOverDuringFirstWave_DiscardsWithoutRefund()
        {
            GoToFloor4WithTowers(2);
            FinishTransition();
            int cash = player.Cash;

            flow.StartNow();
            player.TakeNexusDamage(1000);
            Assert.AreEqual(GameState.GameOver, flow.State);
            Assert.AreEqual(0, towers.RecoveryTotalCount, "목록 소멸");
            Assert.AreEqual(cash, player.Cash, "반환 없음");
        }
    }
}
