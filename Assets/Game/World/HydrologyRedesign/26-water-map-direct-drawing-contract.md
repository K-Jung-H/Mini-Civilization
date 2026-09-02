# WaterMap 직접 Drawing 계약

## 목적

WaterMap 생성은 TerrainMap을 입력으로 하는 단순한 Pattern Drawing이다. 수문 계획,
전역 Graph, 요청 범위 Snapshot, 경로 탐색은 WaterMap 생성의 입력이 아니다.

```text
절대 좌표 TerrainMap
  → Sea / Basin / River 도형 직접 평가
  → WaterMap Tile 기록
  → ChunkData가 TerrainMap + WaterMap을 소비
```

`PatternMapStore`에 seal된 TerrainMap과 WaterMap은 Runtime 동안 남는다. Target 이동은
새 Prepare 범위의 Tile을 추가할 뿐, 이미 seal된 Tile을 삭제하거나 다시 계산하지 않는다.
Store 수명과 Range는 `25-pattern-map-store-streaming-contract.md`를 따른다.

## 생성 원본

TerrainMap은 Seed, 설정, 절대 좌표만으로 결정되는 지형 원본이다. WaterMap은 같은 절대
좌표에서 이미 seal된 TerrainMap을 읽는다.

WaterMap Tile을 만들 때만 해당 Tile Core와 교차할 수 있는 유한한 Basin/River 후보를
직접 계산한다. 이 후보 목록은 Raster loop의 지역 입력일 뿐이며, 전역 Feature 저장소,
Feature 의존성 그래프, 요청별 계획, Snapshot이 아니다.

```text
WaterMap Tile Core
  → 교차 가능한 Basin 후보의 Seed·크기·도형 직접 평가
  → 교차 가능한 River 후보의 Seed·기본 Stroke 직접 평가
  → 각 Cell에서 Sea, Basin, River를 순서대로 Drawing
  → HydrologyPatternCell 기록 및 seal
```

동일 Seed, 설정, 절대 좌표의 TerrainMap과 WaterMap 결과는 Target, Tile 준비 순서,
Chunk 생성 순서, Debugger 접근 순서와 무관해야 한다.

## Brush와 Tile Painter

```text
후보 절대 좌표
  → WaterBrushFactory의 불변 Basin / River Brush
  → Runtime WaterBrushCatalog
  → WaterMapPainter가 새 Tile Core에 Rasterize
  → seal된 WaterMap Tile
```

- Brush는 후보 좌표·Seed·설정과 유한한 TerrainMap 입력으로 한 번 확정되는 도형 데이터다.
  Basin은 연결된 footprint, 일정 수면, 내부 거리와 해안 전이를 기록하고 River는 중심
  수면과 양쪽 제방으로 제한된 profile을 기록한다.
- 같은 `HydrologyFeatureKey`의 Brush는 Runtime 동안 한 번만 만들어 재사용한다. Catalog는
  Graph, Endpoint, 후보 경쟁, 요청별 계획을 보관하지 않는다.
- Tile마다 남는 것은 최종 `HydrologyPatternCell`뿐이다. Brush는 새 Tile이 처음 필요할 때
  그 Tile에만 그리며, seal된 Tile을 다시 칠하거나 변경하지 않는다.
- `HydrologyPatternDrawer`는 교차 후보 좌표를 열거하는 역할만 가진다. `WaterMapPainter`는
  Sea·Basin·River 우선순위와 Cell 기록만 가진다.

## Sea

- Sea는 TerrainMap의 primary·secondary Sea region key와 interior progress를 읽어 WaterMap에 기록한다.
- Sea 수면은 전역적으로 하나이며, interior progress와 S자형 깊이 Curve가 해저 깊이를 결정한다.
- Sea와 다른 지형 region의 경계에서는 두 도형의 groundHeight를 TerrainMap region influence로
  보간한다. 보간된 groundHeight가 Sea 수면보다 낮은 Cell만 Sea Water를 가진다. 따라서
  해안 transition은 물을 억지로 추가하지 않으며, Sea와 지형의 절삭·성토 결과가 같은 Cell
  groundHeight에서 연속된다.
- Sea 도형이 기록되는 Cell은 Basin과 River보다 우선한다.

## Basin

```text
Basin 후보 격자 좌표
  → Seed 기반 발생 여부
  → Seed 기반 목표 면적
  → 면적 기준 Pond 또는 Lake 분류
  → 최대 도달 범위 안에서 TerrainMap Potential·지형 변형·경사 비용이 낮은 이웃부터 확장
  → 목표 면적에 도달한 연결된 불규칙 도형 직접 확정
  → 후보 도형 내부의 절삭·성토·림 비용으로 일정 수면 선택
  → 내부 거리 기반 중앙 심도와 경계 전이 Drawing
```

- Lake와 Pond는 별도 생성기가 아니다. 하나의 Basin 규칙에서 목표 면적으로 분류한다.
- Basin 후보는 Tile마다 필요한 절대 좌표만 직접 평가한다.
- Basin은 전역 성장, 요청 범위 후보 할당, 최소 이격 reservation을 수행하지 않는다.
  수면 선택은 이미 확정된 단일 Brush footprint의 유한한 Cell만 읽는다.
- Basin이 Sea에 겹치면 Sea가 기록된다. Basin끼리의 겹침은 안정된 FeatureKey 순서로 하나만
  기록한다. 이 순서는 후보 준비 순서나 Tile 순서에 의존하지 않는다.

## River

```text
River 후보 격자 좌표
→ Seed 기반 발생 여부·Anchor·길이·곡선·폭·깊이
→ 독립적인 bounded 기본 Node 열
→ 각 Node의 유한한 절대 좌표 후보군
→ TerrainMap을 읽는 대칭 Terrain 보정
→ 최종 Node 열·Corridor Profile 확정
→ 양 Terminus의 Natural 전이와 WaterMap Drawing
```

- 기본 Node 열은 Seed, Anchor, 총 길이, 최초 방향, Node별 회전 성향으로 결정된다. 최초
  Node는 Anchor이고 이후 기본 Node는 직전 기본 Node의 기하 방향에서만 진행한다. 회전은
  `NodeTurnDegrees`보다 작으므로 인접 Node 방향을 역전하지 않는다. 이 과정은 Terrain을
  읽지 않는 Seed 도형 생성이며 Bézier·A*·경로 탐색이 아니다.
- Node는 Chunk·Tile에 속하지 않는 절대 좌표 도형이며 Endpoint Graph, 다른 River 경로,
  Basin 경쟁 결과를 읽지 않는다. Seed 길이에 도달하면 더 이상 Node를 만들지 않고 양 끝은
  Natural Endpoint가 된다.
- 각 기본 Node는 법선 방향의 `TerrainCorrectionRadiusCells` 안에서 유한한 절대 좌표 후보를
  가진다. 후보 비용은 급경사, 기본 Stroke 이탈, 인접 기준 높이 변화, 예상 Corridor
  절삭·성토량, 곡률 급변만 사용한다. 높은 고도 자체는 비용이 아니다.
- 보정은 이전 Node를 따라 다음 Node를 탐색하지 않는다. 모든 Node는 직전 전체 Node 상태만
  읽고, 명시된 `TerrainCorrectionSmoothingPasses` 횟수만 대칭적으로 갱신한다. 동점은 절대
  좌표로 해소한다.
- 최종 Node 열은 River Brush에 한 번 확정된다. 각 WaterMap Cell은 가까운 확정 Stroke 구간의
  폭·단면·깊이와 Natural 전이만 평가한다.
- River 수면은 최종 중심 TerrainMap 표면의 inset을 기준으로 하고 양쪽 제방의 완전 Cell
  높이를 넘지 않는다. 낮은 제방 구간은 `DropTransition` S자 전이로 인접 profile까지
  낮아진다. River 바닥은 이 수면과 Corridor 절삭 깊이 중 더 낮은 `groundHeight`다.
- Natural Endpoint는 합의된 `0 → 1 → 2 → 2 → 1` 적분형 전이로 폭·깊이·수면·Corridor를
  함께 dry Terrain까지 감쇠한다.
- River는 Sea나 Basin이 없는 Cell에만 기록한다. 이 단계는 River–Water Join을 만들거나,
  Cell/Chunk/WaterCell을 연결하지 않는다.
- River Join 규칙은 저장/로드와 함께 별도 확장 단계에서만 도입한다. 그 전에는 모든
  Terminus가 Natural 전이로 종료한다.

## Tile과 ChunkData의 경계

```text
PrepareRange
  → TerrainMap Tile seal
  → WaterMap Tile seal

RenderRange
  → seal된 두 Tile만 읽음
  → CellData, WaterCell(Source), Mesh, Render Object 생성
```

WaterMap Tile은 ChunkData·WaterCell·Mesh·Render Object를 만들지 않는다. ChunkData
materializer는 Pattern Map을 만들거나 Feature를 해석하지 않는다. Combined Map은 TerrainMap과
WaterMap을 소비자가 합성해 보는 관점이며 별도 원본 Tile로 저장하지 않는다.

## 명시적 제외

- Endpoint Graph, River Edge, A* Route, Junction, Join
- Hydrology Batch, Scope, Request-wide Snapshot, Feature Descriptor
- Basin 전역 성장 Queue, 경쟁 할당, 최소 이격 reservation
- River terrain avoidance, route 비용, 경로별 Topology 재생성
- ChunkData 또는 Debugger에서의 WaterMap 생성

이 항목은 WaterMap의 성능을 위해 생략하는 우회가 아니라, WaterMap의 원본 책임이 아닌
기능을 제거한 것이다. 이후 별도 기능으로 다시 도입하려면 이 계약을 변경하고, 지도
원본·수명·결정성에 미치는 영향을 먼저 확정해야 한다.
