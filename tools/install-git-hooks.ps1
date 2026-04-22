Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ToolsRoot = $PSScriptRoot
$ProjectRoot = Split-Path -Parent $ToolsRoot
$GitRoot = Join-Path $ProjectRoot '.git'
$HooksRoot = Join-Path $GitRoot 'hooks'
$HookPath = Join-Path $HooksRoot 'pre-commit'
$ValidatorPath = Join-Path $ToolsRoot 'validate-readme.ps1'

if (-not (Test-Path -LiteralPath $GitRoot -PathType Container)) {
    Write-Error "Git repository was not found: $ProjectRoot"
}

if (-not (Test-Path -LiteralPath $ValidatorPath -PathType Leaf)) {
    Write-Error "README validator was not found: $ValidatorPath"
}

New-Item -ItemType Directory -Path $HooksRoot -Force | Out-Null

$HookContent = @'
#!/bin/sh

REPO_ROOT="$(git rev-parse --show-toplevel)"
SCRIPT_PATH="$REPO_ROOT/tools/validate-readme.ps1"

echo "Running README validation..."

if command -v pwsh >/dev/null 2>&1; then
    PS_CMD="pwsh"
else
    PS_CMD="powershell.exe"
fi

"$PS_CMD" -NoProfile -ExecutionPolicy Bypass -File "$SCRIPT_PATH"
STATUS=$?

if [ "$STATUS" -ne 0 ]; then
    echo "README validation failed. Commit blocked."
    exit "$STATUS"
fi

echo "README validation passed."
exit 0
'@

$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($HookPath, $HookContent, $Utf8NoBom)

Write-Host "Installed pre-commit hook: $HookPath"
Write-Host "Hook validator: $ValidatorPath"
