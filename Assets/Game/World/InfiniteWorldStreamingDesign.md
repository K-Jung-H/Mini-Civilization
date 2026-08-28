# 무한 월드 청크 스트리밍 설계 및 적용 로드맵

## 문서 목적

최종 목표는 Seed와 절대 좌표를 기준으로 X/Z 방향에 연속 생성되는 무한 월드다. 필요한 청크만 준비·렌더링·시뮬레이션하며, 렌더 객체는 Pool로 재사용한다.

먼저 같은 구조에 유한 Bounds만 적용해 한 번에 모든 청크를 활성화하지 않는 범위를 확인한다. 이후 Bounds 제한과 유한 월드 전용 생성 규칙만 제거해 무한 월드로 전환한다. 유한 단계용 별도 데이터 구조를 만들지 않는다.

이 문서는 확정된 최종 구조와 구현 순서를 고정한다. 문서에 없는 환경·생태·문명 기능이나 이전 저장 호환 로직을 구현 과정에서 추가하지 않는다.

## 현재 진행 상태

기준일: 2026-08-26

| 단계 | 상태 | 현재 적용 결과 |
|---|---|---|
| 0. 전환 기준 고정 | 완료 | 이 문서의 확정 원칙과 제외 범위를 구현 기준으로 사용한다. |
| 1. 좌표와 인덱스 기반 교체 | 완료·테스트 통과 | signed Chunk 좌표, 음수 좌표 변환, Local Cell 인덱스와 Y 범위 분리를 적용했다. |
| 2. Chunk와 희소 ChunkSection | 완료·테스트 통과 | `WorldData`를 Chunk Dictionary로 교체하고, 비어 있는 Y Section은 할당하지 않도록 적용했다. |
| 3. CellBiome과 EnvironmentData 제거 | 완료·테스트 통과 | `CellBiome ushort`를 적용하고 `EnvironmentData`와 임시 Biome Edit 경로를 제거했다. |
| 4. Chunk Runtime 상태와 스트리밍 중심 | 완료·테스트 통과 | `Unloaded → Preparing → Ready ↔ Active`, 교체 가능한 Target과 반경 기반 상태 전환을 적용했다. |
| 5. 유한 Bounds Render 스트리밍과 Pool | 완료·테스트 통과 | Render Patch Dictionary와 `WorldRenderPatchView` Pool을 적용하고 전체 View 배열을 제거했다. |
| 6. Chunk별 Cache와 경계 갱신 | 완료·테스트 통과 | Surface·Navigation·Exposure Cache를 준비된 Chunk 단위로 관리하고 준비·해제 시 경계를 갱신한다. |
| 7. Entity·Building·Road/Way 스트리밍 | 완료·테스트 통과 | Minecraft 용어 통일, Entity 소유·Tick·렌더 범위, 다중 Chunk Building 참조, 준비된 Chunk의 Way Graph를 적용했다. |
| 8. Water 스트리밍 | 구현 완료·직접 테스트 필요 | Chunk별 Frontier 소유, Active 범위 실행, 비활성 경계 보류, 준비된 Chunk 한정 WaterBody 해석을 적용했다. |
| 9. 3D Density 기반 절대 좌표 생성 | 교체 진행 중·1~5단계 완료 | 절대 Y 지형 기준과 Water Distribution의 개별 수면 높이를 분리했다. Continental Sea Distribution은 연속 깊이 곡선으로 해안·해저 Density를 합성하며, 설정 Y 범위 밖 Density를 강제로 공기로 바꾸지 않는다. |
| 10~11 | 미진행 | 아래 로드맵 순서대로 진행한다. |

현재 런타임 범위는 다음과 같다.

- Finite는 원점 Chunk를 중심으로 한 초기 N×N 범위를 Bounds로 사용한다.
- Infinite는 같은 초기 N×N을 만들고 스트리밍 요청 시 Bounds 밖 Chunk를 같은 Seed와 절대 좌표로 추가 생성한다.
- 렌더 객체와 Runtime Cache는 Target 반경에 따라 준비·반환된다.
- Entity Controller와 Visual은 EntityRenderRadius에만 유지되고, Entity Tick은 Active 범위에만 적용된다.
- Water Frontier는 Chunk 좌표별로 분리하고 Active Chunk의 처리 가능한 Cell만 파동에 참여시킨다. 파동의 Stage와 Commit은 전역 Resolver가 한 번에 처리해 기존 확산 결과의 원자성을 유지한다.
- Cache가 없는 Chunk의 Cell 사실은 `WorldData`에서 직접 조회하며, Unloaded 영역을 Empty 또는 막힌 Cell로 해석하지 않는다.
- 런타임 전체 Cache `RebuildAll` 경로는 제거했다. 생성 후보 검증용 독립 Preview는 런타임 Cache 스트리밍 대상이 아니다.
- 1~7단계의 직접 빌드·플레이 테스트가 통과했다. 8~9단계는 코드 적용과 컴파일 검증을 완료했으며 직접 플레이 테스트가 필요하다. Save/Load는 현재 테스트 범위에서 제외한다.

## 확정 원칙

- X/Z 좌표는 무한하며 음수 좌표를 허용한다.
- Y 높이와 ChunkSection 수는 현재처럼 고정한다.
- Chunk `(0,0)`과 ChunkSection `(0,0,0)`은 월드 원점 `(0,0,0)`에서 시작한다.
- 모든 생성 결과는 `Seed + GenerationSettings + 절대 XYZ 좌표`로 결정한다.
- `WorldType`은 `Finite`와 `Infinite`이며 지형 생성 규칙은 같고 추가 Chunk 요청 허용 여부만 다르다.
- `InitialChunkCountXZ`는 항상 홀수다. 0은 1, 짝수는 즉시 다음 홀수로 보정한다.
- Chunk 생성·로드 순서와 주변 Chunk 활성 여부가 생성 결과를 바꾸면 안 된다.
- 같은 X/Z의 세로 `ChunkSection`은 하나의 `Chunk`가 관리한다.
- 완전히 빈 Y Section은 생성하지 않는다.
- Unloaded 영역은 Empty Cell로 해석하지 않는다.
- `Resident` 상태는 사용하지 않는다.
- `Ready`는 데이터·Cache 준비 완료 상태다.
- Terrain과 Entity 표현 여부는 Ready 상태와 별도 플래그로 관리한다.
- `Active`는 Ready 상태에 Entity·Water 게임 로직 실행이 추가된 상태다.
- RenderRadius, EntityRenderRadius, SimulationRadius는 서로 크기 순서를 강제하지 않는다.
- Render 범위 안이더라도 Simulation 범위 밖이면 Entity와 Water를 정지한다.
- Simulation 중지 시간에 대한 일괄 보정은 추가하지 않는다.
- 초기 스트리밍 기준은 Main Camera이며, 교체 가능한 `Transform` 참조로 관리한다.
- `EnvironmentData`, 별도 Environment Map, 저장용 Temperature·Moisture·Fertility는 제거한다.
- 바이옴은 모든 지형·수역 생성 검증과 롤백이 끝난 최종 구조에서 확정한다.
- 현재 XZ 단위 Edit Biome은 최종 설계에 포함하지 않는다.
- 기존 저장 파일과의 호환·변환·보정은 전혀 구현하지 않는다.
- 새 저장 구조는 스트리밍·생성·런타임 단계 확인이 끝난 뒤 마지막에 구현한다.

## 좌표 구조

논리 좌표는 정수로 유지하며 Unity Transform만 `CellSize`를 반영한다.

```text
AbsoluteCellX = ChunkX * ChunkCellCountXZ + LocalX
AbsoluteCellZ = ChunkZ * ChunkCellCountXZ + LocalZ
```

예를 들어 `ChunkCellCountXZ=16`, `CellSize=10`이면 다음과 같다.

```text
Chunk X=-1 : Cell X=-16~-1, World X=-160~0
Chunk X= 0 : Cell X=  0~15, World X=   0~160
Chunk X= 1 : Cell X= 16~31, World X= 160~320
```

음수 좌표의 Chunk와 Local Cell 계산에는 0 방향 정수 나눗셈이 아니라 바닥 나눗셈을 사용한다.

월드 Cell은 `CellCoordinate`, X/Z Chunk는 `ChunkCoordinate`, XYZ ChunkSection은 `ChunkSectionCoordinate`, Section 내부 Cell은 `LocalCellIndex`로 식별한다. `World.Size`를 이용해 월드 전체를 하나의 `int`로 인코딩하는 방식은 제거한다.

## 데이터 구조

```text
WorldMetadata
├─ Seed
├─ WorldSettings
├─ 고정 Y 범위
└─ NextEntityId

WorldData
└─ LoadedChunks
   └─ Dictionary<ChunkCoordinate, Chunk>
```

### Chunk

`Chunk`은 같은 X/Z를 공유하는 세로 `ChunkSection`와 Entity 원본을 하나의 스트리밍 단위로 관리하는 컨테이너다.

```text
Chunk
├─ Coordinate X/Z
├─ ChunkSection?[] SectionsByY
└─ EntityData[]
```

- `Chunk` 자체가 `LoadedChunks`에 없으면 Unloaded다.
- Preparing 중인 Chunk는 아직 Cell 조회 결과로 사용하지 않는다.
- Ready 또는 Active Chunk에서 `SectionsByY[y] == null`이면 해당 Y Section은 생성 결과가 완전히 비어 있음이 확정된 상태다.
- Terrain·Water·Road 등 Cell 사실 데이터가 처음 기록될 때 해당 Y 슬롯에 `ChunkSection`을 동적으로 생성한다.
- Entity는 CellData가 아니므로 Entity만 존재한다는 이유로 빈 `ChunkSection`을 생성하지 않는다.
- `ChunkSection`의 모든 Cell이 다시 default가 되면 해당 Y 슬롯을 비울 수 있다.

### ChunkSection과 CellData

```text
ChunkSection
└─ CellData[]

CellData
├─ CellBiome Biome
├─ TerrainData Terrain
├─ WaterData Water
└─ RoadData Road
```

`CellBiome`은 최종 생성 결과를 Cell마다 빠르게 조회하기 위한 런타임 값이며 하나의 `ushort`로 압축한다.

```text
CellBiome ushort
├─ Climate : 최대 8종
├─ Terrain : 최대 32종
├─ Water   : 최대 8종
└─ 예비 Bit
```

논리적으로는 다음 세 값을 제공한다.

```text
CellBiome
├─ Climate
├─ Terrain
└─ Water
```

예시는 `Cold-Snow-Sea`, `Warm-Desert-River`, `Temperate-Cave-Pond`, `Warm-Field-None`이다.

- Climate 계산에 사용한 원본 Temperature는 저장하지 않는다.
- Moisture는 현재 구조에서 제거한다. 향후 농업용 수분은 WaterAmount와 토양 상태를 사용하는 별도 동적 시스템으로 설계한다.
- Fertility는 제거한다.
- 광물은 `TerrainData.Geology`와 `ResourceId`로 생성한다.
- `WaterType`은 런타임에 변경 가능한 현재 물 사실이고, `CellBiome.Water`는 최종 초기 생성 구조에서 고정된 수역 지역 분류다.
- 생성 후 Terrain 편집이나 Water 확산·후퇴가 CellBiome을 자동 변경하지 않는다.

완전히 빈 Y Section에는 `CellData`와 CellBiome 메모리를 할당하지 않는다. 빈 Cell의 바이옴 조회가 필요하면 Seed와 절대 좌표를 사용하는 생성 조회 경로를 사용하며, Unloaded Chunk는 조회하지 않고 준비를 요청한다.

## 런타임 구조와 상태

```text
WorldRuntime
└─ LoadedChunkRuntimes
   └─ Dictionary<ChunkCoordinate, ChunkRuntime>

ChunkRuntime
├─ Chunk
├─ SurfaceCache
├─ NavigationCache
├─ WaterRuntime
└─ State
```

전체 월드 크기의 Surface·Navigation·Water 배열을 만들지 않는다. Cache는 준비된 Chunk 단위로 소유하며 이웃 Chunk가 준비되거나 해제될 때 경계만 갱신한다.

### 상태

```text
Unloaded
→ 데이터 없음

Preparing
→ 생성 또는 로드
→ Cache 계산
→ Mesh 준비

Ready
→ 데이터와 Cache 준비 완료
→ Terrain·Entity 렌더 여부는 각각 별도 플래그
→ 게임 로직은 정지

Active
→ Ready 상태 포함
→ Entity Tick 실행
→ Water Simulation 실행
```

상태 전환은 다음과 같다.

```text
Unloaded → Preparing → Ready ↔ Active → Ready → Unloaded
```

저장이 구현된 뒤 Dirty Chunk는 Unloaded 전에 새 Chunk 저장 경로를 통과한다. 저장 구현 전에는 이전 저장 구조를 임시로 연결하거나 호환 계층을 만들지 않는다.

## 스트리밍 기준과 범위

```text
WorldStreamingController
├─ Transform StreamingTarget
├─ RenderRadius
├─ EntityRenderRadius
└─ SimulationRadius
```

- `StreamingTarget`이 연결되어 있으면 해당 Transform을 사용한다.
- 연결되어 있지 않으면 초기 임시 기준으로 `Camera.main.transform`을 사용한다.
- 이후 Player 등으로 교체할 때는 `SetStreamingTarget(Transform)`으로 대상만 변경한다.
- Streaming Controller를 Camera 전용 구조로 만들지 않는다.

```text
Render 범위
→ TerrainRenderingEnabled 유지

Entity Render 범위
→ 준비된 Chunk의 Entity Controller와 Visual 유지

Simulation 범위
→ Chunk을 Active로 유지

Render 범위 밖
→ Terrain Render View 반환

세 범위 모두 밖
→ Chunk 해제 대상으로 전환
```

세 범위의 합집합에 포함된 Chunk는 데이터와 Cache를 Ready 이상으로 유지한다. Terrain과 Entity는 각자 자신의 반경만 사용하고, Active 범위 밖의 Entity는 Tick하지 않는다. 세 범위 모두 밖인 Chunk만 해제한다.

범위 값은 런타임 설정이며 월드 생성 사실 데이터에 포함하지 않는다.

## 렌더링과 Pool

데이터 스트리밍 단위와 렌더 Pool 단위를 분리한다.

```text
Chunk
→ 데이터·Cache·상태 단위

RenderPatch
→ Terrain·Water Mesh, Road Mask, GameObject Pool 단위
```

```text
WorldRenderer
├─ RenderedPatches
│  └─ Dictionary<RenderPatchCoordinate, WorldRenderPatchView>
└─ WorldRenderPatchView Pool
```

Chunk 준비가 끝나면 필요한 RenderPatch를 계산하고 Pool에서 View를 받아 Mesh와 Road Mask를 구성한다. Render 범위를 벗어나면 View를 비활성화해 Pool로 반환한다. Pool 내부 View는 Chunk 상태가 아니다.

`RenderChunksPerPatch > 1`이면 Patch에 필요한 모든 Chunk을 준비한 뒤 Mesh를 구성한다. 인접 정보가 나중에 준비되면 영향 경계 Patch만 다시 생성한다.

## 생성 Pipeline

```text
Chunk 요청
→ 절대 좌표 기반 Preliminary Terrain Density 계산
→ Preliminary Surface Map 추출
→ Sea·River·Lake/Pond Water Distribution 통합
→ Water Terrain Density Modifier 적용
→ Final Terrain Density 확정
→ 동굴 없는 연결 지형으로 Filled 0~5 변환
→ 정확한 Water Amount와 Source/Dynamic 실행 정책 확정
→ CellBiome 확정
→ 필요한 Y Section만 조립
→ Chunk Cache 준비
→ Ready
```

### 생성 계약

- 생성기는 월드 전체 Size나 가장자리 좌표를 입력으로 사용하지 않는다.
- `EdgeLowering`, 유한 외곽 Flood Fill, 월드 크기 기반 Latitude를 사용하지 않는다.
- Terrain·Mountain·Continental과 수직 Detail은 Seed와 절대 XYZ 좌표를 사용하는 3D Density로 계산한다.
- Density는 생성 중에만 존재하며 `WorldData`나 저장 데이터에 넣지 않는다.
- 동굴 Density와 Carver는 추가하지 않고, 확정 표면 아래를 연결된 고체로 Filled 변환한다.
- Sea·River·Lake/Pond는 완성된 지형을 개별적으로 절삭하지 않고 하나의 Water Distribution에서 최종 Density를 수정한다.
- Sea는 Continental Field와 해안 전이값으로 해저·해안 Density를 연속적으로 만든다.
- Preliminary Terrain의 기준 높이는 절대 Y 값이며 WaterBody 수면 높이에 의존하지 않는다.
- Water Distribution은 Column마다 `Terrain Target`과 `Water Surface`를 독립적으로 가진다. 연결된 WaterBody는 같은 수면을 전달하고, River처럼 경사가 필요한 구조는 Column마다 다른 수면을 전달할 수 있다.
- 현재 Sea는 설정된 기본 Sea 수면을 사용한다. 이후 Lake/Pond는 각 Body의 수면을, River는 경로의 수면을 같은 계약으로 전달한다.
- 육지 쪽 해안은 물을 만들지 않고도 Terrain Target만으로 바다 지형에 연결할 수 있다.
- River는 연속 Field의 실제 거리와 폭 Field를 사용하며 1·3·5 폭 단계는 사용하지 않는다.
- Lake와 Pond는 같은 불규칙 Basin Field를 사용하고 크기로 구분하며, 크기가 클수록 중심부 최대 깊이가 커진다.
- 수역이 겹치는 위치는 Geometry Modifier를 합성한 후 하나의 최종 WaterType을 확정하며 수역별 순차 덮어쓰기를 사용하지 않는다.
- 급한 높이 변화 구간은 Dynamic, 완만한 구간은 Source를 마지막 실행 정책으로 사용한다.
- Source와 Dynamic 모두 생성된 정확한 Water Amount를 보존하며 Source를 Full로 보정하지 않는다.
- Chunk 샘플은 이웃 절대 좌표를 직접 조회하며 생성 순서나 활성 Chunk에 의존하지 않는다.
- Biome은 Feature 후보가 아니라 모든 검증과 롤백 이후의 최종 Terrain·Water 결과로 확정한다.

## 경계 처리

Unloaded 이웃은 Empty나 막힌 공간으로 판정하지 않는다.

```text
이웃 Unloaded
→ 경계 판정 보류

이웃 Preparing 완료
→ 양쪽 경계 재계산
```

이 원칙을 다음 시스템에 공통 적용한다.

- Terrain Shoulder·Corner Mesh
- SurfaceCache와 NavigationCache
- Road 연결
- Building 외부 Way 연결
- Entity 이동
- Water Flow

Road와 Building Way의 저장 사실은 이웃이 Unloaded라는 이유로 제거하거나 수정하지 않는다.

## Entity 소유권과 동작

```text
일반 Entity
→ Center/Anchor Cell이 속한 Chunk가 원본 소유

Dynamic Entity
→ X/Z Chunk 경계를 넘으면 소유 Chunk 이전
→ 같은 Chunk 안의 Y ChunkSection 이동은 소유권을 바꾸지 않음

다중 Chunk Building
→ Center가 속한 Chunk가 EntityData 원본 소유
→ BuildingCells와 TerrainAnchorCells가 걸친 다른 Chunk는 참조만 보유
```

`WorldData`의 Chunk가 EntityData 원본을 소유하고 `EntityRuntime`은 조회·Tick·렌더 참조 Index를 관리한다. Active Chunk별 Tick 목록을 사용하므로 전체 Entity를 매 프레임 순회하지 않는다.

Entity 때문에 빈 `ChunkSection`을 만들지 않는다. EntityRender 범위에서는 Controller와 Visual을 유지하지만 Simulation 범위 밖에서는 Tick하지 않는다. 다중 Chunk Building은 참조 Chunk 중 하나라도 EntityRender 범위이면 하나의 Controller만 표시한다.

## Water 동작

- Ready이지만 Active가 아닌 Chunk의 Water Simulation은 정지한다.
- Active에서 벗어난 시간을 한 번에 보정하지 않는다.
- 다시 Active가 되면 정지한 상태에서 재개한다.
- Unloaded 이웃으로 진행해야 하는 Flow는 이웃이 없다고 확정하지 않고 처리 대상을 보류한다.
- `WaterFlowFrontier`는 `Chunk`의 Cell 사실 데이터가 아니라 `WaterRuntime`의 진행 상태다.
- Water 진행 상태의 새 저장 형식은 Water 스트리밍 동작을 확인한 후 최종 저장 단계에서 결정한다.

## 저장과 Floating Origin

기존 Save/Load 구조는 새 구조와 호환하지 않는다. 마이그레이션, 이전 버전 보정, 임시 호환 Adapter를 만들지 않는다.

새 저장은 마지막 단계에서 다음 계약으로 구현한다.

```text
WorldMetadata
+ Chunk 또는 Region 단위 저장

저장된 Chunk 존재
→ Load

저장된 Chunk 없음
→ Seed로 Generate

Dirty Chunk Unload
→ 새 형식 저장 완료 후 메모리 해제
```

청크별 파일과 Region 묶음 파일 중 물리 배치는 저장 단계에서 결정한다.

실제 무한 거리에 도달하기 전 Floating Origin을 적용한다. 논리 Cell·Chunk 좌표는 절대 정수로 유지하고 Unity의 RenderRoot·EntityRoot만 동일한 Offset만큼 이동한다.

## 단계별 구현 로드맵

각 단계는 명시된 범위만 구현한다. 완료 항목을 직접 확인한 뒤 다음 단계로 이동하며, 이후 단계의 기능을 앞 단계에 임의로 추가하지 않는다.

1~10단계의 수동 테스트에서는 Save/Load를 확인하지 않는다. 새 저장 구조를 구현하는 11단계에서만 Save/Load를 테스트한다.

### 0. 전환 기준 고정

목표:

- 현재 Generate, Terrain/Water Mesh, Edit, Entity, Road/Way 동작을 새 구조 전환 중 비교할 기준으로 둔다.
- 기존 저장 파일 호환은 전환 기준에서 제외한다.

완료 기준:

- 이 문서의 확정 원칙과 제외 범위를 구현 기준으로 사용한다.
- 유한 단계와 무한 단계가 같은 데이터·런타임 구조를 사용한다.

### 1. 좌표와 인덱스 기반 교체

적용:

- signed `ChunkCoordinate`와 Local Cell 변환 추가
- 음수 좌표 바닥 나눗셈 적용
- 전역 `WorldIndex` 의존을 `CellCoordinate` 또는 `ChunkCoordinate + LocalCellIndex`로 교체
- ChangeSet, Entity 공간 Index, Road/Way, Water 처리에 전역 Size 인덱스를 사용하지 않도록 전환
- Y 유효 범위 검사를 X/Z Bounds 검사와 분리

완료 기준:

- 양수·음수 Cell이 정확한 Chunk와 Local Cell로 변환된다.
- 새 스트리밍 핵심 코드가 `World.Size`로 Cell을 인코딩하지 않는다.

### 2. Chunk와 희소 ChunkSection 적용

적용:

- `WorldData`의 전체 3차원 Chunk 배열을 Loaded Chunk Dictionary 구조로 교체
- `Chunk`와 nullable `SectionsByY` 적용
- Unloaded Chunk와 Known Empty Y Section 구분
- Cell 쓰기 시 빈 Y Section 동적 생성
- 모든 Cell이 default인 Y Section 해제 경로 구성
- EntityData의 Chunk 소유 위치 확정

완료 기준:

- 완전히 빈 공중 Y Section이 `ChunkSection`을 할당하지 않는다.
- 기존 Terrain·Water·Road Cell 결과를 Chunk 구조에서 동일하게 조회한다.
- Entity만 존재하는 공중 영역이 빈 `ChunkSection`을 생성하지 않는다.

### 3. CellBiome과 EnvironmentData 제거

적용:

- `Climate + Terrain + Water`를 압축한 `CellBiome ushort` 도입
- 모든 Feature 검증과 롤백 이후 Biome을 확정하도록 현재 Biome Stage 교체
- `EnvironmentData`, `environmentMap`, Environment Edit/Save 경로 제거
- 현재 임시 XZ Edit Biome 제거
- Temperature는 Climate 판정 중간값으로만 사용
- Moisture·Fertility와 방향 없는 설정값 제거
- 광물 생성 데이터는 Terrain의 Geology·ResourceId로 한정

완료 기준:

- River 후보가 롤백된 Cell에 River WaterBiome이 남지 않는다.
- Desert-River, Snow-Sea, Cave-Pond 같은 조합을 Cell에서 조회한다.
- Terrain·Water 런타임 변경이 고정 CellBiome을 자동 변경하지 않는다.

### 4. Chunk Runtime 상태와 스트리밍 중심 적용

적용:

- `ChunkRuntime` 도입
- `Unloaded → Preparing → Ready ↔ Active` 상태 전환 적용
- 교체 가능한 `StreamingTarget Transform` 적용
- 미연결 시 Main Camera 사용
- 가변 RenderRadius와 SimulationRadius 적용

완료 기준:

- Target 이동에 따라 필요한 Chunk 상태가 변한다.
- Entity Render 범위 안·Simulation 범위 밖 Chunk는 Entity가 표시되지만 Tick하지 않는다.
- Water는 Simulation 범위의 Active Chunk만 갱신하고, 비활성 Chunk와 맞닿은 경계 Cell은 이웃 활성화 전까지 Frontier에 보류한다.
- Target Transform을 교체해도 스트리밍 로직을 수정할 필요가 없다.

### 5. 유한 Bounds Render 스트리밍과 Pool 적용

적용:

- 현재 전체 `WorldChunkView[,]`를 Rendered Patch Dictionary로 교체
- `WorldRenderPatchView` Pool 구성
- Pool View에 새 Patch 좌표·Mesh·Road Mask 재할당
- Render 범위 이탈 View를 삭제하지 않고 Pool에 반환
- 유한 Bounds에서는 범위 밖 Chunk 요청만 제외

완료 기준:

- 전체 유한 월드를 한 번에 표시하지 않는다.
- Target 이동과 반경 변경에 따라 Patch가 활성화·비활성화된다.
- 반환된 View가 다른 Patch에 재사용된다.
- Pool 수가 전체 월드 Chunk 수에 비례하지 않는다.

### 6. Chunk별 Cache와 경계 갱신 적용

적용:

- 전체 월드 SurfaceCache·NavigationCache·WorldExposureCache를 Chunk 단위로 분리
- Preparing 과정에서 해당 Chunk Cache 구성
- 이웃 Chunk 준비·해제 시 경계 Cell과 영향 Patch만 갱신
- Unloaded 이웃을 Empty로 판정하는 질의 제거
- Cache가 없는 Chunk의 사실 데이터는 WorldData에서 직접 조회하고 Cache에 보관하지 않음
- Edit ChangeSet을 Chunk 좌표와 Local 범위 기준으로 적용

완료 기준:

- 전체 Cache `RebuildAll` 없이 Chunk 활성화와 Edit가 반영된다.
- Chunk 경계의 Shoulder·Corner·Navigation 결과가 이웃 준비 후 일치한다.

### 7. Entity·Building·Road/Way 스트리밍 적용

적용:

- `WorldChunkColumn → Chunk`, `ChunkData → ChunkSection`으로 Minecraft 용어를 먼저 통일
- Entity 원본의 소유 Chunk와 참조 Chunk Index 등록
- Dynamic Entity의 X/Z Chunk 경계 이동 시 원본 소유권과 Runtime Index 이전
- 다중 Chunk Building의 원본 소유와 BuildingCells·TerrainAnchorCells 참조 분리
- `EntityRenderRadius` 범위 Entity Controller 표시, Active Chunk별 Entity Tick 적용
- Terrain·Entity·Simulation 반경의 크기 순서 제한 제거
- 세 반경의 합집합으로 Chunk 데이터와 Cache 준비 범위 계산
- 준비된 Chunk만 사용해 Road Topology와 Building Way Graph 구성
- Chunk 준비·해제 시 Road와 Building Way 경계 연결 보류·재연결 적용
- 이웃 Unloaded로 인해 Road·Way 사실 데이터를 수정하는 경로 제거

완료 기준:

- Entity가 Chunk 경계를 넘어도 중복되거나 소실되지 않는다.
- 다중 Chunk Building의 점유·Terrain 보호가 Chunk 경계에서 유지된다.
- Entity Render 범위를 벗어나면 Controller가 반환되고, 재진입하면 같은 EntityData로 복원된다.
- Simulation 범위 밖 Entity는 렌더 가능하더라도 Tick하지 않는다.
- 이웃 Chunk 활성화 후 Road와 Building 외부 Way가 다시 연결된다.

### 8. Water 스트리밍 적용

적용:

- Frontier를 `ChunkCoordinate`별 상태로 분리하고 Active Chunk만 Water Simulation에 참여
- Active 이탈 시 미완료 Stage를 폐기하고 해당 파동을 Frontier로 되돌려, 재진입 후 같은 입력으로 다시 계산
- 비활성 이웃과 맞닿은 경계 Cell은 Empty나 벽으로 해석하지 않고 해당 이웃 활성화 전까지 보류
- 처리 가능한 Chunk들의 Cell은 기존과 같은 정렬된 파동으로 묶고, 모든 Stage가 끝난 뒤 한 번에 Commit
- WaterBody 해석 범위를 준비된 Chunk로 제한하고, Frontier에서 전체 월드 Cell 수 기반 용량 의존 제거
- Source·Dynamic 판정, `ResolveDesiredWater`, WaterType 갱신과 Mesh 변경 통지는 기존 규칙 유지

완료 기준:

- Ready이지만 비Active인 물이 변하지 않는다.
- 이웃 Chunk 활성화 전후 물이 Unloaded 영역을 Empty나 벽으로 오판하지 않는다.
- 빈 Cell을 Source로 보정하는 경로가 존재하지 않는다.

### 9. 3D Density 기반 절대 좌표 생성 교체

교체 목표:

- `WorldType(Finite/Infinite)`과 홀수 `InitialChunkCountXZ` 적용
- Chunk `(0,0)`을 중심으로 초기 N×N 범위 생성
- 절대 XYZ 기반 Preliminary Terrain Density와 Preliminary Surface 적용
- `EdgeLowering`, 전체 월드 경계, 유한 외곽 Sea Flood Fill 의존 제거
- Continental Field 기반 해저·해안 Density 적용
- 불규칙 경계와 크기별 깊이를 갖는 Lake/Pond Basin Field 적용
- 실제 Field 거리와 연속 폭을 사용하는 River Field 적용
- Sea·River·Lake/Pond의 통합 Water Distribution과 Density Modifier 적용
- 동굴 없는 연결 지형을 기존 Filled 0~5로 변환
- 정확한 Water Amount와 Source/Dynamic 실행 정책 적용
- 최종 생성 결과에 대해 CellBiome 확정
- 필요한 Y Section만 조립
- Infinite에서 스트리밍 요청 Chunk를 같은 Field 경로로 추가 생성

현재 교체 상태:

- 전체 월드 배열을 소유하던 `WorldBuildData`를 제거했다.
- 초기 생성과 스트리밍 생성은 동일한 `WorldChunkBuildInput`을 사용한다.
- 생성 중간 데이터는 `GenerationWorkingData`가 소유하고 완료 시 최종 Column 소유권을 `WorldChunkBuildData`로 한 번만 이전한다.
- `WorldDataBuilder.ApplyChunk`만 최종 Column을 `CellData`와 필요한 `ChunkSection`으로 조립한다.
- 기존 `SeaWaterDistributionStage`, `SeaDensityField`, `WaterTerrainDensityStage`, `WaterColumnDistribution`을 제거했다.
- Landform과 Water가 함께 사용하는 외부 계약을 `WorldNoiseRouter`, `WorldFieldSample`, `WorldPatternResolver`, `WorldShapeProfile`로 통일했다.
- `WorldNoiseRouter`는 절대 Cell XZ와 Seed로 PatternRegion, Continentalness, Erosion, Weirdness, PeaksValleys, Roughness, Detail, SeaRegion, SeaDetail을 만든다.
- `PatternRegion`은 Landform 영역과 전이 가중치만 결정한다. `WorldPatternResolver`는 이를 사용해 Smooth, Rugged, Mountain의 영역 영향을 계산하고, Canyon은 영역 영향과 방향성 중심축 거리를 함께 사용한다. Sea는 독립적인 Water Pattern 채널을 사용한다.
- `TerrainBaseDensitySettings`가 대륙·침식에 따른 공통 표면과 수직 Density를 한 번 계산한다. 패턴은 공통 기준 높이를 다시 만들지 않고 상대적인 표면·세부 기여만 반환한다.
- Smooth는 Weirdness 기반 완만한 높이 변화, Rugged는 PeaksValleys와 Roughness 기반 양·음 굴곡을 만든다.
- Mountain은 패턴 경계 혼합과 `CenterProximityByRegion`을 분리한다. 저주파 Erosion은 중심 접근도의 진행 속도만 바꾸며, 단조 3차 보간을 사용하는 `HeightByCenterProximity` Curve가 최종 거시 높이를 만든다. 높이에 직접 더하는 Ridge Noise나 Curve 구간마다 기울기를 0으로 만드는 보간은 사용하지 않는다.
- Canyon은 `Continentalness - Erosion = 0`인 저주파 연속 등위선을 중심축으로 사용한다. 같은 Field의 국소 기울기로 축까지의 Cell 거리를 계산한다.
- Canyon 깊이는 양 끝의 기울기가 0인 `SmootherStep(중심 접근도)` 함수로 계산한다. 경계에서는 완만하게 시작하고 중간에서 가장 가파르며 중심에서 다시 완만해진다. 고주파 Roughness와 PeaksValleys는 Canyon 폭·깊이·바닥에 사용하지 않는다.
- 영역 가중치 정규화는 패턴 경계의 연속 혼합에만 사용한다. Mountain 중심 접근도와 Canyon 축 접근도는 별도 형상 값이며 임의 최소 높이·깊이 보정이나 패턴 우선순위를 사용하지 않는다.
- `SeaPatternGenerator`는 SeaRegion을 Sea 경계에서 중심까지의 연속적인 접근도로 변환한다. 수심은 단일 단조 S자 Curve를 따라 경계에서 완만하게 시작하고, 중간에서 가팔라지며, 최대 수심에 도달하면 평탄한 심해저 구간을 유지한다. 넓은 Sea일수록 중심 접근도 1인 영역이 넓어져 깊은 해저도 넓어진다.
- Sea의 목표 해저는 `WaterSurfaceUnits - DepthUnits + SeabedDetailUnits`로 산출한다. SeaDetail은 영역이나 중심 접근도를 흔들지 않고 해저 높이에만 작은 굴곡을 추가한다.
- Water Pattern은 `TargetBedSurfaceUnits`, `DepthUnits`, `WaterTopUnits`, `WaterType`, `WaterRegionKey`, `InteriorProximity`를 공통 출력한다. 여러 Water Pattern이 겹치면 상대 하강량을 합산하지 않고 더 낮은 목표 바닥을 선택한다.
- `WorldShapeComposer`는 Sea 경계에서 Landform과 목표 해저를 연속적으로 섞고, 중심 접근도 1에서는 Sea가 최종 표면을 완전히 소유한다. Landform의 고주파 세부 굴곡도 Sea 중심에 가까워질수록 제거하고 SeaDetail로 교체한다.
- `WorldDensityField`는 합성된 `WorldShapeProfile`로 최종 XYZ Density를 한 번만 계산한다. Preliminary Density를 Final Density로 복사하던 중간 단계는 제거했다.
- 최종 Surface를 한 번 추출한 뒤 Filled 0~5로 한 번만 양자화한다. Water는 각 Column의 Water Pattern 수면과 최종 지형 높이를 비교해 정확한 남은 공간만 기록한다.
- 높이 단차 사이를 연결하는 Water Cell, 생성 단계 Falling, Dynamic River Pattern은 만들지 않는다. 생성된 Water의 낙하와 넘침은 기존 Water Runtime이 처리한다.
- 패턴 디버거는 실제 생성과 동일한 `WorldNoiseRouter`와 `WorldPatternResolver` 결과를 표시한다. Sea 영역, S자 수심, 최종 Water 존재 예측을 분리해 Landform 표시와 혼동하지 않는다. 전체 미리보기에서 선택한 Pixel은 실제 샘플 Cell과 1:1로 대응하고 Scene 표시는 `WorldOrigin` 변환을 적용한다.
- Noise 입력은 `double`, Lattice와 Hash 좌표는 `long`을 사용하여 먼 절대 좌표에서 Chunk 경계 정밀도가 먼저 손실되지 않게 했다.
- Chunk는 1 Cell Halo를 포함해 이웃과 같은 절대 좌표 Field를 다시 계산하며, Loaded 이웃이나 생성 순서를 참조하지 않는다.
- Final Surface는 생성 중 `float`로 유지하고 `WorldColumnBuildData`를 만들 때만 Filled 0~5 단위로 한 번 양자화한다.
- 지하 Cave는 만들지 않으며 최종 Surface 아래를 연결된 고체로 조립한다.

생성 교체 하위 로드맵:

1. 연속 Landform/Terrain 기반 교체: 완료
2. World Pattern 공통 계약과 Sea 위치·수면·해저 Density 연결: 완료
3. River Pattern 공통 출력 연결: 미진행
4. Lake/Pond Pattern 공통 출력 연결: 미진행
5. Water Pattern 결합부와 Runtime 낙하 확인: 미진행
6. Landform/Water 결과 기반 최종 Biome 확정과 이전 River/Lake 생성 설정 제거: 미진행

연속성 계약:

- Field 입력은 Chunk Local 좌표가 아니라 절대 World 좌표다.
- 이웃 Chunk 존재 여부와 생성 요청 순서는 Field 결과에 영향을 주지 않는다.
- 지형 패턴 전이는 단일 패턴 선택이 아니라 공통 Field 가중치 Curve와 정규화 혼합 결과다.
- 경계 Halo는 이웃 결과 복사가 아니라 같은 절대 좌표 재계산이다.
- 수직 Filled 양자화는 최종 출력 직전에만 수행한다.

완료 기준:

- 같은 Seed와 Chunk 좌표는 요청 순서와 관계없이 같은 결과를 만든다.
- 인접 Chunk Terrain·수역·Biome이 경계에서 연속된다.
- Finite와 Infinite의 같은 좌표 생성 결과가 일치한다.
- Sea·River·Lake/Pond가 Chunk 경계에서 중복·단절되지 않는다.
- 완전히 빈 Y ChunkSection을 생성하지 않는다.

### 10. Floating Origin과 원거리 좌표 정밀도 적용

적용:

- 논리 좌표와 Unity 표현 좌표 분리
- 거리 기준 Floating Origin 이동 적용

완료 기준:

- 원점의 양수·음수 방향에서 같은 규칙으로 Chunk가 생성된다.
- 먼 거리 이동 후에도 Mesh·Entity·선택 좌표가 일치한다.
- Origin 이동이 Cell·Chunk·Entity 논리 좌표를 변경하지 않는다.

### 11. 새 Chunk 저장 구조 적용

이 단계는 1~10단계 동작 확인 후 진행한다.

적용:

- WorldMetadata 저장
- Chunk 또는 Region 단위 Snapshot 저장·로드
- 저장 데이터가 없으면 Seed 생성 경로 사용
- Dirty Chunk Unload 전 저장 완료
- Entity 소유권, Water 진행 상태, 변경된 Cell 사실 데이터 저장
- 기존 저장 Codec과 호환·마이그레이션 코드 제거

완료 기준:

- 저장된 Chunk는 변경 상태로 복원된다.
- 저장되지 않은 Chunk는 같은 Seed 결과로 재생성된다.
- Load가 전체 무한 월드를 읽지 않고 초기 Render 범위만 준비한다.

### 전체 로드맵 완료 후 재검토

- Unloaded 목적지까지 이어지는 Road/Way 광역 탐색 구조
- 준비된 Chunk 내부의 상세 Way 탐색과 Unloaded Chunk의 광역 경로 정보를 연결하는 방식
- 원거리 Entity가 StreamingTarget과 무관하게 Chunk 준비를 요청할 수 있는지에 대한 정책

7단계에서는 Unloaded 이웃을 경로 없음으로 판정하지 않고 경계 연결만 보류한다. 원거리 목적지 탐색을 위한 별도 광역 Graph나 Entity 주도 Chunk Load는 1~11단계를 완료하기 전에 추가하지 않는다.

## 구현 중 변경 금지 항목

- `Resident` 상태를 다시 추가하지 않는다.
- Unloaded를 Empty Cell 또는 막힌 Cell로 보정하지 않는다.
- 전체 월드 배열 Cache로 되돌리지 않는다.
- Chunk의 모든 Y Section을 미리 생성하지 않는다.
- `EnvironmentData` 또는 방향이 확정되지 않은 Temperature·Moisture·Fertility 저장 구조를 다시 추가하지 않는다.
- 후보 River·Lake 정보를 최종 CellBiome으로 확정하지 않는다.
- 현재 WaterType 변화로 고정 WaterBiome을 자동 변경하지 않는다.
- Main Camera를 Streaming Controller의 고정 의존성으로 만들지 않는다.
- 유한 단계 전용 WorldData나 Renderer 구조를 별도로 만들지 않는다.
- 저장 구현을 앞 단계에 섞거나 기존 저장 호환을 추가하지 않는다.
- 문서에 없는 생태·농업·문명·오프라인 Simulation 보정 기능을 추가하지 않는다.
