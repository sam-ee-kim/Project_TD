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
}
