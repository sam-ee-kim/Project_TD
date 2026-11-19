using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Tile = TowerDefence.MapBuilderScene.Tile;

namespace TowerDefence.Tests
{
    // 맵 제작 씬: 타일 → 게임 맵(경로 계산), 스타트 줄 → 웨이브 표, 줄별 스타트 지정·동시 진행·가중치, 넥서스 무적.
    public class MapBuilderTests
    {
        // 세로 본선: 스타트 (2,0) → 넥서스 (2,4). 왼쪽 지선: 스타트 (0,2) → (1,2) → 본선 (2,2).
        static Dictionary<Vector2Int, Tile> TwoSpawnLayout(out List<MapBuilderScene.SpawnSetting> spawns)
        {
            var tiles = new Dictionary<Vector2Int, Tile>();
            for (int y = 1; y <= 3; y++)
                tiles[new Vector2Int(2, y)] = Tile.Path;
            tiles[new Vector2Int(2, 0)] = Tile.Spawn;
            tiles[new Vector2Int(2, 4)] = Tile.Nexus;
            tiles[new Vector2Int(0, 2)] = Tile.Spawn;
            tiles[new Vector2Int(1, 2)] = Tile.Path;
            tiles[new Vector2Int(3, 2)] = Tile.Grass;
            tiles[new Vector2Int(3, 3)] = Tile.Tree;
            spawns = new List<MapBuilderScene.SpawnSetting>
            {
                new MapBuilderScene.SpawnSetting { cell = new Vector2Int(2, 0), weight = 3f },
                new MapBuilderScene.SpawnSetting { cell = new Vector2Int(0, 2), weight = 1f },
            };
            return tiles;
        }

        [Test]
        public void BuildMap_ComputesShortestPathPerSpawn()
        {
            var tiles = TwoSpawnLayout(out var spawns);
            var map = MapBuilderScene.BuildMap(tiles, spawns, out string error);
            Assert.IsNull(error);
            Assert.AreEqual(5, map.N, "타일을 감싸는 정사각형");
            Assert.AreEqual(2, map.MonsterPaths.Count);
            CollectionAssert.AreEqual(new[] { new Vector2Int(2, 0), new Vector2Int(2, 1), new Vector2Int(2, 2), new Vector2Int(2, 3), new Vector2Int(2, 4) }, map.MonsterPaths[0]);
            CollectionAssert.AreEqual(new[] { new Vector2Int(0, 2), new Vector2Int(1, 2), new Vector2Int(2, 2), new Vector2Int(2, 3), new Vector2Int(2, 4) }, map.MonsterPaths[1]);
            Assert.AreEqual(TileType.Exit, map.Tiles[2, 4]);
            Assert.AreEqual(TileType.BuildSite, map.Tiles[3, 2], "잔디 = 설치 부지");
            Assert.AreEqual(TileType.Empty, map.Tiles[3, 3], "나무 = 장애물");
            Assert.AreEqual(TileType.Empty, map.Tiles[4, 4], "빈 칸은 장애물로 채운다");
            CollectionAssert.AreEqual(new[] { 3f, 1f }, map.SpawnWeights);
        }

        [Test]
        public void BuildMap_ReportsMissingNexusAndBrokenPath()
        {
            var tiles = TwoSpawnLayout(out var spawns);
            tiles.Remove(new Vector2Int(1, 2)); // 지선 끊김
            Assert.IsNull(MapBuilderScene.BuildMap(tiles, spawns, out string broken));
            StringAssert.Contains("스타트 2", broken);

            tiles = TwoSpawnLayout(out spawns);
            tiles[new Vector2Int(2, 4)] = Tile.Path;
            Assert.IsNull(MapBuilderScene.BuildMap(tiles, spawns, out string noNexus));
            StringAssert.Contains("넥서스", noNexus);
        }

        [Test]
        public void BuildWaves_LinesBecomeParallelWaveTable()
        {
            TwoSpawnLayout(out var spawns);
            spawns[0].lines.Add(new MapBuilderScene.Line { wave = 1, monsterId = "normal", count = 3 });
            spawns[1].lines.Add(new MapBuilderScene.Line { wave = 3, monsterId = "fast", count = 2, delay = 1.5f, random = true });
            var waves = MapBuilderScene.BuildWaves(spawns, out int count);
            Assert.AreEqual(3, count, "가장 큰 웨이브 번호");
            Assert.AreEqual(1, waves[0].lines.Length);
            Assert.AreEqual(0, waves[0].lines[0].spawnIndex, "이 스타트");
            Assert.AreEqual(0, waves[1].lines.Length, "줄이 없는 웨이브는 빈 웨이브");
            Assert.AreEqual(-1, waves[2].lines[0].spawnIndex, "가중치 랜덤");
            Assert.AreEqual(1.5f, waves[2].lines[0].delaySec);
            Assert.IsTrue(waves[0].parallel && waves[2].parallel);
        }

        [Test]
        public void Parallel_SpawnIndex_And_Weights_PickStart()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            var normal = TestAssets.NoIdleCopy("Assets/Data/Monsters/normal.asset");
            var data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"), new[] { normal }, new TowerDef[0]);
            var tiles = TwoSpawnLayout(out var spawns);
            spawns[0].weight = 0f; // 랜덤 줄은 모두 스타트 2로
            var map = MapBuilderScene.BuildMap(tiles, spawns, out _);
            var board = new Board();
            var monsters = new MonsterManager();
            var run = RunState.CreateNew(config);
            board.Init(data);
            board.ResetForNewRun(run);
            board.Load(map);
            monsters.Init(data, board);
            monsters.ResetForNewRun(run);

            // 줄 1: 스타트 1에서 3마리 (지연 0) / 줄 2: 랜덤(가중치 → 스타트 2) 2마리, 지연 0 → 동시에 시작
            var lines = new[]
            {
                new WaveSpawn("normal", 3, 1f, 0f, 0, true),
                new WaveSpawn("normal", 2, 1f, 0f, -1, true),
            };
            monsters.BeginWave(lines, 2);
            monsters.TickSpawn(0.01f);
            Assert.AreEqual(2, monsters.AliveCount, "두 줄이 동시에 첫 마리를 낸다");
            Assert.AreEqual(1, board.MonstersAt(board.CellId(map.Spawns[0])).Count, "줄 1 → 스타트 1");
            Assert.AreEqual(1, board.MonstersAt(board.CellId(map.Spawns[1])).Count, "랜덤 줄 → 가중치가 있는 스타트 2");
            monsters.TickSpawn(5f);
            Assert.AreEqual(0, monsters.RemainingToSpawn);
            Assert.AreEqual(5, monsters.AliveCount);
            Object.DestroyImmediate(normal);
        }

        [Test]
        public void InvincibleNexus_IgnoresDamage()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/Data/GameConfig.asset");
            var data = new GameData();
            data.Init(config, AssetDatabase.LoadAssetAtPath<MapGenRules>("Assets/Data/MapGenRules.asset"), new MonsterDef[0], new TowerDef[0]);
            var player = new PlayerState();
            player.Init(data);
            player.ResetForNewRun(RunState.CreateNew(config));
            bool defeated = false;
            player.Defeated += () => defeated = true;
            player.Invincible = true;
            player.TakeNexusDamage(9999);
            Assert.AreEqual(config.nexusHp, player.NexusHp);
            Assert.IsFalse(defeated);
            player.Invincible = false;
            player.TakeNexusDamage(9999);
            Assert.IsTrue(defeated);
        }
    }
}
