# Stop hook: run `dotnet build` when backend code has uncommitted changes that haven't already
# passed a build this session (fingerprint cache in HookCache.ps1).
# With AnalysisLevel=latest-recommended + TreatWarningsAsErrors (Directory.Build.props), a passing
# build is a meaningful gate: any new warning is an error. Frontend-only / docs-only changes skip it
# entirely. Sibling of svelte-check.ps1 (see CLAUDE.md / docs/agent-notes.md).

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'HookCache.ps1')

# Read the hook payload from stdin.
$raw = [Console]::In.ReadToEnd()
try { $payload = $raw | ConvertFrom-Json } catch { exit 0 }

# Avoid infinite Stop-hook loops: if we already blocked once this turn, let it stop.
if ($payload.stop_hook_active) { exit 0 }

# Don't block on missing tooling.
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { exit 0 }

$repo = (& git rev-parse --show-toplevel 2>$null)
if (-not $repo) { exit 0 }

# Pending backend changes? (.cs, project/build config — tracked, staged, or untracked)
$fingerprint = Get-ChangeFingerprint -Repo $repo `
    -Filter '\.(cs|csproj|props|sln)$|\.editorconfig$|global\.json$|nuget\.config$'
if (-not $fingerprint) { exit 0 }

$cache = Get-HookCachePath -Name 'dotnet-build' -Payload $payload
if (Test-CachedPass -CachePath $cache -Fingerprint $fingerprint) { exit 0 }

Push-Location $repo
try {
    $output = & dotnet build --nologo -v q 2>&1
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}

if ($code -ne 0) {
    Remove-Item -LiteralPath $cache -ErrorAction SilentlyContinue
    [Console]::Error.WriteLine("dotnet build failed (warnings are errors in this repo). Fix these before finishing the turn:`n")
    [Console]::Error.WriteLine(($output | Out-String))
    exit 2  # blocks the stop and feeds stderr back so the errors get addressed
}

Set-Content -LiteralPath $cache -Value $fingerprint
exit 0
