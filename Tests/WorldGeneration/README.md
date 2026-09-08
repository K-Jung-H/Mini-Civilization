# 월드 생성 회귀 검사

프로젝트 루트에서 실행:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/WorldGeneration/RunChecks.ps1
```

.NET 10 SDK와 참조 팩을 사용하며 NuGet 다운로드는 필요 없다. 생성물은 무시된 obj 폴더에 기록한다. 생성·Domain·Materializer·WaterFlowResolver는 실제 소스를 실행하고 Unity authoring 속성과 ScriptableObject 기반형은 대체한다. StreamingChecks는 실제 Coordinator/Scheduler에 기록용 Runtime/Persistence 대역을 연결하므로 캐시·디스크·렌더 동작 검사가 아니다.

## 검사 범위

- Program: 높이/Amount 변환, Cell 용량, 잘못된 입력, 저장 헤더, Terrain 연속성, 합성 순서 및 Tile 결정론.
- HydrologyChecks: Basin 평균 수면 상한과 비용 최소화, 분리·접촉·겹침, River 단면·경사·분기, 실제 Map과 물 시뮬레이션, 역순·병렬·Tile 분할.
- WaterSimulationChecks: 부분 Filled와 초기 Source 배치, Source 유지, Dynamic 낙하, 외부 공급 차단, 열린 제방 대조군, wave 예산과 처리 재활성화.
- StreamingChecks: 준비·활성화 독립 한도, 근거리 활성화·원거리 언로드, 타깃 반전 시 대기 취소, 범위 밖 시뮬레이션 즉시 제외, 저장 확정 호출 횟수.
- WaterSimulationChecks의 추가 검사: 진행 중 wave에서 물 대기 목록 분리·복원과 상태 변경을 묶어도 최종 목록·물 결과가 일치한다.
- RiverPathChecks: 연속 이동, 부모 분기점 일치, 큰 굽이와 회전 전환, 분기 예산, 방향 및 병렬 결정론.
- 캐시: 실제 Store에서 요구 Tile 유지·전체 요구 해제·재생성 Map 일치를 검사한다. Unity 이동 중 Scheduler 수요 변경과 장기 메모리 사용은 별도 실행 검증 대상이다.

현재 설정 에셋을 읽는다. Source/허용 Dynamic 영역을 벗어나는 물은 완료 wave마다 검사한다. 처리되지 않은 frontier나 제한 횟수 초과를 안정화 성공으로 취급하지 않는다. 처리 재활성화 검사는 실제 청크 언로드/저장 복원과 구분한다.

Unity 메시, 전용 성능 계측, 실제 저장/청크 재로드 통합은 이 검사에 포함되지 않는다. 유한 표본 통과를 모든 Seed의 보장으로 확대하지 않는다. 현재 생성 구조는 [기준 문서](../../Docs/WorldGeneration/README.md)를 따른다.

`RunStreamingRuntimeChecks.ps1`는 생성된 World.csproj의 실제 런타임 소스와 Unity 관리형 라이브러리를 사용한다. 네이티브 Profiler 호출만 제외하고 물 영역 증분 갱신/전체 탐색 동등성, Cell 검증 할당, Terrain 메시 계산, 캐시 무효화와 경계 의존성을 검사한다. GPU 업로드·GameObject 수명·디스크 저장은 실행하지 않는다. 출력된 시간은 .NET 비교 시험이며 Unity 프레임 시간으로 해석하지 않는다.
