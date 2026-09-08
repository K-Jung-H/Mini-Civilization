$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dotnetPath = (Get-Command dotnet).Source
$dotnetRoot = Split-Path $dotnetPath
$sdk = Get-ChildItem (Join-Path $dotnetRoot 'sdk') -Directory | Where-Object Name -Like '10.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$pack = Get-ChildItem (Join-Path $dotnetRoot 'packs/Microsoft.NETCore.App.Ref') -Directory | Where-Object Name -Like '10.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$outputDir = Join-Path $PSScriptRoot 'obj/StreamingRuntime'
[IO.Directory]::CreateDirectory($outputDir) | Out-Null
[xml]$project = Get-Content (Join-Path $projectRoot 'MiniCivilization.World.csproj')
$referencePaths = @($project.SelectNodes('//*[local-name()="HintPath"]') | ForEach-Object { [IO.Path]::GetFullPath($_.InnerText, $projectRoot) } | Where-Object { (Split-Path $_ -Leaf) -notmatch '^(System\.|Microsoft\.|netstandard\.|mscorlib\.)' })
$referencePaths += @($project.SelectNodes('//*[local-name()="ProjectReference"]') | ForEach-Object { Join-Path $projectRoot ('Library/ScriptAssemblies/' + [IO.Path]::GetFileNameWithoutExtension($_.Include) + '.dll') })
[IO.File]::WriteAllLines((Join-Path $outputDir 'references.txt'), $referencePaths)
$outputDll = Join-Path $outputDir 'StreamingRuntime.Checks.dll'
# Compile production runtime sources without native Profiler calls; no replacement world/mesher classes.
$compileArgs = @((Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll'), '/nologo', '/target:exe', '/unsafe', '/langversion:latest', '/nostdlib+', '/nowarn:0649', "/out:$outputDll")
$compileArgs += @(Get-ChildItem (Join-Path $pack.FullName 'ref/net10.0') -Filter '*.dll' | ForEach-Object { '/reference:' + $_.FullName })
$compileArgs += @($referencePaths | ForEach-Object { '/reference:' + $_ })
$compileArgs += @($project.SelectNodes('//*[local-name()="Compile"]') | ForEach-Object { Join-Path $projectRoot $_.Include })
$compileArgs += Join-Path $PSScriptRoot 'StreamingRuntimeChecks.cs'
$responseFile = Join-Path $outputDir 'compile.rsp'
[IO.File]::WriteAllLines($responseFile, @($compileArgs | Select-Object -Skip 1 | ForEach-Object { '"' + $_ + '"' }))
& $dotnetPath $compileArgs[0] "@$responseFile"
if ($LASTEXITCODE -ne 0) { throw 'Runtime check compilation failed.' }
[IO.File]::WriteAllText((Join-Path $outputDir 'StreamingRuntime.Checks.runtimeconfig.json'), '{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}')
& $dotnetPath $outputDll
if ($LASTEXITCODE -ne 0) { throw 'Runtime checks failed.' }
