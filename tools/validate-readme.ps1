Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ToolsRoot = $PSScriptRoot
$ProjectRoot = Split-Path -Parent $ToolsRoot
$ReadmePath = Join-Path $ProjectRoot 'README.md'
$LogRoot = Join-Path $ProjectRoot 'Logs'
$LogPath = Join-Path $LogRoot ("validate-readme-{0}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))

New-Item -ItemType Directory -Path $LogRoot -Force | Out-Null

$Results = New-Object System.Collections.Generic.List[object]

function Add-Check {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [bool]$Passed,
        [Parameter(Mandatory = $true)]
        [string]$Detail
    )

    $Results.Add([PSCustomObject]@{
        Name = $Name
        Passed = $Passed
        Detail = $Detail
    }) | Out-Null
}

function Test-FileExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Add-Check -Name $Name -Passed (Test-Path -LiteralPath $Path -PathType Leaf) -Detail $Path
}

function Test-ReadmeContains {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Pattern,
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    Add-Check -Name $Name -Passed ($Content -match $Pattern) -Detail $Pattern
}

Test-FileExists -Name 'README.md exists' -Path $ReadmePath

$ReadmeContent = ''
if (Test-Path -LiteralPath $ReadmePath -PathType Leaf) {
    $ReadmeContent = Get-Content -LiteralPath $ReadmePath -Raw -Encoding UTF8
}

Test-FileExists -Name 'TeacherApp.csproj exists' -Path (Join-Path $ProjectRoot 'src\TeacherApp\TeacherApp.csproj')
Test-FileExists -Name 'StudentApp.csproj exists' -Path (Join-Path $ProjectRoot 'src\StudentApp\StudentApp.csproj')
Test-FileExists -Name 'SchoolMathTrainer.Api.csproj exists' -Path (Join-Path $ProjectRoot 'src\SchoolMathTrainer.Api\SchoolMathTrainer.Api.csproj')
Test-FileExists -Name 'SchoolMathTrainer.TeacherAdmin.csproj exists' -Path (Join-Path $ProjectRoot 'src\SchoolMathTrainer.TeacherAdmin\SchoolMathTrainer.TeacherAdmin.csproj')

$SolutionFiles = @(Get-ChildItem -LiteralPath $ProjectRoot -Filter '*.sln' -File -ErrorAction SilentlyContinue)
Add-Check -Name 'Solution file exists' -Passed ($SolutionFiles.Count -gt 0) -Detail (Join-Path $ProjectRoot '*.sln')

Test-ReadmeContains -Name 'README contains dotnet build' -Pattern 'dotnet\s+build' -Content $ReadmeContent
Test-ReadmeContains -Name 'README contains TeacherApp dotnet run' -Pattern 'dotnet\s+run\s+--project\s+\.\\src\\TeacherApp\\TeacherApp\.csproj' -Content $ReadmeContent
Test-ReadmeContains -Name 'README contains StudentApp dotnet run' -Pattern 'dotnet\s+run\s+--project\s+\.\\src\\StudentApp\\StudentApp\.csproj' -Content $ReadmeContent
Test-ReadmeContains -Name 'README contains apiBaseUrl' -Pattern '(?i)apiBaseUrl' -Content $ReadmeContent

$Failed = @($Results | Where-Object { -not $_.Passed })
$Lines = New-Object System.Collections.Generic.List[string]

$Lines.Add('README validation') | Out-Null
$Lines.Add("Project root: $ProjectRoot") | Out-Null
$Lines.Add("README: $ReadmePath") | Out-Null
$Lines.Add("Log: $LogPath") | Out-Null
$Lines.Add('') | Out-Null

foreach ($Result in $Results) {
    $Status = if ($Result.Passed) { 'OK' } else { 'CHYBA' }
    $Lines.Add(('{0}: {1} - {2}' -f $Status, $Result.Name, $Result.Detail)) | Out-Null
}

$Lines.Add('') | Out-Null
if ($Failed.Count -eq 0) {
    $Lines.Add('FINAL: OK - all checks passed') | Out-Null
}
else {
    $Lines.Add(('FINAL: CHYBA - failed checks: {0}' -f $Failed.Count)) | Out-Null
}

$Lines | Tee-Object -FilePath $LogPath

if ($Failed.Count -gt 0) {
    exit 1
}

exit 0
