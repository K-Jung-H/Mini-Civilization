# 증분 Feature Tile 보존 — Target 이동 재계산 수정 기록

## 범위

이 수정은 Feature 소유 생성으로 이미 해결된 Basin 후보 사전 평가와 Route별
Topology 재생성 문제를 다시 설계하지 않는다. Terrain, Basin, Endpoint, Route,
Junction의 결과 공식도 변경하지 않는다.

목적은 Target 이동 때 직접 Chunk halo만 lease하던 구조가, 이전에 생성 중 실제로
읽었던 Feature Tile을 해제해 같은 범위의 계획을 다시 만들던 문제를 제거하는 것이다.

## 활성 수명 규칙

```text
desired prepared Chunk
  → 직접 halo Tile 참조
  → Chunk materialization 중 실제 접근한 Feature Tile 참조
  → 현재 Target Window의 leased Tile 집합
```

- `StreamingFeatureWorld`는 현재 desired Chunk 집합을 보존한다.
- Chunk materialization은 Base Terrain, Basin Candidate/Allocation, Topology,
  Endpoint, Route, Spatial Tile을 읽을 때 owner `PlanningTileKey`를 기록한다.
- Chunk build가 성공하고 해당 Chunk가 아직 desired 상태이면, 기록한 key 집합을
  Chunk 의존성으로 확정한다.
- Target 이동 후에도 desired 상태인 Chunk의 의존성은 계속 lease된다.
- Chunk가 Window를 벗어나면 그 Chunk의 의존성 참조만 해제한다. 다른 desired
  Chunk나 진행 중 build가 같은 key를 유지하면 결과는 제거하지 않는다.
- 진행 중 build의 의존성은 build 성공 또는 실패가 확정될 때까지 lease된다.

따라서 Target Window의 교집합이 유지되는 이동에서는, 이미 materialize한 Chunk가
실제로 사용한 Feature Tile을 직접 halo 밖이라는 이유만으로 제거하지 않는다.

## 적용 코드

- `StreamingFeatureWorld`는 Chunk별 실제 접근 Tile 집합과 진행 중 build 집합을
  관리한다.
- `StreamingWorldChunkMaterializer`는 정상 Chunk build 뒤에만 의존성 집합을
  commit하고, 예외 시 폐기한다.
- 기존 `SetLeaseChunks`는 새 Target의 직접 halo와 활성 Chunk 의존성의 합집합으로
  lease를 재구성한다.

## 보장하지 않는 범위

- 멀리 이동한 미방문 영역의 최초 Feature 생성 비용은 제거하지 않는다.
- Target Window에서 완전히 떠나 참조가 0이 된 뒤 재방문한 Tile의 재생성은 정상이다.
- Runtime/Debugger Coordinator 분리, 작업 취소·선점, 전역 gate 제거는 이 수정의
  범위가 아니다.
- 실제 Target 이동 성능과 메모리 효과는 사용자 Unity 실행으로만 검증한다.

## 컴파일 확인

`dotnet build MiniCivilization.World.csproj --no-restore --verbosity minimal`은 오류
없이 완료됐다. 출력된 경고는 Unity PackageCache의 기존 경고다.
