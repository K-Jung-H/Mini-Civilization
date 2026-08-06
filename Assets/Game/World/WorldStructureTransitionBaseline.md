# World 구조 전환 기준 (0단계)

## 목적

구조 전환 중 동작 변경을 막기 위해, 현재 Generate / Load / Save / Edit / Water 흐름과 저장 범위를 기준으로 고정한다.

이 문서는 기능 명세를 추가하지 않는다. 이후 단계는 아래의 결과와 저장 범위를 유지한 채 책임 위치만 바꾼다.

## 현재 흐름

### Generate

```text
WorldManager.GenerateWorld
  -> WorldGenerationController.GenerateDataAsset
  -> WorldGenerator.Generate
  -> WorldDataAsset.Initialize
  -> WorldManager.ActivateWorldAsset
```

`WorldGenerator`는 지형, 바다, 수문 지도, 호수·강 계획, 물 타입, Source, 물 frontier, 바이옴, 경로 Cache를 순서대로 구성한다.

활성화는 현재 `WorldManager`가 EditController, WaterFlowController, WorldRenderer를 직접 Bind하고 `WorldChanged`를 발생시킨다.

### Load

```text
WorldManager.LoadWorld
  -> WorldSaveController.LoadDataAsset
  -> WorldSaveCodec.Read
  -> WorldDataAsset.Initialize
  -> WorldManager.ActivateWorldAsset
```

현재 `WorldSaveCodec.Read`는 저장 데이터를 읽은 뒤 `WorldCache.RebuildAll`을 수행한다. 이 Cache 생성 책임은 이후 Load Pipeline 정리 단계에서 Runtime 준비 경로로 옮긴다.

### Save

```text
WorldManager.SaveWorld
  -> WorldSaveController.Save
  -> WorldSaveCodec.Write
```

Save Codec은 WorldData의 저장 사실만 기록한다. Cache, Context, ChangeId, WaterBody, Resolver는 저장하지 않는다.

### Edit

```text
WorldEditController.Commit
  -> CellData / EnvironmentData 변경
  -> Surface / Path / WaterDistance Cache 갱신
  -> ChangeId 및 Chunk 변경 버전 갱신
  -> ChangeCommitted
WorldManager.OnWorldEdited
  -> WaterFlowController.ApplyChanges
  -> WorldRenderer.ApplyChanges
```

### Water

```text
WorldWaterFlowController.Update
  -> WaterFlowResolver.Step
  -> CellData.Water / frontier 갱신
  -> WaterType 및 WaterBody 갱신
  -> Surface / WaterDistance Cache 갱신
  -> ChangeId 및 Chunk 변경 버전 갱신
  -> ChangeCommitted
WorldManager.OnWaterFlowChanged
  -> WorldRenderer.ApplyChanges
```

## 저장 범위

| 저장되는 사실 | 현재 위치 |
|---|---|
| 월드 크기, Chunk 크기, Seed | `WorldData` |
| 물 규칙, Pond 최대 면적 | `WorldData` |
| `CellData` 전체: Terrain, Water Amount, Role, Type, Flow | `ChunkData` |
| `EnvironmentData` | `WorldData` |
| Water Source 그룹 | `WaterSourceCollection` |
| 물 확산 frontier | `WaterFlowScheduleData` |

`WorldDataAsset`은 위 데이터를 직렬화 바이트로 보관할 수 있으며, 준비된 렌더 Mesh 참조도 별도로 가진다. 외부 저장 파일은 `WorldSaveCodec` 형식으로 기록된다.

## 런타임 전용 상태

다음 항목은 저장 사실이 아니며, 구조 전환 후 `WorldRuntime`으로 이동할 대상이다.

| 런타임 상태 | 현재 위치 |
|---|---|
| SurfaceHeight, OpenHeight, WaterDistance Cache | `WorldCache` (`WorldData` 소유) |
| Cell 조회 Context / CellView | `WorldContext` (`WorldData` 소유) |
| 현재 ChangeId, Chunk 변경 버전 | `WorldData`, `ChunkData` |
| WaterFlowState, WaterFlowResolver, WaterBody | `WorldWaterFlowController` 및 WaterFlow 계층 |
| 렌더 Patch, Mesh, 선택 상태, Edit History | Controller / Renderer 계층 |

## 유지 조건

구조 전환 중 아래 결과를 변경하지 않는다. 확인은 기존 Unity 실행 흐름에서 수동으로 수행한다.

1. 같은 Seed로 Generate하면 같은 지형·수역·바이옴 결과를 만든다.
2. Save 후 Load하면 `CellData`, `WaterType`, Source, Dynamic Water, frontier가 유지된다.
3. Asset 시작, Generate, Load 모두 Edit·물 확산·렌더 갱신을 수행한다.
4. Load는 생성 규칙을 다시 실행하지 않는다.
5. 빈 Cell은 `Water.Amount == 0`, `Water.Role == None`, `Water.Type == None`, `Water.Flow == None`을 유지하며 물 Source 또는 물 확산 대상으로 처리되지 않는다.

## 0단계 종료 조건

- 이 문서의 흐름과 저장 범위를 이후 구조 변경의 기준으로 사용한다.
- 코드 책임 이동 전에는 위 유지 조건을 수동으로 확인한다.
- 0단계에서는 런타임 동작, 저장 형식, 물 역할, 생성 규칙을 변경하지 않는다.
