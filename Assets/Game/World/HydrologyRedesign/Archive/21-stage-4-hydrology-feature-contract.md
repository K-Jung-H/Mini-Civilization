# 4단계 계약: 직접 Feature 평가와 Basin/Sea 상호작용

## 문서 지위와 범위

이 문서는 이전 Resolver 초안과 `FeatureDescriptor → 닫힌 의존성 준비 → Feature 해석`
구조를 폐기한다. Hydrology의 생성 원본은 Feature 계획 그래프가 아니라 절대 좌표 Terrain을
읽는 독립 도형 평가다.

```text
절대 좌표 TerrainMap 평가
→ Seed 기반 Sea / Basin / River 도형 평가
→ Basin/Sea와 River의 국소 도형 상호작용
→ HydrologyMap Tile Drawing
→ ChunkData
```

이번 4단계에는 Sea, Lake/Pond Basin, branch 없는 River 기본 Stroke, Terrain-aware Stroke,
Natural Endpoint, River–Basin/Sea 교차 소유권이 포함된다.

Terminus Join 후보 탐색·선택, JoinRange, River–River Junction/JoinFeature, Water 도형 접속
전이는 이 단계에 포함하지 않는다. 이는 임시 실패 처리가 아니라, 독립 River를 먼저 완성한
뒤 별도 확장으로 추가할 구조다. 따라서 현재 계약·설정·Tile FeatureKey에는 Join 전용 값이나
형식을 남기지 않는다.

`FeatureKey`는 Basin 또는 River 도형의 안정적인 정체성일 뿐, 요청 범위를 계획하거나,
의존성을 닫거나, 작업의 선행 준비를 지시하는 그래프 노드가 아니다.

## 세계 경계와 평가 입력

```text
논리 Terrain/Hydrology 세계 = 절대 좌표 전체
고정맵 = ChunkData·Renderer·Streaming 출력 범위 제한
```

- Terrain과 Hydrology는 어떤 절대 좌표에서도 같은 값으로 직접 평가할 수 있다.
- 고정맵 밖 Terrain 값은 경계에 닿는 Sea/Basin/River 도형의 입력으로만 평가하거나 메모할 수
  있다.
- 고정맵 밖에는 HydrologyPatternTile, ChunkData, WaterCell, Mesh, Render Object를 만들지
  않는다.
- Basin/River를 출력 경계에서 자르거나 취소하는 규칙은 없다.
- Terrain Pattern Tile은 Terrain 값의 전달·재사용 단위일 뿐, Hydrology Feature 해석 전에
  Ready여야 하는 의존성이 아니다. Feature 평가는 같은 절대 좌표 Terrain 평가기를 직접
  사용한다.

## Pattern 데이터 계약

```text
groundHeight       = Hydrology 절삭·성토 후 최종 지면 높이
waterSurfaceHeight = 물 표면 높이
```

- 물 아래 지면은 설명에서만 바닥이라고 부른다. 별도 `bedHeight` 데이터는 없다.
- `targetGroundHeight`도 없다. `groundHeight`가 ChunkData가 사용할 유일한 최종 지면 값이다.
- Hydrology Tile은 `WaterType`, tile-local `FeatureKey`, 두 높이, interior/boundary influence만
  기록한다.
- Combined Pattern은 물이 있으면 `groundHeight`, 없으면 Terrain surface를 즉시 선택한다.
- WaterSystem은 Feature가 확정한 두 높이와 생성 시 배치된 Source WaterCell을 다시 계획하지
  않는다. 이후 낙하·흐름 방향만 완성된 지형에 따라 갱신한다.

## 직접 Feature 평가

Feature evaluator는 외부에서 전달된 `competitors`, `candidates`, contact 목록 또는 Ready Tile
목록을 입력으로 받지 않는다. 필요한 도형은 자신의 절대 좌표·Settings·Seed로 직접 평가한다.

```text
HydrologyFieldEvaluator
  ├ EvaluateSea(absolute position or bounds)
  ├ EvaluateBasin(FeatureKey)
  ├ EvaluateRiver(FeatureKey)
  └ EnumerateFeaturesIntersecting(absolute bounds)
```

`EnumerateFeaturesIntersecting`은 출력 Tile이나 요청이 준 목록을 소비하지 않는다. 각 Feature
종류의 명시적인 최대 영향 반경, 후보 격자 간격, River 최대 길이, 최대 폭/제방 범위, Terrain
회피 Corridor로부터 계산한 유한한 FeatureKey 범위만 열거한다. 이 열거는 Tile이 규칙을
결정하는 것이 아니라 Field Evaluator가 도형을 찾기 위해 수행하는 좌표 연산이다.

### Sea

Sea는 절대 좌표 Terrain의 primary Sea region key와 interior progress만 읽어 Sea 영역, 전역 수면, 경계→중심의 S자형 해저
도형을 직접 평가한다. Sea 도형은 River나 Basin의 평가 순서에 의존하지 않는다.

### Lake/Pond Basin

```text
Basin 후보 격자
  → Seed로 발생 여부와 목표 면적 결정
  → 목표 면적이 PondMaximumArea 이하이면 Pond, 초과하면 Lake
  → 후보 anchor Terrain surface를 Basin의 하나의 수면으로 확정
  → 확정 수면 기준 Potential·경사·절삭/성토 비용으로 연결 footprint 확장
  → 목표 면적 도달
  → 유한한 이웃 Basin Raw Geometry와 안정 우선순위 비교
  → active 또는 inactive Basin Geometry
```

- Lake/Pond는 별도 생성기나 별도 발생률이 아니다. 하나의 Basin 후보가 크기로 분류된다.
- Raw Basin은 Terrain/Potential과 Seed를 읽어 자유로운 불규칙 footprint, 하나의 일정한
  수면, 중앙 심도와 경계 전이를 만든다.
- Basin은 자기 최대 영향 범위 안의 Basin 후보 격자만 직접 열거하고, 이웃 Raw Geometry도
  같은 함수로 직접 평가한다. 전역 Basin 계획, 요청 전체 후보 준비, 재배치·재시도는 없다.
- footprint 또는 최소 이격 범위가 충돌하면 더 높은 안정 우선순위 Basin만 active다.
- Sea 내부 Basin은 별도 예외 차단이 아니라 Basin이 육지 Terrain에서만 정의된다는 도메인
  계약에 따라 Raw Basin이 아니다.

### River 기본 Stroke와 Natural Endpoint

```text
River 후보 격자
  → Seed 기반 발생 여부
  → Seed 기반 anchor·방향·길이·곡선 범위의 branch 없는 기본 Stroke
  → 각 위치의 명시적인 Corridor 후보를 Terrain 값으로 평가
  → Terrain-aware Stroke
  → 양 Terminus의 Natural Endpoint 전이·종료
```

- `Terminus A/B`는 양 끝의 기하학 위치일 뿐, 흐름의 시작/끝이나 미리 할당된 Water
  Endpoint가 아니다.
- River 기본 도형은 후보 격자 원점의 Seed jitter anchor, Seed 방향·길이·곡선 진폭으로 만든
  하나의 cubic Bezier다. 두 제어점은 진행 방향의 1/3·2/3 위치와 같은 법선 방향 곡선 진폭으로
  결정한다. 따라서 FeatureKey와 명시적 Settings만으로 최대 영향 Bounds를 계산할 수 있다.
- 독립 River에는 branch를 만들지 않는다. FeatureKey에도 branch path를 넣지 않는다.
- Terrain 회피의 각 위치 후보는 기본 Stroke와 그 위치의 Terrain 입력만 사용한다. 이전
  sample에서 선택된 결과를 다음 후보의 비용 입력으로 쓰지 않는다.
- `ElevationChangeCost`는 Route 탐색 규칙이므로 두지 않는다. Terrain 회피 비용은 횡방향
  displacement, cross slope, Mountain, Canyon만 사용한다.
- Natural Endpoint는 `EndpointTransitionLength`와 `0 → 1 → 2 → 2 → 1` rate curve의 적분형
  profile 전이로 폭·깊이를 감쇠시킨다. 이때 `groundHeight`는 `waterSurfaceHeight`에 수렴하므로
  두 높이가 선택 Terrain surface에서 함께 종료된다.
- 독립 River의 `waterSurfaceHeight`는 terrain-aware Stroke가 선택한 위치의 Terrain
  surface다. `groundHeight`는 그 수면에서 River depth를 뺀 값이다.

## River–Basin/Sea 상호작용

```text
Sea / 활성 Basin Geometry + Terrain-aware River Geometry
  → Cell 해상도로 분할한 Terrain-aware Stroke와 Water 도형의 경계 접점 확정
  → 교차 영역은 Sea/Basin이 소유
  → River는 경계 접점에서 끝나고 Basin/Sea 경계 전이만 남김
```

- 이는 Terminus Join 후보 선택이나 River–River Junction이 아니다. 독립 River가 Sea/Basin
  영역과 교차할 때 물·지형 소유권이 중복되지 않게 하는 Hydrology 기본 규칙이다.
- River–Basin/Sea는 이미 그린 Tile의 sample 구간을 숨기는 방식이 아니다. Feature Geometry
  단계에서 Stroke를 Cell 해상도로 분할하고, 교차 구간을 제외한 River course와 경계 접속
  전이를 확정한다.
- Basin/Sea가 교차 영역의 `groundHeight`와 `waterSurfaceHeight`를 소유한다. River는 내부를
  다시 절삭하거나 물을 기록하지 않는다.

## Tile Drawing과 재사용의 경계

```text
HydrologyPatternTileBuilder(Core bounds)
  → HydrologyFieldEvaluator.EnumerateFeaturesIntersecting(bounds)
  → 각 Feature Geometry 직접 평가 또는 동일 Key 결과 재사용
  → Core만 rasterize
```

- Tile Builder는 Feature 후보를 생성·경쟁시키거나, Natural Endpoint·교차 규칙을 판단하지
  않는다. Field Evaluator가 확정한 도형만 Core에 그린다.
- Feature Geometry 결과를 Key별로 메모하는 것은 5·6단계에서 도입할 수 있다. 이는 동일한
  순수 평가를 반복하지 않기 위한 재사용일 뿐이며, Geometry의 입력, 활성 여부, 또는 작업
  선행 순서를 바꾸지 않는다.
- Cache miss는 직접 평가를 뜻할 뿐 다른 Feature의 준비를 기다리거나 요청 범위 전체를
  생성하지 않는다.

## 후속 Join 확장

현재 로드맵의 완료 후, 별도 확장 설계가 합의되었을 때만 아래를 새 Feature 계층으로 추가한다.

```text
Terminus 주변 Water 조회
→ JoinRange와 후보 선택 규칙
→ River–River Junction / JoinFeature
→ Water 도형 접속 전이
```

이 확장은 독립 River, Basin/Sea 소유권, Terrain/Hydrology Tile의 생성 원본을 바꾸지 않는다.

## 구현 기록과 완료 기준

1. `HydrologyFieldEvaluator`의 절대 좌표 Terrain 조회와 Sea/Basin Raw Geometry 직접 평가를
   구현한다.
2. Basin의 단일 발생·면적 기반 Lake/Pond 분류, anchor 수면, 연결 footprint와 안정 활성
   판정을 구현한다.
3. branch 없는 River 기본 Stroke, Terrain-aware Stroke, Natural Endpoint를 구현한다.
4. River–Basin/Sea 도형 교차와 소유권을 확정한다.
5. Feature 결과의 수명 관리와 Hydrology Tile Drawing은 다음 5단계에서 구현한다.

4단계 완료 기준은 다음 하나다.

```text
Seed + Settings + absolute coordinate / FeatureKey
= 요청·Tile·Chunk·Debugger 접근 순서와 무관한 동일한 Sea/Basin/River Geometry
```

## 구현 상태

- `HydrologyFieldEvaluator`가 Sea, Basin Raw/active geometry, branch 없는 River geometry를
  직접 평가한다. Request, Snapshot, Scope, Batch, Graph, A* 입력은 없다.
- Basin은 하나의 occurrence·area 범위에서 Lake/Pond를 분류하고, 연결 footprint의 실제 경계
  거리를 `shoreTransitionCells`에 적용한다.
- River는 cubic Bezier Terrain-aware 제어점을 Cell 해상도로 분할한 뒤 Basin/Sea 교차 구간을
  제외한 River course와 접속 전이를 확정한다. Water 도형은 River보다 우선해 교차 영역을
  소유한다.
- Feature 후보 최대 범위는 별도 Footprint 설정이 아니라 각 Basin/River 설정값에서 evaluator가
  직접 계산한다.
- `HydrologyPatternTile` Drawing, ChunkData/WaterCell 생성, Cache/수명 관리, Runtime 연결은
  아직 구현하지 않았다. 이는 5·6단계의 범위다.
- Semantic source를 임시 C# compile 목록에 포함한 뒤 오류 없이 컴파일했고, 실행 테스트는
  사용자 환경에서 다음 연결 단계 후에만 수행한다.
