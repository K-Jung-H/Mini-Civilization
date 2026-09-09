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
- **Entities**: `EntityCatalog → EntityDefinition → EntityRuntime → WorldEntityRenderer` 구조다. 논리·저장 상태는 `EntityData`, Animator와 시각 이동은 Controller 및 Render Profile이 담당한다.
- **Persistence**: `SaveLoadManager`와 `WorldPersistenceService`가 월드 메타데이터 및 region 기반 Chunk 저장을 관리한다. 새 영속 상태가 추가되면 Save/Load도 같은 변경 범위에서 확장한다.

## 변경 전파

`WorldManager`는 런타임을 생성하고 Editing, WaterFlow, Renderer, EntityManager, Persistence를 연결한다. 월드 편집과 물 계산 결과는 ChangeSet으로 전달되어 렌더링, 저장 dirty 처리, 필요한 내비게이션·Waypoint 갱신을 유도한다.
