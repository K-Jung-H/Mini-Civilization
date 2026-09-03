# 3단계 — 무방향 River Graph, Natural Endpoint, Junction

## 목적과 산출물

확정된 Sea/Lake/Pond Topology를 입력으로 무방향 `EndpointCatalog`와 `RiverGraph`를
만든다. Graph는 물의 방향, Head, End를 저장하거나 추론하지 않는다. 흐름 방향은
5단계 이후 WaterSystem이 완성된 지형과 WaterCell로 결정한다.

```text
TopologyRegion
  → EndpointCatalog
  → River Edge 후보와 경로
  → RiverGraph (EdgeId, Corridor, Junction Node)
```

이 단계의 산출물은 각 Edge의 경로, Corridor 지형 목표, Natural Endpoint 전이,
Junction Node 계획이다. Chunk Raster 조립·스트리밍·WaterSystem 변경은 4~5단계
책임이다.

## 확정 계약

- Endpoint 종류는 `Lake`, `Pond`, `Sea`, `Natural`뿐이다.
- Lake/Pond Component는 모두 하나의 Endpoint 후보를 제공한다. 선택되지 않거나
  경로가 없는 Component는 독립 Basin으로 남는다.
- Sea Endpoint는 Sea 해안에서 제공한다. 후보 해상도는
  `HydrologyMap.RouteSampleSpacingCells`를 사용한다.
- Natural Endpoint는 다른 유효 연결 Case와 경쟁하는 종단 Case다. Head/End
  발생률이 아니다.
- Endpoint Kind별 가중치는 없다. 후보는 거리 오름차순으로 평가하며, 거리가
  같으면 canonical `EdgeId`로만 순서를 정한다.
- River는 다른 Basin 내부를 통과하지 않는다. 해당 Basin의 Endpoint Cell만
  연결 위치가 될 수 있다.
- 경로 실패·Junction 거부는 그 Edge 후보만 무효로 만든다. EndpointCatalog,
  Topology, 월드 생성은 취소하지 않는다.
- 후보 평가·경로 탐색·Junction 판정은 World Seed와 절대 좌표/식별자만 사용한다.
  요청 순서, 작업 완료 순서, Dictionary 순회는 결과에 영향을 주지 않는다.

## Natural Endpoint 선택

Natural Endpoint의 비율은 Region에 미리 고정하는 발생률이 아니다. 어떤 연결이
실제로 가능한지는 경로 탐색 뒤에만 알 수 있으므로, Graph 후보 선택에서 결정한다.

1. 한 Endpoint의 경쟁 Case를 `Lake`, `Pond`, `Sea`, `Natural`로
   분류한다.
2. 각 Case는 최대 하나의 **가장 가까운 경로 가능 후보**를 제공한다. 가까운
   후보의 경로 탐색이 실패한 경우에만 같은 Case의 다음 후보를 확인한다.
3. 유효 Case 수를 `N`으로 둔다. `Natural` 자신은 항상 이 수에 포함한다.
4. World Seed와 평가 기준 EndpointId로 만든 결정적 난수가 `1 / N`을 통과하면
   Natural Case를 선택한다.
5. Natural이 선택되지 않으면, 남은 유효 후보 중 거리가 가장 가까운 후보를
   선택한다. Endpoint 종류 가중치나 목표 길이 보정은 사용하지 않는다.

한 Endpoint는 위 순서에서 처음 경로가 확정된 Edge 하나만 제안한다. 같은
`EdgeId`의 제안은 하나로 합친 뒤 canonical `EdgeId` 오름차순으로 Junction을
판정한다. 이 순서는 물의 방향이나 생성 작업 순서가 아니다.

Junction 확률이 실패하면 작은 `EdgeId`를 논리적 기존 Edge로 유지하고 큰
`EdgeId` 후보만 무효로 한다. Junction 거부는 Endpoint의 다음 후보 탐색을
유발하지 않는다. 따라서 Junction 결과가 후보 재탐색을 연쇄시키지 않으며,
별도 전역·영구 Edge/Junction Store도 만들지 않는다.

`Natural`이 유일한 유효 Case이면 `N = 1`이므로 Natural을 선택한다. Lake/Pond/
Sea Case가 늘수록 Natural 종단의 확률은 낮아진다. Junction은 Edge가 선택된 뒤
Corridor 상호작용에서만 생성되는 내부 Node이므로 `N`에 포함하지 않는다. 위 절차의
`평가 기준 Endpoint`는 후보 평가의 기준일 뿐, 물의 흐름 방향을 의미하지 않는다.

Natural 후보는 `HydrologyMap.PlanningRegionSizeCells` 단위 Graph Planning Region마다
하나를 만든다. 해당 Region의 물·Basin 보호 영역이 아닌 Cell 전체에서 Seed 기반
점수가 가장 높은 Cell을 선택한다. 이는 재시도나 숨은 위치 격자가 아니며, Region에
유효한 육지 Cell이 없을 때만 Natural Case가 없다.

## Edge 후보와 경로

Edge별 Seed 기반 `ConnectionRadiusCells` 안에 있는 두 Endpoint의 쌍만 Edge 후보가
된다. 거리는 두 절대 XZ Cell 좌표의 유클리드 거리다. 이는 Chunk, Route Sample,
경로의 굴곡 길이가 아니다.

- 현재 설정 Asset은 한 Chunk가 XZ 방향 8 Cell이다.
- 연결 반경은 Inspector의 `5~10 Chunk` 범위를 `ChunkCellCountXZ`와 곱해 Cell 범위로
  바꾼 뒤, EdgeId Seed로 그 안의 상한 하나를 결정한다. 현재 Asset에서는 40~80 Cell이다.
- 이 값은 후보 탐색·Graph Region 의존 범위의 상한이다. 최소 연결 길이를 만들지
  않으며, 멀리 있는 Endpoint와 연결하도록 강제하지 않는다.

후보 Edge는 거리 오름차순, 같은 거리에서는 canonical `EdgeId` 순서로 평가한다.
각 Edge는 지형 고도 변화, 횡단 경사, 계곡 선호, Route Variation, Basin 보호
영역을 사용해 경로를 찾는다.

`ElevationChangeCost`는 인접 경로 Cell의 **절대 고도 차**에 적용하는 대칭 비용이다.
값이 높을수록 능선·급경사 관통과 과도한 Corridor 절삭을 피하고, 값이 낮을수록
짧은 경로를 택한다. `UphillCost` 같은 방향 비용은 사용하지 않는다.

## Natural Endpoint 전이

Natural Endpoint에서 Edge 내부로 갈수록 폭·깊이·수면 변화량·지형 절삭량은
0에서 정상 River Profile까지 같은 전이 배율을 사용한다.

전이 길이는 `NaturalEndpointTransitionCells = 16`이며 단위는 Cell이다.

전이 배율은 직접 0~1 Curve를 저장하지 않는다. 사용자가 확정한 Rate Curve의
고정 제어점은 다음과 같다.

```text
정규화 진행도 t:  0 ── 0.25 ── 0.5 ── 0.75 ── 1
Rate(t):           0 ─── 1 ──── 2 ──── 2 ──── 1
```

`Rate(t)`는 위 제어점 사이를 보간한 변화 속도다. 실제 전이 배율은 다음으로
계산해 항상 0에서 시작해 1로 끝난다.

```text
Transition(t) = integral(0..t, Rate(u) du) / integral(0..1, Rate(u) du)
```

따라서 `2`는 River 폭·깊이를 두 배로 만드는 배율이 아니다. 전이의 변화 속도를
나타낼 뿐이며, 정상 River Profile을 넘는 돌출부를 만들지 않는다.

## River Corridor

- Edge별 폭·깊이·단면·바닥 Noise·수면 Profile은 canonical `EdgeId` 기반 Seed로
  결정한다.
- Corridor는 최종 Terrain 목표를 낮춘다. 계획 Water가 기존 지형 위에 놓이지
  않는다.
- 낙차는 WaterCell을 추가해 연결하지 않는다. 해당 Corridor와 연결 구간의 지면·
  수면 Profile을 조정해 지형이 자연스러운 제방 역할을 하게 한다.
- `CrossSlopeCost`, `ValleyPreference`, `RouteVariationField`,
  `RouteVariationCost`, Corridor 폭·깊이·낙차 설정은 방향과 무관한 기존 의미를
  유지한다.
- `CorridorExposureCost`로 비접속 교차를 비용으로 완화하지 않는다. Junction이
  성립하지 않는 교차는 후보 Edge를 무효로 처리한다.

## RiverGraphStore 소유권

`RiverGraphStore`는 후보, 경로, 활성 판정을 서로 다른 불변 결과로 관리한다.

```text
RiverGraphStore
├ ProposalRegion[TopologyRegionKey]
│  └ 해당 core Endpoint가 anchor인 Edge 후보
├ RoutePlan[EdgeId]
│  └ route / corridor / Natural transition
├ Activity[EdgeId]
│  └ Junction 거부에 따른 final active 상태
└ SpatialIndexRegion[TopologyRegionKey]
   └ core와 교차하는 active RoutePlan 참조 + core Junction
```

- Proposal Region은 core 내부 Endpoint만 anchor로 소유한다. 따라서 같은 Edge가
  요청 사각형마다 후보로 다시 만들어지지 않는다.
- 같은 `EdgeId`의 경로는 `RoutePlan` 하나만 가진다. 여러 SpatialIndexRegion은 그
  객체를 참조할 뿐 Route 배열을 복사하지 않는다.
- `Activity`는 주변 Proposal Region의 이미 확정된 Route만 읽는다. 후보 Endpoint
  수집이나 A* 경로 탐색을 다시 호출하지 않는다. Route AABB와 실제 Corridor 폭/
  Bank Margin으로 교차 불가능한 쌍을 먼저 제외한 뒤에만 Junction 세부 검사를 한다.
- `SpatialIndexRegion`은 요청 Region의 core에 필요한 Edge/Junction만 노출한다.
  Batch는 이 색인과 공유 EdgePlan을 읽기만 하며, 후보 선택·경로 탐색·Junction
  판정을 다시 하지 않는다.
- Proposal Region은 자신이 Route 생성에 사용한 Topology/Catalog Scope를 참조하는
  동안 유지한다. 인접 Proposal이 같은 Topology/Basin을 다시 만들지 않게 하기
  위한 소유권이며, 마지막 Proposal/Activity 참조가 해제되면 함께 제거된다.
- 큰 EdgeId가 Junction 거부로 무효가 되면 이후 작은 EdgeId가 다른 Junction에서
  무효가 되어도 큰 EdgeId를 재활성화하지 않는다. 재탐색하지 않는 Graph 선택
  계약을 그대로 적용한 결과다.
- SpatialIndexRegion과 Activity가 Scope에서 더 이상 참조되지 않으면 확보한
  RoutePlan/Proposal/Topology 참조를 해제한다. 용량·시간 기반의 숨은 보존 규칙은
  사용하지 않는다.

## Junction

Junction 후보는 새 Edge Corridor와 이미 확정된 Edge Corridor가 교차하거나,
Corridor 폭과 `BankMarginCells`로 유도되는 접근 범위 안에 들어올 때만 만든다.

두 확률 입력을 각각 계산한다.

```text
proximity = closestDistance / (halfWidthA + halfWidthB + BankMarginCells)
alignment = abs(dot(tangentA, tangentB))

JunctionProbability = ProximityChanceCurve(proximity)
                    × AlignmentChanceCurve(alignment)
```

- `proximity`는 0이 가장 가깝고 1이 접근 범위 경계다.
- `alignment`는 0이 직교, 1이 평행/반평행이다. Graph에 흐름 방향이 없으므로
  반평행도 같은 동선 유사도로 취급한다.
- 두 Curve 모두 0~1 확률을 반환한다. 곱셈으로 결합하므로 **가깝고 동선도
  비슷한 경우에만** Junction 확률이 높아진다.
- 후보 통과 여부는 canonical EdgeId 쌍과 절대 후보 좌표의 Seed 기반 난수로
  결정한다. 기존 단일 `JunctionChance` 값은 사용하지 않는다.

Junction이 통과하면 공통 Node를 한 번만 계획한다.

- 공통 수면은 접근 Edge 수면 중 최저값이다.
- 공통 바닥 Terrain 목표는 접근 Edge 바닥 중 최저값이다.
- Node footprint는 모든 접근 Edge 단면 footprint의 합집합이다.
- 더 높은 수면으로 접근하던 Edge는 기존 낙차 전이로 공통 수면까지 연결한다.

이 정책은 Cell 지형·물값의 Edge 우선순위 덮어쓰기, Junction 전용 폭·깊이 상수,
연결 WaterCell을 추가하지 않는다. Junction이 거부되면 canonical `EdgeId`가 작은
Edge를 유지하고 큰 후보 Edge만 무효로 처리하며, 해당 Endpoint는 다음 후보를
재탐색하지 않는다.

## 설정 교체표

| 새 설정/정책 | 상태 | 이전 설정 처리 |
|---|---|---|
| `ConnectionRadiusChunks = 5~10` | 확정. `ChunkCellCountXZ`를 곱한 Cell 범위에서 EdgeId Seed로 상한을 결정한다. | `LengthCells`의 최소·목표 길이 의미를 제거한다. |
| `NaturalEndpointTransitionCells = 16` | 확정 | `NaturalHeadTransitionCells`/`NaturalEndTransitionCells`를 자동 변환하지 않는다. |
| Natural Transition Rate Curve `0, 1, 2, 2, 1` | 확정 | Head/End Curve 두 개를 제거한다. |
| `ElevationChangeCost = 0.8` | 확정 | 대칭적인 기존 `TerrainChangeCost`의 의미만 유지하며 `UphillCost`는 제거한다. |
| `ProximityChanceCurve = 1, 0.9, 0.45, 0.1, 0` | 확정 | 단일 `JunctionChance`를 제거한다. |
| `AlignmentChanceCurve = 0, 0.05, 0.25, 0.75, 1` | 확정 | 단일 `JunctionChance`를 제거한다. |
| Endpoint Kind Weight | 제거 | Lake/Pond/Sea/Natural 가중치를 모두 제거한다. |
| `RiverConnectionChance`, `HeadRoleChance` | 제거 | Basin Endpoint 후보를 확률로 막지 않는다. |
| Junction Node Profile | 확정 | 최저 수면·깊은 바닥·footprint 합집합 정책을 사용한다. |

## 구현 기록 (2026-08-31)

- `WorldHydrology`는 `TopologyRegionStore`, `EndpointCatalogRegionStore`,
  `RiverGraphStore`를 각각 Plan Scope로 보존한다. RiverGraphStore는 EdgeId별
  공유 EdgePlan과 Region별 SpatialIndex를 함께 소유하며, 어느 Store도
  Batch/Chunk가 수명을 소유하지 않는다.
- `RiverGraphRegionBuilder`는 반경 의존 범위의 Catalog를 모아, Endpoint별 Case
  선택·대칭 고도 비용 경로 탐색·Natural Rate 전이·Junction Node 계획을 만든다.
  경로가 확정된 후보를 먼저 합친 뒤 canonical `EdgeId` 순서로 Junction을
  판정하므로, Region 내부의 Anchor 순서는 결과에 영향을 주지 않는다. 같은
  Region을 다시 요청해도 Scope 안에서는 같은 불변 결과를 공유한다.
- Graph 작업은 Catalog 범위의 Topology Region을 같은 Plan Scope에서 먼저
  확보한다. Catalog 생성과 이후 경로 탐색이 같은 Topology 결과를 공유하며,
  Graph 작업 종료 뒤에는 Scope 규칙에 따라 함께 해제된다.
- 같은 Edge가 여러 SpatialIndexRegion과 교차해도 route/corridor 자료는
  EdgePlan 하나만 보관한다. Region 결과에는 공유 객체 참조만 남는다.
- 새 Graph는 `RiverNetwork`에서 방향과 무관한 횡단 경사·계곡·Route Variation과
  `RiverCorridor`의 폭·깊이·낙차 설정만 읽는다. Head/End, 종류 가중치,
  `UphillCost`, `JunctionChance`, Basin 연결/역할 확률은 새 코드가 읽지 않는다.
- 새 설정은 `RiverGraphSettings`와 `RiverGraphSettingsData`에 저장한다. Chunk 단위
  반경은 `WorldGenerationSettings.ChunkCellCountXZ`로 Cell 범위로 변환된다.
- 저장 형식은 새 설정을 기록하도록 버전 30으로 변경했다. 이전 저장과의 호환 계층은
  만들지 않는다.
- 기존 활성 생성·스트리밍·패턴맵 디버거 경로는 4단계 전환 전까지 새 Graph를 읽지
  않는다. 따라서 이 단계만으로 현재 실행 성능이나 월드 결과가 개선됐다고
  단정하지 않는다.

## 완료 기준과 다음 단계 인계

- 새 Graph 데이터에 Head/End, upstream/downstream, 역할 기반 후보 선택이 없다.
- 같은 Seed·설정·절대 좌표에서 Endpoint, Edge, Junction 결과가 요청·작업 순서와
  무관하다.
- 경로 실패는 같은 Case의 다음 후보 확인만 허용한다. Junction 거부는 해당 큰
  Edge 후보만 무효로 하며 Basin/Endpoint를 제거하거나 다음 후보를 재탐색하지
  않는다.
- Natural Endpoint는 Rate Curve 적분 결과로 0에서 정상 Profile까지 연속 전이한다.
- Junction이 없는 Edge는 Corridor/WaterCell을 공유하지 않으며, Junction이 있는
  경우에는 공통 Node 계획 하나만 존재한다.

4단계는 확정된 Topology와 River Graph를 읽기만 한다. Batch가 Graph를 만들거나
Graph Store의 수명을 연장하지 않는다.
