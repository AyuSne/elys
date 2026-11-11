#requires -Version 5.1
<#
.SYNOPSIS
    打出 Windows 交付包（Builds/Windows/Elys.exe），并检查产物里没有密钥类文件。
.DESCRIPTION
    构建由 Assets/Editor/FarmBuild.cs 执行：只打农场演示场景，构建前后各扫一遍疑似密钥文件。
    需要 unity CLI 与 Unity 6000.3.10f1（含 Windows Build Support 模块）。
    默认允许在有未提交改动的工作树上构建：打包本身会改写 URP 预过滤等工程文件，
    下一次打包时工作树往往已经不干净。要发布「从干净提交构建」的包时加 -RequireClean。
.EXAMPLE
    pwsh ./scripts/build-windows.ps1
    pwsh ./scripts/build-windows.ps1 -RequireClean -Launch
#>
param(
    [string]$Output = 'Builds/Windows/Elys.exe',
    [switch]$RequireClean,
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command unity -ErrorAction SilentlyContinue)) {
    Write-Error '找不到 unity 命令。先安装 Unity CLI：irm https://unity.com/install.ps1 | iex'
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ($Output -notmatch '\.exe$') {
    Write-Error "输出路径必须以 .exe 结尾：$Output"
    exit 1
}

$outputPath = Join-Path $repoRoot $Output
$outputDirectory = Split-Path -Parent $outputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
if (Test-Path $outputPath) {
    Remove-Item $outputPath -Force
}

$unityArgs = @(
    'build', 'Game',
    '--target', 'StandaloneWindows64',
    '--execute-method', 'ElysFarm.EditorTools.FarmBuild.BuildWindows',
    '--output-path', $outputPath,
    '--timeout', '1800'
)
if (-not $RequireClean) {
    $unityArgs += '--allow-dirty-build'
}

Write-Host '构建 Windows 包（工程：Game，场景：Assets/Scenes/FarmDemo.unity）…'
Push-Location $repoRoot
try {
    & unity @unityArgs
    $exitCode = $LASTEXITCODE
} finally {
    Pop-Location
}

if ($exitCode -ne 0) {
    Write-Error "构建失败（unity 退出码 $exitCode）。日志在 Game/Logs/ 下。"
    exit $exitCode
}

if (-not (Test-Path $outputPath)) {
    Write-Error "构建命令成功，但没有找到产物：$outputPath"
    exit 1
}

# 产物目录里再独立扫一遍：不依赖 C# 构建脚本的结论。
# 规则与 BuildSecrets.LooksLikeSecret 一致：.env / .env.local / *.env 后缀 /
# secrets.json / secrets.local.json / .secrets.local.json。
$secretNames = '^\.env(\.local)?$|\.env$|^\.?secrets(\.local)?\.json$'
$leaks = @(Get-ChildItem $outputDirectory -Recurse -File |
    Where-Object { $_.Name -match $secretNames })
if ($leaks.Count -gt 0) {
    Write-Error ("产物目录里发现疑似密钥文件，拒绝交付：`n" + ($leaks.FullName -join "`n"))
    exit 1
}

$bytes = (Get-ChildItem $outputDirectory -Recurse -File | Measure-Object -Property Length -Sum).Sum
$sizeMb = [math]::Round($bytes / 1MB, 1)
Write-Host ''
Write-Host "构建成功：$outputPath（$sizeMb MB）" -ForegroundColor Green
Write-Host '双击 Elys.exe 即可运行；没有配置密钥时自动走本地模板。'

if ($Launch) {
    Start-Process -FilePath $outputPath
}
