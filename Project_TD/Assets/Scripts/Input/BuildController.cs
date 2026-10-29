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
        Upgrade,  // 테크트리: "○○ → ○○" [진화] / [판매 (+N)]
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

    public enum DragKind
    {
        None,
        NewTower,  // 타워 목록 아이콘을 끌어 맵에 놓으면 설치
        MoveTower, // 설치된 타워를 끌어 빈 부지로 이동
    }

    // 드래그 상태가 바뀌었을 때 (시작, 가리키는 칸 변경, 끝). Kind = None이면 끝.
    public readonly struct TowerDrag
    {
        public readonly DragKind Kind;
        public readonly TowerDef Def;
        public readonly TowerInstance Tower; // MoveTower일 때 옮기는 타워
        public readonly bool HasCell;
        public readonly Vector2Int Cell;
        public readonly bool Valid;          // 여기 놓으면 설치·이동된다 (초록) / 안 된다 (빨강)

        public TowerDrag(DragKind kind, TowerDef def, TowerInstance tower, bool hasCell, Vector2Int cell, bool valid)
        {
            Kind = kind;
            Def = def;
            Tower = tower;
            HasCell = hasCell;
            Cell = cell;
            Valid = valid;
        }
    }

    // 설치 미리보기 (놓을 칸·타워·방향)가 바뀌었을 때. Has = false면 미리보기 없음.
    // 칸이 바뀌면 경로를 가장 많이 덮는 방향으로 맞추고, R키로 90°씩 돌린다.
    public readonly struct PlacementPreview
    {
        public readonly bool Has;
        public readonly Vector2Int Cell;
        public readonly TowerDef Def;
        public readonly int Facing;

        public PlacementPreview(bool has, Vector2Int cell, TowerDef def, int facing)
        {
            Has = has;
            Cell = cell;
            Def = def;
            Facing = facing;
        }
    }

    // G키: 설치된 모든 타워의 사거리 그리드 표시 켜기/끄기.
    public readonly struct RangeDisplayChange
    {
        public readonly bool Visible;

        public RangeDisplayChange(bool visible) => Visible = visible;
    }

    // 확인 팝업 열기·닫기 요청 (Kind = None이면 닫기).
    public readonly struct PopupRequest
    {
        public readonly PopupKind Kind;
        public readonly Vector2Int Cell;
        public readonly TowerDef Def;
        public readonly int Amount; // Build = 건축 비용, Sell = 반환 캐시, Upgrade = 진화 비용
        public readonly TowerDef From; // Upgrade: 지금 타워 종류 (Def = 진화 대상)
        public readonly int Refund;    // Upgrade: [판매]로 돌려받는 캐시

        public PopupRequest(PopupKind kind, Vector2Int cell, TowerDef def, int amount, TowerDef from = null, int refund = 0)
        {
            Kind = kind;
            Cell = cell;
            Def = def;
            Amount = amount;
            From = from;
            Refund = refund;
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
        public event Action<TowerDrag> DragChanged;           // → GameUI (미리보기 타워, 오버레이)
        public event Action<PlacementPreview> PreviewChanged; // → GameUI (놓을 타워 방향, 사거리 미리보기)
        public event Action<RangeDisplayChange> RangeDisplayChanged; // → GameUI (G키 사거리 그리드)

        static readonly Plane Ground = new Plane(Vector3.up, Vector3.zero);

        // 드래그 중인 타워는 마우스 포인터에 잡혀 이 높이에 떠 있다. 놓일 칸 = 떠 있는 타워 바로 아래 칸.
        public const float DragHoldHeight = 1.7f;
        static readonly Plane HoldPlane = new Plane(Vector3.up, new Vector3(0f, DragHoldHeight, 0f));

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
        TowerInstance upgradeTarget; // 진화 팝업 대상

        // 드래그
        DragKind dragKind;
        TowerDef dragDef;
        TowerInstance dragTower;
        TowerInstance pressedTower; // 타워를 누른 채 아직 칸을 벗어나지 않음 (떼면 클릭, 칸을 옮기면 이동 드래그)
        Vector2Int pressCell;

        public BuildMode Mode { get; private set; } = BuildMode.None;
        public TowerDef ModeDef => modeDef;
        public bool HasHover { get; private set; }
        public Vector2Int HoverCell { get; private set; }
        public bool HasSelection { get; private set; }
        public Vector2Int SelectedCell { get; private set; }
        public PopupKind Popup { get; private set; } = PopupKind.None;
        public DragKind Drag => dragKind;
        public bool HasDragPoint { get; private set; }
        public int PlacementFacing { get; private set; } // 놓을 타워 방향 (TowerFacing)
        public bool ShowRanges { get; private set; }     // G키 사거리 그리드
        public bool HasPreview => preview.Has;
        PlacementPreview preview;
        public Vector3 DragPoint { get; private set; } // 포인터가 잡고 있는 공중 위치 (높이 DragHoldHeight)

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
            CancelDrag();
            pressedTower = null;
            ExitMode();
            ClosePopup();
            if (ShowRanges)
                ToggleRanges();
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

            // G: 사거리 그리드 켜기/끄기 (설치된 타워 전부)
            if (keyboard != null && keyboard.gKey.wasPressedThisFrame)
                ToggleRanges();
            // R: 놓을 타워 방향을 시계 방향으로 90°
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                RotatePlacement();

            if (!CanInteract || pointer == null)
                return;

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            var screen = pointer.position.ReadValue();
            Vector2Int cell = default;
            bool hasCell;
            if (dragKind != DragKind.None)
            {
                UpdateDragPoint(screen);
                hasCell = !overUi && HasDragPoint && board.Map != null && board.TryWorldToCell(DragPoint, out cell);
            }
            else
            {
                hasCell = !overUi && TryGetCell(screen, out cell);
            }
            PointAt(hasCell, cell);

            // 드래그 중: 손을 떼는 곳에 놓는다 (UI 위·맵 밖이면 취소).
            if (dragKind != DragKind.None)
            {
                if (pointer.press.wasReleasedThisFrame)
                    Drop();
                return;
            }

            if (pointer.press.wasPressedThisFrame && hasCell)
            {
                // 설치된 타워를 누르면 떼기 전까지 클릭인지 이동 드래그인지 기다린다.
                var tower = Popup == PopupKind.None && Mode != BuildMode.Rebuild ? towers.GetTowerAt(cell) : null;
                if (tower != null)
                {
                    pressedTower = tower;
                    pressCell = cell;
                }
                else
                {
                    Click(cell);
                }
            }

            if (pressedTower != null)
            {
                if (!pointer.press.isPressed)
                {
                    pressedTower = null;
                    Click(pressCell);                  // 칸을 벗어나지 않고 뗌 → 클릭 (일반 상태: 판매 팝업)
                }
                else if (hasCell && cell != pressCell)
                {
                    var tower = pressedTower;
                    pressedTower = null;
                    BeginMoveDrag(tower);              // 누른 채 다른 칸으로 → 이동 드래그
                }
            }
        }

        // 마우스가 가리키는 칸 갱신. 칸이 바뀔 때만 HoverChanged를 낸다 (GDD 8-5 #7).
        public void PointAt(bool hasCell, Vector2Int cell)
        {
            if (hasCell == HasHover && (!hasCell || cell == HoverCell))
                return;
            HasHover = hasCell;
            HoverCell = cell;
            if (Mode != BuildMode.None || dragKind != DragKind.None)
                HoverChanged?.Invoke(new CellHover(hasCell, cell, hasCell && CanDropAt(cell)));
            if (dragKind != DragKind.None)
                RaiseDrag();
            UpdatePreview();
        }

        // 칸 클릭. 팝업이 열려 있으면 무시한다.
        public void Click(Vector2Int cell)
        {
            if (Popup != PopupKind.None || !CanInteract)
                return;

            if (Mode == BuildMode.None)
            {
                // 일반 상태: 설치된 타워 클릭 → 판매 팝업 (GDD 5-8). 테크트리가 켜져 있고 다음 단계가 있으면 진화 팝업
                var tower = towers.GetTowerAt(cell);
                if (tower == null)
                    return;
                var next = towers.GetUpgrade(tower.Def);
                if (next != null)
                {
                    upgradeTarget = tower;
                    Popup = PopupKind.Upgrade;
                    PopupRequested?.Invoke(new PopupRequest(PopupKind.Upgrade, cell, next, towers.GetUpgradeCost(tower.Def, next),
                        tower.Def, towers.GetSellRefund(tower.Def)));
                    return;
                }
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
            UpdatePreview();
            OpenPopup(PopupKind.Build, cell, modeDef, Mode == BuildMode.Rebuild ? 0 : modeDef.buildCost);
        }

        // 타워 목록 아이콘 클릭: 칸 클릭 → 확인 팝업으로 지을 타워를 바꾼다 (정보 표시는 GameUI).
        public void SelectBuildDef(TowerDef def)
        {
            if (def == null || Mode != BuildMode.Build || def == modeDef)
                return;
            ClearSelection();
            if (Popup == PopupKind.Build)
                ClosePopup();
            modeDef = def;
            UpdatePreview();
        }

        // 타워 목록 아이콘을 끌기 시작 (BuildMode에서만). 손을 떼는 곳에 바로 설치한다 (확인 팝업 없음).
        public void BeginNewTowerDrag(TowerDef def)
        {
            if (def == null || Mode != BuildMode.Build || !CanInteract || dragKind != DragKind.None)
                return;
            ClearSelection();
            ClosePopup();
            dragDef = def;
            dragTower = null;
            StartDrag(DragKind.NewTower);
        }

        // 설치된 타워를 끌기 시작: 빈 부지에 놓으면 이동 (캐시 변화 없음).
        public void BeginMoveDrag(TowerInstance tower)
        {
            if (tower == null || !CanInteract || dragKind != DragKind.None || Popup != PopupKind.None || Mode == BuildMode.Rebuild)
                return;
            ClearSelection();
            dragDef = tower.Def;
            dragTower = tower;
            StartDrag(DragKind.MoveTower);
        }

        // 지금 가리키는 칸에 놓는다. 놓을 수 없는 칸·맵 밖이면 아무 일 없이 끝난다.
        public void Drop()
        {
            if (dragKind == DragKind.None)
                return;
            if (HasHover && CanDropAt(HoverCell))
            {
                if (dragKind == DragKind.NewTower)
                    towers.Build(HoverCell, dragDef, PlacementFacing); // 캐시·부지 최종 판정은 TowerManager
                else
                    towers.Move(dragTower, HoverCell, PlacementFacing); // 제자리면 방향만 바뀐다 (같으면 그대로)
            }
            EndDrag();
        }

        public void CancelDrag()
        {
            if (dragKind != DragKind.None)
                EndDrag();
        }

        // 여기에 놓을 수 있는지: 새 타워 = 빈 부지 + 캐시, 이동 = 빈 부지 (제자리는 그대로 두기라 허용).
        bool CanDropAt(Vector2Int cell)
        {
            switch (dragKind)
            {
                case DragKind.NewTower:
                    return board.IsBuildable(cell) && player.CanAfford(dragDef.buildCost);
                case DragKind.MoveTower:
                    return cell == dragTower.Cell || board.IsBuildable(cell);
                default:
                    return board.IsBuildable(cell);
            }
        }

        // 포인터 광선이 공중 높이(DragHoldHeight) 평면과 만나는 곳 = 들고 있는 타워 위치.
        void UpdateDragPoint(Vector2 screen)
        {
            HasDragPoint = false;
            if (camera == null)
                return;
            var ray = camera.ScreenPointToRay(screen);
            if (!HoldPlane.Raycast(ray, out float distance))
                return;
            DragPoint = ray.GetPoint(distance);
            HasDragPoint = true;
        }

        void StartDrag(DragKind kind)
        {
            dragKind = kind;
            if (Pointer.current != null)
                UpdateDragPoint(Pointer.current.position.ReadValue()); // 잡은 순간부터 포인터 위치에 뜨도록
            RaiseDrag();
            if (HasHover)
                HoverChanged?.Invoke(new CellHover(true, HoverCell, CanDropAt(HoverCell)));
            UpdatePreview();
        }

        void EndDrag()
        {
            dragKind = DragKind.None;
            dragDef = null;
            dragTower = null;
            HasDragPoint = false;
            DragChanged?.Invoke(new TowerDrag(DragKind.None, null, null, false, default, false));
            if (HasHover && Mode != BuildMode.None)
                HoverChanged?.Invoke(new CellHover(true, HoverCell, board.IsBuildable(HoverCell)));
            UpdatePreview();
        }

        void RaiseDrag() =>
            DragChanged?.Invoke(new TowerDrag(dragKind, dragDef, dragTower, HasHover, HoverCell, HasHover && CanDropAt(HoverCell)));

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
            if (dragKind == DragKind.NewTower)
                CancelDrag();
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
                        towers.RequestRebuild(modeDef, SelectedCell, PlacementFacing); // 캐시 없음, 목록에서 1개 차감
                    else
                        towers.Build(SelectedCell, modeDef, PlacementFacing);          // 캐시·부지 최종 판정은 TowerManager
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
            else if (Popup == PopupKind.Upgrade)
            {
                towers.Upgrade(upgradeTarget); // 해금·캐시 최종 판정은 TowerManager
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
            if (Popup == PopupKind.Upgrade)
            {
                // [판매] → 판매 확인 팝업으로
                var tower = upgradeTarget;
                ClosePopup();
                if (tower != null && towers.GetTowerAt(tower.Cell) == tower)
                {
                    sellTarget = tower;
                    OpenPopup(PopupKind.Sell, tower.Cell, tower.Def, towers.GetSellRefund(tower.Def));
                }
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
            CancelDrag();
            pressedTower = null;
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
            UpdatePreview();
        }

        // --- 설치 방향 (R키) · 사거리 그리드 (G키) ---

        // 놓을 칸·타워가 정해지는 동안의 미리보기: 끄는 중이면 가리키는 칸(놓을 수 있을 때), 칸을 골랐으면 그 칸,
        // 건축 모드에서 고르기 전이면 가리키는 설치 가능 칸. 칸이나 타워가 바뀌면 방향을 다시 맞춘다
        // (경로를 가장 많이 덮는 방향, 설치된 타워를 제자리에 들고 있으면 원래 방향).
        void UpdatePreview()
        {
            TowerDef def = null;
            Vector2Int cell = default;
            bool has = false;
            if (dragKind != DragKind.None)
            {
                def = dragDef;
                has = HasHover && CanDropAt(HoverCell);
                cell = HoverCell;
            }
            else if (Mode != BuildMode.None && modeDef != null)
            {
                def = modeDef;
                if (HasSelection)
                {
                    has = true;
                    cell = SelectedCell;
                }
                else if (Popup == PopupKind.None && HasHover && board.IsBuildable(HoverCell))
                {
                    has = true;
                    cell = HoverCell;
                }
            }
            if (has == preview.Has && (!has || (cell == preview.Cell && def == preview.Def)))
                return;
            if (has)
                PlacementFacing = dragKind == DragKind.MoveTower && cell == dragTower.Cell ? dragTower.Facing : towers.BestFacing(def, cell);
            RaisePreview(has, cell, def);
        }

        public void RotatePlacement()
        {
            if (!preview.Has)
                return;
            PlacementFacing = TowerFacing.Next(PlacementFacing);
            RaisePreview(true, preview.Cell, preview.Def);
        }

        public void ToggleRanges()
        {
            ShowRanges = !ShowRanges;
            RangeDisplayChanged?.Invoke(new RangeDisplayChange(ShowRanges));
        }

        void RaisePreview(bool has, Vector2Int cell, TowerDef def)
        {
            preview = new PlacementPreview(has, cell, def, PlacementFacing);
            PreviewChanged?.Invoke(preview);
        }

        void ClearSelection()
        {
            if (!HasSelection)
                return;
            HasSelection = false;
            SelectionChanged?.Invoke(new CellSelection(false, SelectedCell));
            UpdatePreview();
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
            upgradeTarget = null;
            if (Popup == PopupKind.None)
                return;
            Popup = PopupKind.None;
            PopupRequested?.Invoke(new PopupRequest(PopupKind.None, default, null, 0));
        }
    }
}
