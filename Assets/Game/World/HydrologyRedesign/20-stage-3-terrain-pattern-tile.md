# 3단계: Terrain Pattern Tile

## 완료 범위

새 Terrain Pattern은 이전 World generation 호출 경로나 density/surface pipeline을 복원하지
않고, 다음 순수 평가기로 구현했다.

```text
TerrainPatternSettings + World seed + absolute XZ
  → TerrainPatternEvaluator
  → TerrainPatternTileBuilder
  → TerrainPatternTile
```

- [TerrainPatternSettings.asset](../Settings/TerrainPatternSettings.asset)가
  Terrain Pattern의 유일한 수치 원본이다.
- `PatternTileChunkSpan`은 `1`이다. Pattern Tile은 정확히 한 Chunk XZ Core와 일치한다.
  현재 15×15 고정 Chunk 범위를 나누며, 새 Render Chunk가 더 큰 Terrain Tile 완료를
  기다리지 않는다.
- 평가기는 절대 좌표만 읽는다. Request, Chunk 상태, Hydrology, Session, Cache, 작업 순서를
  읽지 않는다.
- Region 후보의 중심·Pattern 배정·각 Form의 range는 절대 후보 lattice와 Seed로만 정한다.
  Region 동점은 lattice 좌표 순서로 해소한다.
- Terrain Tile builder는 Core 바깥 한 Cell halo를 같은 순수 evaluator로 읽어 slope를
  계산한다. 이 halo는 다른 Tile을 acquire하거나 생성하지 않는다.
- 고정맵의 출력 범위 밖 Terrain 값도 이후 Hydrology Feature가 절대 좌표에서 직접 평가할 수
  있다. 이는 그 위치의 Hydrology Tile, ChunkData, Renderer를 만드는 경로가 아니며, 출력
  범위는 4단계 이후 `IsOutputAllowed`로 별도 제한한다.
- Terrain Cell은 `Smooth/Rugged/Mountain/Canyon`, base/detail surface, slope, primary Sea region key와 interior progress를
  제공한다. Sea는 Terrain WaterType이나 Terrain Pattern 종류가 아니다. Sea 비중만
  전달하며, 수면/해저/WaterCell은 4단계 Hydrology가 만든다.
- 이 단계는 scheduler, Runtime, ChunkData, Cache를 연결하지 않는다.

## 이관한 Pattern 디자인

이전 asset의 continentalness/erosion, Region 분포, base surface, Smooth, Rugged, Mountain,
Canyon의 noise/curve/range 값을 새 asset에 옮겼다. Noise의 mode, scale, layer,
frequency spacing, persistence, octave seed stride도 모두 asset 값이다. Noise channel의
식별은 이름 기반 seed derivation이며, 실행 중 ID나 임의 난수를 쓰지 않는다.

기존 Sea의 basin/depth/seabed/surface 수치는 새 Terrain asset에 넣지 않았다. 해당 수치는
4단계 Sea Hydrology Feature 설정으로 이관한다. Terrain의 primary Sea region key와 interior progress는 Sea Feature가
영역을 해소하는 입력일 뿐 Sea 자체의 결과가 아니다.

## 검증 범위

- 새 Pattern source를 실제 C# compile 목록에 포함해 `MiniCivilization.World.Editor`
  컴파일 오류 없이 확인했다.
- 실제 Terrain Tile 생성, ChunkData, Renderer, World 생성은 아직 연결되지 않았으므로
  실행 테스트 대상이 아니다.

## 다음 단계 입력

4단계 Hydrology Field evaluator는 동일한 절대 좌표 Terrain evaluator의 base/detail surface,
slope, primary Sea region key와 interior progress, 새 Hydrology 설정을 직접 읽는다. Terrain Tile은 결과 전달·재사용 단위일
뿐 Feature 평가의 선행 조건이 아니다.
