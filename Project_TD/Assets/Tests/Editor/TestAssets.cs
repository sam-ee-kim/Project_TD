using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TowerDefence.Tests
{
    // 테스트용 에셋 도우미. 에셋 파일은 바꾸지 않고 메모리 사본만 고친다.
    static class TestAssets
    {
        // 스폰 직후 대기(Idle) 없이 바로 걷는 사본 — 이동 시간 계산이 대기 시간에 흔들리지 않게.
        public static MonsterDef NoIdleCopy(string path)
        {
            var source = AssetDatabase.LoadAssetAtPath<MonsterDef>(path);
            var copy = Object.Instantiate(source);
            copy.name = source.name;
            copy.spawnIdleSec = 0f;
            return copy;
        }
    }

    // 경로 칸 번호(본선 = 스타트 지점 0의 경로) → 칸 번호 도우미. Board 몬스터 목록·타워 사거리는 칸 번호 기준.
    static class TestBoard
    {
        public static int PathCellId(this Board board, int pathIndex, int pathId = 0) =>
            board.CellId(board.GetPathCell(pathId, pathIndex));

        public static List<MonsterHandle> MonstersAtPath(this Board board, int pathIndex, int pathId = 0) =>
            board.MonstersAt(board.PathCellId(pathIndex, pathId));

        public static bool CoversPathIndex(this TowerInstance tower, Board board, int pathIndex) =>
            tower.CoversCell[board.PathCellId(pathIndex)];
    }
}
