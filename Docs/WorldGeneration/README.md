# 월드 생성 구조

현재 River 형태와 교차부 Ground는 사용자 실행 검증에서 해결 확인을 받았다. 생성 버전은 9다.

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

## 현재 설정과 검증

River는 고도차 테스트 설정(발생 확률 1, Node 160/200/240, 폭 5~9, 지형 우회 0.15)을 유지한다. Lake/Pond 발생 확률은 0.12다. 최종 게임 밀도 확정을 의미하지 않는다.

[회귀 검사](../../Tests/WorldGeneration/README.md)는 Cell 배치·실제 물 갱신·경로·Tile 결정론을 검사한다. 유한 표본 통과와 사용자 시각 검증을 모든 Seed에 대한 보장으로 확대하지 않는다. 성능 전용 계측과 실제 청크 저장/재로드 통합 검증은 별도다.

생성 결과가 바뀌면 저장 생성 버전을 올리고 새 월드로 검증한다. 결과를 유지하는 정리는 버전을 올리지 않는다. 기존 저장은 자동 이관하지 않는다.
