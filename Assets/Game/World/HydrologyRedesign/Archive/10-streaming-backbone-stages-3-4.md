# 생성 백본 이관 3·4단계 — River 계획 구현 기록

## 범위와 Gate

이 단계는 `07-streaming-generation-backbone.md`의 River Proposal, Route,
InteractionResolution, EdgeResolution, SpatialIndex 사실을 새 Snapshot에 추가한다.
기존 Hydrology Store, Scope, Batch, Lazy builder는 입력 또는 의존성으로 사용하지
않는다. 기존 실행 경로는 아직 전환하지 않는다.

## 3단계 완료: Proposal Tile과 canonical Route

`StreamingRiverGraph.cs`는 다음 River 사실을 추가한다.

```text
EndpointTile
  → RiverProposalTile[anchor owner]
  → RiverRoutePlan[canonical EdgeId]
```

- Proposal owner는 anchor Endpoint가 자기 Tile core에 속한 경우뿐이다.
- target 탐색은 Settings의 `ConnectionRadiusCells.Maximum` 범위에서 준비된 Endpoint
  Tile만 읽는다.
- Lake/Pond/Sea/Natural case별 첫 경로 가능 후보, Natural `1/N`, 거리·EdgeId 순서,
  경로 실패 시 같은 case의 다음 후보 규칙을 유지한다.
- Route는 Base Terrain 사실과 명시된 Basin Allocation 사실을 입력으로 한다.
  기존 방향 비의존 비용, Basin 통과 금지, Natural rate transition, corridor seed
  공식을 사용한다.
- 후보 확인 중 만들어진 경로는 planning 중에만 존재한다. Snapshot에는 선택된
  Proposal이 참조하는 EdgeId별 Route 하나만 기록한다.

## 4단계 완료: Interaction·Edge·Spatial 사실

```text
RiverRoutePlan
  → InteractionResolutionTile[interaction coordinate owner]
  → EdgeResolution[EdgeId]
  → RiverSpatialIndexTile[requested core]
```

- Route 쌍의 접근/교차 결과는 interaction 좌표가 속한 Tile 하나만 소유한다.
- 확률은 proximity와 alignment curve의 곱이며, 통과 여부는 두 canonical EdgeId와
  interaction 좌표의 Seed로 결정한다.
- 거부된 interaction은 큰 EdgeId만 inactive로 만든다. 작은 EdgeId가 이후 다른
  이유로 inactive가 되어도 큰 EdgeId를 다시 활성화하지 않는다.
- Junction은 accepted interaction이면서 두 Edge가 active일 때만 만들며, 최저
  수면·최저 바닥·Edge 합집합 정책을 사용한다.
- SpatialIndex Tile은 요청 core에 corridor가 닿는 active Route 참조와 core 소유
  Junction만 보관한다. Route 배열은 Tile마다 복사하지 않는다.

## 명시적 의존 범위

상위 `StreamingRiverPlanningStage.Build(request)`는 다음 Settings 유도 범위만
미리 준비한다.

```text
R = ceil(ConnectionRadiusCells.Maximum)
C = ceil(RiverWidthCells.Maximum / 2 + BankMarginCells)
B = BasinMaximumReachCells + BasinShoreTransitionCells

Spatial core + R + C → Proposal Tile
Proposal core + R     → Endpoint Tile
Proposal core + 2R+B  → Basin Allocation Tile
```

Route가 Raster나 consumer에서 새 Tile을 생성하지 않도록, 위 dependency Tile은
Snapshot seal 전에 모두 준비된다. Candidate raw geometry의 conflict 확인은 같은
Snapshot planning 중에만 수행되며, consumer 수명이나 시간 기반 cache를 만들지
않는다.

## 아직 하지 않은 일

- 새 Hydrology Raster와 Chunk materializer는 아직 없다.
- 초기 생성, streaming target, pattern debugger는 아직 새 planning stage를 호출하지
  않는다.
- 따라서 실제 결과·결정성·메모리·성능이 개선되었다고 판단할 수 없다. 다음 소비
  경로 전환 뒤에 사용자의 실제 실행으로만 확인한다.

## 컴파일 확인

`dotnet build MiniCivilization.World.csproj --no-restore --verbosity minimal` 결과:
경고 0개, 오류 0개.

## 다음 인계

다음 단계는 sealed `PlanningSnapshot`을 입력으로만 받는 Hydrology Raster와 기존
Terrain/Water Source materialization 연결이다. Raster는 Endpoint, Proposal, Route,
Interaction을 새로 만들거나 보충하지 않는다.
