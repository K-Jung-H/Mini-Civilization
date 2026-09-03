# 생성 백본 이관 6단계 — Runtime·Debugger 전환 구현 기록

> 이 문서의 Request/PlanningSnapshot 활성 경로는 성능 측정 후 폐기되었다. 현재
> 활성 구조는 [15-feature-owned-streaming-replacement.md](15-feature-owned-streaming-replacement.md)를 따른다.

## 활성 생성 경로

새 월드는 metadata와 빈 `LoadedChunks`만 가진 `WorldData`로 시작한다. 고정맵도
전체 Chunk를 선생성하지 않으며, 고정 경계는 Request 허용 범위에만 적용된다.

```text
WorldChunkStreamingController
  → desiredPreparedChunks
  → WorldGenerationRequest(Streaming)
  → PlanningSnapshot
  → canonical priority Chunk materialization
  → WorldData / SurfaceCache / NavigationCache / Renderer / WaterSystem
```

- Runtime은 Chunk별 `HydrologyPlanScope`를 만들지 않는다.
- 현재 desired prepared Chunk 집합 전체가 하나의 Streaming Request다.
- Snapshot 계획은 작업 스레드에서 수행되고 sealed된 뒤에만 Chunk가 생성된다.
- 준비 순서는 target 거리 오름차순, 동률이면 `(X, Z)` 순서다.
- Target 변경으로 Request 집합이 달라지면 진행 중 계획은 Tile 경계에서 취소한다.
  이전 Snapshot으로 생성 중이던 Chunk는 새 Request에 적용하지 않는다.
- 모든 requested Chunk가 적용되면 Runtime은 해당 Snapshot 참조를 해제한다.

`WorldGenerationTiming`에는 Snapshot 계획 전체 시간과 Snapshot이 소유한 사실
개수가 기록된다. 이어지는 Chunk 로그의 `hydrologyBatch` 시간은 이제 sealed
Snapshot Raster 시간이며, 계획 시간은 별도 planning 로그에서 읽어야 한다.

## Pattern Debugger 전환

Pattern map의 Pixel 좌표는 Planning Tile key로 deduplicate된다.

```text
preview Pixel Cell
  → unique Planning Tile rectangle request(Debugger)
  → sealed Snapshot
  → one temporary Raster per requested Tile
  → preview sample
```

- Debugger는 `WorldHydrology`, Scope, 기존 Batch 또는 기존 Hydrology resolver를
  사용하지 않는다.
- Preview용 Tile Raster는 현재 preview가 실제로 읽는 Tile에 한정되며, sample 결과를
  만든 직후 해제된다.
- 선택 Cell 정보와 선택 영역 overlay는 각각 그 작은 rectangle만 Raster로 만들고
  즉시 해제한다.
- Debugger Snapshot은 Runtime Snapshot과 공유하거나 Runtime 수명을 변경하지 않는다.

## 취소 범위

`StreamingRiverPlanningStage`의 allocation Tile, endpoint Tile, proposal Tile,
interaction/SpatialIndex Tile 반복은 `CancellationToken`을 확인한다. 취소된 Snapshot은
seal되지 않으며, partial 사실은 Runtime이나 Debugger에 노출되지 않는다.

## 남은 검증 및 정리 경계

이 단계에서는 이전 Store/Scope/Batch 소스를 제거하지 않았다. 하지만 새 월드 생성,
Runtime streaming, Pattern Debugger의 활성 경로는 새 Request/Snapshot/Raster만
사용한다.

다음 7단계에서 사용자가 실제로 다음을 확인한다.

- 초기 시작 후 target 주변 Chunk의 첫 표시 시간
- target 이동 후 새 Chunk 생성·렌더링
- 고정맵 경계와 무한 월드 좌표 결과
- Pattern map과 선택 overlay의 결과·지연
- Hydrology planning 로그와 Chunk Raster 로그

실행 결과가 확인되기 전에는 이전 구조를 삭제하거나 성능 개선 완료를 단정하지
않는다.

## 컴파일 확인

`dotnet build MiniCivilization.World.csproj --no-restore --verbosity minimal` 결과:
경고 0개, 오류 0개.
