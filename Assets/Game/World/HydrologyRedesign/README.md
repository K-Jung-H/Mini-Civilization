# Hydrology 재설계: 확정 설계와 구현 인계 기준

## 문서 우선순위

`26-water-map-direct-drawing-contract.md`가 TerrainMap을 읽어 WaterMap을 직접
그리는 생성 원본의 최우선 계약이다. `18`, `19`, `21`, `22`의 Feature 평가,
Geometry, 경로 회피, Basin 경쟁·성장 설명과 충돌하면 `26`을 따른다.

`25-pattern-map-store-streaming-contract.md`가 Pattern Map 수명, 세 Range와
Debugger 수요에 관한 최우선 계약이다. `26`의 WaterMap을 Runtime Store에
보존·소비하는 범위에서 `18`, `23`, `24`의 이전 표현과 충돌하면 `25`를 따른다.

`19-stage-2-semantic-contract.md`는 위 최종 설계의 2단계 완료 기록과 Pattern 디자인
이관 기준이다. 구현 범위는 `18`의 단계 정의를, 실제 완료 사실과 다음 단계 입력은
`19`를 함께 따른다.

`20-stage-3-terrain-pattern-tile.md`는 Terrain evaluator와 Tile builder의 완료 기록이다.
Sea는 Terrain 결과가 아니라 Hydrology Feature라는 경계도 이 문서 기준으로 유지한다.

`21-stage-4-hydrology-feature-contract.md`는 이력이다. 직접 Feature 평가·Geometry
계약은 `26`의 Tile-local Drawing으로 대체됐다.

`22-stage-5-hydrology-pattern-tile.md`는 이력이다. WaterMap Tile 생성은 `26`의 직접
Drawing 계약을 따른다.

`23-stage-6-chunk-streaming.md`는 Pattern Tile 소비 ChunkData와 Runtime 연결의 초기
구현 기록이다. Tile cache 수명과 streaming 수요 설명은 `25`로 대체됐다.

`24-stage-7-semantic-pattern-debugger.md`는 256 Pixel Terrain/Hydrology/Combined Pattern Map,
선택 영역과 Streaming Target 재배치의 초기 구현 기록이다. 독립 Tile reader 설명은
`25`로 대체됐다.

`00`~`17`과 `../InfiniteWorldStreamingDesign.md`의 기존 생성·수문·스트리밍 구현
설명은 이력으로만 취급한다. 새 구현은 이들 구조를 보존·Adapter·fallback으로
연결하지 않는다. Renderer, WorldData, WaterSystem처럼 새 ChunkData를 소비하는
하위 기능만 별도 보존 대상이다.

구현자는 각 단계 문서의 범위와 완료 기준을 충족한 뒤 다음 단계로 이동한다.
다음 단계의 기능, 임시 호환 계층, 임의 상수, 숨은 재시도 횟수를 앞 단계에
추가하지 않는다.

새 설계의 저장/로드는 기존 형식과 호환하지 않으며, Semantic Pattern Tile 기반
ChunkData와 Runtime 상태가 검증된 뒤 최종 8단계에서만 다시 구현한다.

## 이전 설계 이력

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

## 이전 설계의 과거 계약

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

## 이전 설계의 과거 용어

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

## 이전 설계의 단계 이력

0. [00-cleanup.md](00-cleanup.md) — 이전 Hydrology 구조와 직렬화 흔적 정리, 기준 결과 확보
1. [01-foundation.md](01-foundation.md) — 계약, 설정 의미, 명시적 계획 소유권
2. [02-topology.md](02-topology.md) — Sea와 Lake/Pond Topology 확정
3. [03-river-graph.md](03-river-graph.md) — 무방향 Endpoint Graph, Junction, 경로 실패 처리
4. [04-batch-streaming.md](04-batch-streaming.md) — Batch, 초기 생성, 스트리밍, 디버거 결합
5. [05-water-validation.md](05-water-validation.md) — WaterSystem 경계, 이전 경로 제거, 최종 검증
6. [06-generation-backbone.md](06-generation-backbone.md) — 스트리밍 생성 백본 교체 전 호환성 감사와 고정 계약
7. [07-streaming-generation-backbone.md](07-streaming-generation-backbone.md) — 고정맵/무한 월드 공통 생성 백본의 최종 설계와 이관 단계
8. [08-implementation-gates.md](08-implementation-gates.md) — 기존 구조 보수로 회귀하지 않기 위한 구현 Gate와 인계 규약
9. [09-streaming-backbone-stages-1-2.md](09-streaming-backbone-stages-1-2.md) — 새 생성 백본 1·2단계 구현 사실과 다음 인계
10. [10-streaming-backbone-stages-3-4.md](10-streaming-backbone-stages-3-4.md) — 새 생성 백본 3·4단계 River 계획 사실과 다음 인계
11. [11-streaming-backbone-stage-5.md](11-streaming-backbone-stage-5.md) — sealed Raster와 Chunk materializer 구현 사실 및 실행 전환 경계
12. [12-streaming-backbone-stage-6.md](12-streaming-backbone-stage-6.md) — Runtime·Debugger 전환 사실과 7단계 실제 검증 기준
13. [13-command-stage-7-pattern-debugger.md](13-command-stage-7-pattern-debugger.md) — 최종 진행 명령 7단계 Pattern Debugger 전환 구현 기록
14. [14-command-stage-8-verification-and-cleanup.md](14-command-stage-8-verification-and-cleanup.md) — 최종 진행 명령 8단계 실제 검증과 기존 구조 정리 Gate
15. [15-feature-owned-streaming-replacement.md](15-feature-owned-streaming-replacement.md) — request-wide Snapshot을 Feature 소유 Tile/Edge 생성으로 교체한 활성 구조와 검증 범위
16. [16-incremental-feature-tile-retention.md](16-incremental-feature-tile-retention.md) — Target 이동 시 활성 Chunk의 실제 Feature Tile 참조를 유지하는 증분 streaming 수정 기록
17. [17-active-streaming-ownership-cleanup.md](17-active-streaming-ownership-cleanup.md) — 활성 coordinator/Feature 경로, 제거 완료 계열, UI 연결 전 상태 기준

## 이전 진행 명령과 이관 기록의 관계

최종 진행 명령의 6단계는 Runtime streaming 전환, 7단계는 Pattern Debugger 전환,
8단계는 실제 검증 뒤의 보완·정리다. `06`~`12`의 파일 번호는 생성 백본 재설계와
이관 작업의 상세 기록이며, 최종 진행 명령 번호를 다시 정의하지 않는다.

테스트 전용 코드나 fixture 환경은 만들지 않는다. 에이전트는 코드 컴파일만
확인하며, 기능·성능·결정성은 사용자가 실제 월드 생성·스트리밍·패턴맵 디버거
환경에서 확인한다. 실제 성능 수치는 5단계의 측정에서만 비교한다.

## 이전 구현의 공통 규칙

- 합의되지 않은 Black Box, 예외 조건, 상수, 생성 차단 규칙을 추가하지 않는다.
- 설정이 필요한 정책은 목적·단위·결정 범위를 문서와 Inspector에 함께 기록한다.
- 구조가 불명확하면 구현을 진행하지 않고, 선택지와 영향을 문서의 `결정 필요`
  항목으로 올린다.
- 성능 개선은 지형/수문학 계산을 생략하거나 밀도를 낮추는 방식이 아니라,
  전역 계획의 단일 계산·공유·작업 스레드 실행으로 달성한다.
- 각 단계는 컴파일 오류 없이 끝내며, 완료 기준 밖의 효과는 개선 완료로
  단정하지 않는다.
