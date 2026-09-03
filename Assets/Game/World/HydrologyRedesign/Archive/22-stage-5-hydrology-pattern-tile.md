# 5단계: Hydrology Pattern Tile Drawing

## 완료 범위

이 단계는 4단계에서 확정된 Geometry를 Pattern Tile Core Cell에 기록한다. Tile은 Feature를
생성·경쟁·연결·변형하지 않으며, ChunkData나 WaterCell도 만들지 않는다.

```text
PatternTileKey
  → Core bounds
  → HydrologyFieldEvaluator.EnumerateFeaturesIntersecting
  → 확정 Geometry 목록
  → Core Cell Raster
  → HydrologyPatternTile
```

- `HydrologyPatternTileBuilder`는 한 Tile의 Core bounds에 대해 Field evaluator를 한 번만
  호출한다. 이후 각 Cell은 그 불변 Geometry 목록만 샘플한다.
- 고정 월드에서 `IsOutputAllowed` 밖 Pattern Tile은 만들지 않는다. 경계 Feature에 필요한
  외부 Terrain/Hydrology 값은 여전히 Field evaluator가 절대 좌표에서 직접 읽는다.
- `TerrainPatternTileBuilder`도 같은 출력 경계를 적용한다. 이 제한은 논리 Field 평가가
  아니라 출력 Tile 생성에만 적용된다.
- `HydrologyPatternTile`에는 Tile-local `FeatureKey` table과 각 Cell의 `WaterType`,
  `groundHeight`, `waterSurfaceHeight`, interior/boundary influence만 기록한다.

## Cell 소유권

4단계 Geometry가 Sea/Basin과 River 교차 영역을 이미 해소하므로, 보통 하나의 Cell에는 하나의
Geometry만 남는다. Builder는 Geometry를 바꾸지 않는 출력 안전 규칙만 가진다.

```text
Sea/Lake/Pond sample > River sample
같은 소유권 등급의 복수 sample > 낮은 FeatureKey
```

- 첫 규칙은 Water 도형이 River보다 교차 영역을 소유한다는 4단계 계약을 Cell 기록에 그대로
  반영한다.
- 두 번째 규칙은 하나의 `HydrologyPatternCell`에 두 값을 기록할 수 없을 때의 결정론적
  표현 선택이다. River Join, Junction, 새 Feature 생성, 경로 연결을 만들지 않는다.

## Combined Pattern

Combined Pattern은 별도 원본 Tile이나 Cache가 아니다.

```text
TerrainPatternTile cell + HydrologyPatternTile cell
→ CombinedPatternCell
```

- `PatternTileComposition`은 Key와 Core bounds가 같은 Terrain/Hydrology Tile만 조합한다.
- 물이 없으면 `Terrain.SurfaceHeight`가 최종 `GroundHeight`다.
- 물이 있으면 Hydrology의 `groundHeight`와 `waterSurfaceHeight`를 소비한다.

## 구현 파일과 상태

- `Generation/Semantic/HydrologyPatternTileBuilder.cs`: Core-only Hydrology raster와
  tile-local Feature table.
- `Generation/Semantic/PatternTileContracts.cs`: Terrain/Hydrology Tile pair의 즉시 Combined
  Cell 조합 및 Feature index 계약 보강.
- `Generation/Semantic/TerrainPatternEvaluator.cs`: Terrain 출력 Tile에도 유효 출력 경계 적용.

ChunkData materialization, Source WaterCell, Mesh, Renderer, cache·취소·수명 관리, Runtime,
Pattern Debugger는 구현하지 않았다. 이는 6·7단계 범위다.

Semantic source를 C# compile 목록에 포함해 오류 없이 컴파일했다. 실제 Tile·월드 생성 결과는
6단계 연결 후 사용자 Unity 환경에서 검증한다.
