# Mini Civilization

셀 기반 절차적 월드를 생성하고 편집하며, 물과 엔티티의 변화를 시뮬레이션하는 Unity 프로젝트입니다.

## 주요 기능

- Seed 기반 지형·수문·수역·바이옴 생성
- Terrain과 Biome 계산의 Unity Job System 병렬 처리
- Generate/Load 단계별 진행 상태 표시
- Cell 단위 지형 편집과 Brush·Single 선택
- Source와 Dynamic Water 기반 물 확산
- 변경 영역 중심의 Surface·Navigation·Mesh 갱신
- 월드 데이터 Save/Load
- Prefab·Catalog 기반 엔티티 생성
- sealed Entity 상태머신과 공통 EntityController 표현 구조
- Animator와 Cell 내부 이동을 분리한 `EntityRenderProfile`

## 개발 상태

월드 생성·로드·편집·물·렌더링 구조 전환은 완료되었으며, 현재 엔티티 시스템을 확장하고 있습니다.

- Tree: 배치 및 렌더 위치 확인 완료
- Dog: Idle/Move 상태, Cell 이동, Blend Tree, Local Motion 적용
- 진행 예정: 높이차 기반 Walk/Jump/Fall, 엔티티 LOD
- 이후 예정: Human 목적지 경로 탐색, Building 배치와 이동 우회, WalkLinks

Save/Load 형식은 현재 구조 검증용입니다. 버전 간 저장 파일 호환성은 보장하지 않습니다.

## 개발 환경

- Unity `6000.3.11f1`
- Universal Render Pipeline `17.3.0`
- Input System `1.19.0`
- AI Navigation `2.0.11`

정확한 패키지 버전은 [Packages/manifest.json](Packages/manifest.json)과 `Packages/packages-lock.json`을 기준으로 합니다.

## 프로젝트 실행

이 저장소는 모델·이미지·애니메이션 등 바이너리 에셋에 Git LFS를 사용합니다.

```bash
git lfs install
git clone <repository-url>
```

1. Unity Hub에 프로젝트 폴더를 추가합니다.
2. Unity `6000.3.11f1`로 프로젝트를 엽니다.
3. [Main Scene](Assets/Scenes/Main%20Scene.unity)을 엽니다.
4. Play Mode를 시작합니다.

Unity가 `Packages/manifest.json`을 기준으로 필요한 패키지를 복원합니다.

## Scene

| Scene | 용도 |
|---|---|
| `Assets/Scenes/Main Scene.unity` | 월드 생성·편집·물·엔티티를 실행하는 메인 Scene |
| `Assets/Scenes/Edit Scene.unity` | 엔티티 Prefab의 크기·위치와 Building Local Cell을 확인하는 Authoring Scene |

## 주요 구조

```text
Assets/Game/World
├─ Domain          저장되는 월드 사실 데이터와 변경 데이터
├─ Runtime         WorldRuntime, Cache, EntityRuntime, Manager
├─ Generation      지형·수문·수역·바이옴 생성 Pipeline
├─ Persistence     World Save/Load Codec와 Load Operation
├─ WaterFlow       물 상태, 해석, 확산 Simulation
├─ Editing         월드·엔티티 편집 입력과 적용
├─ Interaction     Cell 선택, Raycast, 정보 표시
├─ Meshing         Terrain·Water Mesh 생성
├─ Presentation    Renderer, UI, EntityController, Render Profile
├─ Definitions     Entity Catalog와 계열별 Definition Container
├─ Entities        sealed Entity 상태머신과 Building Layout
└─ Authoring       Entity Prefab과 Local Cell 시각화 도구
```

### 월드 흐름

```text
Generate / Load
→ WorldData
→ WorldRuntime 준비
→ WorldManager 활성화
→ Editing / Water / Entity / Renderer 연결
```

### 엔티티 흐름

```text
EntityCatalog
→ EntityDefinition
→ 계열별 EntityController Prefab
→ sealed Entity 상태머신 생성
→ EntityRuntime 관리
→ WorldEntityRenderer 표현
```

`EntityData`는 논리적 Cell 위치와 저장 상태를 보관합니다. Animator, VisualRoot, LocalMotionRoot와 `EntityRenderProfile`은 Prefab의 시각적 표현을 담당합니다.

## Git 관리 범위

다음 항목을 저장소에 포함합니다.

- `Assets/`
- `Packages/`
- `ProjectSettings/`
- `.gitignore`
- `.gitattributes`

`Library/`, `Temp/`, `Logs/`, `UserSettings/`와 자동 생성된 IDE 프로젝트 파일은 커밋하지 않습니다.

## 외부 에셋

프로젝트에 포함된 외부 모델·애니메이션·그래픽 에셋은 각 원본 에셋의 라이선스를 따릅니다. 저장소 공개 또는 재배포 전에 해당 라이선스를 별도로 확인해야 합니다.

모델 + 애니메이션
https://quaternius.com/packs/ultimateanimatedanimals.html
