# 0단계 — 이전 Hydrology 구조 정리와 기준 결과 확보

## 목적

새 구조를 추가하기 전에 이전 River/Lake/Pond 구현, 진행 중인 중복 재설계 흔적,
사용되지 않는 설정·직렬화 필드·디버그 경로를 정리한다. 이 단계는 최종 Hydrology
알고리즘을 바꾸는 단계가 아니다. 이후 단계가 무엇을 교체하는지 명확하게 하고,
비교 가능한 컴파일·생성 기준을 남긴다.

## 정리 원칙

- 삭제 전에는 `WorldGenerationPipeline`, `WorldRuntime`, 패턴맵 디버거,
  Save/Load Codec, WaterSystem에서의 참조 여부를 확인한다.
- 현재 실행 경로가 참조하는 코드를 먼저 삭제해 컴파일을 깨뜨리지 않는다.
- 도달 불가능한 코드, 중복 정의, 더 이상 직렬화 대상이 아닌 필드만 이 단계에서
  제거한다.
- 아직 새 계약으로 교체되지 않은 활성 River/Lake/Pond 코드는 제거 목록에
  기록하고 해당 대체 단계에서 원자적으로 교체한다. 임시 Adapter나 두 개의
  Hydrology 경로를 병행하지 않는다.
- 이전 저장과 이전 Hydrology 설정의 호환 계층은 만들지 않는다.

## 적용 순서

1. 현재 Hydrology 관련 타입·설정·직렬화 필드·진입점을 목록화한다.
2. 각 항목을 `유지`, `대체 단계`, `즉시 제거` 중 하나로 분류한다.
3. 이 단계에서는 활성 구조를 삭제하지 않는다. 실제 삭제는 새 활성 경로가
   완성되는 5단계에서 참조·Inspector/YAML 잔여 필드와 함께 수행한다.
4. `대체 단계` 항목은 제거 예정 위치와 대체할 단계를 기록하고, 동작 코드는
   유지한다.
5. 정리 후에는 컴파일만 확인한다. 실제 생성·스트리밍 결과는 사용자가 실행
   환경에서 확인한다.

이전 Hydrology 결과를 영구 정답으로 고정하거나, 현재 성능을 통과 기준으로
삼지 않는다. 새 구조의 Topology·Graph·Batch 성능은 각각 2~5단계에서 실제
실행 환경으로 확인한다.

## 조사 기록 (2026-08-31)

| 현재 항목 | 실제 참조 경로 | 분류 | 처리 |
|---|---|---|---|
| `HydrologySettingsData`, `HydrologyGenerationSettings` | Settings Asset → `WorldGenerationSettings` → Save Codec → BuildInput | 대체 1단계 | 설정 의미를 새 계획 계약으로 원자 교체한다. 이전 설정 호환 계층은 만들지 않는다. |
| `HydrologyGenerationContext`, `TerrainSurfaceSampler` | `WorldBuildInput` → `WorldChunkBuildInput` / Debugger | 대체 1단계 | Base Terrain Field 계약과 명시적 Plan Scope 소유권으로 교체한다. |
| `HydrologyRegionPlanner`, `HydrologyPatternResolver` | `HydrologyBatch` → `WorldPatternStage` | 대체 2단계 | Sea/Basin Topology Store와 `HydrologyCellPlan`으로 교체한다. |
| `RiverHydrologyPlanner`, `HydrologyEndpointRole.Head/End` | `HydrologyBatch` → River raster | 대체 3단계 | 역할 기반 연결을 무방향 Endpoint Graph와 EdgeId로 교체한다. |
| `HydrologyBatch`, `WorldBuildPipeline`, `WorldRuntime`, Pattern Debugger | 초기 생성·스트리밍·디버거 | 대체 4단계 | 요청 범위 Batch와 공유 Plan Scope로 교체한다. 현재 청크별 Batch 소유를 유지하지 않는다. |
| `PondMaximumArea`, `WaterTypeResolver`의 연결 크기 재분류 | WaterSystem 갱신 | 대체 5단계 | 생성 때 확정한 Lake/Pond Type을 WaterSystem이 재분류하지 않도록 제거한다. |
| 과거 `RiverPatternResolver`, `RiverPatternSettings`, 독립 Lake/Pond Resolver | 현재 소스·직렬화 참조 없음 | 즉시 제거 대상 없음 | 현재 작업 트리에 해당 정의와 YAML 필드가 이미 없다. 삭제할 활성/도달 불가 파일은 발견되지 않았다. |

이 조사에서 현재 구현의 모든 Hydrology 관련 타입은 실행 또는 직렬화 경로에
연결되어 있었다. 따라서 0단계에서 삭제한 생산 코드는 없다. 대체 전 삭제는
컴파일 오류 또는 두 경로 병행을 유발하므로, 5단계에서 새 단일 경로를 확인한
뒤 원자적으로 제거한다.

## 검증 기록 (2026-08-31)

- `MiniCivilization.World.csproj` 컴파일: 경고 0, 오류 0.
- 실제 월드 생성·스트리밍 검증은 사용자 실행 범위다.

## 완료 기준

- 유지/대체/제거 목록이 이 문서 또는 연결된 작업 기록에 남아 있다.
- 삭제 대상과 5단계의 원자적 제거 범위가 기록된다.
- 활성 생성 경로는 하나이며 컴파일된다.
- 컴파일 확인 범위와 실제 실행에서 확인할 Topology/Graph/성능 범위가 문서에
  분리돼 있다.

## 다음 단계 인계

1단계는 0단계의 `대체 단계` 목록을 기준으로 새 계약을 도입한다. 0단계에서
살아 있는 역할 기반 구조를 새 코드의 Adapter로 감싸지 않는다.
