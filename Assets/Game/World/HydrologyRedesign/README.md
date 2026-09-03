# Pattern Tile 월드 생성 설계

## 활성 문서

1. `26-water-map-direct-drawing-contract.md` — Terrain Map을 읽어 Water Map을 직접 그리는 생성 계약
2. `25-pattern-map-store-streaming-contract.md` — Pattern Map 수명, 세 Range, Debugger 수요 계약
3. `18-pattern-tile-final-redesign.md` — 전체 구조와 단계 경계
4. `19-stage-2-pattern-contract.md` — Pattern 설계 이관 완료 기록
5. `20-stage-3-terrain-pattern-tile.md` — Terrain evaluator와 Tile builder 완료 기록

충돌 시 위 순서가 우선한다. Save/Load는 이 Pattern Tile·ChunkData·Runtime 상태를
기준으로 다음 8단계에서 설계하고 구현한다.

## 보관 문서

`Archive/`는 이전 생성·수문·스트리밍 설계의 이력이다. 현재 구현 계약이나
새 기능의 호환 기준으로 사용하지 않는다.
