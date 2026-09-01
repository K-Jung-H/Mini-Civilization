# 활성 스트리밍 생성 소유권과 잔여 구조 정리

## 우선순위

이 문서는 현재 활성 코드의 구조 기준이다. 이전 문서의 `PlanningSnapshot`,
`Plan Scope`, `HydrologyBatchBuilder`, 요청 범위 sealed 계획 설명은 구현 이력이므로
현재 구조와 충돌할 때 이 문서를 우선한다.

## 활성 생성 경로

```text
StreamingController
  → StreamingRequest
  → StreamingChunkDemand
  → ChunkStreamingCoordinator
      → StreamingFeatureWorld
      → HydrologyRasterStage
      → WorldChunkGenerator
  → WorldRuntime chunk apply / cache / water / state
  → Renderer
```

- `StreamingRequest`는 Target Chunk와 세 반경만 표현한다.
- `StreamingChunkDemand`는 Prepared, Terrain Render, Entity Render, Active Chunk
  집합을 복사해 불변으로 보유한다. Patch와 3×3 topology 준비 범위 확장은
  `StreamingChunkDemandBuilder`만 결정한다.
- `ChunkStreamingCoordinator`는 demand, Feature lease, 우선순위 대기열, 활성
  worker, 완료 결과를 유일하게 소유한다.
- `WorldRuntime`은 Chunk 결과 적용·해제, Cache, Water Source, Chunk 상태 전환만
  담당한다. Task, 대기열, Feature lease를 직접 소유하지 않는다.
- `StreamingFeatureWorld`는 절대 좌표 Feature Tile/Edge와 lease 수명만 담당한다.
  Target 이동은 최신 lease 요청만 교체하며, worker와 메인 스레드가 같은 생성 잠금을
  기다리지 않는다.

## Chunk 생성 단계

```text
HydrologyRasterStage
  → WorldFieldStage
  → BaseTerrainPatternStage
  → WorldPatternStage
  → DensitySurfaceStage
  → DensityToFilledStage
  → WorldChunkBuildData
```

- `WorldChunkBuildInput`의 활성 입력은 `StreamingFeatureWorld` 하나다.
- `HydrologyRasterStage`가 Raster 생성을 명시적으로 소유한다.
- 수문 Raster와 최종 지형은 Chunk마다 함께 materialize한다. 다만 Hydrology
  Pattern/Graph는 `StreamingFeatureWorld`의 절대 좌표 Feature Tile을 공유하므로,
  Chunk마다 독립된 수문을 생성하지 않는다.
- 초기 UI는 worker 내부 수문/지형 단계를 노출하지 않는다. `WorldOperationProgress`는
  `3 / 3 Chunk 데이터 생성 중`과 현재 Terrain Render demand의 완료 Chunk 수,
  전체 수, 퍼센트만 표시한다.
- 전체 수는 단순 반경 제곱이 아니라, Render Patch 경계를 완성하기 위해 확장된 실제
  `TerrainRenderChunks` 집합이다.

## 제거 완료

- Scope/Batch 기반 `WorldHydrology`, `HydrologyBatchBuilder`, Region/Store/Planner
  계열
- 이전 `HydrologyGenerationContext`, `LegacyHydrologyMap`,
  `RiverHydrologyPlanner` 계열
- 요청 범위 `WorldGenerationRequest`, `PlanningFootprint`,
  `PlanningSnapshot`, sealed snapshot Raster와 Snapshot planner 계열
- Snapshot 전용 진단과 실제 데이터를 반영하지 않던 초기 생성 timing summary
- Streaming timing, Feature build metrics, Planning diagnostics와 콘솔 세부 출력

`StreamingMinHeap`은 현재 FeatureWorld의 Route A*가 사용하므로 제거 대상이
아니다. Hydrology 설정 데이터와 `StreamingFeatureWorld`의 Pattern/Graph 결과식도
제거하거나 변경하지 않는다.

## 남은 검증과 다음 작업

- 사용자는 초기 월드 생성, 연속 Target 이동, 이동 중 재이동, Pattern Debugger를
  실제 환경에서 확인한다.
- 에이전트 검증은 Unity C# 컴파일까지다.
- 초기 생성 UI가 `3 / 3 Chunk 데이터 생성 중`과 전체 Render demand 기준 완료 수,
  퍼센트를 표시하는지 사용자가 확인한다.
