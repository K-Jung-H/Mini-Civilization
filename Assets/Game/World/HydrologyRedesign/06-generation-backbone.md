# 6단계 준비 — 생성 백본 호환성 감사와 고정 계약

## 목적

고정맵 기반 생성과 무한 스트리밍을 하나의 절대 좌표 생성기로 통합하기 전에,
현재 만족하는 Terrain/Hydrology 결과를 결정하는 규칙과 현재의 요청/Scope 소유
구조를 분리한다. 이 문서는 새 생성 백본 설계와 이관의 기준이며, 이 단계에서는
생성 공식이나 런타임 코드를 수정하지 않는다.

## 확정 계약 (2026-09-01)

- 같은 `Seed + GenerationSettings + 절대 XYZ 좌표`의 계획 Terrain, 수문
  `HydrologyCellPlan`, 최초 Water Source는 Cell 단위 결과를 보존한다.
- 고정맵은 생성 규칙의 경계가 아니다. 무한 월드와 같은 좌표 계획을 사용하고,
  고정맵은 Chunk materialization/streaming 허용 범위만 제한한다.
- `HydrologyMap.PlanningRegionSizeCells`는 Natural Endpoint의 위치 단위이므로
  결과 의미를 가진 설정으로 보존한다.
- Lake/Pond는 독립 footprint를 만든 뒤, 실제 활성인 높은 우선순위 Component와
  최소 이격 거리 안에서 충돌하면 낮은 후보를 무효화한다.
- Endpoint는 하나의 우선 Edge를 제안할 수 있고, 다른 Endpoint의 Edge를 받을 수
  있다. Type별 차수 상한은 두지 않는다.
- Junction은 Endpoint 접속이 아닌 River Corridor 내부의 접근/교차에서만 판정한다.
  큰 `EdgeId`가 작은 `EdgeId`와의 Junction 거부로 무효가 되면, 작은 Edge가
  이후 무효가 되어도 큰 Edge를 재활성화하지 않는다.
- Basin/Sea Topology는 River보다 우선한다. River는 Basin 내부를 덮어쓰지 않고
  Endpoint에서만 접속한다.
- 계획 Terrain과 최초 Source는 생성 후 불변이다. WaterSystem은 낙하와 흐름만
  갱신하며 계획 연결/재생성을 담당하지 않는다.
- 스트리밍 요청은 Tile 단계에서 협력 취소할 수 있다. 완료된 불변 Tile만 공유하고
  부분 계획은 WorldData에 적용하지 않는다.
- 계획 결과의 보존 수명은 명시적 요청 Snapshot의 참조로만 결정한다. 시간, 용량,
  LRU, 재시도 횟수 같은 숨은 캐시 정책을 추가하지 않는다.

## 정적 감사 결과

### 결과를 보존해 이식할 활성 평가기

| 평가기 | 현재 결정 입력 | 이관 방침 |
|---|---|---|
| Base Terrain | Settings, Seed, 절대 XZ | `WorldNoiseRouter` → `WorldPatternResolver` → `TerrainSurfaceSampler` 공식을 그대로 보존한다. |
| Sea | Base Terrain의 Sea 사실과 Sea 설정 | 전역 수면, S자형 해저, Detail Noise의 역할을 보존한다. |
| Basin Component | `BasinComponentId(Type, SeedGrid)`, Settings, Base Terrain | 발생, 위치 jitter, Potential/지형/경사 비용 footprint, 수면 선택, 깊이·바닥·shore 공식을 보존한다. |
| Basin 활성 | Component 우선순위/Id, 실제 footprint, 최소 이격 | 현재의 높은 우선순위 활성 Component 우선 판정을 명시적 Allocation 규칙으로 이관한다. |
| Endpoint | 확정 Topology, Route sampling 간격, 절대 Region | Basin 하나당 하나의 Endpoint, Sea 해안 Endpoint, Region당 Natural Endpoint 규칙을 보존한다. |
| River Route/Corridor | 정렬된 `EdgeId`, 두 Endpoint, Settings, Base Terrain/Topology | 반경, 대칭 고도 비용, 횡단 경사·계곡·변형 비용, Natural 적분 전이, 폭·깊이·바닥 공식을 보존한다. |
| Junction | EdgeId 쌍, Route geometry, 절대 상호작용 좌표, Settings | 거리·방향 Curve와 EdgeId 우선 거부, Node의 최저 수면/바닥 결합을 보존한다. |
| Chunk materialization | 읽기 전용 Cell 계획 | 최종 Terrain target과 모든 계획 WaterCell의 `Source` 배치를 보존한다. |

위 활성 경로에서는 `DateTime`, 전역 난수, 작업 완료 시간, 영구 static 생성 상태를
결과 입력으로 사용하지 않는다. `DateTime.UtcNow.Ticks`는 사용자가 새 Seed를 만들 때만
사용하며, 이미 확정된 Seed의 생성에는 관여하지 않는다. Dictionary/ConcurrentDictionary는
캐시 또는 중복 제거에 사용되며, Endpoint/Component/Edge/Junction의 외부 결과는
명시적 Id 정렬 후 소비된다.

`RiverGraphRegionBuilder`의 Endpoint Kind Dictionary는 내부 Route cache를 채우는
순서에만 사용한다. 후보별 Route 계산은 `EdgeId`와 절대 입력으로 결정되고, 후보
선택 전/후에는 canonical Id 순서로 정렬된다. 따라서 정적 검토상 이 Dictionary 순회는
결과 규칙이 아니라 계산 순서다.

### 결과가 아닌 현재 구조 문제

```text
Chunk / Debugger Batch
  → HydrologyPlanScope의 Lazy Region 획득
  → SpatialIndex
  → Proposal
  → Activity[EdgeId]
  → 넓어진 Proposal Scope
  → Topology / Basin / Base Terrain
```

- `HydrologyBatchBuilder`의 River raster가 `SpatialIndexRegion` Lazy 생성을 시작한다.
  Batch가 확정 계획을 읽기만 해야 한다는 계약과 다르다.
- `Activity[EdgeId]`가 route AABB에 연결 반경과 Corridor 여백을 더한 Proposal Scope를
  소유한다. Edge 수만큼 입력 Region이 연쇄 확장되며, 이 소유 관계가 초기 생성과
  새 Chunk 표시 지연의 직접 원인이다.
- `HydrologyPlanScope`는 선언된 계획 범위를 갖지 않고, 내부 조회가 필요할 때마다
  Region을 추가한다. `WorldRuntime`의 desired Chunk 집합은 Scope 생성 시 보존 범위를
  구체적으로 준비하거나 제한하지 않는다.
- Runtime은 한 번에 하나의 Chunk build만 실행한다. 이는 Source/Scope 수명에는
  안전하지만, 위의 광범위한 lazy 계획 시간이 새 Chunk 표시 시간 전체가 되는 이유다.
- Pattern Debugger도 Pixel별 `HydrologyBatchBuilder.Sample`과 별도 Scope를 사용한다.
  따라서 동일한 lazy 계획 구조를 Editor 상호작용에서 다시 유발한다.

이 항목들은 수문/지형의 결과 공식이 아니라 계산의 소유·시점 문제다. 새 구조는
이를 보수하지 않고 제거한다.

### 이관 대상에서 제외할 레거시 경로

- `HydrologyGenerationContext`, `LegacyHydrologyMap`, `LegacyHydrologyBatch`,
  `HydrologyPatternGenerator`, `RiverPatternGenerator`
- 이전 `RiverGraphStore`, 이전 `BuildEdgePlan`, `TopologyRegionBuilder.BuildLegacy`

위 경로는 현재 활성 `WorldBuildInput → WorldHydrology → HydrologyBatchBuilder` 흐름에서
호출되지 않는다. 새 생성 백본의 기준 구현으로 재사용하지 않으며, 새 경로의 실제
검증이 끝난 뒤 별도 정리 단계에서 제거한다.

### 설정 이관 주의점

활성 River Route는 새 `RiverGraph` 설정뿐 아니라 이전 `RiverNetwork`의
`CrossSlopeCost`, `ValleyPreference`, `RouteVariationField`, `RouteVariationCost`도
읽는다. 반면 Head/End 밀도, 길이, 종류 가중치, 단일 Junction 확률, 오르막 비용은
새 활성 Graph 결과에 사용되지 않는다. 새 설정 모델은 전자의 값과 수치를 그대로
Route 평가 설정으로 이관하고, 후자의 방향성 잔여 설정은 새 경로가 검증된 뒤 제거한다.

## 새 백본 설계의 필수 경계

```text
GenerationRequest(Chunk set)
  → derived PlanningFootprint
  → immutable Planning Tiles
      BaseTerrain / BasinAllocation / Topology / Endpoint / Proposal /
      InteractionResolution / SpatialIndex
  → read-only HydrologySnapshot
  → ChunkMaterializer
  → WorldData
```

- `PlanningFootprint`는 요청 Chunk와 설정상의 최대 영향 거리로만 계산한다.
- 모든 Tile 입력 범위는 Settings에서 산출하고, Edge/Batch/Debugger가 임의로 넓히지
  못하게 한다.
- `InteractionResolution`은 좌표 고정 Tile이 River 쌍과 Junction 결과를 한 번
  소유한다. Edge 활성 결과는 경로가 지나는 Resolution Tile만 집계하며 Proposal Scope를
  소유하지 않는다.
- `HydrologySnapshot`이 준비된 뒤 `HydrologyBatch`와 Debugger는 읽기만 한다.
- Scheduler의 동률 우선순위는 거리 뒤 canonical Chunk X/Z 순서로 명시한다. 이는
  Terrain 결과 규칙이 아니라 표시 순서를 안정화하기 위한 실행 규칙이다.

## 검증 범위

이 감사는 소스 정적 검토 결과다. 컴파일 또는 실제 월드 실행으로 결정성·성능을
검증한 것은 아니다. 새 백본 구현 뒤 사용자가 실제 고정맵 생성, 스트리밍 이동,
Pattern Debugger에서 다음을 확인한다.

- 같은 좌표의 Terrain/Water 결과가 요청 순서와 월드 종류에 관계없이 일치하는지
- Basin 경계, 수면, River Endpoint/Junction 결과가 기존 기준 결과와 일치하는지
- Batch/Debugger가 계획 생성을 시작하지 않는지
- Target 이동 시 취소된 요청이 새 Chunk 표시를 막지 않는지
