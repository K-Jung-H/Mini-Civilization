# Feature 소유 스트리밍 생성 교체 기록

## 교체 이유

이전 활성 경로는 target 주변의 모든 desired Chunk를 하나의
`WorldGenerationRequest`로 묶고, 전체 `PlanningSnapshot`이 sealed된 뒤에만 첫
Chunk를 만들었다. 측정된 병목은 다음 두 동작이었다.

- Basin 후보마다 최대 Reach 사각형 전체의 Terrain/Potential을 선평가했다.
- River 후보 순서를 정하는 중에도 실제 Route를 반복 탐색했고, 매 탐색마다 큰
  범위의 Topology Evaluation을 새로 만들었다.

이 방식은 결과 패턴 자체가 아니라, 요청 묶음과 임시 Snapshot이 생성 사실의
소유자가 된 것이 문제였다. target 이동은 이 비용을 다시 발생시키는 증폭 요인일
뿐, 첫 요청의 장기 지연도 설명하는 근본 원인이다.

## 활성 구조

```text
Seed + Settings + absolute Cell coordinate
  → BaseTerrain fact
  → Basin Candidate(ComponentId)
  → Basin Allocation / Topology Tile
  → Endpoint Tile
  → River Edge(ordered EdgeId)
  → River Spatial Tile
  → requested Raster
  → one Chunk materialization
```

- `StreamingFeatureWorld`가 위 Feature의 유일한 활성 소유자다.
- Tile과 Edge는 절대 좌표 키로 결정된다. Request, target, Chunk 순서와
  Pattern Debugger 호출 순서는 생성 사실의 입력이 아니다.
- Runtime은 현재 desired Chunk가 요구하는 의존 Tile만 lease하고, target 변경 시
  겹치는 Feature는 유지한다. 새 target 때문에 전체 Hydrology 계획을 취소하거나
  다시 sealed하지 않는다.
- Debugger는 별도 `StreamingFeatureWorld`를 사용하지만 Runtime과 동일한
  Feature/Raster 알고리즘을 사용한다. Debugger lease는 preview가 읽는 Tile
  집합으로 한정된다.
- 고정맵은 같은 절대 좌표 생성기를 쓰며 finite Chunk 경계에서만 요청을 제한한다.

## Basin 교체 계약

`StreamingBasinCandidateEvaluator`의 활성 입력은 전체 97×97과 같은 사전 Terrain
배열이 아니다. Basin 확장 우선순위 탐색이 실제로 도달한 Cell에서만 Base Terrain과
Potential을 읽는다. Candidate의 ComponentId, Seed 위치, Potential 식, 성장 비용,
수면 선택, 경계/내부 거리 계산은 기존 설정과 동일하다.

따라서 비용 절감은 Basin 크기나 발생률을 낮추는 우회가 아니라, 사용하지 않는
후보 사각형의 Terrain/Potential 평가를 제거하는 생성 소유권 교체다.

## River 교체 계약

- Endpoint 종류는 같은 후보 집합에서 거리 오름차순으로 비교한다.
- Natural Endpoint는 종류 수 `N`에 Natural 자신을 포함한 `1/N` 확률로 첫
  우선 후보가 된다.
- 후보 정렬은 Route 탐색을 호출하지 않는다.
- Anchor가 선택한 첫 정식 후보의 정렬된 `EdgeId`만 Route를 한 번 만든다.
- Route가 없으면 그 `EdgeId`는 route 없음으로 확정한다. 다음 후보를 숨겨서
  재시도하지 않는다.
- Route 탐색은 고정 Topology Tile을 읽는다. 매 후보마다 Basin 목록과 Shore
  Transition을 다시 만드는 경로는 활성 코드에 없다.
- Junction은 EdgeId별 상호작용 해석을 사용하며, 거리와 방향 확률을 모두 적용한다.

## Runtime 진행 상태

WorldData metadata 준비는 지형 생성 완료가 아니다. World operation은 Runtime을
활성화한 뒤 최소 하나의 terrain rendering이 실제로 활성화될 때까지 Mesh 단계에
머문다. 그 시점에만 `[WorldStartup] Initial terrain streaming complete`를 출력한다.

## 이전 코드의 상태

`PlanningSnapshot`, request-wide planner, snapshot raster overload는 아직 소스에
남아 있지만 Runtime과 Pattern Debugger의 활성 진입점에서는 참조하지 않는다. 이
잔여 소스는 실제 월드/이동/Debugger 검증 후 제거하는 정리 대상이다. 활성 구조의
fallback이 아니며, 성능이나 결과 생성에 참여하지 않는다.

## 사용자 실행 검증 범위

- 초기 Chunk가 첫 terrain rendering까지 도달하는 시간
- target 이동 뒤 기존 범위를 기다리지 않고 새 Chunk가 적용되는지
- Lake/Pond/Sea/River, Natural 전이, Junction 결과
- Runtime과 Debugger에서 같은 절대 좌표의 Hydrology 결과 일치
- 메모리 증가가 target 이동 거리 전체가 아니라 활성 lease 범위에 머무는지

에이전트가 확인한 범위는 C# 컴파일뿐이다. 실제 성능, 결과 품질, Unity 실행
수명은 위 실제 실행으로만 확정한다.
