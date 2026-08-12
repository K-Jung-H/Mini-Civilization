# 월드 엔티티 시스템 설계 및 적용 로드맵

## 목적

월드 엔티티는 sealed 실제 타입의 다형성으로 행동을 결정한다. `WorldData`에는 저장되는 사실만 두고, 공간 점유·경로·렌더링용 상태는 `WorldRuntime`에서 파생한다.

이 문서는 현재 확정된 엔티티 구조와 그 적용 순서만 기록한다. 동물·사람의 개별 행동, 생산, 체력 등 기능별 규칙은 이 문서의 범위가 아니다.

## 엔티티 상속 구조

```text
abstract Entity
├─ abstract FixedEntity
│  ├─ abstract NatureEntity
│  │  └─ sealed TreeEntity
│  └─ abstract BuildingEntity
│     └─ sealed HouseEntity, BridgeEntity, ...
└─ abstract DynamicEntity
   ├─ abstract AnimalEntity
   │  └─ sealed GoatEntity, PigEntity, ...
   └─ abstract HumanEntity
      └─ sealed VillagerEntity, ...
```

- 실제 월드에 생성되는 타입은 최하위 sealed 타입뿐이다.
- `AnimalEntity`와 `HumanEntity`는 서로 상속하지 않는다.
- `NatureEntity`와 `BuildingEntity`는 서로 상속하지 않는다.
- `FixedEntity`는 위치가 고정되고, `DynamicEntity`만 셀 단위로 이동한다.

## 저장 데이터와 런타임 상태

```text
WorldData
├─ CellData
│  └─ RoadData
│     ├─ RoadType
│     └─ CrossesCenter
└─ EntityData 목록
   ├─ EntityId
   ├─ EntityTypeKey(Category + 계열 enum 값)
   ├─ AnchorCell
   ├─ Direction
   └─ 실제 타입에 필요한 상태값

WorldRuntime
├─ EntityRuntime
│  ├─ EntityId → sealed Entity
│  ├─ World Cell → 모든 EntityId 목록
│  ├─ BuildingCellIndex
│  └─ TerrainAnchorIndex
└─ WorldWayPointGraph
   ├─ Positions[]
   ├─ NeighborOffsets[]
   └─ Neighbors[]
```

`EntityTypeRegistry`는 `EntityTypeKey`와 Controller가 제공한 sealed 상태머신 생성 함수를 연결한다. 타입별 공간 규칙, 행동, 배치 검증은 실제 sealed 타입이 가진다.

모든 엔티티는 고유 `EntityController`를 가진다. 점유 형태·앵커 좌표 목록·Building 내부 Way·렌더링 중복 목록은 `WorldData`에 저장하지 않고, 타입 규칙·Prefab의 Bake 데이터·`AnchorCell`로부터 런타임에 구성한다. Road의 WayPoint와 Way 역시 `WorldData`에 저장하지 않고 `RoadData`와 주변 상태로부터 파생한다.

모든 엔티티는 같은 셀에 함께 존재할 수 있다. 기본 공간 인덱스는 `World Cell → EntityId 목록`이다. 같은 Cell·`EntityTypeKey`·렌더 상태·방향이면 Controller는 모두 유지하고 대표 `VisualRoot` 하나만 출력한다.

## 건물 구조

건물은 `BuildingEntity : FixedEntity`이며, 타입별 로컬 좌표계로 월드와 상호작용한다.

```text
BuildingLayout
├─ BuildingCells[]
│  └─ LocalOffset
├─ TerrainAnchorCells[]
├─ LocalWayPoints[]
│  ├─ LocalCellOffset
│  ├─ LocalPosition
│  └─ 외부 연결 정보
├─ LocalWays[]
│  ├─ PointA
│  ├─ PointB
│  └─ OneWay
└─ ValidatePlacement(context)
```

### BuildingCells와 내부 Way

`BuildingCells`는 건물 자체가 존재하는 셀이다. 다른 건물의 Building Cell과 중첩될 수 없지만, 나무·동물·사람 같은 일반 엔티티와는 공존할 수 있다. Building의 로컬 중심 Cell은 항상 `(0,0,0)`이며 `BuildingCells`에 포함되어야 한다.

Building 내부 이동은 Cell 중심을 연결하는 `WalkLinks`로 표현하지 않는다. Prefab에 Bake된 `LocalWayPoints`와 `LocalWays`가 진입 위치, 내부 동선, 대기 위치와 퇴장 위치를 정의한다. WayPoint는 Cell 중심을 통과할 필요가 없으며, 하나의 Building이 차지하는 여러 Cell에 자유롭게 배치할 수 있다.

- 내부 Way는 기본적으로 양방향이다.
- 특수한 경우에만 `OneWay=true`로 Bake한다.
- 외부 연결 정보가 있는 WayPoint만 주변 Road 또는 다른 Building과 연결될 수 있다.
- Building Cell에 진입한 동물·사람은 Cell Center나 자유 이동을 사용하지 않고 Bake된 Way 위에서만 이동한다.
- Way 또는 연결 가능한 외부 WayPoint가 없으면 해당 Building Cell에는 진입할 수 없다.

Building의 월드 WayPoint 위치는 `AnchorCell`, `Direction`, `CellSize`와 Prefab의 로컬 좌표를 조합해 런타임에 계산한다. 내부 WayPoint와 Way 구성은 Prefab에 고정되지만, Road·다른 Building과의 외부 연결은 주변 상태에 따라 런타임에 구성한다.

### TerrainAnchorCells

Terrain Anchor는 건물 부피에 포함되지 않는 외부 지형 셀이다. 건물을 유지하기 위해 보존해야 하는 지형을 뜻한다.

- 배치 시 타입이 요구하는 지형 조건을 검사한다.
- 배치 후 그 셀의 지형 제거·추가·높이 변경·물 Cell 전환을 막는다.
- 건물 렌더링·점유 충돌·이동 경로 대상은 아니다.
- Terrain Anchor도 건물끼리 중첩되지 않는다.

### Entity Authoring Cell 다형성과 Building 역할

`EntityAuthoringCellBox`는 로컬 Cell 좌표·지형 높이·Editor 시각화만 공통으로 관리한다. Entity 계열별 Cell 제약은 파생 타입이 소유한다.

```text
EntityAuthoringCellBox
├─ BuildingEntityAuthoringCellBox
│  └─ BuildingRole
├─ AnimalEntityAuthoringCellBox      // 필요할 때 추가
└─ NatureEntityAuthoringCellBox      // 필요할 때 추가
```

Building Authoring은 `EntityAuthoringSystem.CellBoxPrefab`에 `BuildingEntityAuthoringCellBox` Prefab을 연결해야 한다. 각 Building Cell Box에서 역할을 직접 지정한다.

```text
BuildingRole
├─ None
├─ Building
└─ TerrainAnchor
```

- 역할은 자동 할당하지 않는다.
- `Building`으로 지정한 Cell Box만 `BuildingCells`로 Bake한다.
- `TerrainAnchor`로 지정한 Cell Box만 `TerrainAnchorCells`로 Bake한다.
- Center Cell `(0,0,0)`이 `Building`이 아니면 Bake를 거부한다.

집은 아래 지면을, 절벽 건물은 옆 절벽을, 다리는 양 끝 지면을 Terrain Anchor로 사용할 수 있다.

### 배치 높이와 지형 보정

- Building의 로컬 `(0,0,0)` Cell이 대응하는 월드 지면의 실제 `GroundHeight`를 Center 기준 높이로 사용한다.
- 같은 로컬 Y 층의 Building Cell 지면은 Center 기준 높이와 동일하게 맞춘다. 한 XZ 열에 Building Cell이 여러 개면 최하단 Cell만 지형 보정에 참여하고, 위 Cell은 건물 영역으로만 사용한다.
- 지형은 Center 기준 높이에 맞춰 상승하거나 하강할 수 있다.
- 각 열의 현재 지면 높이와 목표 높이 차이가 `BuildingEntityController.MaxTerrainCorrectionSteps`를 넘으면 배치할 수 없다.
- Terrain Anchor는 건물의 바닥을 뜻하지 않는다. 아래·옆·위 어느 위치에도 둘 수 있으며, 배치 결과에서 지정된 Cell이 완전히 Filled 상태여야 한다.
- 지형 보정, Road 제거와 Building 추가는 배치 가능 판정이 끝난 뒤에만 적용한다. 호버 미리보기는 같은 판정 결과를 사용하지만 월드 데이터는 변경하지 않는다.
- Building이 차지하는 XZ 열의 Road는 배치 시 World Edit 경로로 제거한다. `EntityRuntime`은 Road를 직접 수정하지 않는다.

### ValidatePlacement

`ValidatePlacement(context)`는 설치 시점의 월드 상태를 검사한다.

- Building Cell과 Terrain Anchor의 중첩 여부
- 평지·절벽·지형 높이 조건
- 특정 상대 위치의 물 존재 여부
- 타입별 특수 설치 조건

물·절벽을 확인하는 좌표는 Building Cell이나 Terrain Anchor일 필요가 없다. 검증 로직이 필요한 월드 셀·방향을 직접 조회한다.

## Road와 공통 Way 구조

Road는 Biome이나 `TerrainData.Surface`가 아니다. 지형 위에 설치되는 별도의 Cell 사실 데이터이며, 한 지면 Cell에는 하나의 Road만 존재할 수 있다.

```text
CellData.Road
├─ RoadType
└─ CrossesCenter
```

- `RoadType`은 Dirt, Stone처럼 설치된 Road의 종류를 나타낸다.
- `CrossesCenter`는 해당 Cell의 내부 Way가 Center를 경유하는지를 나타낸다.
- 연결 방향, 이웃과의 높이 관계, 활성 WayPoint와 Way는 주변 상태로부터 계산한다.
- Road 종류별 별도 `RoadDefinition`은 두지 않는다.
- 지형의 실제 표면 높이를 사용하므로 높이 관계를 Road 저장 데이터에 중복 저장하지 않는다.
- Road 연결에 허용되는 최대 높이 단계 차이는 `WorldGenerationSettings.RoadMaxHeightSteps`로 관리한다. 생성 시 `WorldSettingsData`에 복사하며 Generate와 Load 이후의 런타임 연결 판정은 활성 월드의 저장된 설정값을 사용한다. Cell별 `RoadData`에는 이 값을 중복 저장하지 않는다.

### Road Way 생성

Road의 Center와 North·East·South·West 위치는 미리 생성된 런타임 WayPoint가 아니라 고정된 후보 위치다.

```text
인접 Road와 Building 외부 연결점 검사
        ↓
실제 표면 높이 차이 검사
├─ 허용값 초과 → 해당 방향 연결 제외
└─ 허용값 이하 → 연결 후보 유지
        ↓
실제로 연결되는 방향의 외곽 Point만 선택
        ↓
CrossesCenter = true
├─ Center Point 생성
└─ 활성 외곽 Point를 Center에 연결

CrossesCenter = false
└─ 두 활성 외곽 Point를 직접 연결
```

현재 구조에서 `CrossesCenter=false`는 두 외곽 Point를 직접 연결하는 경우에 사용한다. 세 방향 이상의 임의 내부 연결은 이 값만으로 형태를 확정할 수 없으므로 포함하지 않는다. 실제 Way가 사용하지 않는 후보 Point는 런타임 그래프에 생성하지 않는다.

### 경계 WayPoint 공유

인접 Cell의 두 후보가 같은 연결점을 뜻하면 WayPoint 두 개를 겹쳐 만들지 않고 하나의 런타임 Point를 공유한다.

```text
Road A의 East 후보 ─┐
                    ├─ 공유 경계 WayPoint
Road B의 West 후보 ─┘
```

공유 여부는 `Vector3`의 실수 비교가 아니라 Cell 경계·방향·경계 내부 위치를 포함한 구조적 Key로 결정한다. 같은 월드 위치라도 연결 조건이 맞지 않으면 합치지 않는다.

Road끼리 높이가 다르더라도 실제 표면 높이 차이가 공통 허용값 이하라면 양쪽 Cell이 공유하는 경계 Point 하나를 생성한다. 공유 Point의 Y를 하나의 연결 높이로 결정하고 양쪽 Road의 내부 Way를 이 Point에 연결해 수직 단차가 없는 연속 경사를 만든다. 높이 차이가 허용값을 초과하면 경계 Point와 외부 Way를 모두 생성하지 않는다. 두 Cell의 `RoadData`는 유지되지만 서로 연결된 Road로 취급하지 않는다.

높이 차이는 실수 월드 좌표가 아니라 지형의 0.2 높이 단계 단위로 비교한다. Building의 외부 WayPoint도 Road와 연결될 때 같은 Road 높이 제한을 통과해야 한다. Building Prefab에 명시적으로 Bake된 내부 Way는 이 제한의 대상이 아니다.

### Building 배치와 Road 대체

Building이 점유하는 XZ 범위에 Road가 있으면 Building 배치 성공 시 해당 지면 Cell의 `RoadData`를 제거한다. Road 제거와 Building 추가는 하나의 변경으로 적용한다.

```text
Building 배치 조건 검증
        ↓ 성공
점유 XZ 범위의 RoadData 제거
        ↓
기존 Road 파생 Way 제거
        ↓
Building과 Bake된 내부 Way 등록
        ↓
주변 Road·Building 외부 Way 재연결
```

배치 검증이 실패하면 Road와 Way 그래프를 변경하지 않는다. 제거된 Road를 숨겨 보관하지 않으므로 Building을 제거해도 기존 Road는 자동 복원되지 않는다.

### 압축된 런타임 그래프

Prefab의 Bake 데이터는 편집을 위해 Point 간 연결과 단방향 여부를 보관할 수 있지만, `WorldWayPointGraph`는 `From`, `To`, `Direction`을 가진 Way 객체를 보관하지 않는다.

```text
WorldWayPointGraph
├─ Positions[]
├─ NeighborOffsets[]
└─ Neighbors[]
```

Point `i`의 이웃은 `NeighborOffsets[i]`부터 `NeighborOffsets[i + 1]` 전까지다.

```text
양방향 A ↔ B
├─ A의 Neighbors에 B 등록
└─ B의 Neighbors에 A 등록

단방향 A → B
└─ A의 Neighbors에 B만 등록
```

따라서 런타임 방향은 별도 필드가 아니라 인접 목록에 어느 연결이 존재하는지로 표현한다. Road 규칙과 Building Bake 데이터는 그래프 구성 과정에서 동일한 Point·인접 관계로 변환되고, 사용되지 않는 Point는 최종 배열에 포함하지 않는다.

## 이동과 경로

동적 엔티티는 셀 단위로 이동한다.

- 목적지형: 경로를 따라 이동한다.
- 배회형: 주변 셀을 매번 판단하여 이동한다.

경로를 따라 이동할 때는 매 이동 직전에 다음 셀을 현재 월드 상태로 재검사한다. Building이 없는 Cell은 기존 지형·물·Road 이동 규칙을 사용한다.

```text
CanEnter(entity, currentCell, nextCell)
├─ 현재 지형·물 상태
├─ 일반 이동 규칙
└─ BuildingCell의 외부 WayPoint 연결 여부
```

Building이 점유한 Cell은 일반 Cell 이동으로 진입할 수 없다. 엔티티는 진입 방향과 연결된 Building 외부 WayPoint의 실제 월드 좌표에 먼저 도달해야 하며, 도달한 뒤에만 내부 Way로 진입한다. 내부에서는 Bake된 Way를 따라 이동하고, 다른 외부 WayPoint를 통해 퇴장한다.

```text
일반 Cell 또는 Road Way
        ↓
Building 외부 WayPoint까지 접근
        ↓
Building 내부 Way 진입
        ↓
Bake된 Way를 따라 이동
        ↓
외부 WayPoint를 통해 퇴장
```

연결 가능한 외부 WayPoint가 없으면 해당 방향에서는 Building Cell에 진입할 수 없다. Building 내부의 Idle·배회·상호작용 이동도 Cell Center나 자유 좌표를 사용하지 않고 내부 WayPoint와 Way 위에서만 수행한다.

다음 진입이 불가능하면 기존 경로를 폐기한 뒤 현재 위치에서 다시 탐색한다. Building 배치·제거와 편집으로 바뀐 지형·물·Road 상태는 이 경로로 반영한다. 별도 경로 변경 ID는 사용하지 않는다.

## 애니메이션과 렌더링

엔티티는 논리 상태를 가지며, 표시 계층이 이를 애니메이션으로 변환한다.

- 애니메이션 없음
- Idle 단일 애니메이션
- 공통 `Idle`, `Move`와 타입별 상태를 사용하는 상태 기반 애니메이션

다중 셀 건물은 `AnchorCell` 기준으로 한 번만 렌더링한다. 동물·사람·나무는 현재 셀 기준으로 렌더링한다. 같은 Cell·`EntityTypeKey`·렌더 상태·방향의 정지 엔티티는 가장 작은 `EntityId`의 `VisualRoot`만 출력한다. 상태가 다르거나 이동 중이면 각각 출력한다.

### Prefab Controller

Unity Prefab은 순수 C# 엔티티와 별개다. `EntityController`는 연결된 엔티티의 ID·위치·방향·상태를 표시하고, 각 Prefab은 정확히 하나의 계열 Controller를 가진다. 계열 Controller는 Inspector의 계열별 enum으로 정확히 하나의 sealed C# 상태머신 타입을 선택한다. 따라서 실제 Entity 타입은 Definition이 아니라 Prefab의 Controller가 결정한다.

```text
Tree Prefab       → NatureEntityController(Tree)   → TreeEntity
Dog Prefab        → AnimalEntityController(Dog)    → DogEntity
Human Prefab      → HumanEntityController(Human)   → HumanEntity
House Prefab      → BuildingEntityController(House) → HouseEntity
```

Controller는 enum에 대응하는 `CreateStateMachine` 생성 함수를 Registry에 제공하고, 같은 `EntityTypeKey`의 sealed 엔티티만 `Bind`한다. `WorldEntityRenderer`는 Catalog에서 Type Key별 Definition·Prefab을 조회하고, `EntityChangeSet`이 발생한 셀만 다시 확인해 Prefab 생성·제거·위치 갱신을 처리한다. Controller 자체는 이동·배치 규칙을 갖지 않는다.

### Entity Catalog

`EntityCatalog`은 상위 SO이며, Nature·Animal·Human·Building `EntityDefinitionContainer` SO를 각각 참조한다. 각 Container는 `List<EntityDefinition>`에 Definition SO를 직접 연결한다. 각 `EntityDefinition`은 Prefab·Thumbnail·Entity Name만 보관한다.

- `EntityTypeKey`는 Controller의 계열과 계열별 enum 값으로 결정된다. Catalog 목록 순서와 무관하다.
- Catalog는 같은 Definition·`EntityTypeKey`의 중복, 계열과 맞지 않는 Controller Prefab을 검증 실패로 처리한다.
- Catalog는 UI Button·Panel 같은 Scene 참조를 갖지 않는다.
- `EntityManager`는 활성 `EntityRuntime`·Catalog·Renderer를 Bind/Unbind하고 Entity 변경을 WorldManager에 전달한다.
- `WorldEntityCatalogView`는 계열 버튼 → Catalog Definition 목록 → Details Panel의 선택 상태와 생성 버튼만 관리한다. `EntityEditController`는 현재 EditSelected 영역에서 지면 Top Surface Cell만 선별해 EntityRuntime의 `Create`·`Add`를 호출한다.
- `EntityTypeRegistry`는 Catalog가 전달한 Type Key와 Controller의 sealed 상태머신 생성 함수를 보관한다.

## 변경 통지

엔티티 변화는 지형·물 변화와 별도 계약으로 전달한다.

```text
EntityChangeSet
├─ 추가 · 제거 · 이동 EntityId
└─ 영향받은 월드 셀 · 청크
```

`EntityRuntime`이 공간 인덱스를 먼저 갱신하고, `WorldEntityRenderer`는 영향 셀의 Entity View만 다시 확인한다. 지형 Mesh를 다시 생성하지 않는다.

## Road 시각화와 Building Way Authoring

### Road 시각화

Road는 지형 Mesh의 정점이나 UV를 Road 폭에 맞춰 다시 분할하지 않는다. 활성 Road Way를 폭이 있는 별도 Strip Mesh로 변환하고, Road가 존재하는 Render Patch에만 Road Mesh를 생성한다.

```text
RoadData + WorldSurfaceQuery
        ↓
RoadTopologyResolver
├─ WorldWayPointGraph 구성 정보
└─ RoadChunkMesh 구성 정보
```

Road 경로와 시각화가 서로 다른 이웃 연결을 계산하지 않도록 동일한 `RoadTopologyResolver` 결과를 사용한다.

- 직선과 대각선 Way는 폭이 있는 Strip으로 생성한다.
- `CrossesCenter=true`인 Cell은 활성 Strip과 Center Junction 면을 함께 생성한다.
- 높이 차이가 `RoadMaxHeightSteps` 이하인 Road끼리는 공유 경계 Point 하나를 사용한다.
- Road끼리의 공유 경계 Y는 양쪽 표면 높이의 중간값으로 계산해 두 Cell Center 사이에 연속 경사를 만든다.
- Road와 Building의 공유 경계 Y는 Building 외부 WayPoint의 Bake된 Y를 사용한다.
- 높이 차이가 `RoadMaxHeightSteps`를 초과하면 그래프 연결과 Road Strip을 모두 생성하지 않는다.
- Road Mesh UV는 폭 방향 `0~1`, 진행 방향 누적 거리로 구성한다.
- 하나의 Road Material과 Texture Array를 사용하고 `RoadType`을 Texture Layer 선택값으로 전달한다.
- Road 폭과 지형 겹침 방지 Offset은 Presentation 설정이며 `RoadData`나 `WorldWayPointGraph`에 저장하지 않는다.

Road 추가·제거와 이웃 연결 변경 시 변경 Cell, 인접 Cell과 해당 Render Patch의 Road Mesh만 다시 구성한다. Building이 Road를 대체하면 `RoadData` 변경 결과에 따라 Road Mesh가 제거되고 Building 외부 WayPoint까지의 남은 Road만 표현한다.

### Building 내부 Way 편집

Way Marker는 Pooling되는 `EntityAuthoringCellBox`의 자식으로 두지 않는다. `EntityAuthoringSystem` 아래의 별도 Marker Container에 배치한다.

```text
Entity Authoring System
├─ Cell Boxes
├─ Entity Preview Scale Root
│  └─ Entity Prefab Instance
└─ Way Marker Container
   ├─ Marker A
   ├─ Marker B
   └─ Marker C
```

`EntityAuthoringSystem`에 원본 Entity Prefab을 연결하면 하위에 Preview Instance를 생성한다. Preview Scale Root만 `WorldGenerationSettings.CellSize`로 균일 확대하며 Prefab Root와 그 하위 배치 Transform은 변경하지 않는다. 런타임에서도 동일하게 Entity Root는 기본 Transform을 유지하고, `CellScaleRoot` 하위의 `VisualRoot`만 현재 World의 `CellSize`를 따른다.

각 `BuildingWayPointMarker`가 자신에서 출발하는 Connection 목록을 관리한다. `BuildingWayAuthoring`은 Marker Container를 순회해 이 연결을 Bake한다.

```text
BuildingWayAuthoring
└─ MarkerContainer
   ├─ Marker A
   │  └─ Connections[]
   │     ├─ Target = Marker B
   │     └─ OneWay = false
   └─ Marker B
```

- Marker Transform은 위치만 표현하고 Cell Offset과 Cell 내부 위치를 사람이 중복 입력하지 않는다.
- Marker의 외부 연결 방향은 `None`, `North`, `East`, `South`, `West` 중 하나로 명시한다.
- Bake 시 Marker 위치를 `EntityAuthoringSystem` 기준으로 변환해 `LocalCellOffset`과 CellSize에 독립적인 정규화 `LocalPosition`을 계산한다.
- Marker가 `BuildingCells` 영역 밖에 있으면 가장 가까운 Building Cell의 내부 또는 경계로 재배치하고, 이동 전·후 위치와 대상 Cell을 로그로 남긴다.
- Connection은 출발 Marker에서 Target Marker를 직접 참조하며 위치 근접으로 자동 추론하지 않는다.
- 기본 Connection은 양방향이므로 한쪽 Marker에만 등록한다. 특수한 경우에만 `OneWay=true`로 설정하며, 이때 현재 Marker가 From이다.
- Link 선은 Editor `Handles`로만 시각화하고 `LineRenderer`나 런타임 GameObject를 만들지 않는다.
- Bake 시 중복 Link, 자기 자신 연결, Container 외부 Marker 참조, 외부 방향과 Cell 경계 불일치를 검증한다.
- Editor Marker와 선택 상태는 런타임 Prefab 데이터로 사용하지 않고 Bake 결과인 `LocalWayPoints[]`, `LocalWays[]`만 Building Layout에 보관한다.

## Way 구조 적용 로드맵

### 1. 설정과 저장 사실 적용

- `WorldGenerationSettings`에 0.2 높이 단계 단위의 `RoadMaxHeightSteps` 추가
- `WorldSettingsData`와 현재 Save/Load에 값을 포함해 Generate·Load가 같은 값 사용
- `CellData`에 `RoadData(RoadType, CrossesCenter)` 추가
- Road의 파생 Way와 Building의 Bake 데이터를 `WorldData`에 복제하지 않음
- Building 인스턴스는 기존 `EntityTypeKey`, `AnchorCell`, `Direction`만으로 Layout을 복원

### 2. Building Way Authoring과 Bake 적용

- `BuildingLayout`의 기존 `WalkLinks`를 `LocalWayPoints`, `LocalWays`로 교체
- `EntityAuthoringSystem` 아래에 별도 Marker Container 연결
- Marker Transform과 외부 방향, Marker별 Target 참조 기반 Connection 편집 구조 추가
- `Handles`로 양방향 선과 단방향 화살표 시각화
- Marker 좌표를 `LocalCellOffset`, 정규화 `LocalPosition`으로 Bake
- Bake 검증 후 Building Prefab Layout에 결과 저장

### 3. Road Topology 계산 통합

- Road 주변 상태에서 활성 WayPoint 후보와 내부 연결 계산
- 실제 표면 높이 차이가 `RoadMaxHeightSteps`를 넘는 방향은 연결 후보에서 제외
- Road끼리는 양쪽 표면 높이의 중간값으로 공유 경계 Y 계산
- Road와 Building은 Building 외부 WayPoint의 Y를 공유 경계 높이로 사용
- `CrossesCenter`에 따라 Center 경유 또는 두 외곽 Point 직접 연결
- 그래프와 시각화가 동일한 `RoadTopologyResolver` 결과 사용

### 4. 공통 그래프 구성

- Building Bake 데이터를 월드 Point와 내부 연결로 변환
- 구조적 경계 Key로 Road·Building의 동일 경계 Point 공유
- 양방향은 양쪽 인접 관계, 단방향은 한쪽 인접 관계로 변환
- 사용되지 않는 Point를 제외하고 `Positions`, `NeighborOffsets`, `Neighbors` 구성

### 5. Building 배치 트랜잭션 적용

- Building 배치 조건을 먼저 검증
- 성공한 경우에만 점유 XZ 범위의 `RoadData` 제거
- Road 파생 Way 제거와 Building 내부 Way 등록을 같은 변경으로 처리
- 주변 Road·Building 외부 연결을 다시 구성
- 실패 시 Road, Building, Way 그래프를 모두 기존 상태로 유지

### 6. 이동 경로 통합

- Building Cell을 일반 Cell 진입 대상에서 제외
- 연결 가능한 외부 WayPoint까지 도달한 뒤 내부 Way로 진입
- Building 내부 이동·Idle·상호작용 위치를 Bake된 Way로 제한
- 외부 WayPoint를 통한 퇴장 연결
- 다음 이동 연결이 사라졌으면 현재 위치에서 경로 재탐색

### 7. Road 시각화 연결

- 지형 Mesh와 분리된 Road Strip Mesh 생성
- Center Junction, 직선, 대각선과 높이 경사 Mesh 구성
- RoadType을 Texture Array Layer로 전달하는 단일 Material 적용
- Road 변경 Cell과 인접 Cell이 포함된 Render Patch만 갱신
- Building 배치로 제거된 RoadData와 그래프 연결을 Road Mesh에 반영

### 8. 통합 확인

- 같은 높이 Road 연결
- `RoadMaxHeightSteps` 이하 경사 연결과 연속 Mesh
- 허용값을 초과한 Road 사이의 Way·Mesh 미연결
- Center 경유와 Center 비경유 Road
- Building 배치 성공 시 점유 범위 Road 제거
- Building 외부 WayPoint가 없는 방향의 진입 차단
- 외부 WayPoint 도달 후 내부 Way 이동과 퇴장
- 양방향 기본 Link와 특수 단방향 Link
- Generate와 Save/Load 이후 동일한 Road 설정·연결 결과

각 단계는 위 항목을 직접 확인한 뒤 다음 단계로 진행하며, 문서에서 확정하지 않은 Road 기능이나 Building 이동 규칙은 추가하지 않는다.
