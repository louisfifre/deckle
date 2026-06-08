# deckle.ps1 — Single interactive entry point for Deckle dev workflows.
#
# Run this with F5 in VSCodium (see .vscode/launch.json) or directly from
# a PowerShell 7+ terminal. The menu groups actions by purpose:
#
#   Build             — daily compile + run loop, per-worktree.
#   Release           — publish the self-contained app ZIP, per-worktree.
#   Worktree maint    — clean artefacts, gather stats, per-worktree.
#   Setup             — bootstrap a fresh dev machine (global, no
#                       worktree picker), install local git hooks. Runtime
#                       assets are handled by the app's first-run wizard.
#
# Per-worktree actions prompt for a worktree after the action is picked
# (worktree auto-resolves when only the main repo exists). Global actions
# go straight to a short parameter prompt (or run on defaults). Every
# concrete action delegates to a single-purpose script in scripts/lib/;
# those scripts remain usable on their own CLI for automation.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir    = Join-Path $ScriptDir 'lib'

Import-Module (Join-Path $LibDir '_menu.psm1') -Force

# Helper used by every per-worktree branch: pick a worktree and bail
# gracefully on Esc (the menu module throws "Cancelled" in that case).
function Get-WorktreeOrReturn {
    try {
        $wt = Select-Worktree -ContextDir $ScriptDir
        Write-Host "Worktree: $wt" -ForegroundColor DarkGray
        return $wt
    } catch {
        Write-Host "Cancelled." -ForegroundColor Yellow
        return $null
    }
}

# Helper for short y/n prompts in global-action sub-flows. Returns
# $true / $false; default applies on bare Enter.
function Read-YesNo {
    param(
        [Parameter(Mandatory)][string]$Question,
        [bool]$Default = $false
    )
    $hint  = if ($Default) { '[Y/n]' } else { '[y/N]' }
    $ans   = Read-Host "$Question $hint"
    if ([string]::IsNullOrWhiteSpace($ans)) { return $Default }
    return ($ans -match '^(y|yes|o|oui)$')
}

# Build the top-level action list. Headers (IsHeader=$true) render as
# section dividers — Up/Down skips them automatically.
$actions = @(
    [pscustomobject]@{ Label = '── Build ──';                       Value = $null;            IsHeader = $true  }
    [pscustomobject]@{ Label = 'Build & run (Debug)';               Value = 'build-debug'                       }
    [pscustomobject]@{ Label = 'Build & run (Release)';             Value = 'build-release'                     }
    [pscustomobject]@{ Label = 'Build only (no run)';               Value = 'build-norun'                       }

    [pscustomobject]@{ Label = '── Release ──';                     Value = $null;            IsHeader = $true  }
    [pscustomobject]@{ Label = 'Publish app - build local ZIP';     Value = 'publish-app'                       }
    [pscustomobject]@{ Label = 'Publish app - GitHub Release';      Value = 'publish-release'                   }
    [pscustomobject]@{ Label = 'Changelog - regenerate from history'; Value = 'changelog'                       }

    [pscustomobject]@{ Label = '── Worktree maintenance ──';        Value = $null;            IsHeader = $true  }
    [pscustomobject]@{ Label = 'Clean bin/obj';                     Value = 'clean'                             }
    [pscustomobject]@{ Label = 'Stats (files, LOC, long files)';    Value = 'stats'                             }

    [pscustomobject]@{ Label = '── Setup ──';                       Value = $null;            IsHeader = $true  }
    [pscustomobject]@{ Label = 'Bootstrap dev environment';         Value = 'bootstrap-dev'                     }
    [pscustomobject]@{ Label = 'Install git hooks';                 Value = 'install-hooks'                     }

    [pscustomobject]@{ Label = '';                                  Value = $null;            IsHeader = $true  }
    [pscustomobject]@{ Label = 'Quit';                              Value = 'quit'                              }
)

try {
    $action = Select-Action -Header 'Pick an action (Up/Down, Enter = confirm, Esc = cancel):' -Items $actions
} catch {
    Write-Host "Cancelled." -ForegroundColor Yellow
    return
}

switch ($action) {

    # ----- Build branches — per-worktree ---------------------------------
    'build-debug' {
        $wt = Get-WorktreeOrReturn
        if ($null -eq $wt) { return }
        & (Join-Path $LibDir 'build-run.ps1') -Target $wt -Configuration Debug
    }
    'build-release' {
        $wt = Get-WorktreeOrReturn
        if ($null -eq $wt) { return }
        & (Join-Path $LibDir 'build-run.ps1') -Target $wt -Configuration Release
    }
    'build-norun' {
        $wt = Get-WorktreeOrReturn
        if ($null -eq $wt) { return }
        & (Join-Path $LibDir 'build-run.ps1') -Target $wt -Configuration Release -NoRun
    }

    # ----- Release — per-worktree ----------------------------------------
    # 'publish-app' builds the self-contained ZIP locally for inspection.
    # 'publish-release' ALSO creates the public GitHub Release (tag + upload)
    # via gh — the maintainer's act, gated behind an explicit confirmation.
    'publish-app' {
        $wt = Get-WorktreeOrReturn
        if ($null -eq $wt) { return }
        & (Join-Path $LibDir 'publish-app.ps1') -Target $wt
    }
    'publish-release' {
        $wt = Get-WorktreeOrReturn
        if ($null -eq $wt) { return }
        # Read <Version> from the target worktree so the confirmation names the
        # exact release. publish-app.ps1 -Publish runs `gh release create vX.Y.Z`,
        # which creates the tag and a PUBLIC release — never silent, always behind
        # this y/N gate (project hard rule: publish is the maintainer's act).
        $csproj = Join-Path $wt 'src\Deckle.App\Deckle.App.csproj'
        $ver    = $null
        $m = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
        if ($m) { $ver = $m.Matches[0].Groups[1].Value.Trim() }
        if (-not $ver) {
            Write-Host "Could not read <Version> from $csproj" -ForegroundColor Red
            return
        }
        Write-Host "This publishes a PUBLIC GitHub Release v$ver (creates tag v$ver, uploads the app ZIP + sha256)." -ForegroundColor Yellow
        if (-not (Read-YesNo -Question "Publish Deckle v$ver to GitHub now?" -Default $false)) {
            Write-Host "Cancelled." -ForegroundColor Yellow
            return
        }
        & (Join-Path $LibDir 'publish-app.ps1') -Target $wt -Publish
    }
    # 'changelog' regenerates CHANGELOG.md from the commit history (no publish,
    # no confirmation — it only rewrites a tracked file the maintainer reviews).
    'changelog' {
        $wt = Get-WorktreeOrReturn
        if ($null -eq $wt) { return }
        & (Join-Path $LibDir 'changelog.ps1') -Target $wt
    }

    # ----- Worktree maintenance ------------------------------------------
    'clean' {
        $wt = Get-WorktreeOrReturn
        if ($null -eq $wt) { return }
        & (Join-Path $LibDir 'clean.ps1') -Target $wt
    }
    'stats' {
        $wt = Get-WorktreeOrReturn
        if ($null -eq $wt) { return }
        & (Join-Path $LibDir 'stats.ps1') -Target $wt
    }

    # ----- Setup — global (no worktree picker) ---------------------------
    'bootstrap-dev' {
        $dryRun = Read-YesNo -Question 'Dry-run first (probe + plan, no install)?' -Default $true
        $full   = Read-YesNo -Question 'Include Tier 2 (native recompile toolchain + Ollama)?' -Default $false
        $bootstrapArgs = @{}
        if ($dryRun) { $bootstrapArgs.DryRun = $true }
        if ($full)   { $bootstrapArgs.Full = $true }
        & (Join-Path $LibDir 'bootstrap-dev-env.ps1') @bootstrapArgs
    }
    'install-hooks' {
        & (Join-Path $LibDir 'install-hooks.ps1')
    }

    'quit' {
        Write-Host "Bye." -ForegroundColor DarkGray
    }
}
