# 로드맵

## 완료

- **World V1 안정화**: Pattern Tile 기반 월드 생성, 청크 스트리밍, Cell/Filled 편집, 물 시뮬레이션, 렌더링 및 Save/Load 기반을 완료했다.

## 진행 중

### Entity Core 정리 및 Animal 생산 파이프라인

- **상태:** 초기 1~7단계 구현 후 설계 차이·수명 결함 보완 필요. 5~6단계 런타임 검증 보류, 두 번째 Animal 표현·통합 검증 미완료. 아래 보완 로드맵을 우선한다.
- **설계 기준:** DEC-008 / DEC-007 / DEC-006. DEC-005는 이전 결정 이력이다.
- **목표:** 다양한 종류를 공통 Runtime 슬롯에서 관리하고, 개체별 속성과 FSM 상태를 독립적으로 유지한다. 기준 샘플 검증 후 기존 행동을 사용하는 같은 계열 콘텐츠를 핵심 코드 수정 없이 생산한다.
- **이번 범위:** Entity Core, Animal 공통 기능, Dog 기준 구현, Deer 등 두 번째 Animal을 통한 생산 검증.
- **제외:** Human·Building·사회 시스템의 기능 확장, 범용 행동 그래프, 자유로운 외형/FSM 교체 기능. 기존 계열의 연결과 참조는 보존한다.

| 순서 | 단계 | 완료 기준 |
|---|---|---|
| 1 | Entity 실행 구조 정리 — 완료 | 월드 단위 `EntitySystem`과 개체별 `EntityRuntime`을 분리했다. 기존 등록·조회·활성 Chunk 업데이트·이동·ChangeSet·저장 payload 경로를 유지한다. |
| 2 | 개체 데이터·FSM 분리 — 완료 | `EntityRuntime`이 `EntityData`와 개체별 `EntityFSM`을 소유한다. 공통 Fixed/Dynamic 및 계열별 FSM 기반과 구체 sealed FSM을 분리했고, 기존 행동 진행·난수 상태의 개체별 저장을 유지한다. |
| 3 | Definition 기반 생성 — 완료 | Type Registry가 Definition을 등록하고, 신규 생성 시 Definition의 이름 후보·나이·Trait 범위로 개별 EntityData를 만든 뒤 Definition의 FSM 생성 경로를 연결한다. 종류별 System 분기는 추가하지 않는다. |
| 4 | 표현 수명 분리 — 완료 | Prefab 컴포넌트를 순수 `EntityView`로 정리하고 FSM 생성 설정을 `EntityFSMDefinition` SO로 분리했다. 종류 식별자는 후속 보완 1단계에서 `EntityDefinition`으로 이동했다. Renderer의 View 생성·해제는 `EntityRuntime`의 Data·FSM 수명과 독립적이다. |
| 5 | Save/Load 통합 — 구현 완료, 검증 보류 | `EntityPersistentState`와 저장 Codec에 이름·나이·Trait 실제 값을 포함했다. 기존 FSM payload·목표 ID·이동 계획과 함께 복원하며 Load에서 속성을 재추첨하지 않는다. 저장 버전은 15이고 이전 버전은 새 월드가 필요하다. 속성과 FSM 진행 상태를 관찰할 Presentation이 아직 없어 런타임 검증은 전체 로드맵 완료 후 수행한다. |
| 6 | 서로 다른 종류 간 슬롯 재사용 — 구현 완료 | EntitySystem이 제거된 `EntityRuntime`을 빈 슬롯으로 회수하고 새 Data·FSM을 연결한다. ID·위치·tick·이동·건물 인덱스를 제거한 뒤 이전 Data·FSM 참조를 비운다. 계열 View Host와 종류별 표현 내용물은 서로 독립된 풀에서 재사용한다. |
| 7 | Animal 생산 파이프라인 완성 — 구현 완료 | Dog 전용 FSM을 공통 `AnimalRoamingFSM`으로 정리하고, Deer를 TypeKey 2의 별도 FSM 설정·Definition·Prefab으로 등록했다. 같은 행동을 사용하는 후속 Animal은 Runtime·World·FSM 코드 수정 없이 에셋 추가와 Catalog 등록만으로 생산한다. |

각 단계의 실제 구현에 맞춰 PROJECT.md를 갱신한다. 구체 식별자·직렬화 형식은 구현 전에 확정하며, 기존 저장에 영향이 있으면 버전·호환 정책을 함께 처리한다.

검증은 프로젝트 규칙에 따라 agent가 컴파일 오류 확인까지 수행하고, 사용자가 다음을 직접 확인한다.

- 같은 Definition으로 생성한 개체들의 속성을 변경했을 때 서로 영향을 주지 않는다.
- 활성 범위 이탈·복귀와 화면 표시 해제·재연결에서 상태가 보존된다.
- 행동 도중 Save/Load 후 개체 속성과 진행 상태가 복원된다.
- 서로 다른 종류를 같은 Runtime 슬롯에 재연결했을 때 이전 개체의 상태·참조가 남지 않는다.
- 두 번째 Animal 추가 시 Runtime·World 핵심 코드 변경 없이 생성·업데이트·표현·저장이 연결된다.

위 1~7단계 표는 초기 구현 이력이다. 이후 확인된 계열 View 공유, 이동 청크 참조, Preview 반환, Runtime 반환 순서·tick 안전성·실패 복구 문제는 아래 보완 1~5단계에서 구현을 정리했으며 런타임 통합 검증은 아직 남아 있다.

### Entity 설계 정합성·생명주기 보완

- **상태:** 보완 구현 및 후속 검토 결함 수정 반영, 사용자 런타임 통합 검증 대기. Deer Animator 연결은 미완료이며 전체 완료로 판정하지 않는다.
- **Host 재사용 정리:** DEC-008 반영 완료. Root 사전 연결, 모델 유지 반환, 동일 모델 우선 대여, 다른 외형일 때만 모델 교체. 사용자 런타임 확인 대기이며 아래 초기 독립 모델 풀 구현 이력보다 DEC-008을 우선한다.
- **목표:** 초기 구현의 설계 차이와 현재 결함을 공유 책임별로 묶어 해결한다.
- **설계 기준:** DEC-007의 소유권·수명·재사용 계약, DEC-006의 저장 계약
- **제외:** 속성 변경·행동 가중치 연결 기능, Entity 조작 UI, Human·Building·사회 기능 확장. 건물 데이터 분리는 기존 기능 보존에 필요한 범위로 한정한다.

| 보완 단계 | 공유 영역과 작업 | 완료 기준 | 상태 |
|---|---|---|---|
| 1 | 정의·데이터 소유권: 종류 ID·계열을 EntityDefinition으로 이동, FSM 설정 이름·역할 정리, 건물 베이크 데이터를 View에서 분리, 저장 스냅샷 외부 변경 차단 | FSM·표현 교체와 무관하게 논리 데이터·식별자 유지. 기존 저장 ID와 Authoring 참조 보존 | 완료 |
| 2 | Runtime 생명주기: 생성·복원·제거 경로, Runtime 연결 책임, FSM 종료, View 해제 후 슬롯 반환, tick 중 개체 정체성 확인, 실패 시 전체 복구 | 이전 개체 참조·인덱스가 남지 않고 새 개체를 이전 개체로 처리하지 않음. 슬롯 중복 반환·회수 누락 방지 | 완료 |
| 3 | 이동·스트리밍 수명: 필요한 청크 참조를 시작·완료·복원에 맞춰 갱신하고 언로드·복원 준비 판단에 공통 적용 | 필요한 셀을 언로드 후 읽지 않음. Save/Load 이동 상태와 참조 일치. 범위 초과와 미로드 오류 구분 | 완료 |
| 4 | 계열 View·Preview: View와 모델 분리, 계열별 Root·풀, 모델·Animator·설정 연결/해제, Preview 전용 Root·반환 정책, Deer 표현 연결 | Animal View를 Dog·Deer 간 재사용. 이전 표현 잔존 없음. 영역 축소·선택 종료 시 Preview 반환. 실제 애니메이션 자산 확인 후 연결하며 자산 부재를 완료로 처리하지 않음 | 구현 완료, Deer Animator 자산 검증 대기 |
| 5 | 전체 경로 정합성: 생성→tick→이동→표시 해제→저장→언로드→복원→재사용 검토, 문서 대조 | 단계 간 계약이 모순되지 않고 구현 완료와 미검증 항목 구분 | 완료 |

진행 순서는 보완 1 → 2 → 3 → 4 → 5다. 보완 2에서 기존 View를 대상으로 해제 완료 계약을 확립하고 보완 4가 그 계약을 유지한다. 보완 3의 언로드·복원은 보완 2의 공통 생명주기 경로를 사용한다. 같은 소유권·반환 문제를 단계별 별도 구현으로 해결하지 않는다.

각 단계에서 관련 코드·에셋·문서를 함께 정리한다. PROJECT.md에는 구현된 구조만 반영한다. 기존 저장 식별자와 payload 형식은 가능한 한 유지하고, 변경이 필요하면 호환 영향을 먼저 명시한다. agent 검증은 컴파일 확인까지이며 사용자 런타임 검증과 구분한다.

후속 통합 검증을 위해 각 단계에서 다음 재현 절차와 기대 결과를 구체화한다: tick 중 제거·재생성, 생성·복원 실패 후 재시도, 이동 중 청크 이탈·복귀 및 Save/Load, Dog→Deer View 재연결, Preview 영역 확대·축소·선택 종료, 건물 레이아웃·Authoring 보존.

보완 구현 완료 후 남은 사용자 런타임 검증은 다음과 같다.

- 실행 전 Main Scene에 계열 Root·Placement Preview Root가 있으며 실행 후 Root가 중복 생성되지 않는다. 같은 모델 반환·재대여 시 Host와 모델의 Instance ID가 모두 유지된다. 다른 모델 후보가 없는 상태에서 Dog→Deer 대여 시 Host ID는 유지되고 모델만 교체된다. 반환 Host에는 중복 EntityView가 없고, 별도 Active/Pooled Views/Pooled Models Root가 생성되지 않는다.

- 이동 중 출발 청크 또는 도착 청크의 언로드 경계로 이동해도 범위 밖 Cell 렌더 오류가 발생하지 않고, 개체가 저장·해제된 뒤 필요한 청크가 모두 준비되면 같은 이동 상태로 복원된다.
- Dog View를 반환한 뒤 Deer를 표시하면 같은 Animal View Host가 재사용되고, Dog 표현 내용물·Animator 상태가 Deer에 남지 않는다. Deer Prefab에는 현재 Animator 참조가 없으므로 실제 Deer 애니메이션 검증은 애니메이션 자산 연결 후 수행한다.
- 배치 영역을 확대한 뒤 축소하면 초과 Preview가 즉시 풀로 반환되고, 선택 종료·도구 변경 후 `Placement Preview Root`에는 사용 중 Preview가 남지 않는다.
- 제거 직후 슬롯 재사용, 복원 실패 후 재시도, 건물 배치 실패 후 재시도에서 중복 ID·Cell·Chunk·tick·건물 인덱스 오류가 발생하지 않는다.

이후 자율 엔티티 및 사회·세력 시뮬레이션의 우선순위는 이번 파이프라인 검증 후 정한다.

## 예정

### Main Scene UI 재설계

- **상태:** Canvas 직속 TMP Box와 고정 컨트롤을 씬에 배치하고 기존 World Edit UI·Toolbar 상태 의존성을 제거했다. 사용자 실행·해상도별 배치 확인 대기이며 런타임 검증 완료로 판정하지 않는다.
- **설계 기준:** DEC-009 / DEC-010 / DEC-011. 현재 구현 구조는 PROJECT.md를 따른다.
- **대상:** `Assets/Content/Scenes/Main Scene.unity`와 연결된 UI·Interaction 코드 및 UI 항목 Prefab.
- **목표:** 중앙 World View를 유지하면서 단일 Entity / World Workspace, 도구별 Options, 독립적인 Simulation·Inspector Box를 제공한다.
- **우선 범위:** 기존 기능의 UI 재구성, 미지원 항목 표시, Cell 기반 Entity Inspector 선택 연결. 미구현 World 도구·Simulation 제어는 아래 후속 작업으로 분리한다.
- **기존 로드맵과의 관계:** Entity 생명주기 보완·Deer Animator 연결·사용자 통합 검증의 미완료 상태를 유지한다. 이번 Inspector는 관찰 수단을 제공하며 속성 편집·행동 가중치 조작까지 완료한 것으로 보지 않는다.

| 순서 | 사용자 기능 단계 | 완료 기준 | 상태 |
|---|---|---|---|
| UI-1 | 표시와 도구·선택 상태 분리 | Workspace 표시 여부로 Mode·Cell 선택 가능 여부를 결정하지 않는다. X·Launcher가 Active Tool·선택·Pending을 해제하지 않는다. 명시적 도구 해제와 실제 도구 변경의 취소 경로는 유지한다. | Active Tool 계약 수정 완료, 사용자 확인 대기 |
| UI-2 | 고정 Workspace와 독립 Box | 좌측 Workspace·독립 SelectMode Palette, 우측 Simulation·Inspector를 배치한다. Simulation은 높이 축소와 같은 위치의 확대 버튼을 사용하고 Inspector 영역·Launcher가 그 아래로 함께 이동한다. 세 Box의 8가지 확장 조합을 지원하고 World View·Camera·좌표계는 유지한다. 기존 확인·진행·Undo/Redo UI와 직렬화 참조를 보존한다. | Launcher·History·내부 스크롤 수정 완료, 사용자 확인 대기 |
| UI-3 | Context Palette와 Tool Options | Entity / World Tab이 같은 Palette를 사용한다. 승인된 Category와 Road를 표시하고 기존 도구를 연결한다. Category 탐색 시 도구·Options 해제, 항목 선택 시 필요한 Options만 활성화한다. 미지원 기능은 실행할 수 없다. Area는 기존 3D Box로 유지한다. | 도구 연결·복귀·선택 표시 구현 완료, 사용자 확인 대기 |
| UI-4 | Cell Inspector | 기존 Cell 정보 조회·표시를 재사용한다. Inspector 축소가 선택을 해제하지 않고 재확장 시 최신 정보를 표시한다. 월드 교체·대상 무효화와 정보 갱신을 처리한다. | Context 상태 및 월드 없음 표시 수정 완료, 사용자 확인 대기 |
| UI-5 | Cell 기반 Entity Inspector | 선택 Cell에 Entity가 0개면 Cell 정보, 1개면 Entity 정보, 여러 개면 정렬된 선택 목록을 표시한다. Cell 정보·목록으로 복귀할 수 있고 Entity ID로 개체 정체성을 유지한다. | Context·정렬 목록·Entity 변경 추적 구현 완료, 사용자 확인 대기 |
| UI-6 | 사용자 통합 확인 | 아래 재현 항목을 사용자가 확인하고 결과에 따른 결함을 정리한다. 구현 완료와 사용자 검증 완료를 구분하여 기록한다. | 컴파일 확인 후 사용자 실행 확인 대기 |

진행 순서는 UI-1 → UI-2 → UI-3 → UI-4 → UI-5 → UI-6이다. 상태 분리를 먼저 수행하여 새 Box의 표시 제어가 기존 편집·선택 취소 경로를 다시 호출하지 않게 한다. 단계별 실제 구현 후에만 PROJECT.md를 갱신한다.

현재 구현 경계는 MainSceneUIView의 씬 참조·Context 표시, WorkspaceItemView/Workspace Item.prefab의 데이터 행 표시, WorldEditToolState의 도구 상태, WorldEditApplyController의 실행·History 연결이다. 기존 Toolbar·Catalog View는 Main Scene에서 사용하지 않는다. Cell 조회·월드 Pointer 차단과 확인·진행 UI는 기존 시스템을 유지한다.

추가 사용자 확인: 최초 실행 시 Workspace·Simulation·Inspector가 모두 축소되고 SelectMode가 숨겨져 있는지 확인한다. Box를 펼친 후 컴포넌트 재활성화로 표시 상태가 초기화되지 않아야 한다. Inspector를 접은 동안 변경한 대상 정보는 재확장 시 반영되고 목록 왕복 시 기존 행을 재사용해야 한다. Play 전 Canvas 직속 Box가 배치되어 있는지, Active Tool 선택/해제 시 독립 SelectMode Palette 전체가 표시/숨김되는지, Workspace 축소 시 도구와 SelectMode가 유지되는지 확인한다. Simulation 축소 시 폭·상단·버튼 규격이 유지되는지, Inspector의 확장 영역과 Launcher 양쪽이 높이 변화에 맞춰 이동하는지 확인한다. 동일 Tool 재선택으로 일반 Cell 선택에 복귀하는지, Brush에서만 Size 행과 Palette 높이가 늘어나는지 확인한다. 고정 취소·적용 버튼은 Pending이 없으면 비활성이고, 취소·적용 후에도 Tool은 유지되어야 한다. 모드·Brush Size 반복 변경, Back 버튼과 스크롤도 확인한다. Entity·Road의 Thumbnail이 없는 데이터와 Terraform은 문자 기호를 표시하며 전용 아트 연결은 별도 콘텐츠 작업이다.
### UI 후속 기능

| 작업 | 범위·선행 조건 | 상태 |
|---|---|---|
| 미구현 World 도구 | Biome, Water, Terrain 재질 등 미지원 항목의 실제 동작을 별도 설계·구현한다. UI-3의 도구 연결 경계를 사용하며 도구별 옵션·변경 전파·저장 영향을 먼저 확정한다. 기존 Terraform·Road를 중복 구현하지 않는다. | 후속 예정, 세부 동작 미확정 |
| Simulation 제어 | 시간 표시·Pause/Play·Speed의 범위와 Entity·물 처리에 적용할 시간 계약을 먼저 정한다. UI-2의 Box에 연결하되 표시 여부는 실행 상태에 영향을 주지 않는다. 기존 공통 제어 API가 없으므로 UI 재배치에 시간 제어를 임의 포함하지 않는다. | 후속 예정, 제어 계약 미확정 |
| Rectangle | 2D Surface Rectangle 선택을 도구별 지원 범위에 맞게 추가한다. 기존 Area의 3D Box 의미는 변경하지 않는다. | 후속 예정 |
| Area Inspector | 선택 영역의 관찰 Context와 표시할 정보를 별도로 정의한다. 편집 Pending 영역과의 관계를 먼저 정한다. | 향후 확장 |
| Road 그룹 재구성 | 콘텐츠 확장 시 Category 배치를 재검토한다. 그전에는 독립 Road Category를 사용한다. | 향후 확장 |

후속 기능은 UI 기반 이후 별도 작업으로 수행한다. 기능별 상세 우선순위·지원 옵션·시간 제어 방식은 아직 확정하지 않는다. 속성 변경·행동 가중치 연결은 기존 Entity 보완 로드맵 이후 논의하며, Inspector만으로 충족되지 않는 초기 5·6단계 및 두 번째 Animal 검증은 필요한 조작 수단 마련 후 재개한다.

### UI 사용자 확인 절차

agent는 코드 변경 단계에서 컴파일 오류 확인까지만 수행한다. 다음 런타임 확인은 사용자가 수행하며 결과를 전달한 뒤 완료 상태를 갱신한다.

- Workspace·Inspector는 X와 Launcher, Simulation은 X와 같은 자리의 확대 버튼을 사용하여 8가지 조합을 확인한다. 다른 Box 상태, Active Tool, 선택 Context, Pending 편집과 Simulation 진행이 유지되어야 한다.
- Tab·Category로 돌아가면 도구가 해제되고 Options가 숨겨져야 한다. Deer 또는 지원되는 World 항목 선택 시에만 필요한 Options가 표시되고 Building은 Single만 허용되어야 한다. 미지원 항목은 실행되지 않아야 한다.
- UI 위 클릭·Drag 및 Pending 중 X·Launcher 클릭을 수행한다. 월드에 적용되지 않고 표시 전용 클릭이 Pending을 취소하지 않아야 한다. 기존 UI 진입 시 Drag 차단·명시적 취소 규칙은 유지되어야 한다.
- Entity·Terraform·Road 활성 항목을 재선택하면 도구·Pending이 해제되고 현재 Category는 유지되어야 한다. Brush → Single/Area 전환 시 Size 행의 빈 공간이 남지 않아야 한다.
- Single·Brush·3D Area로 고정 취소·적용 버튼과 Undo/Redo를 확인한다. UI 이동 전과 같은 월드 변경 결과를 유지해야 한다.
- Entity가 0개·1개·여러 개인 Cell을 각각 선택한다. Cell 정보·Entity 자동 선택·정렬 목록이 대응하고 Cell 정보·목록으로 돌아갈 수 있어야 한다.
- Inspector를 접은 동안 대상 상태를 변경한 뒤 펼친다. 최신 정보가 표시되어야 하며 삭제·언로드·월드 교체 후 다른 개체를 이전 선택으로 표시하지 않아야 한다.
- Box 확장·축소 전후 같은 화면 지점의 Cell Picking과 Camera viewport를 확인한다. UI가 가리지 않는 World View의 좌표와 월드 논리 크기가 바뀌지 않아야 한다.

## 보류

- 초기 5·6단계 런타임 검증: 관찰·조작 수단 마련 후 재개한다.

## 취소

- 없음.



