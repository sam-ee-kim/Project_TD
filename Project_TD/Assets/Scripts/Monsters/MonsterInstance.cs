using UnityEngine;

namespace TowerDefence
{
    // 풀에서 재사용되는 몬스터 한 마리의 런타임 상태. 개별 Update 없음 (MonsterManager가 Tick).
    public class MonsterInstance
    {
        public int Index;            // MonsterManager 배열 인덱스 (핸들의 Index)
        public int Generation;       // 풀에서 다시 나올 때마다 +1 (DamageDesign 3-3)
        public MonsterState State;
        public MonsterDef Def;       // 생성 시점에 1번 조회해 들고 있는다
        public int DefIndex;         // GameData 목록 순번 (타워 종류 우선도 순위표용)
        public int Hp;
        public int PathId;           // MapData.MonsterPaths 번호
        public int PathIndex;        // 경로 칸 번호 (Board 칸 목록·타워 사거리 기준)
        public float PathProgress;   // 경로 칸 번호 + 칸 안 진행률 (targetRule First/Last)
        public float IdleTime;       // 남은 제자리 대기 시간 (스폰 직후 Idle). 0보다 크면 움직이지 않는다
        public MonsterView View;     // 표시 (모델 + 애니메이션 상태머신). 표시가 없으면 null

        public MonsterHandle Handle => new MonsterHandle(Index, Generation);
    }
}
