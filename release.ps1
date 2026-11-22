#Requires -Version 5.1
<#
.SYNOPSIS
    Публикует релиз лаунчера или визуала на GitHub и обновляет update.json.

.DESCRIPTION
    -Part launcher — собирает RevealLoader.exe (dotnet publish), считает SHA-256,
                     создаёт релиз с тегом v<версия> и обновляет блок "launcher".
    -Part visual   — берёт готовый RevealClient.jar, считает SHA-256,
                     создаёт релиз с тегом visual-<версия> и обновляет блок "visual".

    Файлы в релиз кладутся под именами RevealLoader.exe и RevealClient.jar —
    именно на них ссылается update.json, менять имена нельзя.

.PARAMETER DryRun
    Собрать, посчитать хеши и обновить update.json локально, но не создавать релиз
    и не коммитить. Результат можно спокойно посмотреть и откатить через git.

.EXAMPLE
    .\release.ps1 -Part launcher -Version 1.1.0 -Notes "Исправлен запуск игры"
    .\release.ps1 -Part visual -Version 1.0.3
    .\release.ps1 -Part launcher -Version 1.2.0 -DryRun
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('launcher', 'visual')]
    [string]$Part,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Notes = '',

    [string]$JarPath = '',

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

if ($Version -notmatch '^\d+(\.\d+){1,3}$') {
    throw "Версия должна быть вида 1.2 или 1.2.0 (получено: '$Version')"
}

# --- определяем репозиторий из origin ---
$origin = (& git remote get-url origin 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $origin) {
    throw 'Не удалось прочитать origin. Запустите скрипт в клоне репозитория.'
}
$origin = $origin.Trim() -replace '\.git$', ''
if ($origin -notmatch 'github\.com[/:](?<slug>.+)$') {
    throw "origin не похож на ссылку GitHub: $origin"
}
$Slug = $Matches['slug']

if ($Notes -eq '') {
    $Notes = if ($Part -eq 'launcher') { "Лаунчер $Version" } else { "Визуал $Version" }
}

if ($Part -eq 'launcher') {
    $AssetName = 'RevealLoader.exe'
    $Tag = "v$Version"
} else {
    $AssetName = 'RevealClient.jar'
    $Tag = "visual-$Version"
}

# --- готовим файл для публикации ---
if ($Part -eq 'launcher') {
    # в PATH может лежать dotnet без SDK, поэтому проверяем наличие SDK у каждого кандидата
    $dotnetCandidates = @(
        (Get-Command dotnet -ErrorAction SilentlyContinue).Source
        (Join-Path $env:LOCALAPPDATA 'RevealLauncher\dotnet-sdk10\dotnet.exe')
    )
    $dotnet = $null
    foreach ($candidate in $dotnetCandidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate) -and (& $candidate --list-sdks 2>$null)) {
            $dotnet = $candidate
            break
        }
    }
    if (-not $dotnet) {
        throw 'Не найден .NET SDK. Установите SDK или положите его в %LOCALAPPDATA%\RevealLauncher'
    }

    $publishDir = Join-Path $PSScriptRoot 'publish'
    Write-Host "Сборка лаунчера $Version ..." -ForegroundColor Cyan
    & $dotnet publish (Join-Path $PSScriptRoot 'RevealLoader.csproj') `
        -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась' }

    $File = Join-Path $publishDir $AssetName
}
else {
    if ($JarPath -eq '') {
        $JarPath = Join-Path $PSScriptRoot 'Resources\RevealClient.jar'
    }
    if (-not (Test-Path -LiteralPath $JarPath)) {
        throw "Не найден JAR: $JarPath"
    }
    # Ассет в релизе должен называться RevealClient.jar независимо от имени исходного файла.
    $stage = Join-Path $env:TEMP 'revealloader-release'
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    $File = Join-Path $stage $AssetName
    Copy-Item -LiteralPath $JarPath -Destination $File -Force
}

if (-not (Test-Path -LiteralPath $File)) { throw "Не найден файл для публикации: $File" }

$size = (Get-Item -LiteralPath $File).Length
$sha = (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLower()
$url = "https://github.com/$Slug/releases/download/$Tag/$AssetName"

Write-Host ''
Write-Host "файл:   $File" -ForegroundColor Green
Write-Host "размер: $([math]::Round($size / 1MB, 2)) МБ"
Write-Host "sha256: $sha"
Write-Host "ссылка: $url"
Write-Host ''

# --- обновляем update.json ---
$manifestPath = Join-Path $PSScriptRoot 'update.json'

function Get-JsonText([string]$value) {
    if ($null -eq $value) { return '""' }
    $escaped = $value.Replace('\', '\\').Replace('"', '\"')
    $escaped = $escaped.Replace("`r", '').Replace("`n", '\n')
    return '"' + $escaped + '"'
}

function Format-Block([string]$name, $block) {
    return (@(
            "  `"$name`": {",
            "    `"version`": $(Get-JsonText $block.version),",
            "    `"url`": $(Get-JsonText $block.url),",
            "    `"sha256`": $(Get-JsonText $block.sha256),",
            "    `"size`": $($block.size),",
            "    `"notes`": $(Get-JsonText $block.notes)",
            '  }'
        ) -join "`n")
}

$manifest = @{
    launcher = @{ version = '0.0.0'; url = ''; sha256 = ''; size = 0; notes = '' }
    visual   = @{ version = '0.0.0'; url = ''; sha256 = ''; size = 0; notes = '' }
}

if (Test-Path -LiteralPath $manifestPath) {
    $current = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($key in 'launcher', 'visual') {
        if ($current.PSObject.Properties.Name -contains $key) {
            $manifest[$key].version = [string]$current.$key.version
            $manifest[$key].url = [string]$current.$key.url
            $manifest[$key].sha256 = [string]$current.$key.sha256
            $manifest[$key].size = [long]$current.$key.size
            $manifest[$key].notes = [string]$current.$key.notes
        }
    }
}

$manifest[$Part].version = $Version
$manifest[$Part].url = $url
$manifest[$Part].sha256 = $sha
$manifest[$Part].size = $size
$manifest[$Part].notes = $Notes

$json = '{' + "`n" + (Format-Block 'launcher' $manifest.launcher) + ',' + "`n" +
        (Format-Block 'visual' $manifest.visual) + "`n" + '}' + "`n"

# проверяем, что получился валидный JSON, и пишем без BOM
$null = $json | ConvertFrom-Json
[System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "update.json обновлён (блок '$Part')" -ForegroundColor Green

if ($DryRun) {
    Write-Host ''
    Write-Host 'DryRun: релиз не создан, изменения не закоммичены.' -ForegroundColor Yellow
    Write-Host 'Проверь update.json и запусти без -DryRun, когда всё верно.'
    return
}

# --- публикуем релиз ---
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) {
    $candidate = Join-Path $env:ProgramFiles 'GitHub CLI\gh.exe'
    if (Test-Path -LiteralPath $candidate) { $gh = $candidate }
}
if (-not $gh) {
    throw 'Не найден gh (GitHub CLI). Установите: winget install GitHub.cli'
}

# Проверки gh пишут в stderr при ненулевом коде, поэтому временно снимаем Stop,
# иначе PowerShell 5.1 превращает это в фатальную NativeCommandError.
$prevEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & $gh auth status *> $null
    $ghAuthed = ($LASTEXITCODE -eq 0)

    & $gh release view $Tag --repo $Slug *> $null
    $releaseExists = ($LASTEXITCODE -eq 0)
}
finally {
    $ErrorActionPreference = $prevEap
}

if (-not $ghAuthed) {
    throw 'gh не авторизован. Выполните: gh auth login --hostname github.com --git-protocol https --web'
}
if ($releaseExists) {
    throw "Релиз $Tag уже существует. Выбери другой номер версии."
}

Write-Host "Публикация релиза $Tag ..." -ForegroundColor Cyan
& $gh release create $Tag $File --repo $Slug --title "$Tag" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "gh release create завершился с ошибкой" }

# --- коммитим и пушим манифест ---
& git add update.json
& git commit -m "Релиз $Tag"
if ($LASTEXITCODE -ne 0) { throw 'git commit не прошёл (возможно, нечего коммитить)' }
& git push
if ($LASTEXITCODE -ne 0) { throw 'git push не прошёл' }

Write-Host ''
Write-Host "Готово: $Tag" -ForegroundColor Green
Write-Host "Манифест: https://raw.githubusercontent.com/$Slug/main/update.json"
