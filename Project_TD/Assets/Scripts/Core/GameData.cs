using System.Collections.Generic;
using UnityEngine;

namespace TowerDefence
{
    // ScriptableObject 보관과 id → Def 조회 (GDD 8-1). 상태를 바꾸지 않는다.
    // 조회는 생성 시점에만 하고, 인스턴스가 Def 참조를 들고 있는다.
    public class GameData
    {
        readonly Dictionary<string, MonsterDef> monsterById = new Dictionary<string, MonsterDef>();
        readonly Dictionary<string, TowerDef> towerById = new Dictionary<string, TowerDef>();
        MonsterDef[] monsters = new MonsterDef[0];
        TowerDef[] towers = new TowerDef[0];

        public GameConfig Config { get; private set; }
        public MapGenRules MapGenRules { get; private set; }
        public IReadOnlyList<MonsterDef> Monsters => monsters; // 순번 = MonsterDef.Index
        public IReadOnlyList<TowerDef> Towers => towers;       // 순번 = TowerDef.Index

        // GameLoop가 씬에 연결된 에셋을 넘긴다. 사전과 연속 번호는 여기서 1번만 만든다.
        // null 항목과 중복 id는 건너뛰고 에러를 남긴다.
        public void Init(GameConfig config, MapGenRules mapGenRules, MonsterDef[] monsterDefs, TowerDef[] towerDefs)
        {
            Config = config;
            MapGenRules = mapGenRules;
            if (config == null)
                Debug.LogError("[GameData] GameConfig가 지정되지 않았습니다.");
            if (mapGenRules == null)
                Debug.LogError("[GameData] MapGenRules가 지정되지 않았습니다.");

            monsters = BuildIndex(monsterDefs, monsterById, d => d.id, (d, i) => d.AssignIndex(i), "MonsterDef");
            towers = BuildIndex(towerDefs, towerById, d => d.id, (d, i) => d.AssignIndex(i), "TowerDef");
        }

        public bool TryGetMonster(string id, out MonsterDef def)
        {
            def = null;
            return id != null && monsterById.TryGetValue(id, out def);
        }

        public bool TryGetTower(string id, out TowerDef def)
        {
            def = null;
            return id != null && towerById.TryGetValue(id, out def);
        }

        // Monsters 목록 순번. 이 GameData가 등록한 Def가 아니면 -1.
        public int GetMonsterIndex(MonsterDef def)
        {
            if (def == null || def.Index < 0 || def.Index >= monsters.Length || monsters[def.Index] != def)
                return -1;
            return def.Index;
        }

        static T[] BuildIndex<T>(T[] source, Dictionary<string, T> byId, System.Func<T, string> idOf,
            System.Action<T, int> assignIndex, string label) where T : Object
        {
            byId.Clear();
            var list = new List<T>();
            if (source == null)
                return list.ToArray();

            foreach (var def in source)
            {
                if (def == null)
                {
                    Debug.LogError($"[GameData] {label} 목록에 빈 항목이 있습니다.");
                    continue;
                }
                string id = idOf(def);
                if (string.IsNullOrEmpty(id) || byId.ContainsKey(id))
                {
                    Debug.LogError($"[GameData] {label} id가 비었거나 중복입니다: '{id}' ({def.name})");
                    continue;
                }
                assignIndex(def, list.Count);
                byId.Add(id, def);
                list.Add(def);
            }
            return list.ToArray();
        }
    }
}
