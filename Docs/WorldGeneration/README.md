# 월드 생성 구조

현재 River 형태와 교차부 Ground는 사용자 실행 검증에서 해결 확인을 받았다. 생성 버전은 10이다. 높이 출력 통합과 캐시 수명 변경도 사용자 실행 검증을 통과했다.

## 데이터 흐름

`Seed + Settings → TerrainMap → HydrologyMap → Cell/Filled → WaterSystem·메시`

- TerrainMap은 독립적인 원본이다. Hydrology는 이를 읽고 최종 지면 Override를 제공한다.
- 생성 결과는 절대 좌표로 결정하며 Tile 순서·분할·병렬 실행에 의존하지 않는다.
- 전역 연결 Graph나 이웃 최종 결과의 재귀 조회, 물 시뮬레이션 후 반복 지형 보정은 사용하지 않는다.

## Cell과 물

- 한 Cell은 5 Filled 단계다. Terrain은 아래부터, 물은 그 위의 남은 공간을 차지한다.
- 생성하는 물은 바닥부터 수면까지 모두 Source다. Dynamic은 WaterSystem Update에서 생성하며 저장 데이터 복원은 별개다.
- 물 Amount는 남은 용량에 대한 비율이다. 렌더 높이와 실제 공급 판정 높이를 구분한다.
- 작은 높이차는 Source 단차로 허용한다. 높은 Source와 낮은 수역 사이 빈 Cell은 Dynamic 낙하 공간으로 사용한다.

## 생성 책임

- Scheduler가 Terrain Builder와 Hydrology Drawer를 직접 실행한다. 동일 Terrain 생성 요청은 하나의 완료 Task를 공유하며 성공·실패를 같은 경로로 전달한다. 대기 호출의 취소는 별도로 처리한다.
- Drawer는 유한 범위의 도형 후보를 조회한다.
- Brush는 Terrain 기반 수면·수심·지형 전이를 계산한다.
- Collector는 기여를 모으고 외부 경계를 결정한다.
- OverlapResolver는 겹침을 해석하고 HeightSolver는 최종 정수 높이를 계산한다.
- Painter는 Feature 인덱스와 Pixel을 기록한다. Materializer는 확정된 높이를 실제 Cell로 변환한다.

Hydrology 출력은 Sea·마른 전이를 포함해 정수 Filled로 확정한다. Painter는 미확정 float 출력을 받지 않는다. 저장 Pixel 필드 자체는 기존 float 형식을 유지하므로 형식 변경은 아니다. 얕은 Sea 물은 출력 단계에서 마른 Pixel로 바뀔 수 있다(이전에는 최종 Cell 변환에서 사라짐). Materializer의 공통 반올림은 Terrain 원본을 위해 유지한다.

Scheduler의 준비 작업이 모두 끝난 시점에 현재 스트리밍/디버거 요구 밖 Tile을 제거한다. 남은 Tile의 경계 halo에 영향을 주는 Brush는 유지한다. 진행 중 작업은 제거하지 않는다. 수요/저장 revision이 같으면 재정리를 생략한다. 실제 Cell과 저장은 삭제하지 않으며 제거된 Map은 재방문 시 재생성한다. 연속 작업 중에는 정리가 지연되므로 엄격한 메모리 상한은 아니다. 재방문 비용과 디버거의 과거 Map 표시 범위는 이전과 달라질 수 있다.

도형 코드는 `WaterBrushCatalog`(캐시), `WaterBrushFactory`(도형 생성), `BasinWaterBrush`/`RiverWaterBrush`(샘플링), `WaterMapGeometry`(공통 좌표·Profile·공간 인덱스·수학) 파일로 나눈다. 파일 분리는 새 런타임 계층을 추가하지 않는다.

## 수역 규칙

### Lake/Pond

원본 Terrain의 전체 footprint 평균 이하에서 정수 수면을 선택한다. Shore와 성토 결과는 평균에 포함하지 않는다. 바닥 깊이는 월드 바닥 안에서 결정한다. 물을 표현할 높이가 없으면 도형을 생성하지 않는다.

겹침에서는 낮은 수면의 Basin을 선택하며 연결 그룹 전체를 평탄화하지 않는다. 동수면 동률은 내부 영향과 FeatureKey로 결정한다.

### River

- 각 Stroke는 연속 좌표 중심선을 가지며 Terrain 조회만 정수 좌표로 반올림한다. Node당 이동은 약 1 Cell이다.
- 이동 거리 기반 곡률을 누적한다. 공간 노이즈는 굽이 강도에 반영하며 지형 우회는 굽이를 완화한다.
- Branch는 부모의 연속 좌표에서 시작한다. Root의 분기 예산은 자손 전체에 적용한다.
- 수면은 Terrain을 참고해 위치별로 결정한다. Basin 평균 상한은 적용하지 않는다.
- 수중 공간은 최소 한 Filled 깊이에서 중심 수심까지 이어진다. 외부 지형 전이는 한 Cell 경계와 강폭 절반 범위를 사용하며 중심선 영향 반경은 width+1이다.
- 교차부는 수면 선택을 유지하면서 수중 Ground의 최솟값을 합성한다. 같은 Brush에서는 연속 Segment 구간별 최근접 단면만 평가한다.

### 합성과 경계

Sea, Basin 물 내부, River 물 내부, 마른 지형 순으로 우선한다. River 수면의 대표 선택은 FeatureKey 기준이다. 마른 기여는 높은 지면을 유지한다. 외부 마른 Cell은 인접 Lake/Pond/River 수면보다 한 Filled 이상 높게 결정한다. 후보 조회는 도형 영향 범위와 외부 경계 halo를 포함한다.

Sea 수면 규칙은 기존 동작을 유지한다. 의도적 River–Sea/Basin 연결은 별도 기능이다.

## 청크 스트리밍

StreamingCoordinator는 준비·활성화·완전 언로드 대기 목록을 관리한다. 준비·활성화는 가까운 청크, 언로드는 표시 범위 밖 먼 청크부터 처리한다. 타깃 변경 시 대기를 다시 계산하고, 범위 밖 시뮬레이션은 언로드 대기 중에도 중지한다.

WorldGenerationSettings의 Streaming Processing에서 `mapBuildConcurrency`(맵 동시 작업), `chunkPreparePerFrame`(신규 생성/로드·캐시 준비), `chunkActivatePerFrame`(활성화), `chunkUnloadPerFrame`(저장·완전 해제), `meshPatchPerFrame`(신규/갱신 합산 패치)을 관리한다. 기본값은 각각 2/1/1/1/2다. 메시 큐 사이에서도 가까운 패치를 먼저 처리한다. 시간 예산은 사용하지 않는다.

한 번의 스트리밍 Update에서 물 상태 알림·물 대기 목록 확정·언로드 후 수역 재계산·전체 저장 상태 기록은 각각 필요한 경우 한 번 처리한다. 청크 데이터 저장은 개별적으로 유지한다. 파일 I/O와 단일 메시 작업은 여전히 동기 작업이다.

저장 파일의 기존 스트리밍 필드 배치는 호환성을 위해 유지한다. 실행 한도와 범위는 저장값 대신 현재 설정 에셋을 적용하며 추가 실행 한도를 저장 형식에 넣지 않는다. 생성 버전은 변경하지 않는다.

## 현재 설정과 검증

River는 고도차 테스트 설정(발생 확률 1, Node 160/200/240, 폭 5~9, 지형 우회 0.15)을 유지한다. Lake/Pond 발생 확률은 0.12다. 최종 게임 밀도 확정을 의미하지 않는다.

[회귀 검사](../../Tests/WorldGeneration/README.md)는 Cell 배치·실제 물 갱신·경로·Tile 결정론을 검사한다. 유한 표본 통과와 사용자 시각 검증을 모든 Seed에 대한 보장으로 확대하지 않는다. 성능 전용 계측과 실제 청크 저장/재로드 통합 검증은 별도다.

생성 결과가 바뀌면 저장 생성 버전을 올리고 새 월드로 검증한다. 결과를 유지하는 정리는 버전을 올리지 않는다. 기존 저장은 자동 이관하지 않는다.
