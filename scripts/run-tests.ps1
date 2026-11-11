#requires -Version 5.1
<#
.SYNOPSIS
    跑 Elys 的 EditMode 回归测试，并把 NUnit XML 报告归档到 TestResults/。
.DESCRIPTION
    需要 unity CLI（https://unity.com/install）与 Unity 6000.3.10f1。测试只跑编辑模式，
    不需要打开 Unity 编辑器；编辑器开着（尤其 Play Mode）时测试会被拒绝。
.EXAMPLE
    pwsh ./scripts/run-tests.ps1
    pwsh ./scripts/run-tests.ps1 -Junit
#>
param(
    [string]$Output = 'TestResults/editmode.xml',
    [switch]$Junit
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command unity -ErrorAction SilentlyContinue)) {
    Write-Error '找不到 unity 命令。先安装 Unity CLI：irm https://unity.com/install.ps1 | iex'
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot $Output
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null
if (Test-Path $outputPath) {
    Remove-Item $outputPath -Force
}

$unityArgs = @('test', 'Game', '--mode', 'EditMode', '--timeout', '900', '--output', $outputPath)
if ($Junit) {
    $unityArgs += @(
        '--report-format', 'nunit,junit',
        '--junit-output', ($outputPath -replace '\.xml$', '.junit.xml')
    )
}

Write-Host '运行 Elys EditMode 回归测试（工程：Game）…'
Push-Location $repoRoot
try {
    & unity @unityArgs
    $exitCode = $LASTEXITCODE
} finally {
    Pop-Location
}

if (-not (Test-Path $outputPath)) {
    Write-Error "测试没有产出报告：$outputPath（unity 退出码 $exitCode，详见 Game/Logs/ 或编辑器日志）"
    exit 1
}

$text = [IO.File]::ReadAllText((Resolve-Path $outputPath), [Text.UTF8Encoding]::new($false, $true))
[xml]$report = $text
$run = $report.'test-run'
$cases = @($report.SelectNodes('//test-case'))

Write-Host ''
Write-Host "报告：$outputPath"
Write-Host ('总数 {0} · 通过 {1} · 失败 {2} · 跳过 {3} · 用时 {4:0.00} 秒' -f `
    $run.total, $run.passed, $run.failed, $run.skipped, [double]$run.duration)
Write-Host ''
Write-Host '题目要求的三项验收（#4，取自 AcceptanceTests）：'

# 不写死用例名：按类名挑出验收剧本，用方法名第一个下划线前的词当标签。
$acceptance = @($cases | Where-Object { $_.fullname -like '*AcceptanceTests.*' })
if ($acceptance.Count -eq 0) {
    Write-Error '报告里没有 AcceptanceTests 用例，验收剧本没有跑。'
    exit 1
}

foreach ($case in $acceptance) {
    $leaf = ($case.fullname -split '\.')[-1]
    $label = ($leaf -split '_')[0]
    if ($case.result -eq 'Passed') {
        Write-Host ('  {0}：通过' -f $label) -ForegroundColor Green
    } else {
        Write-Host ('  {0}：{1}（{2}）' -f $label, $case.result, $leaf) -ForegroundColor Red
    }
}

Write-Host ''
if ([int]$run.failed -gt 0) {
    Write-Host '有失败的用例，报告里带失败的堆栈。' -ForegroundColor Red
    exit 1
}

if ($exitCode -ne 0) {
    Write-Error "unity 以退出码 $exitCode 结束，但报告显示没有失败用例；按运行失败处理。"
    exit $exitCode
}

Write-Host '全绿。' -ForegroundColor Green
