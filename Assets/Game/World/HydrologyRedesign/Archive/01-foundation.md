# 1단계 — Hydrology 계약과 계획 소유권

## 목적

현재 `HydrologyBatch`가 지역 계획 캐시, 지형 샘플 캐시, 요청 범위 래스터를
동시에 소유하는 구조를 해체한다. 이후 단계가 공통으로 사용할 불변 데이터 계약과
명시적 `WorldHydrology` 소유권을 만든다.

이 단계는 Basin 또는 River 알고리즘을 임시로 단순화하지 않는다. 새 계약은
이전 역할 기반 Graph 의미를 포함하지 않는다. 활성 생성 경로의 전환은 4단계,
이전 구조의 물리적 제거는 5단계에서 수행한다.

## 적용 범위

### 1. 불변 데이터 계약

다음 개념을 별도 타입으로 확정한다.

```text
BaseTerrainSample
├ Base WorldField / Terrain Pattern
├ Base surface units
└ Sea pattern fact and global sea surface

HydrologyCellPlan
├ TargetTerrainSurfaceUnits
├ WaterTopUnits
├ WaterType / WaterRole
├ BasinComponentId
├ RiverEdgeId
└ Membership / shore / corridor metadata

HydrologyEndpoint
├ EndpointId
├ Kind: Lake | Pond | Sea | Natural
├ Absolute cell coordinate
└ Water surface / transition data

RiverEdgeId
└ Canonically sorted EndpointId pair
```

`HydrologyEndpoint`와 `RiverEdgeId`에는 Head/End, upstream/downstream, 생성
순서가 들어가지 않는다. 흐름 방향은 이 타입들이 아닌 WaterSystem의 결과다.

### 2. 설정 의미 교체

다음 역할 기반 설정은 새 계약에서 읽거나 묵시적으로 변환하지 않는다.

- `HeadDensity`, `EndDensity`
- Lake/Pond의 `HeadRoleChance`
- `NaturalHeadTransition`, `NaturalEndTransition`
- 방향을 전제하는 `UphillCost`

대신 무방향 Graph에 필요한 설정은 명시적인 의미로 정의한다.

- Natural Endpoint 발생 규칙
- Natural Endpoint의 단일 종단 전이 Profile
- 대칭적인 고도 변화 비용
- Endpoint 종류별 연결 후보 가중치
- Junction 확률

기존 Asset 값은 의미가 동등하지 않으므로 새 값으로 추정 변환하지 않는다. River
Graph가 구현되는 3단계에서 Natural Endpoint 발생 규칙과 단일 종단 전이 Profile의
값을 사용자 결정으로 확정한다. 이전 저장 또는 이전 설정 의미를 보존하는 Adapter는
만들지 않는다.

### 3. 명시적 계획 서비스

`WorldHydrology` 또는 동등한 이름의 서비스가 다음을 소유한다.

```text
WorldHydrology
├ BaseTerrainField
├ TopologyRegionStore
├ Active HydrologyPlanScope set
├ EndpointCatalogStore (3단계)
└ RiverGraphStore (3단계, EdgePlan + SpatialIndexRegion)
```

- Plan Store 값은 불변이며 절대 Region 좌표 키로 식별한다.
- 동일 키의 동시 요청은 하나의 계산 결과를 공유한다.
- 계산 중에는 전역 잠금을 잡지 않는다. 키별 작업 결과만 동기화한다.
- 보존 Region은 현재 Plan Scope들의 합집합으로 계산한다.
- 해제 대상은 어떤 Scope나 실행 중 작업도 참조하지 않는 Region뿐이다.
- 캐시 용량, 시간, 재시도 횟수 같은 임의 제한을 추가하지 않는다.

`HydrologyBatch`는 Store를 소유하거나 lease를 숨기지 않는다. Batch를 잃어도
Region의 참조 수가 남는 구조를 만들지 않는다.

## 구현 기록 (2026-08-31)

- `Generation/WorldHydrology.cs`에 `WorldHydrology`, `BaseTerrainField`,
  `HydrologyPlanScope`, `TopologyRegionStore`를 추가했다.
- Scope는 Region 키별 `Lazy` 계획 결과를 공유하며, 마지막 Scope가 해제될 때만
  해당 Region을 Store에서 제거한다. 용량·시간·재시도 제한은 없다.
- `HydrologyCellPlan`, 방향 없는 `HydrologyPlanEndpointId`, 정렬된
  `HydrologyGraphEdgeId`, `BasinComponentId`를 확정했다. 새 타입은 Head/End나
  흐름 방향을 포함하지 않는다.
- 당시 `WorldBuildInput`, `WorldRuntime`, 기존 `HydrologyBatch`는 이 Store를
  사용하지 않았다. 4단계에서 새 `WorldHydrology` → `HydrologyBatchBuilder` 단일
  경로로 전환했다.
- 역할 기반 River 설정의 새 값은 결정되지 않았으므로 이 단계 코드가 해당 값을
  읽거나 변환하지 않는다.

## 현재 코드에서 교체 대상

- `HydrologyGenerationContext`의 Batch 종속 Terrain lease
- `HydrologyRegionPlanner`의 Batch lease 기반 RegionPlan cache
- `RiverHydrologyPlanner`의 Batch lease 기반 RiverEdgeStore
- 역할 기반 `HydrologyEndpoint`와 River 설정

정확한 파일 분할은 구현 중 정하되, `Generation` 외의 Runtime/Editor가 Region
계획 내부 자료구조를 직접 참조하지 않는 경계를 유지한다.

## 완료 기준

- `WorldHydrology`와 Plan Scope의 계약이 독립적으로 컴파일된다. 활성 World
  생성·Runtime의 전환은 4단계 책임이다.
- 새 Endpoint/Edge 데이터에 방향 역할이 없다.
- Plan Store의 키, 입력, 결과, 보존 Scope가 코드에서 확인 가능하다.
- Region/Graph 계산이 Batch dispose 여부로 제거되거나 유지되지 않는다.
- 새 계약이 역할 기반 설정을 참조하지 않고 프로젝트가 컴파일된다.

## 다음 단계 인계

2단계는 `BaseTerrainField`와 `TopologyRegionStore`만 사용해 Sea/Lake/Pond 결과를
작성한다. River Graph, Chunk 조립, WaterSystem 변경은 포함하지 않는다.

## 검증 상태

- 프로덕션 코드 컴파일만 확인했다.
- 계획 결과의 런타임 결정성은 사용자가 실제 실행 환경에서 확인할 범위다.

## 3단계 결정 필요

- 기존 역할 기반 설정 asset의 새 설정값 매핑. 의미가 동등하지 않으므로 기본값을
  추정하지 않고 사용자 결정을 받는다.
