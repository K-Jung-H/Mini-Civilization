# 6단계: ChunkData와 Streaming 전환

> 이 문서의 `PatternTileCache`, demand 이탈 Tile 취소·폐기, 단일
> `streamingRadiusChunks` 설명은 더 이상 활성 계약이 아니다. 현재 Runtime PatternMapStore,
> `Update 5 / Render 7 / Prepare 10`, Render 전용 ChunkData materialization은
> [25-pattern-map-store-streaming-contract.md](25-pattern-map-store-streaming-contract.md)를 따른다.

## 완료 범위

6단계는 순수 Terrain/Hydrology Pattern Tile을 출력 ChunkData로 구체화하고, Runtime이
Streaming Target의 출력 수명만 관리하도록 연결한다.

```text
StreamingTarget
  → output ChunkDemand
  → active Pattern Tile cache / background Tile work
  → completed Terrain/Hydrology Tile pair
  → main-thread ChunkData materialization
  → Runtime cache / Renderer / WaterSystem
```

Coordinator는 Feature, Basin, River, Hydrology Tile을 생성 규칙으로 소유하지 않는다.
Tile Builder와 Field evaluator는 Seed·Settings·절대 좌표만 읽고, Coordinator는 완료된
Tile을 어느 출력 Chunk가 지금 필요로 하는지만 관리한다.

## Chunk materialization

`PatternChunkMaterializer`는 Chunk와 교차하는 Terrain/Hydrology Tile pair만 읽어
`WorldData`에 CellData를 쓴다. Combined Pattern Tile, 별도 Chunk Pattern cache, 재-Drawing,
Feature 재평가는 만들지 않는다.

```text
Hydrology 없음
  Terrain surface → final ground

Hydrology 있음
  Hydrology groundHeight → final ground
  Hydrology waterSurfaceHeight → Source WaterCell
```

기존 CellData는 높이를 `WorldGrid.HeightStepsPerCell` 단위로만 기록한다. 연속 Pattern 값을
그대로 저장할 수 없으므로 다음 출력 투영을 명시한다.

```text
groundHeight       → floor(height steps)
waterSurfaceHeight → ceiling(height steps)
```

따라서 실제 수면이 최종 지면보다 높으면 출력 수면도 최소 한 Step 높아진다. 모든 출력
WaterCell은 `WaterRole.Source`와 Hydrology의 WaterType으로 기록한다. 이는 얕은 River 또는
Natural Endpoint가 반올림 때문에 Source 없이 사라지는 것을 막기 위한 표현 계약이며,
Feature 생성 규칙이나 추가 물 생성 규칙이 아니다.

Biome Pattern은 아직 설계 범위가 아니므로 materializer는 CellBiome에 별도 Biome 사실을
추가하지 않는다. TerrainPatternType은 Terrain Tile에 그대로 남고, CellData의 Material과
Geology는 현재의 일반 지형 소비 표현인 Soil로 기록한다.

## Tile 작업과 수명

`PatternTileCache`는 현재 output ChunkDemand가 참조하는 Tile만 보존한다.

- Cache 결과와 실행 중인 작업은 TileKey별로 하나만 존재한다.
- `maximumConcurrentTileBuilds`는 동시에 준비할 순수 Tile 작업 수를 정하는 명시적 월드
  설정이다. 기본 asset 값은 `2`다.
- Target 변경으로 참조가 사라진 Tile의 미완료 작업에는 cancellation token을 보낸다.
  Terrain raster 행과 Hydrology Feature owner 열거 사이에서 취소를 확인한다. 이미 진행 중인
  단일 Feature 평가의 중간 결과는 WorldData에 쓰지 않으며, 작업이 끝나도 참조가 없으면
  결과를 보존하지 않는다.
- TTL, LRU, 시간 기반 제거, 요청 범위 Snapshot, 전체 Demand 완료 대기는 없다.
- 이미 materialize된 WorldData Chunk는 Tile Cache에서 제거돼도 다시 Tile을 만들지 않고
  재활성화한다.

`chunkMaterializationsPerFrame`은 완료된 Tile을 WorldData·Runtime·Renderer에 적용하는
메인 스레드 Chunk 수를 정하는 명시적 설정이다. 기본 asset 값은 `1`이다. Tile 생성 비용을
이 값으로 우회하지 않으며, Tile 계산은 별도 순수 작업에서 끝난 결과만 적용한다.

## Runtime과 WaterSystem

`PatternStreamingCoordinator`는 Target 중심의 거리 우선 ChunkDemand를 만들고, 무한 월드는
`streamingRadiusChunks`, 고정맵은 유한 출력 범위 전체를 사용한다. 새 Chunk는 자신의 Tile이
완료되면 바로 materialize·prepare·active가 되며, 같은 Demand의 다른 Chunk 완료를 기다리지
않는다.

Demand에서 벗어난 Chunk는 Renderer, SurfaceCache, NavigationCache, Water simulation active
상태만 해제한다. WorldData와 편집·동적 Water 사실은 저장/로드 설계가 없는 상태에서 제거할
수 없으므로 8단계 전까지 보존한다. 따라서 탐험 범위에 따라 WorldData 메모리는 증가한다.
이는 Cache 정책이나 임시 생성 차단이 아니라, 새 저장/로드 및 변경 Chunk 보존 정책이
확정되기 전의 명시적 수명 경계다.

새 Chunk의 WaterSystem 입력은 전체 높이 Column 재계산이 아니다.

- materializer가 만든 Source WaterCell을 입력으로 사용한다.
- 새 Chunk와 이미 materialize된 이웃 Chunk의 공유 경계 WaterCell만 다시 검토한다.
- Source 중 실제 낙하·흐름 가능성이 있는 Cell과 경계의 Dynamic Water만 WaterFlowResolver에
  전달한다.

물의 종류·Feature·수면은 이 과정에서 새로 결정하지 않는다. WaterSystem은 완성된 CellData를
읽어 낙하와 흐름만 갱신한다.

## 구현 파일

- `Generation/Semantic/PatternChunkMaterializer.cs`: immutable Tile pair, 취소 가능한 Tile
  cache, CellData/Source WaterCell materializer.
- `Generation/Semantic/SemanticWorldSettings.cs` 및 asset: World, Pattern, Streaming 설정의
  단일 입력.
- `Runtime/PatternStreamingCoordinator.cs`: output Demand, Tile 작업 수명, 완료 Chunk 적용.
- `Runtime/WorldRuntime.cs`: Chunk prepare/active/release, Water frontier 연결.
- `Runtime/WorldManager.cs`: Semantic World 생성과 Streaming Target 연결.

## 검증 범위

Semantic source와 Runtime·Editor source를 임시 컴파일 목록에 포함해 C# 컴파일했다. 오류는
없었고, 기존 `EntityRenderStateChanged` 미사용 경고 하나만 남았다. 임시 csproj 변경은
복원했다.

사용자 실제 검증은 7단계 Pattern Debugger 전환 후 수행한다. 이 단계만으로 초기 생성 시간,
Target 이동, 재이동 중 취소, Renderer 결과, WaterSystem 결과를 통과했다고 단정하지 않는다.
