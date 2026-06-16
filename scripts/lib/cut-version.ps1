# cut-version.ps1 — Bump the single source-of-truth <Version> and tag it.
#
# The version lives in exactly one place: <Version> in Deckle.App.csproj. This
# script increments one SemVer segment, commits ONLY that one-line change as
# `chore(release): vX.Y.Z`, and lays a lightweight tag vX.Y.Z on that commit —
# atomically, so the csproj value and the git tag can never drift apart.
#
# It does NOT push. Pushing — and cutting the public GitHub Release from the
# tag (publish-app.ps1 -Publish) — stays a deliberate, separate act. Run this
# on `main` right after a merge, when the tree is clean and the milestone is
# real.

[CmdletBinding()]
param(
    # SemVer segment to increment. patch (default) for a fix or small step,
    # minor for a real cycle (feature, engine change), major for an overhaul.
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Bump = 'patch',

    # Target worktree. Resolves like the other lib scripts: -Pick prompts for
    # one, -Target takes an explicit path, otherwise the repo two levels up.
    [string]$Target,
    [switch]$Pick,

    # Write the bumped <Version> WITHOUT committing or tagging — an escape
    # hatch to inspect the rewrite. The whole point is the atomic bump, so
    # this is off by default.
    [switch]$NoCommit
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot

function Step($msg) { Write-Host "`n[cut-version] $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "             $msg"   -ForegroundColor Green }
function Warn($msg) { Write-Host "             $msg"   -ForegroundColor Yellow }

# ── Resolve the target worktree (same shape as launch-app.ps1) ───────────────
if ($Pick) {
    Import-Module (Join-Path $ScriptDir '_menu.psm1') -Force
    $RepoRoot = Select-Worktree -ContextDir $ScriptDir
} elseif ($Target) {
    if (-not (Test-Path $Target)) { throw "Target not found: $Target" }
    $RepoRoot = (Get-Item $Target).FullName
} else {
    $RepoRoot = Split-Path -Parent (Split-Path $ScriptDir)
}
Write-Host "Repo: $RepoRoot" -ForegroundColor DarkGray

$Csproj = Join-Path $RepoRoot 'src\Deckle.App\Deckle.App.csproj'
if (-not (Test-Path $Csproj)) { throw "csproj not found at $Csproj — is '$RepoRoot' a Deckle worktree?" }

# ── Guard: no pending TRACKED change ─────────────────────────────────────────
# We stage and commit ONLY the csproj, so no tracked change may be pending —
# otherwise it could ride into the release commit, or we'd tag a half-done
# state. Untracked files (scratch benches, in-progress notes) have nothing to
# do with a version bump, so they are deliberately ignored (-uno). The bump
# runs on main just after a merge, where the tracked state is already clean.
Step 'Check no tracked change is pending'
$dirty = & git -C $RepoRoot status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0) { throw "git status failed (code $LASTEXITCODE)" }
if ($dirty) {
    throw "Tracked changes are pending — commit or stash them first so the bump is the only change in its commit:`n$($dirty -join "`n")"
}
Ok 'Clean (no tracked change pending)'

# ── Read the current <Version> — the single source of truth ──────────────────
Step 'Read current <Version>'
$content = Get-Content -Raw -LiteralPath $Csproj
$verRe = [regex]'<Version>([^<]+)</Version>'
$m = $verRe.Match($content)
if (-not $m.Success) { throw "<Version> not found in $Csproj" }
$current = $m.Groups[1].Value.Trim()
if ($current -notmatch '^\d+\.\d+\.\d+$') {
    throw "Current <Version> '$current' is not MAJOR.MINOR.PATCH — refuse to guess."
}
Ok "current: v$current"

# ── Compute the next version ─────────────────────────────────────────────────
$p = $current.Split('.') | ForEach-Object { [int]$_ }
switch ($Bump) {
    'major' { $p = @($p[0] + 1, 0, 0) }
    'minor' { $p = @($p[0], $p[1] + 1, 0) }
    'patch' { $p = @($p[0], $p[1], $p[2] + 1) }
}
$next = $p -join '.'
$tag  = "v$next"
Step "Bump ($Bump): v$current -> $tag"

# ── Guard: the tag must not already exist ────────────────────────────────────
$existing = & git -C $RepoRoot tag --list $tag
if ($LASTEXITCODE -ne 0) { throw "git tag --list failed (code $LASTEXITCODE)" }
if ($existing) { throw "Tag $tag already exists — refuse to overwrite." }

# ── Rewrite ONLY the <Version> value ─────────────────────────────────────────
# Bounded replace on the FIRST <Version>…</Version> occurrence; every other
# line, the line endings, and the encoding stay byte-for-byte. The csproj has
# no BOM, so we write UTF-8 without BOM to keep the diff to that single line.
$newContent = $verRe.Replace($content, "<Version>$next</Version>", 1)
[System.IO.File]::WriteAllText($Csproj, $newContent, [System.Text.UTF8Encoding]::new($false))
Ok 'csproj written'

if ($NoCommit) {
    Warn 'NoCommit: wrote the bump only. No commit, no tag.'
    return
}

# ── Commit the bump and tag it ───────────────────────────────────────────────
# Stage ONLY the csproj so nothing else can ride along. The tag is lightweight,
# matching the gh-created release tags; the changelog reads its date from the
# commit, so annotation would buy nothing.
Step 'Commit and tag'
& git -C $RepoRoot add -- $Csproj
if ($LASTEXITCODE -ne 0) { throw "git add failed (code $LASTEXITCODE)" }
& git -C $RepoRoot commit -m "chore(release): $tag"
if ($LASTEXITCODE -ne 0) { throw "git commit failed (code $LASTEXITCODE)" }
& git -C $RepoRoot tag $tag
if ($LASTEXITCODE -ne 0) { throw "git tag failed (code $LASTEXITCODE)" }
Ok "committed chore(release): $tag and tagged $tag"

# ── Summary — push and publish stay deliberate, separate acts ────────────────
Write-Host ''
Write-Host "Cut v$current -> $tag on $RepoRoot" -ForegroundColor Green
Write-Host 'Not pushed. To ship this version:' -ForegroundColor DarkGray
Write-Host "  git -C `"$RepoRoot`" push; git -C `"$RepoRoot`" push origin $tag" -ForegroundColor DarkGray
Write-Host "  then menu: Release > Publish app release  (gh reuses the existing tag $tag)" -ForegroundColor DarkGray
