# 중요 설계 결정

## DEC-001 — 절대 좌표 기반 결정론적 Pattern Tile 생성

- **상태:** Active
- **결정:** 월드 생성 결과는 Seed, 설정, 절대 좌표로 결정한다. Chunk/Tile의 요청 순서, 분할, 병렬 실행에 따라 결과가 달라져서는 안 된다.
- **이유:** 스트리밍과 재방문, 저장 월드의 일관성을 보장하기 위해서다.
- **영향 시스템:** Generation, Pattern Map, Streaming, Persistence, 회귀 검사

## DEC-002 — 영속 월드 데이터와 런타임 상태의 분리

- **상태:** Active
- **결정:** `WorldData`는 저장 가능한 월드 사실을 보유하고, 캐시·스트리밍·물 시뮬레이션 등 실행 중 상태는 `WorldRuntime` 및 관련 런타임 객체가 보유한다.
- **이유:** 저장 데이터와 실행 최적화 상태의 수명·책임을 분리하기 위해서다.
- **영향 시스템:** Domain, Runtime, WaterFlow, Persistence, Streaming

## DEC-003 — ChangeSet 기반 변경 전파

- **상태:** Active
- **결정:** 월드 변경은 `WorldChangeSet`, 엔티티 변경은 `EntityChangeSet`으로 후속 시스템에 전달한다.
- **이유:** 편집·물 변화 후 렌더링, 저장, 내비게이션 등의 갱신을 변경 범위 중심으로 조율하기 위해서다.
- **영향 시스템:** Editing, Runtime, WaterFlow, Presentation, Persistence, Entities

## DEC-004 — 생성 규칙 변경 시 기존 저장 자동 이관을 하지 않음

- **상태:** Active
- **결정:** 생성 결과를 변경하는 규칙 변경은 생성 버전을 올리고 새 월드로 검증한다. 기존 저장을 자동으로 이관하지 않는다.
- **이유:** 생성 규칙 변경에 따른 기존 월드의 의미와 결과를 자동 변환으로 추정하지 않기 위해서다.
- **영향 시스템:** Generation, Persistence, World Save, 검증
