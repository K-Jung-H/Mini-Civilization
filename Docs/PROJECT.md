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

## Main Scene UI의 현재 구조

`Assets/Content/Scenes/Main Scene.unity`의 `World System/World UI/Canvas`에 좌측 Workspace·SelectMode Palette, 우측 Simulation·Inspector Box 및 Workspace·Inspector Launcher를 uGUI Hierarchy로 직렬화해 배치한다. `MainSceneUIView`는 이 Scene 참조를 사용해 표시 상태와 데이터 기반 목록만 갱신하며 실행 시 고정 UI 구조를 생성하지 않는다. Workspace·SelectMode Palette·Simulation·Inspector와 Workspace·Inspector Launcher는 Canvas의 직접 자식이다. Simulation 확대 버튼은 Box 내부 X와 같은 위치에 있다. Box는 표시만 제어하며 상태 소유 컴포넌트는 Box 외부에 유지한다. 텍스트는 프로젝트 TMP 폰트를 사용한다. 고정 컨트롤은 씬에 저장하고 데이터 목록만 `Workspace Item.prefab` 인스턴스로 표시한다.

Workspace는 Entity / World Tab이 하나의 Palette를 공유한다. Entity는 Animal / Nature / Human / Building Catalog를 기존 EntityDefinition으로 구성한다. World는 Biome / Water / Terrain / Terraform / Road를 표시하며 현재 지원되는 Terraform과 Road만 기존 WorldEditAction으로 연결한다. 미지원 Category와 Rectangle은 비활성 상태로 표시한다. Tool Options는 Active Tool이 있을 때만 Single / Brush / 기존 3D Area와 필요한 Brush Size를 표시하며 Building의 Single 제한을 유지한다. 현재 Tab·Palette 항목·Interaction Mode·Brush Size는 선택 색상으로 구분한다. Tab·Category로 돌아가거나 이미 활성화된 Palette 항목을 다시 선택하면 도구가 해제된다. 항목 재선택은 현재 Category를 유지한다.

WorldEditToolState가 Active Tool·Mode·Brush Size를 직접 소유한다. WorldEditToolSnapshot의 지원 모드·Brush 옵션 규칙을 상태 검증과 UI가 함께 사용한다. Building은 Single만 허용하고 나머지 기존 도구는 Single/Brush/Area를 지원한다. Brush 크기는 Snapshot에서 1~3으로 제한하며 Brush 모드에서만 변경한다. MainSceneUIView는 명시적인 도구 선택 명령을 호출하며 Undo/Redo는 WorldEditApplyController에 연결한다. SelectMode Palette는 Workspace 밖의 독립 Box로 배치되어 Active Tool이 있을 때만 전체가 표시된다. Workspace를 접어도 도구가 활성화되어 있으면 유지되며 도구 활성 여부가 Workspace 크기를 바꾸지 않는다. Brush에서는 Brush Size 행과 간격만큼 SelectMode 높이를 늘리고 Single·Area에서는 줄인다. 상단·폭은 유지하며 확인 버튼은 하단을 따른다. Palette 제목 우측의 정사각 Back 버튼은 Category 안에서만 활성화한다. Palette와 Inspector는 세로 ScrollRect와 자동 숨김 Scrollbar를 사용한다. Entity·Road는 기존 Thumbnail을 연결하며 이미지가 없는 항목은 문자 기호를 표시한다. WorldInteractionController와 WorldEditInputController는 기존 EventSystem의 UI Pointer 차단, DDA Cell Picking, 편집 Preview·확인 경로를 유지한다.

Inspector는 펼쳐진 동안 0.2초 간격으로 선택 대상의 값을 확인하고 표시 데이터가 달라졌을 때 본문을 갱신한다. 선택·월드·관련 Entity 변경 및 재확장 시 Cell의 Entity ID 목록을 기존 EntitySystem 조회 경로에서 다시 확인하고 정렬한다. 0개면 Cell, 1개면 해당 Entity, 여러 개면 선택 목록을 표시하며 목록 행은 기존 항목 Prefab 인스턴스를 재사용한다. Entity Context는 Entity ID로 유지하고 EntityData의 이름·나이·Trait 및 Runtime의 방향·Activity를 읽는다. Inspector 표시를 접어도 선택은 유지되며 대상 삭제·언로드 후에는 유효성을 다시 확인한다.

`WorldInspectorView`는 Inspector의 Cell·Entity Context, 데이터 조회·표시, 목록 행 재사용, 독립 확장·축소와 관련 Scene 참조를 소유한다. `WorldUIManager`가 기존 선택 상태·정보 Provider·WorldManager를 연결한다. 컴포넌트는 Box 외부의 World UI에 있어 축소 중에도 변경 통지를 받는다. `MainSceneUIView`는 Workspace·SelectMode·Simulation을 담당하고 Simulation 높이 변경 시 Inspector에 상단 위치만 전달한다. 일반 Cell 선택과 월드 데이터의 소유권은 기존 런타임에 남는다. EntityCatalog·RoadVisualCatalog 데이터와 Streaming 진행 UI의 기존 참조는 유지한다. Edit Selection Confirmation은 SelectMode 하단에 취소 / 적용 버튼을 가로로 배치한다. WorldEditConfirmationView는 Pending 여부와 실행 가능 상태만 버튼에 표시하며 위치 이동·Panel 표시 제어를 하지 않는다. 취소·적용 처리는 기존 WorldEditInputController와 WorldEditApplyController를 사용한다.

일반 Cell 선택의 월드 교체·제거·선택 Chunk 언로드 처리는 `WorldInteractionController`가 담당하며 UI 표시 여부와 무관하게 동작한다. `WorldEditInputController`는 Pending 영역·도구 스냅샷을 소유하고 취소·완료 전환을 한 경로로 처리한다. `WorldTileSelectionState.EditSelected`는 표시용 영역이며 실행 대상은 Input이 보유한 Pending 영역이다. ApplyController는 실행 결과에 따라 Input에 완료를 요청하며 확정 영역을 다시 쓰지 않는다. Inspector와 Highlighter는 선택을 해제하지 않는다. `WorldManager.WorldChanged`는 월드 준비 완료뿐 아니라 제거·준비 실패 후에도 통지하며 각 입력 Controller는 자신이 소유한 상태만 정리한다.

Workspace·Simulation·Inspector는 씬 배치와 최초 초기화에서 모두 축소 상태로 시작한다. 이후 컴포넌트 재활성화는 사용자가 선택한 확장 상태를 초기화하지 않는다. 이 상태는 영구 저장하지 않는다. Simulation Box는 확장 시 월드 로드 여부와 제어 미지원 안내를, 축소 시 월드 로드 여부만 표시한다. 상단과 폭은 고정하고 높이만 줄이며 X와 같은 위치·규격의 확대 버튼으로 복귀한다. Inspector 영역과 Launcher는 Simulation의 현재 높이 아래에 같은 간격으로 따라 이동한다. Inspector의 표시 여부와 선택 Context는 이 이동과 독립적이다. EntityManager와 WorldWaterFlowController는 각각 기존 업데이트 경로에서 계속 시뮬레이션을 처리한다. 공통 시간·Pause/Play·Speed 제어는 아직 구현되어 있지 않다.

## 변경 전파

`WorldManager`는 런타임을 생성하고 Editing, WaterFlow, Renderer, EntityManager, Persistence를 연결한다. 월드 편집과 물 계산 결과는 ChangeSet으로 전달되어 렌더링, 저장 dirty 처리, 필요한 내비게이션·Waypoint 갱신을 유도한다.

편집 평가는 `WorldEditApplyController.Evaluate`에서 수행하고, `Present`는 평가 결과를 Preview로 표시한다. Entity 배치 규칙은 기존 `EntityEditController`에 유지하며 평가에 전달한 Cell 목록을 다시 복사하지 않는다. 적용 직전에 새로 평가한 결과를 실행에 사용하고 Transaction의 최종 보호 규칙은 유지한다. Input은 실행 가능 여부를 별도 저장하지 않고 평가 결과를 확인 버튼에 전달한다. Pending·Hover의 World/Entity 변경은 기존 Cell revision과 World 변경 번호를 0.2초 간격으로 비교해 재평가한다. 변경 번호는 월드 전체 기준이며 영역별 갱신 최적화는 아직 적용하지 않았다. 미로드 Cell이 포함된 선택은 전체 적용을 막고, 건물 외곽·Terrain Anchor의 미로드 Cell도 배치 불가로 처리한다. 재로드 이후에는 같은 Pending을 다시 평가한다.

## World 편집 이력의 현재 구현

Undo/Redo는 다음 기록의 대상 Chunk 데이터가 모두 로드되어야 실행된다. 미로드이면 기록을 보존하고 그 기록을 넘어 진행하지 않는다. `WorldEditApplyController`는 기존 0.2초 갱신에서 실행 가능 상태가 달라질 때 버튼을 갱신하고, 실제 호출에서도 `WorldEditController`가 다시 검사한다. 기록 적용 준비 중 실패한 트랜잭션도 정리하여 이후 편집을 막지 않는다.

`WorldEditController`는 직접 Terraform·Road 편집의 Cell 변경 기록으로 Undo/Redo를 처리한다. Entity 배치는 이력을 남기지 않는다. 건물의 부수 지형·Road 변경은 `CommitExternalChange`에서 Entity 등록까지 성공한 뒤 확정하며, World 변경이 있으면 Undo·Redo 이력을 모두 폐기한다. 등록 실패 시 Entity 정리와 World 값·파생 데이터 복구를 수행하며 기존 이력은 유지한다. World 변경 없는 배치도 이력을 유지한다. 물 시뮬레이션의 실제 Cell 변경은 `WorldManager.OnWaterChanged`에서 같은 이력 폐기 경로로 전달한다. Undo/Redo 자체의 재적용은 외부 작업으로 취급하지 않는다. 사용자 확인 상태는 ROADMAP.md를 따른다.

World 편집의 확정 후 통지는 구독자별 예외를 로그로 기록하고 계속한다. Undo/Redo는 이력 이동 완료 후 변경을 통지하며, 통지 오류 때문에 적용된 기록을 이전 스택으로 복원하지 않는다. `WorldManager`의 편집 후 저장 dirty·Waypoint·물·렌더링 처리는 각각 오류를 격리한다. 확정 전 Cell·파생 데이터 적용 및 관련 작업 실패의 복구 경계는 유지한다.

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




