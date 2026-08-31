# Hydrology 재설계: 확정 설계와 구현 인계 기준

## 문서 우선순위

이 디렉터리의 문서는 2026-08-31에 합의된 Hydrology 재설계의 단일 기준이다.
`../InfiniteWorldStreamingDesign.md`의 청크 좌표, 스트리밍 상태, 절대 좌표
생성 계약은 유지한다. 다만 River/Lake/Pond Water Pattern과 관련된 기존 설명은
이 문서와 충돌할 경우 이 문서를 우선한다.

구현자는 각 단계 문서의 범위와 완료 기준을 충족한 뒤 다음 단계로 이동한다.
다음 단계의 기능, 임시 호환 계층, 임의 상수, 숨은 재시도 횟수를 앞 단계에
추가하지 않는다.

## 확정 목표

```text
BaseTerrainField
  → HydrologyTopologyStore
      ├ Sea Topology
      └ Basin Topology (Lake / Pond)
  → EndpointCatalog
  → RiverGraphStore
  → HydrologyBatchBuilder
  → Chunk materialization (terrain + planned WaterCell(Source))
  → WaterSystem (falling / flow)
```

Sea, Lake, Pond, River는 순서대로 덧씌우는 효과가 아니다. 동일한 Seed, 설정,
절대 좌표를 입력으로 하는 하나의 Hydrology 계획에서 최종 `HydrologyCellPlan`을
확정한다. 계획 결과는 접근·청크 생성·스트리밍·디버거의 순서에 영향을 받지
않는다.

## 변하지 않는 계약

- Hydrology는 기초 Terrain Field를 입력으로 하고, 최종 지형 목표 높이와 Water
  Source를 함께 결정한다.
- 모든 계획된 River/Lake/Pond/Sea WaterCell은 처음부터 `Source`다. Dynamic
  Water는 생성 단계의 연결 수단이 아니며, WaterSystem이 이후 낙하와 흐름만
  갱신한다.
- Lake/Pond Component는 하나의 일정한 수면 높이를 가진다. Basin은 지형을
  절삭하거나 성토해 그 수면에 맞는다.
- River Graph는 Head/End가 없는 무방향 그래프다. 흐름 방향은 WaterSystem이
  완성된 지형과 인접 WaterCell을 기준으로 결정한다.
- River는 다른 Basin의 내부를 가로지르지 않는다. Lake/Pond/Sea/Natural
  Endpoint에서만 연결된다.
- 경로가 없는 Edge 후보는 후보만 무효가 된다. Basin, Endpoint, 월드 생성은
  취소하지 않으며, 지형 관통·연결 WaterCell 추가·임의 우회로를 만들지 않는다.
- 캐시의 보존과 해제 범위는 활성 생성/스트리밍 요청 범위와 설정으로부터
  산출된 의존 범위로 결정한다. 청크나 디버거 Batch가 캐시 수명을 소유하지
  않는다.

## 용어

| 용어 | 의미 |
|---|---|
| Base Terrain | Hydrology 적용 전 Terrain Pattern, 표면, Sea Pattern 사실 |
| Topology Region | Basin/Sea의 core Cell 결과를 소유하는 절대 좌표 Region |
| Component | 하나의 Lake 또는 Pond Basin 수면과 지형 목표를 공유하는 영역 |
| Endpoint | Lake, Pond, Sea, Natural 위치에 놓이는 무방향 Graph 연결점 |
| River Edge | 정렬된 두 EndpointId로 식별되는 무방향 경로와 Corridor 계획 |
| HydrologyCellPlan | 한 Cell의 최종 지형 목표, 물 사실, Component/Edge 식별자 |
| HydrologyBatch | 요청한 사각 범위의 `HydrologyCellPlan` 결과. 전역 계획의 소유자가 아님 |
| Plan Scope | 생성 작업, 스트리밍 준비 범위, 디버거 질의가 명시적으로 요청한 Region 보존 범위 |

## 단계

0. [00-cleanup.md](00-cleanup.md) — 이전 Hydrology 구조와 직렬화 흔적 정리, 기준 결과 확보
1. [01-foundation.md](01-foundation.md) — 계약, 설정 의미, 명시적 계획 소유권
2. [02-topology.md](02-topology.md) — Sea와 Lake/Pond Topology 확정
3. [03-river-graph.md](03-river-graph.md) — 무방향 Endpoint Graph, Junction, 경로 실패 처리
4. [04-batch-streaming.md](04-batch-streaming.md) — Batch, 초기 생성, 스트리밍, 디버거 결합
5. [05-water-validation.md](05-water-validation.md) — WaterSystem 경계, 이전 경로 제거, 최종 검증

테스트 전용 코드나 fixture 환경은 만들지 않는다. 에이전트는 코드 컴파일만
확인하며, 기능·성능·결정성은 사용자가 실제 월드 생성·스트리밍·패턴맵 디버거
환경에서 확인한다. 실제 성능 수치는 5단계의 측정에서만 비교한다.

## 공통 진행 규칙

- 합의되지 않은 Black Box, 예외 조건, 상수, 생성 차단 규칙을 추가하지 않는다.
- 설정이 필요한 정책은 목적·단위·결정 범위를 문서와 Inspector에 함께 기록한다.
- 구조가 불명확하면 구현을 진행하지 않고, 선택지와 영향을 문서의 `결정 필요`
  항목으로 올린다.
- 성능 개선은 지형/수문학 계산을 생략하거나 밀도를 낮추는 방식이 아니라,
  전역 계획의 단일 계산·공유·작업 스레드 실행으로 달성한다.
- 각 단계는 컴파일 오류 없이 끝내며, 완료 기준 밖의 효과는 개선 완료로
  단정하지 않는다.
