using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace TowerDefence
{
    public enum BuildMode
    {
        None,    // 일반 상태 (타워 클릭 → 판매)
        Build,   // 건축 BuildMode
        Rebuild, // 회수 타워 재건축 BuildMode (캐시 없음, 작업 9에서 연결)
    }

    public enum PopupKind
    {
        None,  // 팝업 닫힘
        Build, // "설치하시겠습니까?" [설치] / [취소]
        Sell,  // "판매하시겠습니까? (+N 캐시)" [판매] / [취소]
        Recovery, // 회수 목록 항목: [재건축] / [철거] (GDD 5-9)
    }

    // BuildController.ModeChanged 인자 (GDD 8-4).
    public readonly struct BuildModeChange
    {
        public readonly BuildMode Mode;
        public readonly TowerDef Def; // 건축·재건축 대상 타워 (None이면 null)

        public BuildModeChange(BuildMode mode, TowerDef def)
        {
            Mode = mode;
            Def = def;
        }
    }

    // 마우스 아래 칸이 바뀌었을 때 (BuildMode 중에만 발행).
    public readonly struct CellHover
    {
        public readonly bool HasCell;   // false = 맵 밖·UI 위
        public readonly Vector2Int Cell;
        public readonly bool Buildable; // 설치 가능(초록) / 불가(빨강 진하게 + 툴팁)

        public CellHover(bool hasCell, Vector2Int cell, bool buildable)
        {
            HasCell = hasCell;
            Cell = cell;
            Buildable = buildable;
        }
    }

    // 파란 선택 칸이 바뀌었을 때.
    public readonly struct CellSelection
    {
        public readonly bool HasCell;
        public readonly Vector2Int Cell;

        public CellSelection(bool hasCell, Vector2Int cell)
        {
            HasCell = hasCell;
            Cell = cell;
        }
    }

    // 확인 팝업 열기·닫기 요청 (Kind = None이면 닫기).
    public readonly struct PopupRequest
    {
        public readonly PopupKind Kind;
        public readonly Vector2Int Cell;
        public readonly TowerDef Def;
        public readonly int Amount; // Build = 건축 비용, Sell = 반환 캐시

        public PopupRequest(PopupKind kind, Vector2Int cell, TowerDef def, int amount)
        {
            Kind = kind;
            Cell = cell;
            Def = def;
            Amount = amount;
        }
    }

    // 마우스·ESC 입력, BuildMode, Hover(초록)/Selected(파랑)/불가(빨강) 판정, 판매·회수 목록 클릭 (GDD 8-1).
    // 캐시·맵은 직접 바꾸지 않고 TowerManager에 요청만 한다. 화면 갱신은 이벤트로 GameUI에 알린다.
    // 마우스 → 칸: 카메라 광선과 바닥 평면(y = 0)의 교점을 계산한다 (물리 Raycast 미사용).
    public class BuildController
    {
        public event Action<BuildModeChange> ModeChanged;     // → GameUI (오버레이, 안내 문구, 패널 슬라이드)
        public event Action<CellHover> HoverChanged;          // → GameUI (오버레이 2칸, 툴팁)
        public event Action<CellSelection> SelectionChanged;  // → GameUI (파란 칸)
        public event Action<PopupRequest> PopupRequested;     // → GameUI (확인 팝업)

        static readonly Plane Ground = new Plane(Vector3.up, Vector3.zero);

        GameData data;
        GameFlow flow;
        Board board;
        TowerManager towers;
        PlayerState player;
        Camera camera;
        RunState run;

        TowerDef modeDef;
        TowerInstance sellTarget;
        TowerDef recoveryDef; // 회수 팝업 대상 종류

        public BuildMode Mode { get; private set; } = BuildMode.None;
        public TowerDef ModeDef => modeDef;
        public bool HasHover { get; private set; }
        public Vector2Int HoverCell { get; private set; }
        public bool HasSelection { get; private set; }
        public Vector2Int SelectedCell { get; private set; }
        public PopupKind Popup { get; private set; } = PopupKind.None;

        // 건축·판매 상호작용은 휴식·웨이브 중에만 (게임은 멈추지 않는다).
        bool CanInteract => flow.State == GameState.Break || flow.State == GameState.Wave;

        public void Init(GameData data, GameFlow flow, Board board, TowerManager towers, PlayerState player, Camera camera)
        {
            this.data = data;
            this.flow = flow;
            this.board = board;
            this.towers = towers;
            this.player = player;
            this.camera = camera;
        }

        // 새 판: BuildMode 해제, 선택·팝업 해제.
        public void ResetForNewRun(RunState run)
        {
            this.run = run;
            ExitMode();
            ClosePopup();
        }

        // 입력 처리. GameLoop가 1단계(GameFlow.Tick) 직전에 부른다.
        public void Tick(float dt)
        {
            var pointer = Pointer.current;
            var keyboard = Keyboard.current;

            // Ready: 화면 아무 곳이나 클릭(터치 포함) → 게임 시작. 시작 버튼은 없다 (GDD 2-1).
            if (flow.State == GameState.Ready)
            {
                if (pointer != null && pointer.press.wasPressedThisFrame)
                    flow.RequestStartGame();
                return;
            }

            // BuildMode를 나가는 방법은 ESC 하나 (우클릭 없음). 일반 상태에서는 열린 팝업(판매·회수)을 닫는다.
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (Mode != BuildMode.None)
                    ExitMode();
                else if (Popup != PopupKind.None)
                    ClosePopup();
            }

            if (!CanInteract || pointer == null)
                return;

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            Vector2Int cell = default;
            bool hasCell = !overUi && TryGetCell(pointer.position.ReadValue(), out cell);
            PointAt(hasCell, cell);

            if (pointer.press.wasPressedThisFrame && hasCell)
                Click(cell);
        }

        // 마우스가 가리키는 칸 갱신. 칸이 바뀔 때만 HoverChanged를 낸다 (GDD 8-5 #7).
        public void PointAt(bool hasCell, Vector2Int cell)
        {
            if (hasCell == HasHover && (!hasCell || cell == HoverCell))
                return;
            HasHover = hasCell;
            HoverCell = cell;
            if (Mode != BuildMode.None)
                HoverChanged?.Invoke(new CellHover(hasCell, cell, hasCell && board.IsBuildable(cell)));
        }

        // 칸 클릭. 팝업이 열려 있으면 무시한다.
        public void Click(Vector2Int cell)
        {
            if (Popup != PopupKind.None || !CanInteract)
                return;

            if (Mode == BuildMode.None)
            {
                // 일반 상태: 설치된 타워 클릭 → 판매 팝업 (GDD 5-8)
                var tower = towers.GetTowerAt(cell);
                if (tower == null)
                    return;
                sellTarget = tower;
                OpenPopup(PopupKind.Sell, cell, tower.Def, towers.GetSellRefund(tower.Def));
                return;
            }

            // BuildMode: 설치 가능 칸만 반응 (불가 칸은 반응 없음). 파랑 고정 + 확인 팝업 (GDD 5-4 4번)
            if (!board.IsBuildable(cell))
                return;
            HasSelection = true;
            SelectedCell = cell;
            SelectionChanged?.Invoke(new CellSelection(true, cell));
            OpenPopup(PopupKind.Build, cell, modeDef, Mode == BuildMode.Rebuild ? 0 : modeDef.buildCost);
        }

        // 하단 [타워 건축] → Build 모드.
        public void EnterBuildMode(TowerDef def)
        {
            if (def == null || !CanInteract || Popup == PopupKind.Sell)
                return;
            SetMode(BuildMode.Build, def);
        }

        // 회수 목록 [재건축] → Rebuild 모드. 오버레이·선택·확인 팝업은 Build와 같고, 확정 시 캐시 없이 목록에서 1개 차감.
        public void EnterRebuildMode(TowerDef def)
        {
            if (def == null || !CanInteract || towers.GetRecoveryCount(def) == 0)
                return;
            if (Mode != BuildMode.None)
                ExitMode();
            SetMode(BuildMode.Rebuild, def);
        }

        // 좌측 회수 목록 항목 클릭 → [재건축] / [철거] 팝업 (일반 상태에서만).
        public void OpenRecoveryPopup(TowerDef def)
        {
            if (def == null || !CanInteract || Popup != PopupKind.None || Mode != BuildMode.None || towers.GetRecoveryCount(def) == 0)
                return;
            recoveryDef = def;
            OpenPopup(PopupKind.Recovery, default, def, towers.GetRecoveryRefund(def));
        }

        // TowerManager.RecoveryExpired 구독: 목록이 사라지면 재건축 모드와 회수 팝업을 닫는다.
        public void HandleRecoveryExpired(RecoveryExpire e)
        {
            if (Mode == BuildMode.Rebuild)
                ExitMode();
            if (Popup == PopupKind.Recovery)
                ClosePopup();
        }

        // ESC → 일반 상태. 열린 팝업·선택도 닫는다.
        public void ExitMode()
        {
            if (Mode == BuildMode.None)
                return;
            ClearSelection();
            ClosePopup();
            SetMode(BuildMode.None, null);
        }

        // 팝업 왼쪽 버튼: [설치] / [판매] / [재건축].
        public void ConfirmPopup()
        {
            if (Popup == PopupKind.Build)
            {
                if (HasSelection)
                {
                    if (Mode == BuildMode.Rebuild)
                        towers.RequestRebuild(modeDef, SelectedCell); // 캐시 없음, 목록에서 1개 차감
                    else
                        towers.Build(SelectedCell, modeDef);          // 캐시·부지 최종 판정은 TowerManager
                }
                ClearSelection();
                ClosePopup();                                         // BuildMode는 유지 (연속 건축)
                // 재건축: 그 종류의 회수 타워를 다 쓰면 자동 종료
                if (Mode == BuildMode.Rebuild && towers.GetRecoveryCount(modeDef) == 0)
                    ExitMode();
            }
            else if (Popup == PopupKind.Sell)
            {
                towers.Sell(sellTarget);
                ClosePopup();
            }
            else if (Popup == PopupKind.Recovery)
            {
                var def = recoveryDef;
                ClosePopup();
                EnterRebuildMode(def);
            }
        }

        // 팝업 오른쪽 버튼: [취소] (선택만 해제하고 계속 위치를 고른다) / 회수 목록이면 [철거].
        public void CancelPopup()
        {
            if (Popup == PopupKind.Recovery)
            {
                towers.RequestScrap(recoveryDef); // 확인 없이 바로 철거, 70% 반환
                ClosePopup();
                return;
            }
            ClearSelection();
            ClosePopup();
        }

        // GameFlow.StateChanged 구독: 휴식·웨이브가 아니면(층 전환·게임오버·클리어·Ready) 모드와 팝업을 닫는다.
        public void HandleStateChanged(StateChange e)
        {
            if (e.State == GameState.Break || e.State == GameState.Wave)
                return;
            ExitMode();
            ClosePopup();
        }

        // 화면 좌표 → 바닥 칸. 맵 밖이면 false.
        bool TryGetCell(Vector2 screen, out Vector2Int cell)
        {
            cell = default;
            if (camera == null || board.Map == null)
                return false;
            var ray = camera.ScreenPointToRay(screen);
            if (!Ground.Raycast(ray, out float distance))
                return false;
            return board.TryWorldToCell(ray.GetPoint(distance), out cell);
        }

        void SetMode(BuildMode mode, TowerDef def)
        {
            Mode = mode;
            modeDef = def;
            ModeChanged?.Invoke(new BuildModeChange(mode, def));
            if (mode != BuildMode.None && HasHover)
                HoverChanged?.Invoke(new CellHover(true, HoverCell, board.IsBuildable(HoverCell)));
        }

        void ClearSelection()
        {
            if (!HasSelection)
                return;
            HasSelection = false;
            SelectionChanged?.Invoke(new CellSelection(false, SelectedCell));
        }

        void OpenPopup(PopupKind kind, Vector2Int cell, TowerDef def, int amount)
        {
            Popup = kind;
            PopupRequested?.Invoke(new PopupRequest(kind, cell, def, amount));
        }

        void ClosePopup()
        {
            sellTarget = null;
            recoveryDef = null;
            if (Popup == PopupKind.None)
                return;
            Popup = PopupKind.None;
            PopupRequested?.Invoke(new PopupRequest(PopupKind.None, default, null, 0));
        }
    }
}
