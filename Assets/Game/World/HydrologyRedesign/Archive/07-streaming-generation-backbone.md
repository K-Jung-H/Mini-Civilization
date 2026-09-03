# 7단계 설계 — 통합 스트리밍 월드 생성 백본

## 목적

현재의 `Chunk → HydrologyBatch → Lazy Plan` 흐름을 제거하고, 절대 좌표 계획을
먼저 확정한 뒤 Chunk가 그 결과만 materialize하는 생성 백본으로 교체한다.

고정맵과 무한 월드는 Terrain/Hydrology 평가기, Planning Tile, Chunk materializer를
공유한다. 차이는 요청 가능한 Chunk 좌표의 경계뿐이다.

이 설계는 [06-generation-backbone.md](06-generation-backbone.md)의 결과 보존 계약을
전제로 한다. 렌더링, `WorldData`의 Chunk/Section 형식, Runtime Cache, Entity,
WaterSystem의 낙하·흐름 처리는 생성 백본의 교체 대상이 아니다.

## 금지되는 이전 흐름

```text
금지: Chunk / Batch / Debugger → Store Lazy 생성 → 추가 Scope 확장
```

- Chunk, Batch, Debugger는 Topology, Proposal, Route, Junction을 새로 만들지 않는다.
- EdgeId 결과가 Proposal/Topology Scope를 소유하지 않는다.
- 계획 Region 전체의 `HydrologyCellPlan[]`을 단지 한 Cell 또는 한 Chunk를 읽기 위해
  미리 rasterize하지 않는다.
- 시간·용량·최근 사용 여부로 계획 결과를 유지하거나 제거하지 않는다.
- 고정맵 경계, 아직 materialize하지 않은 Chunk, Target 이동은 좌표 계획의 입력이
  되지 않는다.

## 공통 생성 흐름

```text
WorldGenerationRequest
  └ requested Chunk 집합 + 목적(initial / stream / debugger)
        ↓
PlanningFootprint
  └ 요청 Cell과 Settings로부터 유도한 Tile/Component/Edge 입력 관계
        ↓
PlanningSnapshot (sealed, immutable)
  ├ BaseTerrainEvaluator
  ├ BasinCandidate / BasinAllocationTile
  ├ TopologyEvaluator
  ├ EndpointTile
  ├ ProposalTile / RoutePlan
  ├ InteractionResolutionTile / EdgeResolution
  └ SpatialIndexTile
        ↓
HydrologyRaster (요청 사각형만)
        ↓
ChunkMaterializer
        ↓
WorldData.LoadedChunks
```

`initial`, `stream`, `debugger`는 결과를 바꾸는 입력이 아니다. 목적은 우선순위,
취소, 결과를 소비하는 대상만 결정한다.

## 기본 단위와 유도 범위

모든 Planning Tile의 좌표 단위는 기존 `HydrologyMap.PlanningRegionSizeCells`를
사용한다. 현재 값 128은 Natural Endpoint의 위치와 결과를 결정하므로 성능용
상수로 변경하지 않는다.

다음 값은 별도 상수가 아니라 현재 Settings에서 매 요청 계산한다.

```text
T = PlanningRegionSizeCells
R = ceil(ConnectionRadiusCells.Maximum)
C = ceil(RiverWidthCells.Maximum / 2 + BankMarginCells)
B = BasinMaximumReachCells + BasinShoreTransitionCells
K = ceil((2 * BasinMaximumReachCells + BasinMinimumSeparationCells)
         / BasinSeedSpacingCells)
```

- `B`는 어떤 Basin footprint 또는 shore가 한 Topology Cell에 영향을 줄 수 있는
  최대 거리다.
- `K`는 한 Basin 후보의 활성 판정이 확인해야 하는 Seed Grid 반경이다.
- `R`은 Endpoint 후보와 Route lattice의 최대 연결 반경이다.
- `C`는 River Corridor가 SpatialIndex 경계를 넘어 영향을 줄 수 있는 최대 거리다.

`PlanningFootprint`는 이 값과 명시적 부모 관계로만 확장한다. 예를 들어
Interaction Tile은 `core + R + C` 안의 Proposal owner만 읽고, Proposal Tile은
현재 Route 탐색 공식이 요구하는 topology lattice만 읽는다. Route가 확정된 뒤에는
그 실제 AABB와 `C`로 EdgeResolution이 읽을 Interaction Tile을 정확히 기록한다.

이 과정은 lazy Scope의 임의 확장이 아니다. 모든 추가 key는 `parent key`, 관계명,
Settings로 계산한 범위를 기록하며, 각 관계에는 위 값으로 증명 가능한 상한이 있다.
Snapshot이 sealed된 뒤에는 새 key를 추가할 수 없다.

## 계획 데이터와 소유권

### 1. Base Terrain과 Basin

```text
BaseTerrainEvaluator(X, Z)
  → WorldFieldSample + Base Terrain Pattern + TerrainSurfaceSample

BasinCandidate[ComponentId]
  → 발생 여부, 독립 footprint, 수면, 깊이, boundary

BasinAllocationTile[TileKey]
  → 해당 owner core의 ComponentId별 활성/무효 결과
```

- `BaseTerrainEvaluator`는 순수 평가기다. Snapshot은 실제로 요청된 absolute Cell
  표본만 공유한다. Natural Endpoint처럼 core 전체를 순회하는 평가기만 그 범위의
  표본을 요구한다.
- `BasinCandidate`는 현재 `BasinComponentBuilder`의 공식과 `ComponentId`를 그대로
  사용한다. Base Terrain을 다시 표본화한 지역 halo는 결과물이 아니다.
- `BasinAllocationTile`은 Seed 위치가 core에 속한 Candidate의 활성 결과를 소유한다.
  판정 중 필요한 Candidate는 `K` 범위에서 같은 raw Candidate 사실을 읽는다.
  높은 우선순위의 실제 활성 Candidate만 낮은 Candidate를 무효화하는 현재 규칙을
  그대로 사용한다.
- Candidate raw geometry와 Allocation 결과는 Snapshot의 참조가 있을 때만 보존된다.
  Topology나 Edge가 독자적으로 Basin Scope를 보존하지 않는다.

### 2. Topology와 Endpoint

```text
TopologyEvaluator.Sample(X, Z)
  → Sea / Lake / Pond의 최종 HydrologyCellPlan 사실

EndpointTile[TileKey]
  → Basin Endpoint, Sea 해안 Endpoint, Natural Endpoint
```

- `TopologyEvaluator`는 Sea 사실, 활성 Basin Component, shore 규칙을 해당 Cell에
  투영하는 순수 조립기다. `TopologyRegion` 전체를 primary 결과로 rasterize하지 않는다.
- Basin Endpoint는 현재처럼 Component seed가 속한 core에서 단 하나만 만든다.
  Sea Endpoint는 기존 Route sampling 간격의 해안 판정으로, Natural Endpoint는
  core의 물/보호 영역이 아닌 Cell 중 Seed 점수가 가장 높은 Cell로 만든다.
- Natural Endpoint의 full-core 순회는 결과 규칙이므로 유지한다. 다만 그 결과를
  얻기 위해 River Graph나 Chunk용 dense Hydrology map을 만들지 않는다.

### 3. 무방향 River Graph

```text
ProposalTile[TileKey]
  → core Endpoint가 제안한 canonical EdgeId 후보

RoutePlan[EdgeId]
  → Route, Corridor profile, Natural transition

InteractionResolutionTile[TileKey]
  → Route 쌍의 접근/교차, 허용·거부, Junction plan

EdgeResolution[EdgeId]
  → 경로가 지나는 Interaction 결과를 집계한 final active 상태

SpatialIndexTile[TileKey]
  → core에 영향을 주는 active RoutePlan 참조 + core Junction
```

- Proposal은 Endpoint core owner가 한 번만 만들며, target 탐색·Natural 1/N 선택·
  거리/Id 정렬·Route 실패 처리의 현재 규칙을 보존한다.
- `RoutePlan`은 canonical `EdgeId`별 하나다. 동일 Edge가 두 Endpoint에서 보이면
  canonical endpoint 순서와 동일 Seed 입력으로 같은 geometry를 만들고 하나로
  합친다.
- `InteractionResolutionTile`이 고정 좌표 영역에서만 Route 쌍을 판정한다. 실제
  interaction 좌표가 자기 core에 속한 경우만 저장하므로, 같은 쌍을 여러 Tile이
  결과로 소유하지 않는다.
- `EdgeResolution`은 자신의 route AABB와 `C`가 교차하는 Interaction Tile 결과만
  읽는다. 이는 참조 목록일 뿐 Proposal/Topology Scope가 아니다.
- Interaction이 거부되면 큰 EdgeId가 무효다. 작은 EdgeId의 이후 상태와 무관하게
  재활성화하지 않는다. 허용된 Junction도 접근 Edge가 모두 active일 때만
  `SpatialIndexTile`에 노출한다.
- `SpatialIndexTile`은 core와 Corridor가 실제로 교차하는 active Edge와 core 소유
  Junction만 보관한다. Region별 route 복사는 하지 않는다.

이 구조에서 Junction 판정은 전역 반복이나 Edge별 넓은 Scope 보존 없이도 현재의
불변 EdgeId 우선 정책을 유지한다.

### 4. Snapshot과 Raster

```text
PlanningSnapshot
  └ sealed Tile/Component/Route/Resolution 참조 집합

HydrologyRaster(rectangle, snapshot)
  1. rectangle Cell의 TopologyEvaluator 결과 작성
  2. rectangle + C의 SpatialIndexTile에서 active Route를 수집
  3. River Corridor를 rasterize하되 Topology Membership Cell은 덮어쓰지 않음
  4. core 소유 Junction을 같은 우선순위 규칙으로 작성
```

- Snapshot은 Planner만 만들고 sealed한다. Raster와 Debugger는 누락된 계획을
  계산하거나 확보하지 않는다.
- Raster가 소유하는 유일한 dense 결과는 요청 rectangle의 `HydrologyCellPlan[]`이다.
  Chunk build가 끝나면 이 배열은 해제한다.
- River의 폭·깊이·바닥·최종 Terrain target 및 Basin/Sea 우선순위는 현재
  `HydrologyBatchBuilder` 공식을 그대로 사용한다.

## 고정맵과 스트리밍 적용

### WorldData 시작 상태

새 월드는 Settings/Seed/WorldType/고정맵 경계 metadata와 빈 `LoadedChunks`로 시작할
수 있다. 전체 고정맵을 시작 시점에 materialize하지 않는다.

- `WorldType.Finite`는 `IsChunkWithinBounds`가 고정 사각형 밖 요청을 거부한다.
- `WorldType.Infinite`는 같은 절대 좌표 규칙으로 모든 Chunk 요청을 허용한다.
- 두 경우 모두 허용된 Chunk의 Terrain/Hydrology 계획은 같다.

### Initial / Stream 요청

`WorldRuntime`은 Chunk별 독립 Hydrology Scope를 만들지 않는다. 현재 desired prepared
Chunk 집합을 하나의 `WorldGenerationRequest`로 제출한다.

1. Coordinator가 Request의 PlanningSnapshot을 작업 스레드에서 준비한다.
2. sealed Snapshot으로 가까운 Chunk부터 materialize한다.
3. `WorldData` 적용, Cache 준비, Renderer/WaterSystem 연결은 현재의 안전한 메인
   스레드 순서를 유지한다.
4. Target이 바뀌면 이전 Request의 미완료 Tile 단계만 취소한다. 다른 Request가
   참조 중인 완료 Tile은 공유하고, 더 이상 참조되지 않는 Snapshot은 해제한다.

Chunk 표시 순서는 거리 오름차순, 동률이면 canonical `(X, Z)` 순서로 정의한다.
이는 지형 결과가 아니라 사용자에게 보이는 준비 순서만 결정한다.

### Pattern Debugger

Debugger는 표시 Pixel의 absolute Cell을 Tile key로 중복 제거해 별도
`WorldGenerationRequest`를 만든다. Planner가 sealed Snapshot을 반환한 뒤에만
해당 Cell을 읽는다. Runtime Snapshot의 수명이나 요청 범위를 변경하지 않는다.

선택된 실제 Terrain overlay만 작은 rectangle Raster를 만들고 즉시 해제한다.

## 이관 단계

### 1. 활성 평가기 분리

- 현재 활성 Base Terrain, Basin Candidate/활성, Topology Cell 조립, Endpoint,
  Route/Corridor, Junction 계산을 결과 변경 없이 순수 평가기 입력으로 분리한다.
- `RiverNetwork`에서 실제로 읽는 방향 비의존 Route 설정을 새 Route 평가 설정으로
  옮기되 수치는 바꾸지 않는다.
- 기존 generator를 호출하는 adapter나 결과 fallback을 만들지 않는다.

### 2. PlanningSnapshot과 Tile 사실 구현

- BasinAllocation, Endpoint, Proposal, InteractionResolution, SpatialIndex의
  불변 데이터와 명시적 소유 관계를 구현한다.
- 모든 Builder가 입력/출력 key와 유도 범위를 선언하고, 소비 계층에서의 Lazy Plan
  생성을 금지한다.
- 이 단계까지 Renderer/WorldRuntime의 실제 생성 경로는 전환하지 않는다.

### 3. Raster와 Chunk materializer 전환

- sealed Snapshot만 받는 새 Hydrology Raster를 구현한다.
- 기존 Terrain density/column materialization과 Water Source 배치에 연결한다.
- 이전 `WorldGenerationPipeline`의 전체 고정맵 선생성 경로를 새 Request 기반 초기
  materialization으로 교체한다.

### 4. Runtime과 Debugger 전환

- Runtime의 desired prepared Chunk 집합을 Request/Snapshot lifecycle에 연결한다.
- Tile 단계 취소, canonical 준비 우선순위, 완료 Chunk 적용만 Runtime이 맡는다.
- Debugger의 Pixel별 Hydrology Batch 질의를 Snapshot 요청으로 교체한다.

### 5. 실제 검증 후 정리

- 사용자가 고정맵, 경계, Target 이동, Pattern Debugger에서 결과와 성능을 확인한다.
- 결과 보존과 스트리밍 지연 문제가 확인된 뒤에만 이전 Store, Legacy generator,
  방향성 잔여 설정과 사용하지 않는 직렬화 항목을 제거한다.

각 단계에서 에이전트가 수행하는 검증은 컴파일 오류 확인뿐이다. 실제 결과,
결정성, 메모리, 초기 생성 시간, 새 Chunk 첫 표시 시간은 사용자의 실행 환경에서만
판정한다.

## 완료 기준

- 고정맵과 무한 월드가 같은 Generator/Tile/Raster/Materializer를 사용한다.
- 같은 절대 좌표의 계획 결과가 Request 목적, 요청 순서, Chunk 적용 순서와 무관하다.
- Batch와 Debugger가 계획 Builder를 호출하지 않는다.
- Edge가 Proposal/Topology Scope를 소유하지 않고, Junction 결과는 좌표 Tile 하나가
  소유한다.
- 계획 메모리는 활성 Request Snapshot의 참조 범위를 넘어서 보존되지 않는다.
- 렌더링·Runtime Cache·WaterSystem의 기존 역할을 생성 계층이 대체하지 않는다.
