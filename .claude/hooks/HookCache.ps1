# Shared by the Stop hooks: skip a check when nothing it covers changed since it last PASSED this session.
# Without this, every turn of a session with a dirty tree re-ran the checks (~11s each), Q&A turns included.
# Only a pass is cached; a failure clears the entry, so a turn that stopped anyway re-checks next time.

function Get-ChangeFingerprint {
    # $Filter: regex a changed path must match to count (e.g. '\.cs$'); $Pathspec: git pathspecs to scope status.
    param([string]$Repo, [string[]]$Pathspec = @('.'), [string]$Filter = '.')

    # -z: unquoted paths; -uall: list files inside new untracked dirs (default collapses them to "dir/").
    $entries = (& git -C $Repo status --porcelain -z --untracked-files=all -- @Pathspec) -split "`0"
    $lines = @(); $paths = @()
    for ($i = 0; $i -lt $entries.Count; $i++) {
        $e = $entries[$i]
        if ($e.Length -lt 4) { continue }
        $status = $e.Substring(0, 2); $path = $e.Substring(3)
        if ($status -match '[RC]') { $i++ }  # rename/copy: the next entry is the source path
        if ($path -notmatch $Filter) { continue }
        $lines += "$status $path"
        if (Test-Path -LiteralPath (Join-Path $Repo $path) -PathType Leaf) { $paths += $path }
    }
    if (-not $lines) { return $null }

    # Content hashes, so an edit to an already-dirty file changes the fingerprint.
    $hashes = if ($paths) { $paths | & git -C $Repo hash-object --stdin-paths } else { @() }
    $text = ($lines + $hashes) -join "`n"
    $sha = [System.Security.Cryptography.SHA256]::Create()
    return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))) -replace '-', ''
}

function Get-HookCachePath {
    param([string]$Name, $Payload)
    $session = if ($Payload.session_id) { $Payload.session_id } else { 'no-session' }
    $dir = Join-Path ([IO.Path]::GetTempPath()) 'ophi-claude-hooks'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return Join-Path $dir "$Name-$session.txt"
}

function Test-CachedPass {
    param([string]$CachePath, [string]$Fingerprint)
    return (Test-Path -LiteralPath $CachePath) -and ((Get-Content -LiteralPath $CachePath -Raw).Trim() -eq $Fingerprint)
}
