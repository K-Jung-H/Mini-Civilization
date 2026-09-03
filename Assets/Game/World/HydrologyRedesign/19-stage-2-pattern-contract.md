# 2단계: Pattern Tile 계약

## 완료 범위

이 단계는 새 생성기의 좌표와 Pattern 출력 데이터 계약만 만든다. Terrain, Sea, Basin, River를 평가하거나
ChunkData를 만들지 않는다. 따라서 이 단계만으로 실행 가능한 World 생성 경로는 생기지 않는다.

- `PatternTileGridSettingsData`의 `PatternTileKey`는 절대 XZ Tile 좌표이며,
  `PatternTileChunkSpan × ChunkCellCountXZ`로 Core Cell 사각형을 결정한다.
- 고정 월드는 `InitialChunkCountXZ`가 `PatternTileChunkSpan`으로 정확히 나누어질 때만
  출력 Tile 경계가 Chunk 출력 범위와 일치한다. 논리 Terrain/Hydrology TileKey는 경계 없이
  계속되며, Feature는 출력 범위 밖 Terrain 값도 절대 좌표로 직접 평가할 수 있다.
- Feature 후보 열거 반경은 별도 Settings나 요청 범위가 아니라, 후속
  `HydrologyFieldEvaluator`가 Basin 최대 도달 거리와 River 길이·곡선·폭·회피 Corridor의
  명시값에서 직접 유도한다.
- `PatternTileGridSettingsData`는 Terrain/Hydrology에서 독립적으로 쓸 수 있는 Tile grid만 가진다.
- `WaterFeatureIdentity`는 Feature 종류, 절대 후보 소유 좌표, world seed, seed salt로
  구성된다. 실행 중 증가 ID가 아니다.
- `TerrainPatternTile`은 Cell별 Terrain 종류, 기본/상세 표면, slope, Sea 입력을 보관한다.
- `HydrologyPatternTile`은 Tile-local Feature Table과 Cell별 feature index, ground, surface,
  interior/boundary influence를 보관한다. `None` Cell은 Feature를 참조하지 않는다.
- Combined Pattern은 `PatternCellComposition.Combine`의 즉시 관점이다. 별도 Tile이나
  Cache를 만들지 않는다.

## 아직 결정하지 않은 수치

`PatternTileChunkSpan`과 새 Feature 도형의 최대 도달 범위 수치는 코드 기본값으로 넣지 않았다.
이 값들은 Tile 크기와 새 River Feature의 실제 최대 도달 범위를 동시에 결정한다. 3단계
Terrain evaluator와 4단계 Hydrology Feature 설정을 함께 도입할 때, 동일한 새
ScriptableObject에서 명시한다. 이때 고정 월드라면 Tile span은 `15` Chunk 월드 폭을
정확히 나누는 값이어야 한다.

## 이전 Pattern 디자인 이관 기준

아래 값은 삭제된 생성 구조의 호출 순서나 자료구조가 아니라, 이후 새 순수 evaluator에
이관할 시각적 Pattern 디자인 기준이다. 기존 ScriptableObject나 Settings 타입을 복원하지
않는다.

| 영역 | 이관 기준 |
|---|---|
| World | Infinite, cell 1, Chunk XZ 8, Chunk section Y 10, initial Chunk XZ 15, section count 10, render patch 1, terrain base height 32 |
| Noise Router | continentalness 0.004/4, erosion 0.0055/4, weirdness 0.008/4, peaks-valleys ridge 0.014/4, roughness 0.018/4, detail signed 0.09/3, sea detail signed 0.012/3; 모든 주파수 간격 2, persistence 0.4 |
| Region | size 128, center jitter 0.35, warp scale 0.0025, warp strength 24, boundary blend 10, interior reach 0.35, smooth/rugged/mountain/canyon/sea share 0.24/0.24/0.20/0.16/0.16 |
| Base terrain | continental surface -5/-2/0/3/6, erosion surface 2/1/0/-1/-2, erosion vertical 1/1/1/1/1, roughness detail 0.25/0.4/0.6/0.8/1 |
| Smooth | warp signed 0.006/3/persistence .45/strength 10, height value 0.012/4/.45 response -1/-.45/0/.45/1 amplitude 1.5~4, detail signed .055/3/.4 amplitude .1~.5 |
| Rugged | warp signed .007/3/.45/strength 14, relief ridge .018/5/.48 response -1/-.5/0/.5/1 amplitude 4~10, detail mode 3 .075/4/.42 amplitude .4~1.5 |
| Mountain | warp signed .0045/4/.48/strength 24, mass value .006/4/.5 response .12/.28/.55/.82/1 height 22~46, ridge .018/5/.48 response 0/.12/.42/.78/1 strength 6~18, detail signed .06/3/.42 amplitude .5~2.5 |
| Canyon | warp signed .006/4/.48/strength 28, basin value .006/4/.5 response .25/.38/.55/.72/.88 ratio .35~.65, valley ridge .025/5/.5 response 0/.05/.32/.72/1 ratio .75~1, depth 16~32, detail signed .065/3/.4 amplitude .1~.6 |
| Sea | warp signed .0045/3/.45/strength 18, basin value .005/4/.48 variation .15, depth curve 0/.05/.5/.95/1, max depth 10, seabed signed .035/4/.42 amplitude .25~.8, surface 12 |
| Basin | potential value .018/4/.48 response 1/.55/.2/.05/0, seed spacing 24, occurrence .5908, area 8~420, Pond 분류 상한 72, depth .6~10, max reach 48, separation 6, potential/slope cost 3/1.2, cut/fill 1/1.35, shore 5 and curve 0/.08/.5/.92/1, depth curve 0/.12/.55/.9/1, bed signed .055/3/.42 amplitude .05~.35 |
| Lake/Pond | 하나의 Basin 후보가 Seed 목표 면적으로 생성되며, 면적 72 이하를 Pond, 초과를 Lake로 분류 |
| River appearance | candidate spacing 128, anchor jitter 32, length 28~112, curve amplitude 4~16, stroke sample spacing 4, width value .025/3/.45 range 1~7, cross section 0/.05/.5/.95/1, depth 1.5~4, riverbed signed .08/3/.4 amplitude .05~.3, bank margin 1, avoidance corridor 6 |
| River termination | Natural rate curve 0/1/2/2/1, transition 16 |

3단계에서는 Terrain 관련 행만 새 evaluator 설정으로 이관한다. 4단계에서는 Basin, Sea,
River 관련 행을 새 `HydrologyFieldEvaluator` 설정으로 이관한다. 후보 열거 반경은 Feature의
입력 준비나 요청 범위를 확장하지 않고, 각 도형의 명시 최대 영향 범위에서 직접 유도한다.
