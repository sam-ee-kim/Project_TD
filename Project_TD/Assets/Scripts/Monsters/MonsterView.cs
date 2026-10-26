using UnityEngine;

namespace TowerDefence
{
    // 몬스터 표시 상태 (유한상태머신).
    public enum MonsterViewState
    {
        Hidden, // 풀에서 쉬는 중
        Idle,   // 스폰 직후·움직일 경로가 없거나 대기 중 (Idle 애니메이션 반복)
        Walk,   // 경로를 따라 넥서스로 이동 중 (Walk 애니메이션 반복, MonsterDef.walkAnimSpeed 배속)
        Die,    // 포탑에 맞아 죽음 (Die 애니메이션 1번, 끝나면 Hidden)
    }

    // 몬스터 한 마리의 모델과 애니메이션 상태머신. 스크립트 Update 없이 MonsterManager.TickViews가 구동한다.
    // Animator Controller에는 Idle / Walk / Die 상태만 있고 전환은 없다. 어떤 상태를 재생할지는 여기서 정한다.
    // 죽은 몬스터의 게임 데이터는 같은 프레임에 풀로 돌아가지만(DamageDesign 5단계), 표시는 Die가 끝날 때까지 남는다.
    public sealed class MonsterView
    {
        static readonly int IdleHash = Animator.StringToHash("Idle");
        static readonly int WalkHash = Animator.StringToHash("Walk");
        static readonly int DieHash = Animator.StringToHash("Die");

        const float TurnSpeedDeg = 540f; // 진행 방향으로 몸을 돌리는 속도

        public readonly MonsterDef Def;
        public readonly Transform Root;
        readonly Animator animator;  // 기본 캡슐이면 null
        readonly float dieLength;    // Die 애니메이션 길이 (끝나면 숨김)

        Quaternion facing = Quaternion.identity;

        public MonsterViewState State { get; private set; } = MonsterViewState.Hidden;
        public float StateTime { get; private set; }

        public MonsterView(MonsterDef def, Transform root, Animator animator)
        {
            Def = def;
            Root = root;
            this.animator = animator;
            dieLength = FindClipLength(animator, "Die");
            root.gameObject.SetActive(false);
        }

        // 상태 전환 (같은 상태면 아무것도 하지 않는다).
        public void ChangeState(MonsterViewState next)
        {
            if (next == State)
                return;
            State = next;
            StateTime = 0f;
            switch (next)
            {
                case MonsterViewState.Hidden:
                    Root.gameObject.SetActive(false);
                    break;
                case MonsterViewState.Idle:
                    Show();
                    Play(IdleHash, 1f);
                    break;
                case MonsterViewState.Walk:
                    Show();
                    Play(WalkHash, Def.walkAnimSpeed * moveScale);
                    break;
                case MonsterViewState.Die:
                    Show();
                    Play(DieHash, 1f);
                    break;
            }
        }

        // 매 프레임 (표현 단계). 몸을 진행 방향으로 돌린다. Die가 끝나면 true (풀에 돌려줄 때).
        public bool Tick(float dt)
        {
            StateTime += dt;
            if (State == MonsterViewState.Die)
                return StateTime >= dieLength;
            if (State != MonsterViewState.Hidden)
                Root.rotation = Quaternion.RotateTowards(Root.rotation, facing, TurnSpeedDeg * dt);
            return false;
        }

        // 위치(바닥)와 바라볼 방향 지정. snap이면 바로 돌린다 (스폰 직후).
        public void Place(Vector3 groundPosition, Vector3 direction, bool snap)
        {
            Root.position = groundPosition;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                facing = Quaternion.LookRotation(direction);
            if (snap)
                Root.rotation = facing;
        }

        float moveScale = 1f;

        // 이동 속도 배율 (나노필드 감속). 걷는 중이면 걷기 애니메이션 속도도 같이 바꾼다.
        public void SetMoveScale(float scale)
        {
            moveScale = scale;
            if (State == MonsterViewState.Walk && animator != null)
                animator.speed = Def.walkAnimSpeed * scale;
        }

        void Show()
        {
            if (!Root.gameObject.activeSelf)
                Root.gameObject.SetActive(true);
        }

        void Play(int stateHash, float speed)
        {
            if (animator == null)
                return;
            animator.speed = speed;
            animator.CrossFadeInFixedTime(stateHash, Def.animCrossFadeSec, 0, 0f);
        }

        // 컨트롤러 안 클립 중 이름이 suffix로 끝나는 것의 길이 (생성 때 1번).
        static float FindClipLength(Animator animator, string suffix)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
                return 0f;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                if (clip.name.EndsWith(suffix))
                    return clip.length;
            return 0f;
        }
    }
}
