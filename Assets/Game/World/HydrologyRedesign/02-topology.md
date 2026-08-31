# 2단계 — Sea와 Basin Topology

## 목적

Base Terrain 위에서 Sea, Lake, Pond의 최종 Topology를 먼저 확정한다. 이 단계의
결과는 River가 조회하는 보호 Basin, 일정 수면, Endpoint 후보의 유일한 근거다.

## 적용 범위

### 1. Sea Topology

- Sea Pattern의 경계→중심 접근도를 사용한다.
- 전역 Sea 수면은 하나의 설정값이다.
- 해저는 S자형 심도 구조와 평탄한 심해 영역을 유지한다.
- Seabed Noise는 해저 높이의 세부 효과일 뿐, Sea 영역·심도 구조·수면을 바꾸지
  않는다.
- Sea 해안 Cell은 EndpointCatalog에 Sea Endpoint 후보를 제공한다.

### 2. Basin 후보와 Component

- Lake/Pond는 같은 후보 생성과 지형·Potential Map 확장 알고리즘을 사용한다.
- 후보의 Type, 발생률, 목표 면적, 최대 깊이만 각 프로필 설정에서 다르다.
- 후보와 ComponentId는 Seed, Type, 절대 후보 격자 좌표로 결정한다.
- 후보의 확장은 원·경계 Noise가 아니라 Potential, 지형 변형 비용, 경사 비용을
  사용한다.
- Component별 `WaterTopUnits`를 하나만 선택한다.
- 최종 Terrain 목표는 Component 수면과 중앙 깊이 구조, 바닥 Noise를 함께
  사용해 산출한다.
- Shore Transition은 Basin 내부 Water와 육지 사이의 지형 전이만 만든다. 별도
  WaterCell을 만들지 않는다.

### 3. Region 경계 결정성

`TopologyRegion`은 core 영역만 결과로 소유한다. core를 계산할 때 필요한 확장
영역은 다음 설정에서 계산으로 유도한다.

- Basin 최대 도달 거리
- Basin 최소 이격 거리
- Shore Transition 거리
- Basin Seed 격자의 위치 편차

모든 영향을 줄 수 있는 후보를 확장 영역에서 같은 정렬 규칙으로 평가한 뒤 core만
잘라 저장한다. 후보 우선순위는 Seed 기반 값과 ComponentId의 명시적 tie-break로
결정하며, Dictionary 순회나 작업 완료 순서를 사용하지 않는다.

River는 아직 생성하지 않는다. 이 단계에서 다른 Basin 내부는 미래 River 경로가
통과할 수 없는 보호 영역으로 제공된다.

활성 Chunk 생성 경로가 이 Topology를 Raster/조립하는 작업은 4단계다. 2단계는
독립 Plan Store 결과만 확정하며, 기존 Batch에 Adapter로 연결하지 않는다.

## 구현 기록 (2026-08-31)

- `BaseTerrainRegionStore`가 Planning Region core의 Base Terrain 표본을 한 번만
  소유한다. Topology와 Basin Component가 겹치는 Halo 배열을 각각 다시 표본화하지
  않는다.
- `BasinComponentStore`는 `(Type, Basin Seed Grid)`가 소유하는 독립 footprint,
  수면, 내부 깊이, 경계 자료를 만든다. Component는 요청 Topology Region을 알지
  않는다.
- 최소 이격은 우선순위가 높은 Component가 **실제로 활성인지**를 먼저 확인한 뒤
  footprint와 설정된 이격 거리를 비교해 결정한다. 요청 순서·Dictionary 순회·시간
  기반 보존 규칙은 사용하지 않는다.
- `TopologySpatialBuilder`는 core와 영향을 줄 수 있는 활성 Component만 투영해
  Sea/Basin Cell과 Endpoint를 작성한다. 기존의 `302 × 302` 지역 Halo 표본·후보
  경쟁·footprint 확장을 core마다 반복하지 않는다.
- Sea는 기존 Base Terrain의 전역 수면·해저 사실을 유지하고, Route sample 격자의
  해안만 Sea Endpoint 후보로 기록한다.
- Lake/Pond 후보는 Seed 격자·Type·발생률에서 결정한다. Potential Map, 지형
  변형 비용, 경사 비용을 사용해 설정된 면적까지 확장하고, 우선순위와
  `BasinComponentId`의 정렬로 최소 이격을 결정한다.
- 확정 Basin은 하나의 `WaterTopUnits`를 비용으로 선택하고, 내부 깊이 곡선과
  Bed Field로 Terrain 목표를 작성한다. Shore는 Terrain 목표만 전이하며 WaterCell을
  추가하지 않는다.
- Component와 Base Terrain의 참조는 Topology/Proposal/Graph Plan Scope가 유지하는
  동안에만 보존되며, 마지막 참조 해제 시 함께 제거된다.
- 이전 local-halo 구현은 5단계 물리 제거 전까지 소스에만 남고 활성
  `TopologyRegionStore`에서는 호출하지 않는다.

## 검증

- 같은 Topology Region을 어떤 순서로 요청해도 동일한 ComponentId와 결과가
  나온다.
- 인접 Region core 경계에서 Basin Type, ComponentId, WaterTopUnits,
  TargetTerrainSurfaceUnits가 일치한다.
- 하나의 Lake/Pond Component에 계단식 수면이 없다.
- Basin끼리 겹치지 않고 최소 이격 거리 내에 생기지 않는다.
- Sea 수면은 절대 좌표와 Region에 관계없이 같다.
- 서로 다른 Component는 같은 수면을 공유할 수 있으나, 그것을 연결된 하나의
  Component로 취급하지 않는다.

## 검증 상태

- 프로덕션 코드 컴파일만 확인했다.
- Sea/Basin의 실제 분포와 Region 경계 결과는 사용자가 실제 월드 생성과 패턴맵
  디버거에서 확인할 범위다.

## 다음 단계 인계

3단계는 확정된 Sea 해안과 Basin Component만 Endpoint로 사용한다. River가
Basin을 가로지르는 것을 사후 덮어쓰기 우선순위로 해결하지 않는다.

## 구현 금지

- River 연결을 위해 Basin 수면이나 Basin 경계를 변경하지 않는다.
- Cell 연결 상태 크기로 Lake/Pond Type을 다시 분류하지 않는다.
- 요청 순서에 따라 후보를 먼저 점유하거나 무효화하지 않는다.
