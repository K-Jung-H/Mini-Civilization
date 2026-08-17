# 무한 월드 청크 스트리밍 설계 및 적용 로드맵

## 문서 목적

최종 목표는 Seed와 절대 좌표를 기준으로 X/Z 방향에 연속 생성되는 무한 월드다. 필요한 청크만 준비·렌더링·시뮬레이션하며, 렌더 객체는 Pool로 재사용한다.

먼저 같은 구조에 유한 Bounds만 적용해 한 번에 모든 청크를 활성화하지 않는 범위를 확인한다. 이후 Bounds 제한과 유한 월드 전용 생성 규칙만 제거해 무한 월드로 전환한다. 유한 단계용 별도 데이터 구조를 만들지 않는다.

이 문서는 확정된 최종 구조와 구현 순서를 고정한다. 문서에 없는 환경·생태·문명 기능이나 이전 저장 호환 로직을 구현 과정에서 추가하지 않는다.

## 현재 진행 상태

기준일: 2026-08-17

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
| 9. 절대 좌표 Field 생성 | 구현 완료·직접 테스트 필요 | WorldType, 원점 중심 초기 범위, 절대 좌표 Terrain·Climate·Continental·River·Lake Field와 요청 Chunk 추가 생성을 적용했다. |
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
→ 절대 좌표 기반 Terrain·Mountain·Continental Field 계산
→ Sea·Lake/Pond·River Field 계산
→ River 실행 정책(Source/Dynamic) 결정
→ 최종 Terrain·Water 구조 확정
→ CellBiome 확정
→ 필요한 Y Section만 조립
→ Chunk Cache 준비
→ Ready
```

### 생성 계약

- 생성기는 월드 전체 Size나 가장자리 좌표를 입력으로 사용하지 않는다.
- `EdgeLowering`, 유한 외곽 Flood Fill, 월드 크기 기반 Latitude를 사용하지 않는다.
- Terrain·Mountain·Climate·Continental·River Channel·Lake Basin은 Seed와 절대 좌표를 사용한다.
- Sea는 Continental Field가 Ocean이고 최종 지형이 Sea Level보다 낮은 Cell에만 생성한다.
- Lake와 Pond는 같은 Lake Basin Field를 사용하며 크기로 구분하고, 크기가 클수록 중심부 최대 깊이가 커진다.
- 수역 우선순위는 `Sea > Lake/Pond > River > None`이다.
- River Channel은 연속 Field로 만들며 하류로 갈수록 폭이 넓고 수심은 얕아진다.
- River가 끝나는 구간은 폭과 절삭 깊이를 완만하게 줄여 주변 지형으로 닫는다.
- River 경로 생성은 하나이며 `Source/Dynamic`은 마지막 실행 정책이다.
- 급한 하강 구간은 실제 WaterFlow 확산 가능 범위가 있을 때 Dynamic을 사용하고, 완만하거나 긴 수평 구간은 Source를 사용한다.
- Dynamic 구간의 WaterData를 직접 배치하지 않고 Source에서 기존 WaterFlow가 확산한다.
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

### 9. 절대 좌표 Field 생성 적용

적용:

- `WorldType(Finite/Infinite)`과 홀수 `InitialChunkCountXZ` 적용
- Chunk `(0,0)`을 중심으로 초기 N×N 범위 생성
- Terrain Noise 입력을 절대 X/Z로 교체
- `EdgeLowering`, 전체 월드 경계, 유한 외곽 Sea Flood Fill 의존 제거
- Continental Field 기반 Sea 적용
- 크기·깊이가 연결된 Lake/Pond Basin Field 적용
- 연속 River Channel Field와 공통 경로·폭·깊이 적용
- River 경로와 분리된 Source/Dynamic 실행 정책 적용
- 최종 생성 결과에 대해 CellBiome 확정
- 필요한 Y Section만 조립
- Infinite에서 스트리밍 요청 Chunk를 같은 Field 경로로 추가 생성

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
