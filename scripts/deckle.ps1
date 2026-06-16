# deckle.ps1 — Single interactive entry point for Deckle dev workflows.
#
# Run this with F5 in VSCodium (see .vscode/launch.json) or directly from
# a PowerShell 7+ terminal. The menu groups actions by purpose:
#
#   Launch            — start an already-built app, per-worktree.
#   Build             — daily compile + run loop, per-worktree.
#   Release           — cut release artefacts / GitHub releases only,
#                       per-worktree where applicable.
#   Worktree maint    — clean artefacts, gather stats, update docs,
#                       per-worktree.
#   Setup             — bootstrap a fresh dev machine (global, no
#                       worktree picker), install local git hooks, provision
#                       optional runtime assets.
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

function Read-Optional {
    param([Parameter(Mandatory)][string]$Question)
    $answer = Read-Host $Question
    if ([string]::IsNullOrWhiteSpace($answer)) { return $null }
    return $answer.Trim()
}

# Build the top-level action list. Headers (IsHeader=$true) render as
# section dividers — Up/Down skips them automatically.
$actions = @(
    [pscustomobject]@{ Label = '── Launch ──';                      Value = $null;            IsHeader = $true  }
    [pscustomobject]@{ Label = 'Launch app (Release)';              Value = 'launch-release'                    }
    [pscustomobject]@{ Label = 'Launch app (Debug)';                Value = 'launch-debug'                      }

    [pscustomobject]@{ Label = '── Build ──';                       Value = $null;            IsHeader = $true  }
    [pscustomobject]@{ Label = 'Build and run app (Release)';       Value = 'build-release'                     }
    [pscustomobject]@{ Label = 'Build and run app (Debug)';         Value = 'build-debug'                       }
    [pscustomobject]@{ Label = 'Build app without running';         Value = 'build-norun'                       }

    [pscustomobject]@{ Label = '── Release ──';                     Value = $null;            IsHeader = $true  }
    [pscustomobject]@{ Label = 'Publish app release';               Value = 'publish-release'                   }
    [pscustomobject]@{ Label = 'Prepare app release artifacts';     Value = 'build-release-artifacts'           }
    [pscustomobject]@{ Label = 'Prepare native runtime release';    Value = 'native-runtime'                    }

    [pscustomobject]@{ Label = '── MCP ──';                         Value = $null;            IsHeader = $true  }
    [pscustomobject]@{ Label = 'Install / update Anytype MCP';      Value = 'install-anytype-mcp'               }

    [pscustomobject]@{ Label = '── Worktree maintenance ──';        Value = $null;            IsHeader = $true  }
    [pscustomobject]@{ Label = 'Clean build outputs';               Value = 'clean'                             }
    [pscustomobject]@{ Label = 'Show module stats';                 Value = 'stats'                             }
    [pscustomobject]@{ Label = 'Update README pulse';               Value = 'readme-stats'                      }
    [pscustomobject]@{ Label = 'Update changelog';                  Value = 'changelog'                         }

    [pscustomobject]@{ Label = '── Setup ──';                       Value = $null;            IsHeader = $true  }
    [pscustomobject]@{ Label = 'Bootstrap dev environment';         Value = 'bootstrap-dev'                     }
    [pscustomobject]@{ Label = 'Set up runtime assets';             Value = 'setup-assets'                      }
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

    # ----- Launch branches — per-worktree --------------------------------
    # Both configurations launch an ALREADY-built exe without recompiling;
    # launch-app.ps1 resolves the freshest Deckle.exe under the matching
    # release\ or debug\ pivot. Build the configuration first if it is missing.
    'launch-release' {
        $wt = Get-WorktreeOrReturn
        if ($null -eq $wt) { return }
        & (Join-Path $LibDir 'launch-app.ps1') -Target $wt -Configuration Release
    }
    'launch-debug' {
        $wt = Get-WorktreeOrReturn
        if ($null -eq $wt) { return }
        & (Join-Path $LibDir 'launch-app.ps1') -Target $wt -Configuration Debug
    }

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
    # 'publish-release' builds both release artefacts (installer exe + app
    # payload ZIP) and creates the public GitHub Release (tag + upload) via gh —
    # the maintainer's act, gated behind an explicit confirmation. To build the
    # artefacts locally WITHOUT publishing, call publish-app.ps1 (no -Publish).
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
        Write-Host "This publishes a PUBLIC GitHub Release v$ver (creates tag v$ver, uploads the installer exe + app ZIP + sha256)." -ForegroundColor Yellow
        if (-not (Read-YesNo -Question "Publish Deckle v$ver to GitHub now?" -Default $false)) {
            Write-Host "Cancelled." -ForegroundColor Yellow
            return
        }
        & (Join-Path $LibDir 'publish-app.ps1') -Target $wt -Publish
    }
    'build-release-artifacts' {
        $wt = Get-WorktreeOrReturn
        if ($null -eq $wt) { return }
        & (Join-Path $LibDir 'publish-app.ps1') -Target $wt
    }
    'native-runtime' {
        $version = Read-Optional -Question 'Native bundle version (X.Y.Z)'
        if (-not $version) {
            Write-Host "Cancelled: version is required." -ForegroundColor Yellow
            return
        }
        $whisperRepo = Read-Optional -Question 'Path to whisper.cpp clone with build/bin'
        if (-not $whisperRepo) {
            Write-Host "Cancelled: whisper.cpp path is required." -ForegroundColor Yellow
            return
        }
        $outDir = Read-Optional -Question 'Output directory (blank = temp)'
        $publish = Read-YesNo -Question 'Publish native runtime GitHub Release after building?' -Default $false
        if ($publish) {
            Write-Host "This publishes a PUBLIC GitHub Release native-v$version via gh." -ForegroundColor Yellow
            if (-not (Read-YesNo -Question "Publish native-v$version now?" -Default $false)) {
                Write-Host "Cancelled." -ForegroundColor Yellow
                return
            }
        }

        $nativeArgs = @{
            Version = $version
            WhisperRepo = $whisperRepo
        }
        if ($outDir)  { $nativeArgs.OutDir = $outDir }
        if ($publish) { $nativeArgs.Publish = $true }
        & (Join-Path $LibDir 'publish-native-runtime.ps1') @nativeArgs
    }

    # ----- MCP — publish a versioned host + repoint the `current` junction -
    # 'install-anytype-mcp' publishes the Anytype MCP host into a fresh
    # versioned dir under %LOCALAPPDATA%\Deckle\mcp\anytype\ and points the
    # `current` junction (Scoop model) at it; .claude.json targets current\ once.
    # AI clients stop spawning (and locking) the build-output exe. Re-run any
    # time to cut a new version — no need to close clients, nothing running gets
    # overwritten. publish is the maintainer's act — gated behind this y/N.
    'install-anytype-mcp' {
        Write-Host "Publishes the Anytype MCP to %LOCALAPPDATA%\Deckle\mcp\anytype\ (versioned + 'current' junction) and points .claude.json at current\ — AI clients stop locking the build output." -ForegroundColor Yellow
        Write-Host "Safe to re-run to cut a new version: open sessions keep theirs, new spawns get the fresh one." -ForegroundColor Yellow
        if (-not (Read-YesNo -Question 'Install / update the Anytype MCP now?' -Default $false)) {
            Write-Host "Cancelled." -ForegroundColor Yellow
            return
        }
        & (Join-Path $LibDir 'install-anytype-mcp.ps1')
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
    'readme-stats' {
        $wt = Get-WorktreeOrReturn
        if ($null -eq $wt) { return }
        & (Join-Path $LibDir 'update-readme-stats.ps1') -Target $wt
    }
    # 'changelog' regenerates CHANGELOG.md from the commit history (no publish,
    # no confirmation — it only rewrites a tracked file the maintainer reviews).
    'changelog' {
        $wt = Get-WorktreeOrReturn
        if ($null -eq $wt) { return }
        & (Join-Path $LibDir 'changelog.ps1') -Target $wt
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
    'setup-assets' {
        Write-Host "This may download native runtime and Whisper model files." -ForegroundColor Yellow
        if (-not (Read-YesNo -Question 'Continue with runtime asset setup?' -Default $false)) {
            Write-Host "Cancelled." -ForegroundColor Yellow
            return
        }

        $assetArgs = @{}
        $fromRelease = Read-Optional -Question 'Native runtime release version X.Y.Z (blank = local/sibling source or skip)'
        if ($fromRelease) { $assetArgs.FromRelease = $fromRelease }
        if (Read-YesNo -Question 'Download ggml-large-v3.bin (~3 GB)?' -Default $false) { $assetArgs.WithLarge = $true }
        if (Read-YesNo -Question 'Force re-copy / re-download existing files?' -Default $false) { $assetArgs.Force = $true }
        & (Join-Path $LibDir 'setup-assets.ps1') @assetArgs
    }
    'install-hooks' {
        & (Join-Path $LibDir 'install-hooks.ps1')
    }

    'quit' {
        Write-Host "Bye." -ForegroundColor DarkGray
    }
}
