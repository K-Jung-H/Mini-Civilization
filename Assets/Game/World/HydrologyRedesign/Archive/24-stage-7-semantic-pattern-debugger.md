# 7단계: Semantic Pattern Debugger

> 이 문서의 독립 일회성 Tile reader와 Debugger 결과 폐기 설명은 더 이상 활성 계약이
> 아니다. Debugger는 Runtime PatternMapStore에 Map 전용 Prepare demand만 추가하고 seal된
> Tile을 읽는다. 현재 계약은
> [25-pattern-map-store-streaming-contract.md](25-pattern-map-store-streaming-contract.md)를 따른다.

## 완료 범위

7단계는 삭제된 Batch, Scope, Snapshot, Preview World 기반 Debugger를 복원하지 않는다.
새 Debugger는 같은 Seed와 Semantic World Settings에서 나온 Terrain/Hydrology Pattern Tile만
소비해 지형, 수문, 통합 Pattern Map을 표시한다.

```text
선택 중심 Chunk + Map Level
  → 256 × 256 표시 Pixel의 absolute Cell 표본
  → 필요한 Pattern TileKey 중복 제거
  → 독립적인 일회성 Tile reader
  → Terrain / Hydrology / Combined Texture
```

Debugger의 Tile reader는 `WorldData`, `ChunkData`, Renderer, WaterSystem, Runtime의 active
Tile cache를 읽거나 변경하지 않는다. Terrain/Hydrology Tile은 동일한 순수 builder에서
준비하지만, Debugger 요청의 Tile 결과와 실행 수명은 Debugger가 닫힐 때 함께 폐기된다.
따라서 Debugger의 선택, Palette, Texture, Target 이동은 생성 원본이나 Streaming Demand의
입력이 아니다.

## Map 계약

- 출력 Texture는 항상 `256 × 256` Pixel이다.
- Map Level은 Chunk 한 변을 최소 `2 × 2` Pixel로 표시하며, 현재 `ChunkCellCountXZ = 8`에서는
  `2`, `4`, `8` Pixel Level을 제공한다. 최대 Level의 Pixel과 Cell은 1:1이다.
- 축소 Level의 Pixel은 해당 absolute 영역의 중심 Cell 하나를 샘플한다. 집계, River 강제
  표시, 별도 저해상도 Hydrology는 만들지 않는다.
- Tile은 화면 중심에 가까운 Pixel을 먼저 완성하고, `MaximumConcurrentTileBuilds` 설정 수만
  동시에 백그라운드에서 준비한다.
- 완료된 Tile이 차지하는 Pixel만 즉시 Texture에 적용한다. 취소 또는 선택 변경된 작업은
  cancellation token으로 중단하고 결과를 버린다.
- Terrain Map은 `TerrainPatternType`, Hydrology Map은 `WaterType`, Combined Map은 Terrain 위에
  Hydrology Palette alpha를 적용한다. Palette는 이 타입별 색상을 명시 asset으로 가진다.
- 고정맵 출력 범위 밖 Pixel은 Tile을 만들지 않으며 검은 미완성 Pixel으로 남는다.

이 Map은 Combined Pattern을 저장하거나 새 원본 Map으로 만들지 않는다. 각각의 표시 Pixel은
같은 Terrain/Hydrology Tile pair의 즉시 조합 결과다.

## 선택과 Streaming Target

- 빨간 5 Pixel 십자는 현재 Streaming Target의 absolute Cell 위치다.
- 노란 사각형은 선택 중심의 output Demand 영역이다. 무한 월드는 `StreamingRadiusChunks`,
  고정맵은 전체 유한 output 범위를 표시한다.
- Map Pixel을 선택하면 선택 중심 Chunk를 바꾸고, 그 중심으로 새 Map 요청을 시작한다.
- `선택 영역으로 Streaming Target 이동`은 Target Transform만 선택 중심 Chunk의 중앙으로
  이동한다. ChunkData, WaterCell, Pattern Tile을 직접 만들지 않는다. 다음 Runtime frame의
  Streaming coordinator가 일반 Target 변경으로 처리한다.

## 구현 파일

- `Presentation/WorldTerrainPatternDebugger.cs`: Semantic Settings, Streaming Target 이동의
  명시적 경계.
- `Editor/WorldTerrainPatternDebuggerEditor.cs`: 256 Pixel Map, 비동기 Tile reader, Palette
  적용, 선택 및 overlay.
- `Presentation/SemanticPatternMapPalette.cs` 및 asset: Terrain/Hydrology Type별 표시 색상.
- `Scenes/Main Scene.unity`: `Pattern Debugger` GameObject와 World Manager, Palette 참조.

## 검증 범위

새 Runtime·Editor source를 임시 Unity project compile 목록에 포함해 C# 컴파일했다. 오류는
없다. Unity Package 및 기존 `WorldRuntime.EntityRenderStateChanged` 미사용 경고는 남아 있다.

실제 검증은 사용자 환경에서 수행한다.

1. 초기 월드 생성과 첫 Chunk 표시
2. 작은 Target 이동, 큰 Target 이동, 생성 중 재이동
3. 각 Map Level의 지형·수문·통합 표시와 River 축소 샘플 결과
4. Target 십자, 선택 overlay, Map 선택 후 Target 이동
5. Debugger 취소·재선택 시 Editor 응답성과 Runtime Streaming 독립성
