# Project_TD

Unity 6 (6000.3.11f1) 3D URP 프로젝트. 타워 디펜스 게임.

## 환경
- 렌더 파이프라인: URP 17.3
- 입력: Input System 1.19 (구 `Input.GetKey` 대신 `UnityEngine.InputSystem` 사용)
- 길찾기: AI Navigation 2.0 (NavMeshSurface)
- MCP for Unity 패키지를 쓰면 씬/게임오브젝트/콘솔 로그는 MCP 도구로 확인·수정

## 작업 규칙
- 스크립트는 `Assets/Scripts/` 아래 기능별 폴더에 둔다.
- `.meta` 파일은 직접 만들거나 지우지 않는다 (Unity가 관리).
- `Library/`, `Temp/`, `Logs/`는 건드리지 않는다.
- 스크립트를 수정한 뒤에는 콘솔을 읽어 컴파일 에러가 없는지 확인한다.
- 씬(.unity)·프리팹(.prefab) YAML을 직접 편집하지 말고 MCP 도구나 에디터 스크립트를 쓴다.

# 타워디펜스 프로젝트 규칙

## 문서
- 기획: Docs/GDD.md, Docs/DataArchitecture.md, Docs/OptimizationReview.md, Docs/DamageDesign.md
- 규칙이 헷갈리면 문서를 먼저 읽는다. 문서와 다르게 구현해야 하면 멈추고 질문한다.
- 맵 생성 규칙은 이미 작업된 맵 생성기 코드가 정본이다. 그 코드의 규칙을 임의로 바꾸지 않는다.

## 구조
- 엔진 Unity(C#). 게임 규칙은 순수 C# 클래스, MonoBehaviour는 GameLoop와 화면 연결에만 쓴다.
- 모듈은 10개만: GameData, LevelRules, GameFlow(GameLoop 포함), MapProvider, Board, MonsterManager, TowerManager, PlayerState, BuildController, GameUI. 새 매니저를 임의로 만들지 않는다.
- 의존 방향: 데이터 → 조회·계산·흐름 → 게임 시스템 → 입력·표현. 화면(GameUI, BuildController)은 상태를 직접 바꾸지 않고 매니저에 요청만 한다.
- 소유권: 한 데이터는 한 모듈만 쓴다. 캐시·체력=PlayerState, 맵·칸=Board, 타워·회수 목록=TowerManager, 몬스터=MonsterManager, 층·웨이브·상태=GameFlow.
- 이벤트: 전역 EventBus 없음. 각 매니저의 C# event, 인자는 readonly struct. 매니저는 앱 시작 때 1번 생성, 구독도 1번.
- 데이터: ScriptableObject는 런타임에 수정 금지. 바뀌는 값은 RunState. id 조회는 생성 시점 1번, 인스턴스가 Def 참조를 들고 있다.
- 새 판(사망·클리어 후 다시 시작)은 RunState를 새로 만들고 모든 객체를 풀에 반납한다. 매니저는 다시 만들지 않는다.

## 최적화 필수 (어기면 작업 실패)
1. `void Update`는 GameLoop 하나만. 나머지는 `Tick(float dt)`.
2. 몬스터·타워·투사체·이펙트는 `ObjectPool<T>`. 웨이브 중 Instantiate/Destroy 금지.
3. 타겟팅은 타워 설치 때 계산한 "정사각형 사거리 안 경로 칸" 기준. 전체 몬스터를 훑지 않는다.
4. 맵 생성은 순수 C# + `System.Random(seed)`, UnityEngine API 금지, 백그라운드(또는 프레임당 2ms 분할).
5. Tick 안에서 `new`, LINQ, 문자열 결합, 인터페이스 대상 foreach, GetComponent 금지. 캐시 사유는 `CashReason` enum.
- 물리 엔진(Collider, Rigidbody, OnTrigger) 사용 금지. 명중·사거리는 칸/거리 계산.

## 범위
- 보류 기능(캐시 로그 UI, 타워 업그레이드, 웨이브 보너스, 다중 스폰, 층 보정 배율)은 만들지 않는다. 플래그와 빈 자리만.
- 요청받은 작업 범위 밖의 파일은 고치지 않는다. 필요하면 먼저 묻는다.

## 보고 형식 (모든 작업 끝에)
1. 바뀐 파일 목록
2. 문서와 다르게 한 점과 이유 (없으면 "없음")
3. 완료 기준 각 항목의 통과 여부
4. 남은 TODO
