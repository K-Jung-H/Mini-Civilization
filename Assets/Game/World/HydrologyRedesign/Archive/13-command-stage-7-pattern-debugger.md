# 최종 진행 명령 7단계 — Pattern Debugger 전환 구현 기록

## 단계 범위

이 문서는 최종 완료까지의 진행 명령에서 7단계인 Pattern Debugger 전환을 기록한다.
`06`~`12`의 생성 백본 이관 번호와 이 단계 번호를 혼용하지 않는다.

목표는 Pattern Debugger의 Preview, 선택 Cell 정보, 선택 영역 overlay가 기존
`WorldHydrology`/`HydrologyPlanScope`/`HydrologyBatchBuilder`를 사용하지 않고,
새 생성 백본의 sealed Snapshot만 소비하게 만드는 것이다.

```text
Debugger absolute Cell
  → WorldGenerationRequest(Debugger)
  → PlanningSnapshot
  → StreamingHydrologyRaster
  → Preview / Cell details / overlay
```

## 구현 사실

`Assets/Game/World/Editor/WorldTerrainPatternDebuggerEditor.cs`에 적용됐다.

- Preview는 표시 Pixel의 absolute Cell을 먼저 계산하고 Planning Tile key로 중복을
  제거한다.
- Tile core들의 `WorldGenerationRequest(Debugger)`를 만들고,
  `StreamingRiverPlanningStage.Build`가 sealed Snapshot을 준비한다.
- Preview는 Tile별 `StreamingHydrologyRaster`만 만들고, sample 소비 뒤 즉시 dispose한다.
- 선택 Cell 정보와 선택 영역 overlay는 각각 필요한 작은 rectangle Request/Raster만
  만들고 즉시 dispose한다.
- Final surface 표시는 순수 `StreamingBaseTerrainEvaluator`를 사용한다.
- Debugger에는 `WorldHydrology`, `HydrologyPlanScope`, `HydrologyBatchBuilder`, 기존
  Hydrology resolver 장기 보관 필드가 없다.
- Debugger Snapshot은 Runtime Snapshot을 참조하거나 Runtime의 계획 수명을 바꾸지
  않는다.

## 활성 경계

Raster는 sealed Snapshot의 사실만 읽는다. Preview, Cell details, overlay가 Endpoint,
Proposal, Route, Interaction, SpatialIndex, Basin Allocation을 보충 계획하지 않는다.
필요한 사실이 없으면 Raster가 실패하며, 기존 Scope/Batch fallback은 없다.

이 전환 코드는 Runtime 전환 작업과 같은 변경 묶음에서 먼저 추가됐지만, 최종 진행
명령상 책임은 이 7단계다. 중복 구현은 하지 않는다.

## 확인 범위

`dotnet build MiniCivilization.World.csproj --no-restore --verbosity minimal` 결과:
경고 0개, 오류 0개.

실제 Preview 지연, Cell/overlay 결과, Runtime과 동일 좌표 결과는 사용자가 8단계
실행 검증에서 확인한다. 테스트 전용 소스는 추가하지 않는다.
