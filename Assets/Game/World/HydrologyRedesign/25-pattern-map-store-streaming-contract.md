# Pattern Map Store와 세 Range 계약

## 우선순위

이 문서는 `18`의 Pattern 원본 계약을 Runtime 수명과 수요 처리까지 확정한다. 이 문서와
충돌하는 `18`, `23`, `24`의 cache, streaming, debugger 설명은 이 문서로 대체한다.

WaterMap을 만드는 규칙은 `26-water-map-direct-drawing-contract.md`가 소유한다. 이 문서는
그 결과 Tile의 보존·수요·소비만 소유한다.

## 원본과 수명

```text
Runtime PatternMapStore
  TerrainPatternTile
    → HydrologyPatternTile
      → ChunkData
```

- `PatternMapStore`는 `WorldRuntime`이 소유하며 Runtime 동안 seal된 Terrain/Hydrology Tile을
  보존한다.
- Hydrology는 Store에 seal된 Terrain Tile만 읽는다. 별도 Terrain evaluator를 다시 만들지
  않는다.
- Target 이동, Chunk release, Debugger demand 제거는 seal된 Tile을 삭제하거나 재계산하지
  않는다.
- 저장/로드는 아직 이 Store를 직렬화하지 않는다. 이는 최종 저장/로드 단계의 별도 범위다.

## Range와 실행 순서

기본 설정은 다음이며 반드시 `Update <= Render <= Prepare`를 만족한다.

| Range | 값 | 책임 |
|---|---:|---|
| UpdateRange | 5 Chunk | Water/Entity simulation 활성 범위 |
| RenderRange | 7 Chunk | seal된 Map pair를 ChunkData·Renderer로 구체화하는 범위 |
| PrepareRange | 10 Chunk | Terrain/Hydrology Pattern Map만 준비하는 범위 |

```text
Streaming Target 변경
  → PrepareRange의 Map Tile 요구 갱신
  → Streaming Target / Debugger 중심까지의 Tile 거리순 준비
  → Terrain Tile seal
  → Terrain Store를 읽는 Hydrology Tile seal
  → RenderRange의 seal된 pair만 ChunkData materialize
  → Target에서 가까운 Render Patch부터 Mesh 생성
  → UpdateRange의 활성 Chunk만 simulation
```

Prepare가 ChunkData, WaterCell, Mesh, Render Object를 만들면 안 된다. Render가 Tile을
생성하거나 Hydrology Feature를 해석하면 안 된다. Update가 Pattern Map이나 ChunkData를
만들면 안 된다.

Map scheduler는 Streaming Prepare demand와 Debugger Map-only demand가 공유하는 Tile을
한 번만 준비한다. 각 미준비 Tile의 실행 순서는 활성 demand 중심들까지의 최소 거리이며,
동률은 `PatternTileKey`로 고정한다. Target 이동 뒤 이미 시작된 Tile build는 취소하지
않고, 아직 시작하지 않은 Tile만 새 중심 거리로 다시 정렬한다.

Render Patch의 중복 상태는 하나의 우선 큐가 소유한다. Patch 생성·Terrain·Water·Road
재구축은 각 대기열에서 Streaming Target과의 거리순으로 꺼내며, Target 이동 후 대기
Patch의 거리 키도 다시 계산한다.

고정맵에서는 Prepare/Render/Update의 **출력 Chunk 수요**만 월드 범위에서 잘린다. 경계
Feature 해석용 Terrain 지원 Tile은 절대 좌표에서 준비될 수 있지만, 그 위치의 Hydrology
출력 Tile, ChunkData, WaterCell, Mesh, Render Object는 만들지 않는다.

## Debugger

```text
Debugger viewport / selection
  → Map 전용 Prepare demand
  → Runtime PatternMapStore
  → Terrain / Hydrology / Combined 256 Pixel 표시
```

- Debugger demand는 Terrain/Hydrology Map만 확장하며 ChunkData를 만들지 않는다.
- Terrain Map은 Terrain Tile이 seal되면 바로 표시할 수 있다. Hydrology와 Combined는 해당
  Hydrology Tile이 seal된 뒤 표시한다.
- Debugger는 독립 Tile builder, Preview World, 결과 전용 cache를 갖지 않는다.
- Map demand를 제거해도 이미 seal된 Pattern Tile은 Runtime에 남는다.
- Debugger Texture는 새로 seal된 Tile이 차지하는 Pixel 영역만 갱신한다. Layer 변경 때만
  256×256 전체 색상을 다시 합성한다.

## 소비자 경계

- Pattern Map: Terrain/Hydrology의 유일한 원본.
- ChunkData materializer: seal된 pair를 `CellData`와 `WaterCell(Source)`로 투영하는 소비자.
- Renderer와 WaterSystem: ChunkData를 소비한다. Pattern Tile을 생성하거나 변경하지 않는다.
