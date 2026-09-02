# Semantic Pattern Tile 기반 월드 생성: 최종 재설계

> 이 문서의 `locally resolved Hydrology Features`, Feature Geometry, River terrain-aware
> 처리 설명은 더 이상 활성 계약이 아니다. WaterMap 생성 원본은
> `26-water-map-direct-drawing-contract.md`의 `TerrainMap → WaterMap 직접 Drawing`이다.
> Pattern Map Store와 Range는 `25-pattern-map-store-streaming-contract.md`를 따른다.

## 문서 지위

이 문서는 기존 Hydrology 재설계 문서와 현재 생성 코드의 구조를 설계 입력으로
사용하지 않는다. 이 문서와 충돌하는 `00`~`17` 문서의 생성·수문·스트리밍 설명은
전부 구현 이력이다. 이후 구현은 여기의 계약만 따른다.

현재 월드의 지형·수문 **표현 결과 규칙**은 보존 대상이다. 반면 그 결과를 만들던
Batch, Scope, Snapshot, Endpoint Graph, Route 탐색, 요청 범위 Raster, Preview 전용
생성기는 보존 대상이 아니다.

## 목표와 제외 범위

```text
Seed + Settings + absolute world coordinate
  → TerrainPatternTile
  → locally resolved Hydrology Features
  → HydrologyPatternTile
  → Combined Pattern
  → ChunkData / WaterCell(Source)
  → Renderer and WaterSystem
```

- 원본 의미 데이터는 `TerrainPatternTile`과 `HydrologyPatternTile` 두 종류뿐이다.
- Combined Pattern은 두 원본을 합성해 즉시 얻는 관점이며, 저장하거나 별도 생성하지
  않는다.
- Sea는 Terrain Pattern이 아니라 Hydrology Pattern이다. Terrain은 Sea 판정에 필요한
  기본 지형 사실을 제공할 수 있지만, Terrain Map의 Water Type으로 Sea를 기록하지
  않는다.
- Chunk 생성, Renderer, WaterSystem, Pattern Debugger는 Feature 후보·연결·경로를
  만들지 않는다. 모두 확정된 Pattern Tile을 소비한다.
- River의 입력은 전역 Graph나 A* Route가 아니다. Graph는 필요할 때 최종 Feature에서
  파생하는 디버그 식별 정보일 뿐이다.
- 이 설계는 대륙 단위 배수 체계, 먼 Sea/Lake 종착지 보장, 무순환 전역 River Network를
  보장하지 않는다. 가까운 수역과의 자연스러운 국소 결합을 목표로 한다.

Terrain/Hydrology의 논리 세계는 경계 없이 절대 좌표에서 계속된다. 고정맵은 별도
생성기가 아니라, 같은 시스템에서 **ChunkData·Renderer·Streaming Demand가 출력할 Core
TileKey 범위만** 유한하게 제한한 월드다. 경계 밖 Terrain 값은 경계 Feature 해석에서
절대 좌표로 직접 평가할 수 있지만, 그 위치의 Hydrology 출력 Tile, ChunkData, WaterCell,
Render Object는 만들지 않는다.

## 변하지 않는 결과 계약

- 동일 Seed, Settings, 절대 좌표는 요청 순서, Chunk 생성 순서, 작업 완료 순서,
  Debugger 접근 순서와 무관하게 같은 Terrain/Hydrology Cell을 만든다.
- Lake/Pond는 같은 Basin 원리를 공유한다. Component 하나는 일정한 하나의 수면을 가지며,
  불규칙한 Potential·Terrain 기반 footprint와 중앙 심도/경계 전이를 가진다.
- Sea는 전역적으로 하나의 수면을 갖는다. 경계에서 중심으로 S자형으로 깊어지고, 넓은 Sea는
  넓은 평탄 심해를 갖는다. Seabed Detail은 깊이 구조를 대체하지 않는다.
- River는 폭 방향 단면, Corridor 지형 절삭, 수면/바닥 profile을 가진 독립 Stroke다.
  물을 띄우거나 연결용 WaterCell을 더하지 않는다.
- 모든 계획된 Sea/Lake/Pond/River WaterCell은 처음부터 `Source`다. 이후 낙하·흐름
  방향은 WaterSystem이 완성된 지형과 WaterCell을 기준으로 갱신한다.
- Hydrology가 존재하면 Hydrology의 최종 지면/바닥/수면이 Terrain 기본 표면을 대체한다.
  Basin의 낮은 지형은 성토되고 높은 지형은 절삭되어 일정 수면과 경계가 완성된다.

## 의미 데이터와 메모리 계약

### TerrainPatternTile

Terrain Tile은 Tile Core의 모든 Cell에 다음 논리 사실을 제공한다.

| 사실 | 용도 |
|---|---|
| Terrain Pattern 및 기본 표면 | Terrain Map 표시와 최종 지형의 기본값 |
| Terrain 상세/경사/형상 입력 | Basin 성장, River의 국소 지형 회피 |
| Sea 판정 입력 Field | Hydrology의 Sea Feature 해석 |

Terrain Tile은 Seed와 절대 좌표의 순수 평가 결과다. Hydrology를 읽거나 Chunk 상태를
읽지 않는다.

### HydrologyPatternTile

Hydrology Tile의 기본값은 `None`이다. Water 영향 Cell은 다음 논리 값을 조회 가능해야
한다.

| 사실 | 용도 |
|---|---|
| WaterType, FeatureKey | Water 종류와 안정적인 소유 식별 |
| ground, water surface | ChunkData의 지형·WaterCell 생성 |
| interior/boundary influence | 경계 전이, 디버그 표시, profile 재현 |

이는 Cell마다 완전한 FeatureKey와 float 묶음을 복제해야 한다는 뜻이 아니다. 물리 저장은
`None` 기본 참조, Tile-local Feature Table, Cell별 compact sample 참조로 구현한다.
그러나 위 논리 값은 어떤 소비자도 재계획 없이 정확히 얻을 수 있어야 한다.

### Combined Pattern

```text
Terrain cell only       → Terrain 기본 표면
Terrain + Hydrology cell → Hydrology의 ground, surface, WaterType 적용
```

Combined Pattern에는 세 번째 Feature, 별도 캐시, 별도 Noise, Debugger 전용 합성 규칙을
추가하지 않는다.

## Tile, Feature, 출력 범위

`PatternTileKey`는 절대 Tile 좌표다. Tile의 Cell 크기는 설정의
`PatternTileChunkSpan × ChunkCellCountXZ`으로만 정한다. 따라서 Tile과 Chunk 경계는
항상 정렬된다. 이 값은 코드 상수가 아니라 명시적인 월드 생성 설정이며, Tile 변경은
세계 생성 설정 변경이다.

`PatternTileKey` 자체에는 고정 월드 경계가 없다. Terrain 값은 Feature가 경계 밖에서도
절대 좌표로 직접 평가할 수 있다. 반면 `IsOutputAllowed`와
`EnumerateOutputIntersecting`은 고정맵의 출력 Core Tile 범위만 판정한다. Hydrology 출력
Tile, ChunkData, Renderer, Debugger가 요청하는 Core Tile은 이 범위 밖에 만들지 않는다.

Hydrology Field evaluator가 Tile Core 주변에서 직접 평가할 후보 범위는 다음 기하학 상한으로
유도한다.

```text
Core
+ Basin 최대 반경과 최소 이격 거리
+ River 최대 길이
+ River 최대 폭/제방 범위
+ Terrain 회피 Corridor 반경
= 유한 후보 열거 반경
```

모든 항목은 Cell 단위의 유한 설정이다. Hydrology Field evaluator는 이 반경과 절대 좌표
후보 격자만으로 FeatureKey를 직접 열거하고, Tile은 Core에 걸리는 확정 도형만 기록한다.
Tile이 Feature를 소유하거나, 자신의 경계에서 Feature를 새로 분할하거나, Feature 준비
의존성을 만들지 않는다.

## Water Feature 해소

### 안정적인 FeatureKey

FeatureKey는 실행 중 증가하는 ID가 아니다.

```text
FeatureKind
+ candidate lattice의 절대 소유 좌표
+ world seed
+ Feature별 seed salt
```

으로 구성한다. FeatureKey만으로 후보 위치·기본 도형·profile·AABB를 재현할 수 있어야
한다. 후보 격자 간격, 최대 반경과 최대 길이는 모두 설정으로 제한한다.

### Sea, Lake, Pond

1. Sea Feature는 Terrain Tile의 Sea 판정 입력을 읽어 Sea 영역과 동일한 전역 수면,
   S자형 해저 profile을 해소한다.
2. Lake/Pond 후보는 절대 후보 격자와 FeatureKey에서 결정한다. Terrain/Potential을 읽어
   자유로운 불규칙 footprint, 수면, 바닥/경계 profile을 만든다.
3. Basin 후보는 Sea 영역에서는 후보가 되지 않는다. 이는 Sea 위 Lake/Pond를 예외적으로
   막는 규칙이 아니라, Basin이 육지 Terrain에서만 정의된 Feature라는 도메인 계약이다.
4. 두 Basin 후보의 footprint 또는 최소 이격 범위가 충돌하면 FeatureKey 기반의 안정적
   우선순위가 낮은 후보 전체를 비활성화한다. 재배치·재시도·요청 순서 의존은 없다.

이 해소는 한 FeatureKey마다 한 번만 이루어진다. Tile Drawing은 Basin 후보를 경쟁시키지
않고 이미 활성인 Basin 도형만 그린다.

### River 기본 Stroke와 지형 회피

River는 먼저 FeatureKey에서 branch 없는 양방향 기하학 도형을 만든다.

```text
River FeatureKey
  → anchor, 기본 방향, 길이, 폭, 깊이, 수면, 곡선
  → Terminus A/B를 갖는 기본 Stroke
  → bounded Terrain-aware Stroke
```

`Terminus A/B`는 흐름의 시작/끝이나 미리 정한 연결 Endpoint가 아니다. 단지 Stroke의
두 기하학 끝점이다.

지형 회피는 A* 또는 전역 Route 탐색으로 구현하지 않는다. 기본 Stroke의 정규화된 샘플
순서마다 설정된 회피 Corridor 안의 유한 후보 위치를 평가하고, Terrain의 고도 변화,
횡단 경사, Mountain/Canyon 성질, 변위 비용을 합산해 가장 낮은 비용을 선택한다.
동점은 후보의 고정 순번으로 정한다. 선택된 점을 연속 곡선으로 보간해 최종 Stroke를
만든다.

후보 배치, sample spacing, Corridor 반경, 각 비용 Curve는 모두 설정으로 명시한다.
따라서 이 과정은 River 전체 도형의 Feature 해소 안에서 한 번만 수행되며, Chunk/Tile마다
경로를 재탐색하지 않는다.

### Natural Endpoint

현재 재설계 범위의 River Terminus는 모두 Natural Endpoint다. `EndpointTransitionLength`와
Rate Curve `0 → 1 → 2 → 2 → 1`의 적분값으로 폭·깊이·수면·지면을 감쇠시켜 자연스럽게
종료한다. `2`는 폭/깊이 배율이 아니라 전이 변화 속도다.

Terminus Join 후보 탐색·선택, JoinRange, River–River Junction/JoinFeature, Water 도형
접속 전이는 현재 범위에 포함하지 않는다. 이는 독립 River와 Basin/Sea 상호작용을 완성한 뒤
별도 확장 설계로 추가한다.

### River–Basin 상호작용

이는 전역 Graph 규칙이 아니라 Hydrology Feature Drawing 규칙이다. 확정 Basin/Sea 영역과
교차하는 River 구간은 Basin/Sea가 지면·수면의 소유자다. 교차 구간의 River Corridor는
그 영역을 덮거나 다시 절삭하지 않는다. Basin 경계에 닿은 인접 River 구간은 Basin 접속
전이로 끝난다.

이 처리는 Tile Raster를 먼저 그리고 Pixel로 판단하는 것이 아니다. 확정된 Feature 도형의
교차를 해소한 뒤, Tile은 그 결과를 잘라 그린다. 따라서 River가 Basin 내부를 보이거나,
Tile 경계마다 다른 방식으로 잘리지 않는다.

## Hydrology Tile Drawing과 합성

Hydrology Tile Builder는 Core의 `None` buffer에 이미 확정된 Feature만 투영한다.

```text
Sea/Basin Feature Drawing
  → River Stroke Drawing
  → Basin/Sea 경계 전이 Drawing
  → HydrologyPatternTile
```

Builder는 후보 생성, Basin 경쟁, A*, Endpoint Graph, 새로운 River 생성, 요청 범위 전체
Raster를 수행하지 않는다. 겹침의 최종 소유권은 Feature 해소에서 결정된 안정적인
FeatureKey 규칙과 위 Basin/River 상호작용 규칙만 사용한다.

ChunkData 생성은 자신과 교차하는 Pattern Tile을 읽고 Combined Pattern을 Cell 단위로
구체화한다. 여기서만 terrain column, target ground, WaterCell(Source), ChunkData를
만든다. Pattern Tile을 다시 Drawing하거나 Hydrology Feature를 해소하지 않는다.

## Streaming과 Debugger

```text
StreamingTarget
  → Update / Render / Prepare Demand
  → persistent PatternMapStore
      → TerrainPatternTile seal
      → Terrain Map을 읽는 HydrologyPatternTile seal
  → Render ChunkData materializer
  → Renderer
```

- `UpdateRange <= RenderRange <= PrepareRange`는 설정 계약이다. 현재 기본값은 각각
  `5`, `7`, `10` Chunk다.
- Prepare는 Pattern Map만 확장한다. Render는 seal된 Terrain/Hydrology Tile pair를 읽어
  ChunkData·Renderer를 만들고, Update는 활성 Chunk의 Water/Entity simulation만 갱신한다.
- Pattern Map은 Runtime 수명 동안 보존되는 원본 데이터다. Target 변경, Render 해제,
  Debugger 닫힘이 이미 seal된 Tile을 제거하거나 다시 계산하게 해서는 안 된다.
- Sea/Basin/River 후보, FeatureKey, 도형, Basin/Sea 상호작용은 Demand·Tile·요청 순서와
  독립적으로 Field evaluator가 직접 결정한다.
- 새 Render Chunk는 자신의 seal된 Core Tile pair만 materialize한다. 전체 Prepare 또는
  Debugger 수요의 완료를 기다리지 않는다.
- 고정맵은 출력 Hydrology Tile·ChunkData·Renderer를 위한 Core Demand만 유효 범위 안에서
  만든다. 경계 밖 Terrain 값은 Feature evaluator가 직접 읽을 수 있지만, 그 위치에
  Hydrology 출력 Tile·ChunkData·Renderer를 만들지 않는다.

Pattern Debugger는 별도 Preview World, Tile builder, ChunkData를 생성하지 않는다.

```text
선택 영역 → Terrain/Hydrology Tile 읽기
  → Terrain Map / Hydrology Map / Combined Map 선택
  → Palette를 적용한 256 × 256 Texture
```

- Map Level은 Chunk 하나가 최소 `2 × 2` Pixel이 되도록 시작하고, 최대 level에서는
  Chunk XZ Cell과 Pixel이 1:1이 된다.
- 중간 level의 Pixel은 하나의 절대 Cell을 샘플한다. 영역 집계나 River 강제 표시는 없다.
  축소 상태에서 얇은 River가 보이지 않을 수 있는 것은 의도된 샘플링 결과다.
- Debugger는 선택 영역의 **Map 전용 Prepare demand**를 Coordinator에 추가하고, 같은
  PatternMapStore에서 seal된 Terrain/Hydrology Tile을 읽는다. 이 수요는 ChunkData를
  만들지 않는다.
- Texture, Palette, 선택 표시, Streaming Target 재배치는 표시 계층이다. Pattern Tile의
  생성 규칙·의미 데이터에는 영향을 주지 않으며, Map 전용 demand만 추가할 수 있다.

## 금지되는 구조

- Endpoint를 먼저 골라 River를 Route하는 전역 Graph/A* 생성기
- Request/Scope/Batch/Snapshot 전체를 sealed한 뒤 Chunk를 만드는 경로
- Chunk, Debugger Pixel, Renderer가 수문 Feature를 계획하거나 Tile을 직접 생성하는 경로
- 동일한 절대 Feature를 Tile/Chunk/Debugger별로 다시 만들거나, Tile 경계에서 Feature 도형을
  새로 분할·결정하는 구조
- 세 번째 Combined 원본 Map, Debug Texture를 데이터 원본으로 취급하는 구조
- 이전 결과를 유지하기 위한 fallback, Adapter, 임시 생성 차단 조건, 숨은 재시도,
  시간/용량 기반 Tile 보존 규칙

## 적용 로드맵

각 단계는 컴파일 오류 없이 끝낸다. 테스트 전용 코드나 fixture는 만들지 않는다. 실제
월드 결과·성능·스트리밍·Debugger 검증은 사용자가 Unity에서 수행한다.

### 1. 기존 생성 구조 전면 제거

기존 월드 생성과 수문 계획에 속하는 모든 구현, 직렬화 설정, 호출 경로를 제거한다.
여기에는 Batch/Scope/Snapshot, Topology/Endpoint/Graph/Route, 이전 Raster, 이전 Feature
캐시, Preview 전용 생성기, 호환 Adapter와 fallback이 포함된다.

유지 대상은 최종 `ChunkData` 소비, Mesh/Render Object 생성, WorldData 적용, WaterSystem,
절대 좌표 기본형처럼 새 생성 결과를 소비할 수 있는 하위 계층뿐이다. 기존 생성 경로를
살리기 위한 빈 구현이나 임시 World 생성은 만들지 않는다.

#### 1단계 완료 기록

- 기존 Terrain/Hydrology 생성기, Graph/Route/Batch/Scope/Snapshot/Raster,
  FeatureWorld, 기존 streaming coordinator, Pattern Debugger/Palette/진단 코드를
  제거했다.
- 이전 생성 설정 ScriptableObject와 수문 설정 데이터, 이전 WorldData 저장/로드
  codec·asset·controller, 이전 생성 진행 UI를 제거했다. 저장/로드는 8단계에서 새
  ChunkData 형식으로만 복원하며, 이전 저장 형식은 읽거나 변환하지 않는다.
- Scene에서 이전 Generation, Save, streaming controller, Pattern Debugger,
  generation progress component 참조를 제거했다.
- `WorldSettingsData`에는 WorldData/Renderer/WaterSystem이 소비하는 월드 크기,
  Chunk 크기, 렌더 Patch, Road, WaterFlow 규칙만 남겼다. Terrain/Hydrology Pattern
  설정은 2단계의 새 의미 계약으로 다시 정의한다.
- `WorldRuntime`은 더 이상 생성 작업, Pattern Feature, 기존 Chunk demand를 소유하지
  않는다. 따라서 6단계 전에는 실행 가능한 월드 생성 경로가 없다. 이는 fallback이나
  임시 빈 월드가 아닌, 이전 생성 결과가 새 구조에 섞이지 않도록 한 전면 제거의 결과다.
- Unity C# 컴파일과 `MiniCivilization.World.Editor.csproj` 컴파일은 오류 없이
  완료됐다. 현재 프로젝트 자체 경고와, 후속 streaming 단계가 다시 구동할 Runtime
  상태 이벤트의 미발행 경고는 남아 있다.

### 2. 새 의미 계약과 Tile 좌표

PatternTileKey, FeatureKey, Terrain/Hydrology 논리 Cell, compact Tile 저장 형식과 설정의 유한
기하학 범위를 구현한다.
이 단계는 아직 지형/수문 결과를 만들지 않는다.

#### 2단계 완료 기록

- `Generation/Semantic`에 절대 `PatternTileKey`/bounds, Terrain·Hydrology 논리 Cell,
  tile-local Feature Table, Combined 즉시 합성 계약을 추가했다.
- `PatternTileGridSettingsData`는 Terrain에서도 독립적으로 쓸 Tile span을, 완전한
  `PatternTileSettingsData`는 Feature 영향 범위를 명시 입력으로 받는다. 고정 월드의
  Tile/world 경계 불일치는 자동 보정하지 않고 생성 전 검증으로 드러낸다.
- 아직 evaluator, scheduler, Runtime 연결, Tile 결과 Cache는 없다. 새 Tile span과
  Feature reach 수치를 코드 기본값으로 정하지 않았으므로, 3·4단계의 실제 Terrain/
  Hydrology 설정과 동시에 명시한다.
- 삭제된 설정 asset의 Pattern 디자인 수치와 이관 경계는
  `19-stage-2-semantic-contract.md`에 기록했다.

### 3. TerrainPatternTile 구현

절대 좌표 기반 Terrain evaluator와 Terrain Tile 준비를 구현한다. 기존 지형 Pattern과
Height Field의 **디자인 규칙**만 새 순수 evaluator로 옮긴다. Terrain Map 소비 계약도
이 단계에서 확정한다.

#### 3단계 완료 기록

- `SemanticTerrainPatternSettings.asset`에 기존 Terrain Pattern 디자인 수치만 새
  설정 원본으로 이관했다. `PatternTileChunkSpan`은 1 Chunk로 명시했고, Sea 수면/
  해저와 Hydrology 수치는 이 Terrain 설정에 섞지 않았다.
- `TerrainPatternEvaluator`는 Settings·Seed·절대 XZ만으로 Region, land Form,
  base/detail surface, primary Sea region key와 interior progress를 평가한다. Terrain은 Hydrology나 Chunk/Session 상태를
  읽지 않는다.
- `TerrainPatternTileBuilder`는 Tile Core와 순수 평가 halo만 사용해 slope를 완성한다.
  아직 이를 호출하는 scheduler나 Runtime 경로는 없다.
- 상세 이관 기준과 검증 범위는 `20-stage-3-terrain-pattern-tile.md`에 기록했다.

### 4. Hydrology Feature 해소 구현

Sea, Lake/Pond, branch 없는 River 기본 Stroke, bounded Terrain 회피, Natural Endpoint,
River–Basin/Sea 상호작용을 독립 FeatureKey 기반 Field evaluator로 구현한다. 전역 Endpoint Graph,
Route 탐색, 요청 범위 계획은 추가하지 않는다.

#### 4단계 재시작 계약

- 기존 Resolver 초안과 `targetGroundHeight`/`bedHeight` Pattern 계약은 폐기했다.
- `FeatureDescriptor → 닫힌 의존성 준비 → Feature 해석` 구조도 폐기했다. 이는
  FeatureKey를 생성 원본과 계획 그래프로 바꾸므로 Terrain 기반 Feature Drawing과 맞지 않는다.
- FeatureKey는 도형의 안정적인 식별자일 뿐이다. Basin은 자기 주변의 유한한 절대 후보를
  직접 평가해 활성 여부를 정하고, River는 Natural Endpoint까지 독립 Geometry를 정한다.
- Terrain Pattern Tile은 Terrain 값의 전달·재사용 단위일 뿐, Hydrology Feature 해석 전에
  준비해야 하는 의존성은 아니다. Feature는 같은 절대 좌표 Terrain 평가기를 직접 사용한다.
- EndpointTransitionLength는 Natural profile 전이 길이다. River 수면/지면과 실제 Basin/Sea
  교차의 4단계 계약·구현 순서는 `21-stage-4-hydrology-feature-contract.md`가 정한다.
- Terminus Join, JoinRange, River–River Junction/JoinFeature와 Water 도형 접속 전이는 현재
  로드맵 완료 뒤 별도 확장으로만 다룬다.

### 5. HydrologyPatternTile Drawing과 Combined Pattern

Feature 후보 열거, Hydrology Tile Drawing, Tile 경계 연속성, Terrain+Hydrology
합성 규칙을 구현한다. 이 단계부터 어떤 Tile도 Feature를 새로 결정하지 않고 확정
Feature를 읽어 Drawing한다.

#### 5단계 완료 기록

- `HydrologyPatternTileBuilder`는 Field evaluator가 Core bounds에서 한 번 열거한 Geometry만
  Cell별로 rasterize한다. Tile은 Basin 경쟁, River 생성, Join/Junction, 요청 범위 계획을 하지
  않는다.
- 고정 월드의 Terrain/Hydrology 출력 Tile은 `IsOutputAllowed` 범위 안에서만 생성한다. 경계
  밖 절대 Terrain/Hydrology는 Feature 평가 입력으로만 남는다.
- Hydrology Tile은 tile-local Feature table과 최종 Water/ground/surface/influence만 가진다.
  Combined Pattern은 Terrain/Hydrology Tile pair를 즉시 조합하며, 세 번째 원본 Map이나 Cache는
  만들지 않는다.
- 상세 Cell 소유권과 컴파일 범위는 `22-stage-5-hydrology-pattern-tile.md`에 기록했다.

### 6. ChunkData와 Streaming 전환

Chunk materializer를 Pattern Tile 소비자로 구현하고, Runtime을 출력 ChunkDemand와 작업
순서만 관리하는 Streaming coordinator로 전환한다. 결과 Cache·취소·공유 수명은 Field와
Tile Drawing을 생성 원본으로 바꾸지 않는 운영 계층으로만 추가한다. Render와 WaterSystem은
새 ChunkData를 그대로 소비한다. 고정맵은 유한 Demand 범위로 같은 경로를 사용한다.

#### 6단계 완료 기록

- `SemanticWorldSettings`가 WorldData 크기·WaterFlow·Streaming과 Terrain/Hydrology Pattern
  source를 하나의 명시적 입력으로 만든다. Tile span과 Pond 분류 기준의 불일치는 생성 전에
  오류로 드러난다.
- `PatternStreamingCoordinator`는 Target의 output ChunkDemand만 계산한다. 순수 Tile 작업은
  제한된 동시 작업 수 안에서 백그라운드로 준비되고, Tile 완료 후에만 메인 스레드가 ChunkData를
  materialize한다. Target 변경은 비참조 Tile 작업을 취소하고 결과를 버리며, 겹치는 Demand의
  Tile과 WorldData는 재생성하지 않는다.
- `PatternChunkMaterializer`는 Terrain/Hydrology Tile pair를 즉시 조합해 final ground와
  Source WaterCell을 WorldData로 투영한다. Hydrology Feature 재평가, Combined 원본 Tile,
  Chunk별 Hydrology 생성은 없다.
- 기존 CellData 높이 표현에는 ground floor / water surface ceiling 투영을 사용한다. 실제
  수면이 지면보다 높은 Hydrology Cell은 Source WaterCell을 잃지 않는다.
- Runtime은 Chunk의 prepare/active/release와 Renderer/WaterSystem 상태 이벤트만 담당한다.
  WaterSystem에는 새 Source와 공유 Chunk 경계의 WaterCell만 전달하며, 전체 높이 Column을
  재계산 입력으로 보내지 않는다.
- 저장/로드가 아직 없으므로 Demand 밖 Chunk의 Renderer·Runtime cache만 해제하고 WorldData는
  유지한다. 이는 8단계의 변경 Chunk 저장/해제 정책 전까지의 명시적 메모리 제약이다.
- C# Runtime·Editor 컴파일은 오류 없이 완료했다. 실제 생성·스트리밍·수문·Renderer 결과는
  7단계 Debugger 전환 뒤 사용자 환경에서 검증한다.

### 7. Pattern Debugger와 실제 검증

Debugger를 Terrain/Hydrology/Combined Tile 소비자로 전환하고 256×256
Map Level, 선택 영역, Streaming Target 재배치를 연결한다. 사용자는 초기 생성, 작은/큰
Target 이동, 이동 중 재이동, Terrain/Hydrology/Combined Map 일치와 메모리 수명을 실제
환경에서 검증한다.

7단계에서 결과 계약과 성능을 확인한 뒤에도 이전 생성 구조는 되살리지 않는다. 결함은
이 문서의 Feature/Tile/Runtime 책임 안에서 원인을 분리해 수정한다.

#### 7단계 완료 기록

- `WorldTerrainPatternDebugger`와 전용 Editor가 삭제된 Preview/Batch/Snapshot 경로 없이
  Semantic Terrain/Hydrology Tile pair를 읽는다.
- Map은 고정 `256 × 256` Texture, Chunk당 `2` Pixel부터 Cell 1:1 Level까지의 표시 단계,
  Terrain/Hydrology/Combined Palette를 제공한다.
- Map request는 필요한 TileKey를 deduplicate하고 설정된 Tile 동시 준비 수 안에서 중심 우선
  비동기 작업으로 처리한다. 이 작업은 Runtime active cache, ChunkData, Renderer, WaterSystem을
  공유하거나 변경하지 않는다.
- Target은 빨간 5 Pixel 십자로 표시되고, 선택 영역 이동은 Transform만 변경해 다음 Runtime
  frame의 정상 Streaming 요청으로 이어진다.
- 상세 계약과 사용자 실제 검증 항목은 `24-stage-7-semantic-pattern-debugger.md`에 기록했다.

### 8. 새 저장/로드 구조 복원

7단계의 실제 생성·스트리밍·Debugger 검증이 끝난 뒤에만 새 저장/로드를 구현한다.

```text
World metadata (seed + 새 Settings)
+ 변경된 ChunkData
+ WaterSystem 진행 상태
→ 새 저장 형식

저장 Chunk 존재 → Load
저장 Chunk 없음 → Pattern Tile 기반 Generate
```

이전 `WorldDataAsset`, 단일 전체 월드 codec, 이전 저장 파일, 마이그레이션과 호환
Adapter는 복원하지 않는다. 저장/로드는 새 ChunkData와 Runtime 상태를 소비할 뿐,
Terrain/Hydrology Feature나 Pattern Tile을 계획하지 않는다.
