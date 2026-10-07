# Stop hook: run svelte-check (bun run check) when src/Ophi.Web/ has uncommitted changes that
# haven't already passed a check this session (fingerprint cache in HookCache.ps1).
# Backend-only / non-frontend changes skip it entirely.
# svelte-check catches TS errors that `bun run build` does NOT (see CLAUDE.md).

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'HookCache.ps1')

# Read the hook payload from stdin.
$raw = [Console]::In.ReadToEnd()
try { $payload = $raw | ConvertFrom-Json } catch { exit 0 }

# Avoid infinite Stop-hook loops: if we already blocked once this turn, let it stop.
if ($payload.stop_hook_active) { exit 0 }

# Don't block on missing tooling.
if (-not (Get-Command bun -ErrorAction SilentlyContinue)) { exit 0 }

$repo = (& git rev-parse --show-toplevel 2>$null)
if (-not $repo) { exit 0 }

# Pending changes under the frontend? (tracked, staged, or untracked)
$fingerprint = Get-ChangeFingerprint -Repo $repo -Pathspec 'src/Ophi.Web/'
if (-not $fingerprint) { exit 0 }

$cache = Get-HookCachePath -Name 'svelte-check' -Payload $payload
if (Test-CachedPass -CachePath $cache -Fingerprint $fingerprint) { exit 0 }

$web = Join-Path $repo 'src/Ophi.Web'
Push-Location $web
try {
    $output = & bun run check 2>&1
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}

if ($code -ne 0) {
    Remove-Item -LiteralPath $cache -ErrorAction SilentlyContinue
    [Console]::Error.WriteLine("svelte-check (bun run check) failed in src/Ophi.Web. Fix these TypeScript/Svelte errors before finishing the turn:`n")
    [Console]::Error.WriteLine(($output | Out-String))
    exit 2  # blocks the stop and feeds stderr back so the errors get addressed
}

Set-Content -LiteralPath $cache -Value $fingerprint
exit 0
