# 최종 진행 명령 8단계 — 실제 검증과 기존 구조 정리

> 이 문서의 Snapshot 재계산 보완 판단은 폐기되었다. 활성 구현은
> [15-feature-owned-streaming-replacement.md](15-feature-owned-streaming-replacement.md)의 Feature 소유 lease 구조다.

## 목적

6단계 Runtime streaming과 7단계 Pattern Debugger 전환의 실제 결과·지연·자원 수명을
사용자 환경에서 확인한다. 실제 확인 뒤에만 새 경로와 중복되는 기존 Scope/Store,
Legacy generator, 설정·직렬화 잔여를 제거한다.

## 실행 기록 항목

- 초기 시작의 `Hydrology planning` 시간과 첫 `Streaming chunk applied=True` 대기
- 인접·연속 Target 이동 뒤 새 Chunk 표시, stale 결과 미적용, 취소 지연
- Finite 경계와 Infinite의 동일 절대 좌표 결과
- Preview, 중심 이동, Cell 선택, overlay의 결과·지연
- Runtime과 Debugger의 같은 절대 좌표 Terrain/Hydrology 일치 여부

`[WorldGenerationTiming] Initial terrain`은 빈 `WorldData` metadata 생성 시간이다.
성능 판단에는 `Hydrology planning`과 `Streaming chunk` 로그를 사용한다.

## 6단계 보완 판단

Target 이동 시 겹치는 범위가 있어도 Planning 전체 재계산이 반복되거나, 이전 요청이
새 Chunk 첫 표시에 지연을 만들면, Request 간 완료 Tile 공유가 없는 현재 6단계를
Window/Tile lease 구조로 보완한다.

측정 결과가 어떻더라도, 완료 Tile 요청 간 공유가 아직 구현되지 않았다는 구조 사실은
변하지 않는다. 측정은 보완 우선순위와 범위를 결정하는 근거다.

## 정리 Gate

실제 검증 전에는 기존 생성 소스를 제거하지 않는다. 검증 뒤에도 새 경로와 기존
Scope/Store를 runtime fallback으로 병행하지 않는다.

## 현재 상태

- 실행 결과 미기록
- 테스트 전용 소스 추가 없음
- 실제 검증 결과를 받은 뒤에만 6단계 보완 또는 기존 구조 정리를 진행한다.
