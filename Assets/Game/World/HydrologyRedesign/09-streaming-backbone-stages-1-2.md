# 생성 백본 이관 1·2단계 — 구현 기록

## 범위와 Gate

이 기록은 `06-generation-backbone.md`, `07-streaming-generation-backbone.md`,
`08-implementation-gates.md`를 기준으로 한 새 생성 백본의 첫 구현 단위다.

- Gate A: 새 평가기는 Settings·Seed·절대 좌표 또는 명시적 입력 사실만 읽는다.
- Gate B: Tile output은 owner key를 가지며, consumer가 계획을 시작하지 않는다.
- 기존 생성·렌더링·Runtime·WaterSystem 경로는 전환하지 않는다.
- 테스트 fixture 또는 실행용 코드는 추가하지 않는다. 이 단계의 확인은 C# 컴파일뿐이다.

## 1단계 완료: 요청·Footprint·Snapshot·Base Terrain

`Assets/Game/World/Generation/Streaming/StreamingGenerationCore.cs`에 다음을
추가했다.

```text
WorldGenerationRequest
  → PlanningFootprint
  → PlanningSnapshotBuilder
  → sealed PlanningSnapshot
```

- `WorldGenerationRequest`는 목적과 정렬·중복 제거된 Chunk 집합을 소유한다.
  `WorldType.Finite`의 경계 확인은 이 요청 입력에서만 수행한다.
- `PlanningFootprint`는 요청 Chunk의 Cell 사각형과 Planning Tile key를 절대 좌표로
  계산한다.
- `StreamingBaseTerrainEvaluator`는 기존 순수 Terrain 공식만 사용해 Cell 사실을
  평가한다. Snapshot builder는 요청 중 실제로 읽은 표본만 공유한다.
- `PlanningSnapshot.Seal()` 뒤에는 base terrain, allocation, endpoint 사실을
  추가할 수 없다.

이 새 코드에는 기존 Hydrology Store, Scope, Batch, Lazy builder를 입력 또는
의존성으로 사용하지 않았다.

## 2단계 완료: Basin·Topology·Endpoint Tile 사실

`Assets/Game/World/Generation/Streaming/StreamingBasinTopology.cs`에 다음을
추가했다.

```text
BasinCandidate[ComponentId]
  → BasinAllocationTile[owner Tile]
  → StreamingTopologyEvaluation[명시적 core 입력]
  → EndpointTile[owner Tile]
```

- Basin candidate는 Seed grid Id, 명시적 base-terrain 사각 입력으로 footprint,
  일정 수면, depth, boundary를 만든다.
- Allocation은 seed가 Tile core에 속한 candidate의 active/inactive 결과를
  소유한다. 충돌 범위는 `2 * MaximumReachCells + MinimumSeparationCells`와
  `BasinSeedSpacingCells`에서 산출하며, 높은 priority만 낮은 priority를
  무효화한다.
- `StreamingTopologyEvaluation`은 Sea, active Basin, shore transition을 Cell에
  조립한다. Tile 전체 `HydrologyCellPlan[]`을 보존하지 않으며, Endpoint 생성 중
  필요한 core 표본만 명시적 입력으로 읽는다.
- Endpoint Tile은 Basin seed owner, 기존 Sea coast sampling, 그리고 Basin 보호
  영역과 물을 제외한 core의 Natural 최고 점수 규칙을 기록한다.
- `StreamingTopologyPlanningStage`는 상위 planning 단계가 명시한 Allocation/Endpoint
  Tile key를 준비한다. Snapshot sealing은 River 사실까지 준비하는 상위 단계만 한다.

## 아직 하지 않은 일

- River Proposal/Route/Interaction/Spatial Index는 다음 단계다.
- 새 Hydrology Raster, Chunk materializer, 초기 생성, streaming, debugger는 아직
  이 Snapshot을 사용하지 않는다.
- 따라서 현재 실행 결과·결정성·메모리·성능은 이 단계에서 검증하거나 개선됐다고
  판단하지 않는다. 사용자의 실제 실행 검증은 새 소비 경로 전환 뒤에만 의미가 있다.

## 컴파일 확인

`dotnet build MiniCivilization.World.csproj --no-restore --verbosity minimal` 결과:
경고 0개, 오류 0개.

## 다음 인계

다음 단계는 `EndpointTile`의 sealed 사실만 입력으로 River Proposal·Route와
InteractionResolutionTile을 만든다. Edge별 주변 Topology/Proposal 조회, 기존
Store/Scope 재사용, raster 또는 debugger에서의 누락 계획 생성은 허용하지 않는다.
