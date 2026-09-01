# 8단계 준비 — 생성 백본 구현 인계 규약

## 목적

이 문서는 구현 편의, 기존 코드 재사용, 작은 diff를 이유로 7단계 설계를 기존
`HydrologyPlanScope`/Store 구조에 다시 끼워 넣는 것을 금지한다. 다음 에이전트는
코드 수정 전에 이 문서와 `06-generation-backbone.md`,
`07-streaming-generation-backbone.md`를 모두 읽어야 한다.

## 최우선 판단 기준

기존 구조 보존이나 변경량 최소화보다 다음을 우선한다.

```text
절대 좌표 결과 보존
  > 명시적 PlanningFootprint / Snapshot 소유권
  > 고정맵·무한 스트리밍 공통 생성기
  > 기존 코드 재사용과 작은 변경량
```

현재 구현이 이 기준과 충돌하면 기존 구현을 adapter, fallback, 병행 경로로
연장하지 않는다. 새 경로가 계약을 충족하도록 교체하고, 기존 경로의 제거는 실제
검증 뒤로 미룬다.

## 새 생성 백본의 진입 조건

새 구현은 아래 의존성 방향을 코드 수준에서 가져야 한다.

```text
WorldGenerationRequest
  → PlanningFootprint
  → PlanningSnapshot (sealed)
  → HydrologyRaster
  → ChunkMaterializer
  → WorldData
```

다음 조건을 모두 만족하지 않으면 이를 새 백본 구현으로 인정하지 않는다.

- `HydrologyRaster`와 Debugger 소비 코드는 `WorldHydrology`,
  `HydrologyPlanScope`, 기존 Region Store, `Lazy` Builder를 입력으로 받지 않는다.
- `PlanningSnapshot`은 Builder가 준비·seal하며, Raster/Chunk/Debugger가 누락 Tile을
  추가하거나 계획을 시작할 수 없다.
- River Edge/Activity 결과는 Proposal, Topology, Basin Scope의 소유자가 아니다.
  Junction은 `InteractionResolutionTile` 하나가 좌표 기준으로 소유한다.
- 고정맵/무한 월드의 분기는 Request 허용 좌표 확인에만 존재한다. Terrain,
  Topology, Endpoint, River, Raster 경로에 WorldType 분기가 들어가면 안 된다.
- 모든 의존 거리와 Tile key 확장은 Settings와 명시적 부모 관계에서 산출된다.
  임의 halo, 최대 시도 횟수, TTL, LRU, 생성 차단 조건을 추가하지 않는다.

## 기존 구조의 허용·금지 범위

### 재사용 허용: 결과 평가 공식

- `WorldNoiseRouter`, `WorldPatternResolver`, `TerrainSurfaceSampler`
- Sea 수면/해저 평가
- `BasinComponentBuilder`의 candidate footprint, 수면, 깊이, shore 공식
- Endpoint, Route, Natural 전이, Corridor, Junction의 확정 규칙
- `HydrologyCellPlan`의 Terrain/Water Source materialization

위 코드는 순수 평가기 입력으로 분리해 이식한다. 호출 위치와 데이터 소유권은
그대로 재사용하지 않는다.

### 재사용 금지: 현재 생성 소유 구조

- `WorldHydrology`, `HydrologyPlanScope`
- `TopologyRegionStore`, `EndpointCatalogRegionStore`
- `RiverGraphStoreV2`, `RiverProposalRegionStore`, `RiverEdgeActivityStore`
- 기존 `HydrologyBatchBuilder`의 계획 획득 흐름
- Runtime의 Chunk별 Hydrology Scope 및 Scope 교체/retire 흐름

새 경로가 위 타입을 인자로 받거나 내부에서 호출하면, 기존 구조를 유지한
부분 수정이다. 그 구현은 진행하지 않고 설계 검토로 되돌린다.

## 기존 코드 동결과 제거 원칙

기존 생성 구조는 새 평가기를 이식하기 전에는 결과 공식 확인을 위한 **동결된
참조 소스**로만 남긴다. 먼저 물리적으로 제거하지 않는다. 현재 구조에는 보존할
Terrain/Basin/River 평가 공식과 제거할 Scope/Store 소유 구조가 섞여 있기 때문이다.

- 새 구현은 기존 생성 구조를 호출, 상속, adapter, runtime fallback으로 사용하지
  않는다. 기존과 새 생성기를 선택하는 조건문도 만들지 않는다.
- 새 `Request → Snapshot → Raster → ChunkMaterializer` 경로가 완성되면 기존 활성
  생성 경로를 새 경로로 한 번에 전환한다. 둘을 병행 실행 경로로 유지하지 않는다.
- 사용자의 실제 고정맵·스트리밍·Debugger 검증이 끝난 뒤, 이전 Scope/Store,
  Legacy generator, 중복 설정과 직렬화 항목을 물리적으로 제거한다.

따라서 금지 대상은 잔여 소스 자체가 아니라, 새 설계가 그 소스를 의존하거나
대안으로 취급하게 만드는 구조다.

## 단계별 Gate

### Gate A — 평가기 분리

- 결과 평가기는 `Seed + Settings + 절대 좌표/Id + 명시적 입력 사실`만 받는다.
- Cache, Scope, Chunk, WorldType, Task, Debugger 객체를 받지 않는다.
- 어떤 입력 사실이 현재 코드에서 명확하지 않으면 임의 구현하지 않고 문서에
  `결정 필요`로 기록하고 사용자에게 논의를 요청한다.

### Gate B — Planning 사실 구현

- 각 Tile/Component/Edge/Interaction 데이터는 owner key와 입력 key를 가진다.
- Builder가 요구한 추가 key는 관계명과 Settings 유도 범위를 남긴다.
- `InteractionResolutionTile`을 만들지 않고 Edge별로 주변 Proposal을 계속 조회하는
  구현은 허용하지 않는다.
- Topology의 primary 결과를 128×128 `HydrologyCellPlan[]`로 미리 만드는 구현은
  허용하지 않는다. Dense raster는 요청 rectangle의 결과만 소유한다.

### Gate C — 소비 경로 전환

- Snapshot seal 이전에만 계획 작업이 실행된다.
- Raster/Chunk/Debugger는 읽기 전용 Snapshot만 소비한다.
- Runtime은 desired Chunk 집합 하나를 Request로 제출한다. Chunk마다 계획 Scope를
  만들거나 Target 이동마다 기존 Scope를 retire하는 방식으로 되돌아가지 않는다.
- 취소는 Tile 경계에서만 허용하며, 부분 결과를 WorldData에 적용하지 않는다.

### Gate D — 정리

- 실제 고정맵/무한 스트리밍/Debugger 검증 전에는 기존 경로를 삭제하지 않는다.
- 검증 뒤에는 새 경로와 중복되는 Legacy Store, generator, 설정, 직렬화 항목을
  남기지 않는다. 둘 중 하나를 runtime fallback으로 보존하지 않는다.

## 중단 조건

다음 상황에서는 코드로 추측해 메우지 않는다.

- 기존 결과가 Scope, 요청 순서, Dictionary 순서에 의존한다는 증거가 발견된 경우
- Basin/Endpoint/Route/Junction 결과가 새 Tile 경계에서 정확히 어느 owner에게
  속하는지 확정할 수 없는 경우
- 결과 보존과 성능 요구가 실제로 양립하지 않아, 수문 규칙 자체를 바꿔야 하는 경우
- 렌더링·WaterSystem·Runtime Cache 변경 없이는 새 생성 결과를 적용할 수 없는 경우

중단 시에는 발견한 코드 근거, 영향을 받는 결과, 선택지를 MD에 기록하고 사용자에게
결정을 요청한다. 임시 상수, 추가 WaterCell, 재시도, 기존 경로 fallback으로
진행하지 않는다.

## 인계 메시지 최소 형식

다음 에이전트에게 작업을 넘길 때에는 아래 내용을 함께 전달한다.

```text
필수 문서: HydrologyRedesign/06, 07, 08
현재 단계와 통과해야 할 Gate
보존할 평가기와 변경 금지 소유 구조
이번 작업에서 만들 데이터 owner / input / output
실제 실행 검증은 사용자가 수행하며, 에이전트는 컴파일만 확인
```

## 검토 질문

코드 작성 전과 단계 종료 전에 반드시 답한다.

1. 이 변경은 새 Request → Snapshot → Raster 경로를 강화하는가, 아니면 기존
   Scope/Store를 연장하는가?
2. 새 데이터의 owner와 입력 범위가 Settings/좌표로 명시되는가?
3. Batch, Chunk, Debugger가 계획 생성에 관여하지 않는가?
4. 결과 보존 규칙과 실행/캐시 최적화가 분리되어 있는가?
5. 실제 검증 없이 성능 개선 또는 결과 보존을 단정하지 않았는가?

하나라도 아니면 구현을 멈추고 설계를 수정한다.
