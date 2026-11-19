# 반도체 공장 (모든 타워 기본 스탯 강화)

로비(도시 화면)의 **반도체 공장** 핀을 누르면 열리는 창입니다. 재화 **작은 반도체**로 노드를 강화하며, 효과는 판을 넘어 계속 남습니다.

## 규칙

| 항목 | 값 |
|---|---|
| 재화 | 작은 반도체. 판이 끝날 때(게임 오버·게임 클리어) **클리어한 층 수 × 2개**를 한 번에 받는다 (10층 클리어 = 20개) |
| 노드 | 5종, 노드마다 최대 5단계 |
| 단계당 효과 | 5% (5단계 = 25%) |
| 단계별 가격 | 1 → 2 → 3 → 4 → 5개 (노드 하나 끝까지 15개) |
| 초기화 | 무료. 쓴 작은 반도체를 모두 돌려받고 모든 노드를 0단계로 |
| 저장 | `Application.persistentDataPath/meta.json` (smallChips, factory 단계) |
| 설정 | `GameConfig.factory` (enabled, perLevel, maxLevel, levelCosts, chipsPerFloor) |

## 노드

| 노드 | 효과 (단계당) | 적용 위치 |
|---|---|---|
| 공격력 | 모든 타워 공격력 +5% | `TowerManager.GetDamage` (모든 타워 피해가 지나는 한 곳) |
| 공격 속도 | 모든 타워 공격 속도 +5% (공격 간격 ÷ (1 + 0.05 × 단계)) | `TowerManager.GetInterval` |
| 설치 비용 할인 | 설치·진화 비용 −5% (반올림). 판매·회수 환급은 원래 비용 기준 | `TowerManager.GetBuildCost` · `GetUpgradeCost`, 타워 목록·테크트리 표시 |
| 시작 캐시 | 판 시작 캐시 +5% (100 → 5단계 125) | `PlayerState.ResetForNewRun` |
| 넥서스 체력 | 넥서스 체력 +5% (20 → 단계당 +1, 5단계 25) | `PlayerState.ResetForNewRun` |

층 클리어 아이템(화력·연사)과는 곱해서 함께 적용됩니다.

## 코드

| 파일 | 역할 |
|---|---|
| `Scripts/Core/MetaProgress.cs` | smallChips, factory 단계 목록, `FactoryNode`, `MetaChange`(SmallChips·SmallGranted 추가) |
| `Scripts/Player/PlayerState.cs` | 소유 모듈: `FactoryLevel`, `TryUpgradeFactory`, `ResetFactory`, `DamageMul`·`SpeedMul`·`ApplyCost`, 판 끝 지급, 새 판 시작 캐시·넥서스 체력 |
| `Scripts/Core/GameConfig.cs` | `FactorySettings` |
| `Scripts/Towers/TowerManager.Build.cs` | 공격력·공격 간격·설치/진화 비용에 공장 효과 반영 |
| `Scripts/UI/FactoryView.cs` | 반도체 공장 창 (GameUI 소속) |
| `Scripts/UI/GameUI.cs`, `LobbyView.cs` | 공장 핀(`factory`) → 창, 판 결과에 작은 반도체 지급 줄, 비용 표시 갱신 |
| `Tests/Editor/FactoryTests.cs` | 지급·가격·최대 단계·무료 초기화·효과 5종·저장 테스트 |

새 매니저는 만들지 않았습니다 (재화·단계 = PlayerState 소유, 창 = GameUI 소속).
