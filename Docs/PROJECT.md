# 프로젝트 현황

## 목적

Mini Civilization은 Minecraft식 청크 기반 셀 월드, WorldBox식 자율 엔티티 시뮬레이션, Crusader Kings III식 엔티티·세력·사건 개입을 지향하는 Unity 프로젝트다.

개발은 먼저 최소 토대를 만들고, 대표 샘플로 검증한 뒤, 같은 종류의 콘텐츠를 기반 코드 수정 없이 추가할 수 있는 생산 파이프라인을 만든 후 확장한다.

## 실행 환경

- Unity 6000.3.11f1, Universal Render Pipeline
- 주요 Scene: `Assets/Content/Scenes/Main Scene.unity`(실행), `Assets/Content/Scenes/Edit Scene.unity`(엔티티 authoring 확인)

## Assets 구성

- `Assets/Scripts`: 프로젝트 C# 코드. 현재 월드 시스템은 `Assets/Scripts/World` 아래에 있다.
- `Assets/Content`: Scene, 모델, 폰트, Prefab, Material, Texture, Shader, Animator Controller, ScriptableObject 설정을 종류별로 보관한다.
- `Assets/TextMesh Pro`: Unity가 제공한 TextMesh Pro 리소스다.

## 월드 구조

월드는 Chunk로 분할되고, 각 Chunk는 Cell과 세분화된 Filled 정보를 가진다. `WorldData`는 Chunk·Cell·엔티티·저장 대상 물 스케줄을 보유하는 영속 데이터다. `WorldRuntime`은 `WorldData`에 Surface/Navigation 캐시, Pattern Map 캐시, 물 시뮬레이션 상태, 엔티티 런타임과 도로·Waypoint 그래프를 조합한다.

```text
WorldGenerationSettings
→ Terrain / Climate / Hydrology Pattern Map
→ PatternChunkMaterializer
→ WorldData
→ WorldRuntime
→ Streaming · Editing · WaterFlow · Entity · Rendering
```

Terrain, Climate, Hydrology는 Pattern Tile로 생성된다. 생성·수문 세부 구현은 관련 실제 코드를 기준으로 확인한다.

## 주요 시스템

- **Generation**: Seed·설정·절대 좌표를 바탕으로 지형, 기후, 수문 Pattern Map을 만들고 Cell 데이터로 materialize한다.
- **Streaming**: `PatternStreamingCoordinator`가 Chunk의 준비, 활성화, 시뮬레이션 범위, 저장 후 언로드를 관리한다.
- **Editing**: Cell 및 엔티티 편집은 `WorldChangeSet`/`EntityChangeSet`을 통해 후속 시스템에 변경을 알린다.
- **WaterFlow**: Source/Dynamic 물과 별도의 런타임 물 상태를 관리하며, 활성화된 시뮬레이션 범위에서 증분 계산한다.
- **Meshing / Presentation**: Terrain·Water 메시, 렌더 patch, 선택과 정보 UI, 엔티티 시각 표현을 담당한다.
- **Entities**: `EntityCatalog → EntityDefinition → EntitySystem → EntityRuntime → WorldEntityRenderer` 구조다. `EntitySystem`은 월드 단위 등록·인덱스·활성 Chunk tick을 관리하고, 개체별 `EntityRuntime`은 `EntityData`와 `EntityFSM`을 묶는다. 논리·저장 사실은 `EntityData`, 행동과 진행 상태는 계열·종류별 FSM, Animator와 시각 이동은 `EntityView` 및 Render Profile이 담당한다.
- **Persistence**: `SaveLoadManager`와 `WorldPersistenceService`가 월드 메타데이터 및 region 기반 Chunk 저장을 관리한다. 새 영속 상태가 추가되면 Save/Load도 같은 변경 범위에서 확장한다.

## 변경 전파

`WorldManager`는 런타임을 생성하고 Editing, WaterFlow, Renderer, EntityManager, Persistence를 연결한다. 월드 편집과 물 계산 결과는 ChangeSet으로 전달되어 렌더링, 저장 dirty 처리, 필요한 내비게이션·Waypoint 갱신을 유도한다.

## Entity 실행 구조

계열 Root와 Placement Preview Root는 Main Scene에 사전 배치하고 Renderer의 직렬화 참조로 연결한다. Root가 없거나 부모 관계가 잘못되면 Bind 시 오류를 보고하며 런타임에 대체 Root를 생성하지 않는다. 활성·비활성 Host는 같은 계열 Root에 유지하며 `Active`, `Pooled Views`, `Pooled Models` 하위 Root를 사용하지 않는다.

건물 등록 실패 복구는 해당 개체 ID가 소유한 Cell·Terrain Anchor만 제거한다. FSM 종료 또는 View 해제가 실패한 Runtime은 오류를 보고하고 격리하여 재사용하지 않는다. 격리 슬롯은 해당 EntitySystem 수명 동안 유지하며 자동 재시도하지 않는다.

`WorldRuntime.Entities`는 월드 전체의 `EntitySystem`이다. `EntityTypeRegistry`는 TypeKey별 `EntityDefinition`을 보유한다. 신규 생성 시 Definition이 개체별 속성을 포함한 `EntityData`를 만들고, 저장된 데이터 복원 시에는 같은 Definition이 개체별 FSM을 만든다. Registry는 데이터와 FSM을 `EntityRuntime` 슬롯에 연결한다. EntitySystem은 활성 simulation Chunk에 속하면서 tick이 필요한 슬롯만 갱신한다.

```text
EntitySystem.Tick
→ EntityRuntime.Tick
→ EntityFSM.Tick
→ EntitySystem을 통한 이동·인덱스·ChangeSet 반영
→ WorldEntityRenderer / EntityView 표현 갱신
```

`EntityFSM`은 공통 기반인 `FixedEntityFSM`, `DynamicEntityFSM`과 계열 기반인 `AnimalEntityFSM`, `HumanEntityFSM`, `NatureEntityFSM`, `BuildingEntityFSM`으로 나뉜다. 구체 sealed FSM은 독자적인 상태와 전이를 가지며, Animal 계열은 공통 Cell 이동·진입 검사·결정론적 난수를 공유한다.

현재 Animal 기준 행동은 `AnimalRoamingFSM`이다. Dog와 Deer는 서로 다른 TypeKey·Definition·Prefab을 사용하지만 같은 Roaming FSM 구현과 필요에 따라 공유하거나 분리할 수 있는 이동·판단 Profile을 사용한다. 같은 행동을 쓰는 Animal은 고유 종류 ID의 `EntityDefinition`, `AnimalRoamingFSMDefinition` 설정, 표현 Prefab을 만들고 Animal Catalog에 등록하면 된다. Runtime·World 핵심 코드와 FSM 구현은 수정하지 않는다. 독자 행동이 필요한 종류만 별도 FSM 구현 및 설정으로 확장한다.

`EntityDefinition`은 안정적인 종류 ID·계열과 공유 생성 설정을 소유한다. 이름 후보, 나이 범위, FSM Weight 등에 사용할 숫자 Trait 범위를 보유하고 표현 Prefab과 `EntityFSMDefinition`을 연결한다. 신규 생성에서 범위를 개체별 실제 값으로 확정하여 `EntityData.Attributes`에 저장한다. `EntityFSMDefinition`은 종류 식별자가 아니라 특정 FSM을 만드는 데 필요한 Profile·논리 설정만 소유한다. FSM은 같은 Definition을 공유하더라도 각 개체의 Data와 Attributes를 읽으므로 독립적으로 판단할 수 있다.

건물 점유 Cell·Terrain Anchor·Waypoint·Way 베이크 결과는 `BuildingLayoutDefinition`이 소유한다. `HouseFSMDefinition`은 이 논리 Definition에서 `BuildingLayout`을 만들며 `BuildingEntityView`는 레이아웃을 소유하거나 제공하지 않는다. `BuildingWayAuthoring`은 기존 편집 Scene의 Marker·Cell 입력을 유지하면서 결과를 Building Layout 에셋에 기록한다.

`EntityRuntime`은 사용 중 하나의 `EntityData`와 하나의 `EntityFSM`을 연결하는 종류 비종속 실행 슬롯이다. 각 연결에는 증가하는 binding version이 있어 tick 목록에 남은 이전 슬롯 참조를 같은 슬롯의 새 개체와 구분한다. EntitySystem은 제거 시 ID·위치·Chunk·tick·이동 계획·건물 인덱스를 해제하고 FSM 종료, Renderer의 View 연결 해제, Data·FSM 비우기 순서가 끝난 뒤 슬롯을 반환한다. 생성·복원 또는 등록이 실패하면 그 작업이 추가한 WorldData와 인덱스를 되돌리고 확보한 슬롯을 같은 반환 경로로 회수한다. 다음 생성에서는 Entity 종류와 무관하게 빈 슬롯에 새 Data와 새 FSM을 연결한다. 저장 시 TypeKey·위치·방향·이름·나이·Trait와 FSM payload·목표 ID·활성 이동 계획을 방어적으로 복사한 스냅샷으로 남기며, Load는 저장된 실제 속성을 사용하고 Definition 범위에서 다시 추첨하지 않는다. 이 Entity 저장 형식은 생성 버전 15부터 유효하며 이전 버전 저장은 자동 이관하지 않는다.

`WorldEntityRenderer`는 EntityRoot 아래 `Human Root`, `Animal Root`, `Nature Root`, `Building Root`에서 계열별 View Host를 관리한다. 반환 시 개체 연결과 표현 상태를 초기화하고 모델·Profile·Animator 연결은 유지한 채 Host를 비활성화한다. 대여는 같은 표현 Prefab을 가진 비활성 Host를 우선하고, 없으면 같은 계열의 다른 Host를 사용하며 모델만 제거·교체한다. 비활성 Host가 없을 때만 공통 GameObject와 View 컴포넌트를 생성한다. 별도 모델 풀은 사용하지 않는다. Renderer Unbind 시 보관 Host와 부착 모델을 함께 제거한다.

기존 Definition의 표현 Prefab과 Authoring 참조는 유지한다. 런타임에는 Prefab 전체가 아니라 `CellScaleRoot` 표현 하위 구조만 복제하므로 모델에 중복 EntityView가 생성되지 않는다. 표현 하위 구조에는 EntityView를 넣지 않는다. 원래 LocalMotionRoot의 계층·배율을 보존하고 Model 교체 시 Profile·Animator를 새 모델에 연결한다. 동일 모델 재사용을 포함한 매 대여 시 Animator를 초기화한다.

`Placement Preview Root`는 EntityRoot의 형제다. 사용 중 Preview Host만 이 Root로 이동하며 영역 축소·선택 종료·도구 변경 시 모델을 붙인 채 해당 계열 Root의 풀로 반환한다. Preview 소유 목록은 즉시 비워진다. View Host 풀과 Runtime 슬롯 풀은 독립적이며 FSM 객체 자체는 풀링하지 않는다. 표시 해제는 개체 삭제가 아니며 영속 데이터와 저장 계약을 변경하지 않는다.

이동 중인 Dynamic Entity의 청크 참조는 Anchor뿐 아니라 MoveFrom·MoveTo와 Way 경로가 통과하는 청크를 포함한다. 이동 시작 시 참조를 확장하고 완료 후 도착 위치 기준으로 축소한다. 이동 시작은 모든 필요 청크가 로드된 경우에만 허용된다. 저장 복원도 FSM payload를 먼저 임시 복원해 같은 참조 집합을 계산하고, 모든 청크가 준비된 뒤 실제 Runtime을 등록한다. 저장된 좌표나 Way 경로가 월드 범위를 벗어나면 미로드 상태로 취급하지 않고 손상된 상태로 거부한다.
