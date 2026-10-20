using UnityEngine;

namespace TowerDefence
{
    // 투사체 한 개. 타워 참조 없이 대상 핸들과 피해 값 복사본만 든다 (DamageDesign 3-3).
    // 그래서 비행 중 타워가 판매·회수돼도 그대로 날아가 명중한다.
    public class Projectile
    {
        public MonsterHandle Target;
        public DamageInfo Info;
        public float Speed;       // 칸/초 (= 월드 단위/초)
        public Vector3 Position;  // 월드 좌표
        public Vector3 Direction; // 진행 방향 (단위 벡터). 처음엔 포신 방향, 날면서 대상 쪽으로 돈다
        public float Age;         // 발사 후 지난 시간
        public bool Active;
        public TowerDef Kind;     // 어느 풀에 돌려줄지 (쏜 타워 종류의 정의 데이터, 타워 인스턴스가 아님)
        public Transform View;
    }
}
