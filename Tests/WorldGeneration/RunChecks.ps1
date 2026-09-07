$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dotnetPath = (Get-Command dotnet).Source
$dotnetRoot = Split-Path $dotnetPath
$sdk = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'sdk') -Directory |
    Where-Object { $_.Name -match '^10\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$pack = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'packs/Microsoft.NETCore.App.Ref') -Directory |
    Where-Object { $_.Name -match '^10\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$sdk -or !$pack) { throw '.NET 10 SDK/reference pack required.' }
$outputDir = Join-Path $PSScriptRoot 'obj'
[IO.Directory]::CreateDirectory($outputDir) | Out-Null
$outputDll = Join-Path $outputDir 'WorldGeneration.Checks.dll'
$compileArgs = @((Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll'), '/nologo', '/target:exe', '/langversion:latest', '/nostdlib+', '/nowarn:0649', "/out:$outputDll")
$compileArgs += @(Get-ChildItem -LiteralPath (Join-Path $pack.FullName 'ref/net10.0') -Filter '*.dll' | ForEach-Object { '/reference:' + $_.FullName })
$sources = @(
    'Runtime/PatternStreamingCoordinator.cs', 'Runtime/PatternMapPreparationScheduler.cs', 'Runtime/ChunkRuntime.cs', 'Runtime/WorldStreamingProgress.cs', 'Generation/Patterns/WorldGenerationSettings.cs',
    'Domain/WorldPrimitives.cs', 'Domain/WorldSettingsData.cs',
    'Domain/WorldData.cs', 'Domain/WorldEntities.cs', 'Domain/WaterState.cs',
    'WaterFlow/WaterFlowState.cs', 'WaterFlow/WaterBody.cs', 'WaterFlow/WaterFlowSimulation.cs',
    'Generation/Patterns/PatternChunkMaterializer.cs',
    'Generation/Patterns/PatternMapStore.cs', 'Generation/Patterns/ClimatePatternMap.cs',
    'Generation/Patterns/TerrainPatternSettings.cs', 'Generation/Patterns/TerrainPatternEvaluator.cs',
    'Generation/Patterns/PatternNoise.cs', 'Generation/Patterns/PatternTileContracts.cs',
    'Generation/Patterns/PatternTileSettingsData.cs', 'Persistence/WorldGenerationSaveHeader.cs',
    'Generation/Patterns/PatternColumnHeights.cs'
    'Generation/Patterns/HydrologyDrawingSample.cs', 'Generation/Patterns/HydrologyOverlapResolver.cs',
    'Generation/Patterns/WaterMapPainter.cs'
    'Generation/Patterns/HydrologyHeightSolver.cs', 'Generation/Patterns/HydrologyFeatureSettings.cs',
    'Generation/Patterns/WaterBrushCatalog.cs', 'Generation/Patterns/WaterBrushFactory.cs', 'Generation/Patterns/BasinWaterBrush.cs', 'Generation/Patterns/RiverWaterBrush.cs', 'Generation/Patterns/WaterMapGeometry.cs', 'Generation/Patterns/ITerrainPatternMapReader.cs',
    'Generation/Patterns/SeaPatternSampler.cs', 'Generation/Patterns/HydrologyContributionCollector.cs',
    'Generation/Patterns/HydrologyPatternDrawer.cs', 'WaterFlow/WaterFlowReachability.cs'
)
$compileArgs += @($sources | ForEach-Object { Join-Path $projectRoot "Assets/Game/World/$_" })
$compileArgs += @((Join-Path $PSScriptRoot 'Program.cs'), (Join-Path $PSScriptRoot 'UnityAuthoringStubs.cs'))
$compileArgs += (Join-Path $PSScriptRoot 'HydrologyChecks.cs')
$compileArgs += (Join-Path $PSScriptRoot 'ClimateChecks.cs')
$compileArgs += (Join-Path $PSScriptRoot 'WaterSimulationChecks.cs')
$compileArgs += (Join-Path $PSScriptRoot 'RiverPathChecks.cs')
$compileArgs += (Join-Path $PSScriptRoot 'StreamingChecks.cs')
& $dotnetPath @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'Checks compilation failed.' }
[IO.File]::WriteAllText((Join-Path $outputDir 'WorldGeneration.Checks.runtimeconfig.json'), '{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}')
& $dotnetPath $outputDll $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
