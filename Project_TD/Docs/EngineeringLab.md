# 공학연구소 (2단계 이상 타워 해금)

로비(게임 시작 전 도시 화면)의 **공학연구소** 핀을 누르면 열리는 창입니다. 2단계 이상 타워를 재화 **M1반도체**로 해금합니다.

## 규칙

| 항목 | 내용 |
|---|---|
| 재화 | M1반도체. 판이 끝날 때(게임 오버·게임 클리어) **클리어한 층 수만큼 한 번에** 받는다. 게임 오버 = 도달 층 − 1, 게임 클리어 = 마지막 층까지 전부 (10층 클리어 = 10개) |
| 해금 대상 | 1단계 타워의 진화 갈래(`TowerDef.upgradeTo`)에 있는 2단계 이상 타워 |
| 해금 효과 | 해금한 타워만 게임 중 진화할 수 있다. 해금 전 타워는 진화 목록(테크트리)에 보이되 "공학연구소에서 해금"으로 잠기고, 진화 팝업의 [진화]도 꺼진다 |
| 저장 | 판을 넘어 남는다. `Application.persistentDataPath/meta.json` (M1반도체 개수, 해금한 타워 id) |
| 켜고 끄기 | `GameConfig.labUnlock` (GameConfig.asset은 켬. 끄면 모든 타워 해금 상태, M1반도체 지급 없음) |

## 해금 가격 (M1반도체, `TowerDef.unlockPrice`)

| 타워 | 진화 원본 | 가격 |
|---|---|---:|
| 냉기 | 듀얼샷(기본) | 1 |
| 전기 | 듀얼샷(기본) | 1 |
| 미니건 | 듀얼샷(기본) | 2 |
| 혼합분진 | 화염방사 | 2 |
| 혼합백린 | 화염방사 | 2 |
| 플라즈마 | 화염방사 | 3 |
| 합계 | | 11 |

한 판을 끝까지 클리어하면 10개라, 한 판으로 거의 다 해금하고 두 번째 판에서 전부 해금됩니다.

## 창 구성

- 위: 제목, 보유 M1반도체, [닫기]
- 타워 카드: 모델 아이콘 · 이름 · 단계 · 공격 형식 · 진화 경로(원본 → 이 타워) · 공격력·공격 간격·사거리·설치 비용 · 설명 · 가격 · [해금] (해금됨 / 부족)
- 창 밖 어두운 곳을 누르면 닫힌다. 창이 열린 동안 화면 클릭으로 게임이 시작되지 않는다.
- 판 결과창에 "M1반도체 +N개 (보유 M개)"가 붙는다.

## 코드

| 파일 | 역할 |
|---|---|
| `Scripts/Core/MetaProgress.cs` | 판을 넘어 남는 진행 데이터 (M1반도체, 해금 id), 불러오기·저장, `MetaChange` 이벤트 인자 |
| `Scripts/Player/PlayerState.cs` | 소유 모듈: `InitMeta`, `IsTowerUnlocked`, `TryUnlockTower`, `HandleStateChanged`(판 끝 지급), `MetaChanged` 이벤트 |
| `Scripts/Towers/TowerManager.Build.cs` | `IsUpgradeUnlocked`에 공학연구소 해금 조건 추가 |
| `Scripts/UI/LabView.cs` | 공학연구소 창 (GameUI 소속) |
| `Scripts/UI/LobbyView.cs` | `Pin.action`이 있는 핀만 누를 수 있음 → `PinClicked` (SampleScene: 공학연구소 핀 = `lab`) |
| `Scripts/UI/GameUI.cs`, `TechTreeView.cs` | 창 열기, 잠긴 진화 표시, 결과창의 M1반도체 줄 |
| `Scripts/Input/BuildController.cs` | Ready에서 UI 위 클릭은 게임 시작이 아님 |
| `Tests/Editor/LabUnlockTests.cs` | 지급(한 판에 한 번)·해금·진화 막기·저장 테스트 |

새 매니저는 만들지 않았습니다 (재화 = PlayerState 소유, 창 = GameUI 소속).
