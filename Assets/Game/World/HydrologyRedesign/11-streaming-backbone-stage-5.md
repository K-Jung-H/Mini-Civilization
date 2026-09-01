# 생성 백본 이관 5단계 — Sealed Raster와 Chunk Materializer 구현 기록

## 완료 범위

`StreamingHydrologyRaster`는 sealed `PlanningSnapshot`과 요청 사각 범위만
입력으로 받아 다음 순서로 Cell 결과를 만든다.

```text
Snapshot BaseTerrain + BasinAllocation
  → Sea / Basin topology raster
Snapshot RiverSpatialIndex
  → River corridor / Junction raster
  → StreamingHydrologyCell
  → 기존 Chunk terrain·Water Source materialization
```

Raster는 Endpoint, Proposal, Route, Interaction, Allocation Tile을 만들거나
보충하지 않는다. 필요한 Snapshot 사실이 없으면 즉시 실패한다.

## 새 Cell 및 Raster 계약

- `StreamingHydrologyCell`은 새 ComponentId와 EdgeId를 그대로 유지한다. 기존
  `HydrologyCellPlan` 또는 기존 Hydrology identity로 변환하지 않는다.
- debug 식별자는 새 Basin ComponentId 또는 River EdgeId에서 직접 해시된다.
- River corridor의 폭, cross section, riverbed field, 가까운 Edge 선택, Junction
  적용은 기존 확정 패턴 공식을 새 RoutePlan으로 재현한다.
- Basin/Sea membership이 있는 Cell에는 River가 덮이지 않는다. 이는 기존 Topology
  우선순위를 그대로 보존한 결과다.
- Raster와 Snapshot의 Settings 인스턴스가 다르면 materialization 하지 않는다.

## Materialization 연결

`IWorldHydrologyRaster`는 Chunk 생성기가 필요한 두 사실만 제공한다.

- BaseTerrain fact
- BaseTerrain Pattern에 대한 Hydrology composition 결과

기존 `HydrologyBatch`는 이 계약의 이전 구현으로 남아 있으며, 새
`StreamingHydrologyRaster`도 같은 Chunk 생성 단계를 통과한다. 따라서 최종
지형 절삭·성토, WaterRole.Source 배치, WaterSystem 이후 동작은 변경하지 않았다.

`StreamingWorldChunkMaterializer.Build(snapshot, chunk)`은 sealed Snapshot을
소비하는 명시적 Chunk 생성 진입점이다. Snapshot 요청에 포함되지 않은 Chunk는
materialize하지 않는다.

## Snapshot 준비 범위 보완

planning stage는 각 요청 Chunk의 기존 `WorldFieldStage.RequiredHaloCellCount`
범위에 BaseTerrain 사실을, 그 범위에 최대 River corridor extent를 더한 영역에
River SpatialIndex Tile을 미리 준비한다. 이 범위는 Chunk materializer가 실제로
읽는 범위이며, Raster가 소비 중 계획을 확장하지 않게 하는 계약이다.

## 아직 전환하지 않은 실행 경로

현재 `WorldGenerationPipeline`과 `WorldRuntime`은 여전히 이전 Scope 기반
스케줄러를 사용한다. 고정맵 전체를 미리 생성하는 그 스케줄러에 새 Snapshot을
부분 연결하지 않았다. 그렇게 하면 고정맵 경계를 계획 생존 범위로 되돌리고,
초기 생성과 이동 생성이 서로 다른 Hydrology 결과를 가질 수 있다.

다음 단계에서 초기 생성, streaming target 이동, Snapshot 생명주기, 패턴맵
debugger 입력을 하나의 request scheduler로 동시에 전환한다. 그 전에는 새 Raster
경로의 실제 성능이나 결과가 기존 실행에 적용되었다고 판단할 수 없다.

## 컴파일 확인

`dotnet build MiniCivilization.World.csproj --no-restore --verbosity minimal` 결과:
경고 0개, 오류 0개.
