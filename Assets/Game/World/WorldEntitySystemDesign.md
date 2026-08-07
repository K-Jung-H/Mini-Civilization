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
└─ EntityData 목록
   ├─ EntityId
   ├─ EntityTypeId
   ├─ AnchorCell
   ├─ Direction
   └─ 실제 타입에 필요한 상태값

WorldRuntime
└─ EntityRuntime
   ├─ EntityId → sealed Entity
   ├─ World Cell → 모든 EntityId 목록
   ├─ BuildingCellIndex
   └─ TerrainAnchorIndex
```

`EntityTypeRegistry`는 `EntityTypeId`와 sealed 엔티티 타입을 연결한다. 타입별 공간 규칙, 행동, 배치 검증은 실제 sealed 타입이 가진다.

엔티티마다 MonoBehaviour를 만들지 않는다. 점유 형태·앵커 좌표 목록·렌더링 중복 목록은 `WorldData`에 저장하지 않고, 타입 규칙과 `AnchorCell`로부터 런타임에 구성한다.

모든 엔티티는 같은 셀에 함께 존재할 수 있다. 기본 공간 인덱스는 `World Cell → EntityId 목록`이며, 같은 실제 `EntityTypeId`의 같은 셀 렌더링은 하나만 표시한다.

## 건물 구조

건물은 `BuildingEntity : FixedEntity`이며, 타입별 로컬 좌표계로 월드와 상호작용한다.

```text
BuildingLayout
├─ OccupiedCells[]
│  ├─ LocalOffset
│  └─ WalkLinks[]
├─ TerrainAnchorOffsets[]
└─ ValidatePlacement(context)
```

### OccupiedCells와 WalkLinks

`OccupiedCells`는 건물 자체가 존재하는 셀이다. 다른 건물의 점유 셀과 중첩될 수 없지만, 나무·동물·사람 같은 일반 엔티티와는 공존할 수 있다.

`WalkLinks`는 해당 건물 셀에서 연결 가능한 다음 셀의 로컬 오프셋이다. 링크가 없으면 경로로 진입할 수 없고, 링크가 있으면 엔티티가 그 건물 셀을 이동 경로로 사용할 수 있다.

```text
평면 다리: (+X), (-X), (+Z), (-Z)
사다리:    (+Y), (-Y)
계단:      (+X,+Y), (-X,-Y)
```

따라서 다리는 수면 Cell에 존재하면서도 `WalkLinks`를 통해 수면 위 이동 경로를 제공할 수 있다. 별도 WalkSurface 구조는 두지 않는다.

### TerrainAnchorOffsets

Terrain Anchor는 건물 부피에 포함되지 않는 외부 지형 셀이다. 건물을 유지하기 위해 보존해야 하는 지형을 뜻한다.

- 배치 시 타입이 요구하는 지형 조건을 검사한다.
- 배치 후 그 셀의 지형 제거·추가·높이 변경·물 Cell 전환을 막는다.
- 건물 렌더링·점유 충돌·이동 경로 대상은 아니다.
- Terrain Anchor도 건물끼리 중첩되지 않는다.

집은 아래 지면을, 절벽 건물은 옆 절벽을, 다리는 양 끝 지면을 Terrain Anchor로 사용할 수 있다.

### ValidatePlacement

`ValidatePlacement(context)`는 설치 시점의 월드 상태를 검사한다.

- 점유 셀과 Terrain Anchor의 중첩 여부
- 평지·절벽·지형 높이 조건
- 특정 상대 위치의 물 존재 여부
- 타입별 특수 설치 조건

물·절벽을 확인하는 좌표는 점유 셀이나 Terrain Anchor일 필요가 없다. 검증 로직이 필요한 월드 셀·방향을 직접 조회한다.

## 이동과 경로

동적 엔티티는 셀 단위로 이동한다.

- 목적지형: 경로를 따라 이동한다.
- 배회형: 주변 셀을 매번 판단하여 이동한다.

경로를 따라 이동할 때는 매 이동 직전에 다음 셀을 현재 월드 상태로 재검사한다.

```text
CanEnter(entity, currentCell, nextCell)
├─ 현재 지형·물 상태
├─ 일반 이동 규칙
└─ BuildingCell의 WalkLinks
```

진입할 수 있으면 이동하고, 불가능하면 기존 경로를 폐기한 뒤 현재 셀에서 다시 탐색한다. 건물 배치·제거와 편집으로 바뀐 지형·물 상태는 이 경로로 반영한다. 별도 경로 변경 ID는 사용하지 않는다.

## 애니메이션과 렌더링

엔티티는 논리 상태를 가지며, 표시 계층이 이를 애니메이션으로 변환한다.

- 애니메이션 없음
- Idle 단일 애니메이션
- 공통 `Idle`, `Move`와 타입별 상태를 사용하는 상태 기반 애니메이션

다중 셀 건물은 `AnchorCell` 기준으로 한 번만 렌더링한다. 동물·사람·나무는 현재 셀 기준으로 렌더링한다. 같은 셀에 같은 실제 `EntityTypeId`가 여러 개면 가장 작은 `EntityId` 하나만 표시하고, 다른 타입은 모두 표시한다.

### Prefab Controller

Unity Prefab은 순수 C# 엔티티와 별개다. `EntityController`는 연결된 엔티티의 ID·위치·방향만 표시하고, 각 Prefab은 정확히 하나의 계열 Controller를 가진다. 계열 Controller는 Inspector에서 정확히 하나의 sealed C# Entity 클래스를 연결한다. 따라서 실제 Entity 타입은 Definition이 아니라 Prefab의 Controller가 결정한다.

```text
Tree Prefab       → NatureEntityController   → NatureEntity
Goat Prefab       → AnimalEntityController   → AnimalEntity
Villager Prefab   → HumanEntityController    → HumanEntity
Building Prefab   → BuildingEntityController → BuildingEntity
```

Controller는 자신에게 연결된 sealed 엔티티만 `Bind`한다. `WorldEntityRenderer`는 Catalog에서 Type ID별 Definition·Prefab을 조회하고, `EntityChangeSet`이 발생한 셀만 다시 확인해 Prefab 생성·제거·위치 갱신을 처리한다. Controller 자체는 이동·배치 규칙을 갖지 않는다.

### Entity Catalog

`EntityCatalog`은 상위 SO이며, Nature·Animal·Human·Building `EntityDefinitionContainer` SO를 각각 참조한다. 각 Container는 `List<EntityDefinition>`에 Definition SO를 직접 연결한다. 각 `EntityDefinition`은 Prefab·Thumbnail·Entity Name만 보관한다.

- `EntityTypeId`는 Catalog가 Container 순서와 Definition List 순서로 런타임에 자동 부여한다. Inspector에서 직접 입력하거나 Controller가 보관하지 않는다.
- Catalog는 같은 Definition·sealed Entity 클래스의 중복, 계열과 맞지 않는 Controller Prefab을 검증 실패로 처리한다.
- Catalog는 UI Button·Panel 같은 Scene 참조를 갖지 않는다.
- `EntityManager`는 활성 `EntityRuntime`·Catalog·Renderer를 Bind/Unbind하고 Entity 변경을 WorldManager에 전달한다.
- `WorldEntityCatalogView`는 계열 버튼 → Catalog Definition 목록 → Details Panel의 선택 상태와 생성 버튼만 관리한다. `EntityEditController`는 현재 EditSelected 영역에서 지면 Top Surface Cell만 선별해 EntityRuntime의 `Create`·`Add`를 호출한다.
- `EntityTypeRegistry`는 Catalog의 내부 타입 연결값으로 Type ID와 sealed Entity 생성 함수를 구성한다.

## 변경 통지

엔티티 변화는 지형·물 변화와 별도 계약으로 전달한다.

```text
EntityChangeSet
├─ 추가 · 제거 · 이동 EntityId
└─ 영향받은 월드 셀 · 청크
```

`EntityRuntime`이 공간 인덱스를 먼저 갱신하고, `WorldEntityRenderer`는 영향 셀의 Entity View만 다시 확인한다. 지형 Mesh를 다시 생성하지 않는다.

## 구조 적용 로드맵

### 1. 엔티티 도메인 계약 추가

- `EntityId`, `EntityTypeId`, `EntityData` 추가
- `WorldData`에 엔티티 목록 추가
- `EntityTypeRegistry`와 sealed 엔티티 생성 계약 추가
- `WorldDataValidator`에 ID·타입·AnchorCell 검증 추가

이 단계에서는 엔티티 생성·표시·이동 기능을 추가하지 않는다.

### 2. EntityRuntime과 수명 경로 추가

- `WorldRuntime.CreatePrepared`에서 `EntityRuntime` 구성
- ID 조회와 `World Cell → EntityId 목록` 인덱스 구성
- 엔티티 추가·제거·이동의 단일 변경 경로 구성
- `EntityChangeSet`으로 영향 셀·청크 통지

`WorldData`가 사실을 소유하고, `EntityRuntime`은 언제든 `WorldData`로부터 다시 구성 가능해야 한다.

### 3. BuildingLayout과 배치 검증 추가

- `BuildingEntity`, `BuildingLayout`, `OccupiedCells`, `TerrainAnchorOffsets` 추가
- `AnchorCell`·회전 기준으로 로컬 좌표를 월드 셀로 변환
- 기존 건물 점유·Terrain Anchor와의 중첩을 배치 전에 거부
- `ValidatePlacement(context)`를 통해 타입별 설치 조건 검사
- `BuildingCellIndex`, `TerrainAnchorIndex`를 런타임에 구성

점유 셀·Terrain Anchor·설치 조건 좌표는 저장 데이터에 복제하지 않는다.

### 4. 이동 가능성 질의 통합

- 기존 지형·물 기반 NavigationCache를 입력으로 사용
- `CanEnter`에서 일반 지형 이동 규칙과 BuildingCell의 `WalkLinks` 결합
- 목적지 경로 이동은 다음 셀 진입 직전에 재검사하고, 실패 시 현재 위치에서 재탐색

이 단계에서 건물은 경로를 차단하거나, 다리·계단처럼 새 경로 연결을 제공할 수 있다.

### 5. 편집·렌더링·상호작용 연결

- 지형 편집 전에 TerrainAnchorIndex를 확인해 보호 대상 변경 거부
- 엔티티 변경은 EntityChangeSet으로 영향 청크만 렌더 갱신
- 다중 셀 건물은 AnchorCell 기준 한 번만 렌더링
- 같은 셀·같은 실제 타입은 가장 작은 EntityId 하나만 렌더링

### 6. 실제 sealed 타입 적용

- 첫 NatureEntity, BuildingEntity, AnimalEntity, HumanEntity를 실제 sealed 타입으로 추가
- 각 타입의 로컬 배치 규칙, WalkLinks, 이동·상태 규칙을 해당 sealed 타입에만 구현

공통 기반에는 실제 타입에 필요하지 않은 기능을 미리 추가하지 않는다.
