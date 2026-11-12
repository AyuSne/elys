#requires -Version 5.1
<#
.SYNOPSIS
    打出可交付的 Windows 包，生成 SHA256 / 文件清单，并把录屏、证据与说明收进 Builds/Delivery。
.DESCRIPTION
    -RequireClean 要求工作树干净，并从该提交构建（provenance 里的 dirty 为 false）；
    不带它时允许脏树，供开发期快速出包。
    构建产物在 Builds/Windows，压缩包是 Builds/Elys-Windows.zip；
    交付目录 Builds/Delivery 收纳压缩包、README、交付说明、录屏与证据报告。
.EXAMPLE
    pwsh ./scripts/package-windows.ps1 -RequireClean
    pwsh ./scripts/package-windows.ps1 -SkipSmoke
#>
param(
    [switch]$RequireClean,
    [switch]$SkipBuild,
    [switch]$SkipSmoke,
    [switch]$SkipZip,
    [switch]$AllowMissingEvidence
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$windowsDirectory = Join-Path $repoRoot 'Builds/Windows'
$exePath = Join-Path $windowsDirectory 'Elys.exe'
$zipPath = Join-Path $repoRoot 'Builds/Elys-Windows.zip'
# 构建本身会改写 URP/ProjectSettings 三个文件；这里先记下构建前的状态，报告与 manifest 用 provenance 的 source.dirty。
$sourceDirty = [bool](& git -C $repoRoot status --porcelain)

if (-not $SkipBuild) {
    if ($RequireClean) {
        $status = & git -C $repoRoot status --porcelain
        if ($status) {
            Write-Error "工作树不干净，无法从提交构建。先提交或冻结改动：`n$status"
            exit 1
        }
    }

    if (Test-Path $windowsDirectory) {
        Remove-Item $windowsDirectory -Recurse -Force
    }

    $buildArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'build-windows.ps1'))
    if ($RequireClean) {
        $buildArguments += '-RequireClean'
    }

    & powershell @buildArguments
    if ($LASTEXITCODE -ne 0) {
        Write-Error "构建失败（退出码 $LASTEXITCODE）。"
        exit $LASTEXITCODE
    }
}

if (-not (Test-Path $exePath)) {
    Write-Error "没有构建产物：$exePath"
    exit 1
}

if (-not $SkipSmoke) {
    Write-Host '冒烟验收：启动包、截图、读 Player.log…'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'smoke-test-build.ps1')
    if ($LASTEXITCODE -ne 0) {
        Write-Error "冒烟验收未通过（退出码 $LASTEXITCODE）。"
        exit $LASTEXITCODE
    }
}

$files = @(Get-ChildItem $windowsDirectory -Recurse -File)
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
$provenancePath = Join-Path $windowsDirectory 'Elys.provenance.json'
$provenance = $null
if (Test-Path $provenancePath) {
    $provenance = Get-Content $provenancePath -Raw -Encoding UTF8 | ConvertFrom-Json
}

$headCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
$commit = if ($provenance -and $null -ne $provenance.source -and $provenance.source.revision) {
    [string]$provenance.source.revision
} else {
    $headCommit
}
$shortCommit = $commit.Substring(0, 7)
$buildDirty = if ($provenance -and $null -ne $provenance.source) { [bool]$provenance.source.dirty } else { $sourceDirty }
$zipHash = ''

if (-not $SkipZip) {
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Write-Host "压缩 Builds/Windows → $zipPath…"
    Compress-Archive -Path (Join-Path $windowsDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $zipBytes = (Get-Item $zipPath).Length

    $manifest = [ordered]@{
        schemaVersion    = 1
        product          = 'Elys'
        builtAt          = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        unityVersion     = if ($provenance) { $provenance.editor.version } else { '' }
        commit           = $commit
        worktreeDirty    = $buildDirty
        fileCount        = $files.Count
        uncompressedBytes = $bytes
        zip              = [ordered]@{
            file   = 'Elys-Windows.zip'
            bytes  = $zipBytes
            sha256 = $zipHash
        }
    }
    $manifestPath = Join-Path $repoRoot 'Builds/Elys-Windows.manifest.json'
    [IO.File]::WriteAllText(
        $manifestPath,
        ($manifest | ConvertTo-Json -Depth 5),
        (New-Object Text.UTF8Encoding($false)))
    $shaPath = Join-Path $repoRoot 'Builds/Elys-Windows.sha256'
    [IO.File]::WriteAllText($shaPath, "$zipHash  Elys-Windows.zip`n", (New-Object Text.UTF8Encoding($false)))
} else {
    $zipBytes = 0
}

# 交付目录：包 + 说明 + 录屏 + 证据。Builds/ 被 .gitignore 排除，不会污染仓库。
$deliveryDirectory = Join-Path $repoRoot 'Builds/Delivery'
if (Test-Path $deliveryDirectory) {
    Remove-Item $deliveryDirectory -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $deliveryDirectory | Out-Null
if (Test-Path $zipPath) {
    Copy-Item $zipPath (Join-Path $deliveryDirectory 'Elys-Windows.zip')
}

$required = @(
    'README.md',
    'docs/delivery.md',
    'docs/video/elys-demo.mp4',
    'docs/video/elys-demo.chapters.txt',
    'TestResults/scenarios/summary.md',
    'TestResults/video/video-report.md',
    'TestResults/perf/perf-report.md',
    'TestResults/clean-environment/report.md',
    'TestResults/editmode.xml'
)

$missing = @($required | Where-Object { -not (Test-Path (Join-Path $repoRoot $_)) })
if ($missing.Count -gt 0) {
    if ($AllowMissingEvidence) {
        Write-Host ('缺少交付证据（已按 -AllowMissingEvidence 继续）：' + ($missing -join ', ')) -ForegroundColor Yellow
    } else {
        Write-Error ("缺少交付证据，拒绝打「完整」交付目录：`n  " + ($missing -join "`n  ") + "`n先跑 run-scenarios / record-demo / perf-test / verify-clean-environment / run-tests，或加 -AllowMissingEvidence 只打开发包。")
        exit 1
    }
}

$testReportPath = Join-Path $repoRoot 'TestResults/editmode.xml'
if (Test-Path $testReportPath) {
    $testText = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($testReportPath))
    [xml]$testReport = $testText
    $totalTests = [int]$testReport.'test-run'.total
    if ($testText -notmatch 'ScenarioTests' -or $totalTests -lt 174) {
        Write-Error "TestResults/editmode.xml 里没有 #10 的 ScenarioTests 或总数不足 174（当前 $totalTests）；先跑 scripts/run-tests.ps1 刷新报告。"
        exit 1
    }
}

$copies = @(
    @{ Source = 'README.md'; Target = 'README.md' },
    @{ Source = 'docs/delivery.md'; Target = 'DELIVERY.md' },
    @{ Source = 'docs/video/elys-demo.mp4'; Target = 'elys-demo.mp4' },
    @{ Source = 'docs/video/elys-demo.chapters.txt'; Target = 'elys-demo.chapters.txt' },
    @{ Source = 'TestResults/scenarios/summary.md'; Target = 'evidence/scenarios-summary.md' },
    @{ Source = 'TestResults/video/video-report.md'; Target = 'evidence/video-report.md' },
    @{ Source = 'TestResults/perf/perf-report.md'; Target = 'evidence/perf-report.md' },
    @{ Source = 'TestResults/clean-environment/report.md'; Target = 'evidence/clean-environment.md' },
    @{ Source = 'TestResults/clean-environment/window.png'; Target = 'evidence/clean-environment.png' },
    @{ Source = 'TestResults/editmode.xml'; Target = 'evidence/editmode.xml' },
    @{ Source = 'Builds/Elys-Windows.sha256'; Target = 'Elys-Windows.sha256' },
    @{ Source = 'Builds/Elys-Windows.manifest.json'; Target = 'Elys-Windows.manifest.json' }
)

foreach ($copy in $copies) {
    $sourcePath = Join-Path $repoRoot $copy.Source
    if (Test-Path $sourcePath) {
        $targetPath = Join-Path $deliveryDirectory $copy.Target
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $targetPath) | Out-Null
        Copy-Item $sourcePath $targetPath -Force
    } else {
        Write-Host "  跳过（还没生成）：$($copy.Source)" -ForegroundColor Yellow
    }
}

if (Test-Path (Join-Path $repoRoot 'TestResults/scenarios')) {
    $scenarioRoot = Join-Path $repoRoot 'TestResults/scenarios'
    $scenarioEvidence = Join-Path $deliveryDirectory 'evidence/scenarios'
    New-Item -ItemType Directory -Force -Path $scenarioEvidence | Out-Null
    Get-ChildItem $scenarioRoot -File -Filter '*.trace' |
        Copy-Item -Destination $scenarioEvidence -Force

    $referenceRoot = Join-Path $scenarioRoot 'reference'
    if (Test-Path $referenceRoot) {
        $referenceEvidence = Join-Path $scenarioEvidence 'reference'
        New-Item -ItemType Directory -Force -Path $referenceEvidence | Out-Null
        Get-ChildItem $referenceRoot -File -Filter '*.trace' |
            Copy-Item -Destination $referenceEvidence -Force
    }
}

$deliveryBytes = (Get-ChildItem $deliveryDirectory -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ''
Write-Host "包：$windowsDirectory（$($files.Count) 个文件，$([math]::Round($bytes / 1MB, 1)) MB）" -ForegroundColor Green
if (-not $SkipZip) {
    Write-Host "压缩包：$zipPath（$([math]::Round($zipBytes / 1MB, 1)) MB，sha256 $zipHash）" -ForegroundColor Green
}
Write-Host "交付目录：$deliveryDirectory（$([math]::Round($deliveryBytes / 1MB, 1)) MB）" -ForegroundColor Green
Write-Host "提交：$shortCommit · 构建时工作树脏：$buildDirty"
exit 0
