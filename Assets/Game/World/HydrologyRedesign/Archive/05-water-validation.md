# 5단계 — WaterSystem 경계, 이전 구조 제거, 검증

## 목적

계획된 Hydrology 결과를 실제 Cell에 정확히 기록하고, WaterSystem이 생성 Topology를
재구성하지 않도록 경계를 확정한다. 이전 cache lease 기반 구현을 제거한 뒤
결정성·성능·자원 수명을 검증한다.

## Chunk materialization과 WaterSystem

- `HydrologyCellPlan.TargetTerrainSurfaceUnits`를 최종 Terrain Filled로 조립한다.
- `WaterTopUnits > TargetTerrainSurfaceUnits`인 계획 Cell에만 정확한 Water Amount,
  `WaterRole.Source`, 계획된 `WaterType`을 기록한다.
- River/Lake/Pond/Sea 연결을 위해 계획에 없는 WaterCell을 추가하지 않는다.
- `WaterTypeResolver`는 Dynamic Water의 현재 상태만 갱신하며 계획된 Source의
  Type/Role/Component 사실을 덮어쓰지 않는다.
- WaterSystem은 완성된 Terrain과 WaterCell을 기준으로 Falling/Flow를 처리한다.
  Graph의 가상 방향이나 Endpoint 역할을 사용하지 않는다.
- Biome 확정 시에는 최종 계획 WaterType을 사용한다. 이후 동적 흐름 변화가
  고정된 생성 Biome을 자동 변경하지 않는다.

## 제거 대상

- `HydrologyBatch`가 Terrain tile, Region plan, River graph lease를 소유하는 경로
- `WorldRuntime`이 Chunk별 HydrologyBatch를 보관하는 경로
- 디버거가 장기 보존 HydrologyBatch를 유지하는 경로
- Head/End 역할과 역할 기반 설정/직렬화/디버그 표시
- River 연결 또는 낙차 해결을 위해 WaterCell을 보정하는 경로
- Lake/Pond를 연결된 Dynamic Water 크기로 재분류하는 경로

기존 저장 호환이나 이전 Hydrology 설정의 묵시적 변환은 추가하지 않는다.

## 필수 검증

### 결정성 및 경계

- 동일 Seed·설정·좌표를 초기 생성, 임의 청크 순서, 스트리밍, 디버거 순서로
  요청해 `HydrologyCellPlan`과 최종 Cell을 비교한다.
- 인접 Chunk와 Topology/Graph Region 경계의 Terrain, WaterTop, WaterType,
  ComponentId, EdgeId가 일치한다.
- 동일 Edge가 두 Region 또는 두 Batch에서 서로 다르게 래스터화되지 않는다.

### 지형·물 계약

- 계획된 물이 지형 위에 떠 있지 않다.
- Basin Component 내부 수면은 하나다.
- Basin끼리 겹치지 않고, River는 허용 Endpoint 외 Basin 내부를 통과하지 않는다.
- 모든 계획 WaterCell은 처음부터 Source다.
- 낙차와 Natural Endpoint에 연결용 WaterCell이 없다.

### 성능과 자원 수명

- Profiler marker로 Base Terrain, Topology, Graph, Batch, Chunk apply 시간을 분리한다.
- 스트리밍 메인 스레드에는 결과 적용만 남는지 확인한다.
- 초기 생성에서 동일 Region/Graph가 한 번만 계획되는지 계수로 확인한다.
- Scope 종료, 작업 취소, Debug preview 해제 뒤 Region 참조와 임시 버퍼가
  남지 않는지 확인한다.

Profiler 측정은 사용자의 실제 실행 환경에서 별도 기록한다. 그 결과만으로 모든
설정·기기에서 성능 문제가 해결됐다고 결론내리지 않는다.

## 완료 기준

- 프로젝트가 컴파일되고, 위 결정성·지형·자원 검증이 자동 또는 재현 가능한
  수동 절차로 기록된다.
- 측정한 범위에서만 초기 생성 시간, 스트리밍 frame time, 메모리 보존 범위를
  이전 구조와 비교해 보고한다.
- 측정하지 않은 기기·월드 설정·WaterSystem 장기 안정성은 미검증 항목으로
  분리한다.
