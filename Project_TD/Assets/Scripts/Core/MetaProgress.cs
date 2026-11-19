using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TowerDefence
{
    // 판을 넘어 남는 진행 (공학연구소): M1반도체 개수와 해금한 타워 id.
    // 소유: PlayerState (읽기·쓰기는 PlayerState 한 곳). 파일: Application.persistentDataPath/meta.json (GameLoop가 경로를 준다).
    // RunState와 달리 새 판에서 새로 만들지 않는다.
    [Serializable]
    public class MetaProgress
    {
        public int m1Chips;                                  // 재화: M1반도체 (판이 끝날 때 클리어한 층 수만큼)
        public List<string> unlocked = new List<string>();   // 공학연구소에서 해금한 타워 id
        public int smallChips;                               // 재화: 작은 반도체 (반도체 공장)
        public List<int> factory = new List<int>();          // 반도체 공장 노드별 단계 (FactoryNode 순서)

        public int FactoryLevel(FactoryNode node)
        {
            int i = (int)node;
            return factory != null && i < factory.Count ? factory[i] : 0;
        }

        public void SetFactoryLevel(FactoryNode node, int level)
        {
            if (factory == null)
                factory = new List<int>();
            while (factory.Count < FactoryNodes.Count)
                factory.Add(0);
            factory[(int)node] = level;
        }

        public bool IsUnlocked(string id) => unlocked != null && unlocked.Contains(id);

        // 파일이 없거나 읽을 수 없으면 빈 진행으로 시작한다.
        public static MetaProgress Load(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var meta = JsonUtility.FromJson<MetaProgress>(File.ReadAllText(path));
                    if (meta != null)
                    {
                        if (meta.unlocked == null)
                            meta.unlocked = new List<string>();
                        if (meta.factory == null)
                            meta.factory = new List<int>();
                        return meta;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MetaProgress] 불러오기 실패, 새로 시작: {e.Message}");
            }
            return new MetaProgress();
        }

        // 경로가 비어 있으면 저장하지 않는다 (테스트·시뮬레이터).
        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(this, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MetaProgress] 저장 실패: {e.Message}");
            }
        }
    }

    // 반도체 공장 노드 (MetaProgress.factory 순서)
    public enum FactoryNode { Damage, Speed, Cost, StartCash, NexusHp }

    public static class FactoryNodes
    {
        public const int Count = 5;
    }

    // PlayerState.MetaChanged 인자. Granted > 0 = 판이 끝나 받은 M1반도체, SmallGranted = 받은 작은 반도체, UnlockedId = 방금 해금한 타워.
    public readonly struct MetaChange
    {
        public readonly int M1Chips;
        public readonly int Granted;
        public readonly string UnlockedId;
        public readonly int SmallChips;
        public readonly int SmallGranted;

        public MetaChange(int m1Chips, int granted, string unlockedId, int smallChips = 0, int smallGranted = 0)
        {
            M1Chips = m1Chips;
            Granted = granted;
            UnlockedId = unlockedId;
            SmallChips = smallChips;
            SmallGranted = smallGranted;
        }
    }
}
