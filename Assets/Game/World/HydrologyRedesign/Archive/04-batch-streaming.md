# 4단계 — HydrologyBatch, 초기 생성, 스트리밍, 디버거

## 목적

확정된 계획을 Chunk와 디버거가 소비하게 만든다. 무거운 Topology/Graph 계획은
메인 스레드나 Batch 수명에 결합하지 않는다.

## HydrologyBatchBuilder

```text
HydrologyBatchBuilder.Build(request rectangle, plan snapshot)
  → HydrologyCellPlan[]
```

- Batch는 요청 사각 범위에 필요한 Topology Region과 RiverGraphStore의
  SpatialIndexRegion만 조회한다. 색인이 참조하는 공유 EdgePlan과 Junction 결과를
  읽으며, 경로·Junction을 다시 계산하거나 Edge route를 Region별로 복사하지 않는다.
- Batch는 Sea/Basin/River를 덧씌우는 순서 코드를 갖지 않는다. 확정된 Component,
  Edge, Junction 결과에서 Cell별 단일 계획을 조립한다.
- Batch 완료 후에는 `HydrologyCellPlan[]`만 Chunk 생성으로 전달한다.
- Batch dispose는 자체 임시 버퍼만 해제한다. Region/Graph 캐시의 보존 여부를
  변경하지 않는다.
- River raster는 SpatialIndexRegion이 참조하는 활성 EdgePlan의 route segment를
  요청 사각형과 corridor 의존 여백에서만 읽는다. 같은 EdgeId는 Batch 안에서도 한 번만
  읽으며, Cell에는 최종 선택된 EdgeId 하나만 기록한다.
- Basin/Sea Topology가 이미 확정한 Cell은 River가 덮어쓰지 않는다. Junction은 Graph가
  확정한 지점의 단일 River Cell 사실로 조립한다.

## 초기 월드 생성

1. 초기 Chunk 사각 범위와 계산상 의존 범위로 `HydrologyPlanScope`를 만든다.
2. 필요한 Topology/Graph Region을 한 번 요청한다.
3. 각 Chunk는 같은 불변 계획 Snapshot으로 Batch와 `WorldChunkBuildData`를 만든다.
4. `WorldData` 반영은 기존 안전한 순서로 수행한다.
5. 초기 범위의 Chunk 생성이 끝나면 Scope를 명시적으로 전환하거나 해제한다.

초기 N×N Chunk마다 같은 Region을 재계산하거나, Chunk마다 Region 계획 전체를
보관하지 않는다.

구현에서는 `WorldGenerationPipeline`이 전체 초기 생성 동안 하나의 Scope를 전달하고,
각 Chunk의 `GenerationWorkingData`가 필요한 범위의 Batch만 만든다. Batch는 Chunk
출력 전 해제되고 Scope는 초기 생성 종료에서 해제된다. 그 Scope 아래의 Proposal은
자신이 사용한 Topology/Basin Component를 공유하므로, 초기 Chunk가 같은 계획을
반복 생성하지 않는다.

## 스트리밍

- 메인 스레드는 desired Chunk와 Plan Scope 갱신, 완료된 결과 적용만 수행한다.
- 작업 스레드는 Plan 조회, HydrologyBatch 생성, Chunk 생성 전체를 수행한다.
- `WorldRuntime`은 실행 중 작업과 완성된 `WorldChunkBuildData`만 추적하며,
  완료된 Chunk용 `HydrologyBatch`를 보관하지 않는다.
- Target 이동으로 작업이 더 이상 필요하지 않으면 결과 적용만 생략한다. 실행 중
  작업이 참조하는 Scope는 작업 종료 뒤에 해제한다.
- Region Store의 활성 범위는 준비 대상 Chunk의 합집합과 의존 범위로 갱신한다.

구현에서는 desired prepared Chunk 집합이 달라질 때에만 Runtime Scope를 교체한다.
교체 시 실행 중 Chunk Build가 참조한 이전 Scope는 해당 Task가 끝난 뒤 해제한다.
`ClearStreamingChunks`도 실행 중 Task를 버리지 않고, 완료 결과만 적용하지 않아 이
해제 순서를 유지한다.

## 패턴맵 디버거

- 일반 Terrain Pattern 뷰는 BaseTerrainField만 희소 샘플링한다.
- Hydrology 뷰는 표시 Pixel에 대응하는 절대 Cell만 질의한다. 미리보기 면적 전체를
  고해상도로 래스터화하지 않는다.
- 선택한 실제 지형 오버레이만 필요한 작은 범위를 임시 Batch로 만들고, 렌더용
  결과를 복사한 뒤 즉시 해제한다.
- 재생성 전에 이전 미리보기 리소스를 항상 해제한다.
- 디버거 질의 Scope는 Runtime의 스트리밍 보존 범위를 소유하거나 누적시키지
  않는다.

구현에서는 넓은 Preview를 `previewAreaCells × previewAreaCells` Batch로 만들지 않는다.
Preview Pixel마다 절대 Cell 계획을 같은 일시 Scope에서 질의하고, 선택된 Overlay 영역만
작은 Batch를 만들어 즉시 해제한다. Runtime 월드가 있으면 같은 `WorldHydrology`를 읽되,
디버거 Scope는 Runtime Scope와 별개다.

## 검증

- 초기 생성의 동일 Region이 Chunk마다 반복 계획되지 않는다.
- 새 Chunk 요청 때 Topology/Graph 계산이 메인 스레드에서 시작되지 않는다.
- 이동·취소·Unload 뒤 실행 중 작업의 Scope와 임시 버퍼가 모두 해제된다.
- 디버거 재생성/중심 이동/선택 반복 후 Region 참조와 임시 Batch가 누적되지
  않는다.
- 디버거, 초기 생성, 스트리밍이 같은 좌표에서 같은 `HydrologyCellPlan`을 얻는다.
- 실제 월드에서 초기 생성 시간, Target 이동 뒤 첫 새 Chunk 표시 시간, Pattern Map
  재생성 시간을 별도로 확인한다. 컴파일 성공은 이 성능/결정성 검증을 대신하지
  않는다.

## 다음 단계 인계

5단계는 Batch 출력이 실제 WaterCell과 WaterSystem에 주는 사실을 검증하고,
현재 비활성 상태로 남은 `LegacyHydrologyMap`, `LegacyHydrologyBatch`, 이전
Region/River Planner를 물리적으로 제거한다.
