<#
.SYNOPSIS
    Bootstrap the dev environment
.DESCRIPTION
    Brings a fresh Windows 11 machine up to speed for building and running
    Deckle. Probes what is already installed, then installs the missing pieces
    with winget (OS-level installers) and scoop (dev toolchain). Idempotent:
    safe to re-run.
.NOTES
    section: Machine
    order: 110
    action: Plan the bootstrap (dry run) | -DryRun
    action: Bootstrap dev environment | -Yes | danger | Installs tools on this machine with winget and scoop.
#>

# Left out on purpose: cloning whisper.cpp (its location is a per-dev choice)
# and pulling Ollama models (the model is a per-deployment choice). With -Full
# the script prints both commands instead.

[CmdletBinding()]
param(
    # Probe + report only. No install, no env var change, no asset download.
    [switch]$DryRun,

    # Include the native recompile toolchain (CMake, Ninja, Vulkan SDK, and
    # MinGW for the Anytype backend's cgo build) and Ollama. whisper.cpp
    # itself is built with the MSVC toolset of the Visual Studio install
    # above. Required to rebuild native DLLs or to use the local LLM rewrite
    # path. Off by default to keep first setup lean.
    [switch]$Full,

    # Also invoke scripts/commands/setup/setup-assets.ps1 after environment bootstrap.
    # Kept for explicit dev-machine provisioning; the app's setup wizard is
    # the normal runtime-assets path.
    [switch]$IncludeAssets,

    # Deprecated. Runtime assets are skipped unless -IncludeAssets; this switch
    # only cancels -IncludeAssets.
    [switch]$SkipAssets,

    # Release tag passed to setup-assets.ps1 -FromRelease. Pins which
    # native-vX.Y.Z bundle gets downloaded. Bump this default whenever a
    # new native-vX.Y.Z release ships on GitHub.
    [string]$AssetsRelease = '1.9.1',

    # Skip the confirmation prompt before installing.
    [switch]$Yes
)

$ErrorActionPreference = 'Stop'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path (Split-Path -Parent (Split-Path -Parent $ScriptDir)) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')

$Workflow = 'Bootstrap dev environment'
$plan = $null
$results = $null
$RuntimeAssetsStatus = 'Skipped'

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence "Development environment bootstrap failed before completion." `
        -Details ([ordered]@{
            Tier             = $(if ($Full) { 'Full' } else { 'Default' })
            'Dry run'        = $(if ($DryRun) { 'Yes' } else { 'No' })
            'Planned items'  = $(if ($plan) { $plan.Count } else { $null })
            'Runtime assets' = $RuntimeAssetsStatus
            Error            = $_.Exception.Message
        })
    throw
}

function Write-Section($title) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
}

function Write-Step($msg)  { Write-Host "  $msg" -ForegroundColor Gray }
function Write-Good($msg)  { Write-Host "  [OK]      $msg" -ForegroundColor Green }
function Write-Miss($msg)  { Write-Host "  [MISSING] $msg" -ForegroundColor Yellow }
function Write-Skip($msg)  { Write-Host "  [SKIP]    $msg" -ForegroundColor DarkGray }
function Write-Fail($msg)  { Write-Host "  [FAIL]    $msg" -ForegroundColor Red }

# $args is an automatic variable: a parameter of that name silently breaks
# splatting, hence the fixed --version flag, which every probed tool accepts.
function Test-Command([string]$exe) {
    if (-not (Get-Command $exe -ErrorAction SilentlyContinue)) { return $null }
    try {
        $out = & $exe --version 2>$null | Select-Object -First 1
        if ($LASTEXITCODE -ne 0) { return $null }
        return ($out | Out-String).Trim()
    } catch { return $null }
}

function Get-VsInfo {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { return $null }
    $json = & $vswhere -latest -prerelease -products * -format json 2>$null
    if (-not $json) { return $null }
    return $json | ConvertFrom-Json | Select-Object -First 1
}

# One list feeds both the fresh install (winget --override) and the check of
# an existing install (setup.exe modify --add). VC.Tools.x86.x64 is needed
# even for pure-C# projects: the WinUI XAML compiler's GetLatestMSVCVersion
# task enumerates VC\Tools\MSVC\, and without it the build fails with MSB4018.
$RequiredVsComponents = @(
    'Microsoft.VisualStudio.Workload.ManagedDesktop',
    'Microsoft.VisualStudio.ComponentGroup.WindowsAppSDK.Cs',
    'Microsoft.VisualStudio.Component.VC.Tools.x86.x64'
)

function Ensure-VsComponents {
    param([string[]]$RequiredComponents = $script:RequiredVsComponents)

    $vs = Get-VsInfo
    if (-not $vs) {
        throw "No Visual Studio install detected — install VS first."
    }

    $installedIds = @($vs.packages | ForEach-Object { $_.id })
    $missing = @($RequiredComponents | Where-Object { $_ -notin $installedIds })

    if ($missing.Count -eq 0) {
        Write-Good "All required VS components are present"
        return
    }

    Write-Step "Missing VS components:"
    foreach ($m in $missing) { Write-Host "    - $m" -ForegroundColor Yellow }
    Write-Step "Adding via VS Installer (a UAC prompt will appear)..."

    $setup = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\setup.exe'
    if (-not (Test-Path $setup)) {
        throw "VS Installer setup.exe not found at $setup"
    }

    $setupArgs = @('modify', '--installPath', $vs.installationPath)
    foreach ($c in $missing) { $setupArgs += @('--add', $c) }
    $setupArgs += @('--passive', '--wait', '--norestart')

    & $setup @setupArgs

    if ($LASTEXITCODE -ne 0) {
        throw "setup.exe modify exited with code $LASTEXITCODE — try the VS Installer GUI manually"
    }
    Write-Good "VS components added successfully"
}

Write-Section "Probing current state"

$state = [ordered]@{
    PowerShell  = $PSVersionTable.PSVersion.ToString()
    Winget      = Test-Command 'winget'
    Git         = Test-Command 'git'
    Gh          = Test-Command 'gh'
    Dotnet      = Test-Command 'dotnet'
    Scoop       = Test-Command 'scoop'
    Gcc         = Test-Command 'gcc'
    Cmake       = Test-Command 'cmake'
    Ninja       = Test-Command 'ninja'
    VulkanSdk   = if ($env:VULKAN_SDK -and (Test-Path $env:VULKAN_SDK)) { $env:VULKAN_SDK } else { $null }
    Ollama      = Test-Command 'ollama'
    VsCodium    = Test-Command 'codium'
}

foreach ($k in $state.Keys) {
    $v = $state[$k]
    if ($v) { Write-Good "$k : $v" } else { Write-Miss $k }
}

# winget and git both missing is almost always a session PATH problem, not an
# install gap: the PowerShell extension's Integrated Console in VS Code /
# VSCodium starts a host without WindowsApps in PATH, which hides winget.
if (-not $state.Winget -and -not $state.Git) {
    Write-Host ""
    Write-Host "Both winget and git report missing — this is almost certainly a session" -ForegroundColor Yellow
    Write-Host "PATH issue, not a real install gap." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Try running this script from a regular PowerShell terminal:" -ForegroundColor Yellow
    Write-Host "  - Win+X -> Terminal (Windows Terminal with pwsh.exe), OR" -ForegroundColor DarkGray
    Write-Host "  - Right-click in Explorer -> 'Open in Terminal'" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "If it still fails there, then winget / git are genuinely missing." -ForegroundColor Yellow
    throw "Aborting — re-run from a fresh PowerShell session."
}
if (-not $state.Winget) {
    throw "winget is required. Install 'App Installer' from the Microsoft Store, then re-run."
}
if (-not $state.Git) {
    throw "git is required. Install via 'winget install --id Git.Git -e', then re-run."
}

Write-Section "Install plan"

$plan = New-Object System.Collections.Generic.List[object]

function Add-Plan($name, $why, $cmd) {
    $plan.Add([pscustomobject]@{ Name = $name; Why = $why; Cmd = $cmd })
}

if (-not $state.Gh) {
    Add-Plan 'GitHub CLI' 'GitHub auth for push and PR workflows' {
        winget install --id GitHub.cli -e --accept-source-agreements --accept-package-agreements
    }
}

if (-not $state.Dotnet) {
    # .NET 10 SDK is required both for MSBuild to resolve Microsoft.NET.Sdk
    # and for `dotnet` CLI diagnostics. The VS ManagedDesktop workload does
    # NOT reliably bundle it — install explicitly.
    Add-Plan '.NET 10 SDK' '.NET SDK resolution for MSBuild + dotnet CLI' {
        winget install --id Microsoft.DotNet.SDK.10 -e `
            --accept-source-agreements --accept-package-agreements
    }
}

if (-not (Get-VsInfo)) {
    # This winget ID carries no year suffix, unlike the IDs of earlier
    # Visual Studio releases.
    Add-Plan 'Visual Studio 2026 Community + WinUI components' `
        'Windows SDK + .NET desktop + WinAppSDK + MSVC tools (needed by the WinUI 3 templates and native module work; build itself runs through dotnet build)' {
        # Only a workload accepts ;includeRecommended; component groups and
        # individual components don't.
        $overrideParts = @('--quiet', '--wait', '--norestart')
        foreach ($c in $script:RequiredVsComponents) {
            if ($c -match '\.Workload\.') {
                $overrideParts += "--add $c;includeRecommended"
            } else {
                $overrideParts += "--add $c"
            }
        }
        $override = $overrideParts -join ' '
        winget install --id Microsoft.VisualStudio.Community -e `
            --accept-source-agreements --accept-package-agreements `
            --override $override
    }
} else {
    # winget refuses to modify an installed VS ("already up to date"), so
    # missing components go through setup.exe modify.
    Add-Plan 'VS WinUI components verification' `
        'Probe vswhere for required workloads/components and add missing ones via setup.exe modify' {
        Ensure-VsComponents
    }
}

if ($Full) {
    if (-not $state.Scoop) {
        Add-Plan 'Scoop' 'Per-user package manager for the native toolchain' {
            Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser -Force
            Invoke-RestMethod -Uri 'https://get.scoop.sh' | Invoke-Expression
        }
    }

    Add-Plan 'Scoop extras bucket' 'Provides the Vulkan SDK package' {
        scoop bucket add extras 2>$null
    }

    if (-not $state.Gcc) {
        Add-Plan 'MinGW (GCC 15.2.0)'  'GCC toolchain for the Anytype backend cgo build (whisper.cpp uses MSVC)' { scoop install mingw }
    }
    if (-not $state.Cmake) {
        Add-Plan 'CMake' 'Build system for whisper.cpp' { scoop install cmake }
    }
    if (-not $state.Ninja) {
        Add-Plan 'Ninja' 'Fast generator used by the whisper.cpp CMake preset' { scoop install ninja }
    }
    if (-not $state.VulkanSdk) {
        Add-Plan 'Vulkan SDK (LunarG)' 'Headers + loader for ggml-vulkan.dll' { scoop install vulkan }
    }

    if (-not $state.Ollama) {
        Add-Plan 'Ollama' 'Local LLM runtime for the rewrite feature' {
            winget install --id Ollama.Ollama -e --accept-source-agreements --accept-package-agreements
        }
    }
}

if ($plan.Count -eq 0) {
    Write-Step "Nothing to install — current state already covers the requested tier."
} else {
    foreach ($item in $plan) {
        Write-Host ("  + {0,-50} {1}" -f $item.Name, $item.Why) -ForegroundColor White
    }
}

if ($DryRun) {
    Write-Section "Dry run — exiting before any install."
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Success `
        -Sentence "Development environment bootstrap was probed only; no install was run." `
        -Details ([ordered]@{
            Tier            = $(if ($Full) { 'Full' } else { 'Default' })
            'Dry run'       = 'Yes'
            'Planned items' = $plan.Count
            Winget          = $(if ($state.Winget) { 'Present' } else { 'Missing' })
            Git             = $(if ($state.Git) { 'Present' } else { 'Missing' })
            Dotnet          = $(if ($state.Dotnet) { 'Present' } else { 'Missing' })
            VisualStudio    = $(if (Get-VsInfo) { 'Present' } else { 'Missing' })
        }) `
        -Next @("Re-run without -DryRun to apply the plan.")
    return
}

if ($plan.Count -gt 0 -and -not $Yes) {
    Write-Host ""
    # Both the VS winget install and setup.exe modify need elevation; a UAC
    # prompt nobody answers stalls the install.
    $needsUac = $plan | Where-Object { $_.Name -match 'Visual Studio|VS WinUI components' }
    if ($needsUac) {
        Write-Host "Heads-up: any Visual Studio install or modify triggers a UAC prompt." -ForegroundColor Yellow
        Write-Host "Stay at the keyboard to click 'Yes' when it appears." -ForegroundColor Yellow
        Write-Host ""
    }
    # The menu confirms a danger action before running it and passes -Yes; this
    # prompt serves a terminal run without the menu.
    $answer = Read-Host 'Proceed with environment bootstrap? [y/N]'
    if ($answer -notmatch '^(y|yes)$') {
        Write-Host "Aborted." -ForegroundColor Yellow
        return
    }
}

$results = New-Object System.Collections.Generic.List[object]

if ($plan.Count -gt 0) {
    Write-Section "Installing"
    foreach ($item in $plan) {
        Write-Step "→ $($item.Name)"
        $itemFailed = $false
        $itemError = $null
        try {
            & $item.Cmd
            if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
                $itemFailed = $true
                $itemError = "installer exited with code $LASTEXITCODE"
                Write-Fail "$($item.Name) — $itemError"
            } else {
                Write-Good $item.Name
            }
        } catch {
            $itemFailed = $true
            $itemError = $_.Exception.Message
            Write-Fail "$($item.Name) — $itemError"
        }
        $results.Add([pscustomobject]@{
            Name    = $item.Name
            Failed  = $itemFailed
            Error   = $itemError
        })
    }
}

Write-Section "Environment variables"

# Scoop's vulkan package does not always set VULKAN_SDK.
if ($Full -and -not $env:VULKAN_SDK) {
    $scoopVulkan = "$env:USERPROFILE\scoop\apps\vulkan\current"
    if (Test-Path $scoopVulkan) {
        [Environment]::SetEnvironmentVariable('VULKAN_SDK', $scoopVulkan, 'User')
        Write-Good "VULKAN_SDK = $scoopVulkan (User)"
    } else {
        Write-Skip "VULKAN_SDK — scoop vulkan path not found; set manually if you installed elsewhere"
    }
}

if ($IncludeAssets -and -not $SkipAssets) {
    Write-Section "Runtime assets"
    $setup = Join-Path $ScriptDir 'setup-assets.ps1'
    if (-not (Test-Path $setup)) {
        Write-Fail "setup-assets.ps1 not found at $setup"
        $RuntimeAssetsStatus = 'Failed: setup-assets.ps1 missing'
    } else {
        Write-Step "Invoking setup-assets.ps1 -FromRelease $AssetsRelease"
        & $setup -FromRelease $AssetsRelease
        $RuntimeAssetsStatus = "Requested from native-v$AssetsRelease"
    }
} else {
    Write-Section "Runtime assets"
    Write-Skip "Skipped — handled by the app's first-run wizard. Pass -IncludeAssets to provision from this script."
    $RuntimeAssetsStatus = 'Skipped: handled by first-run wizard'
}

# A tool installed above may stay off this session's PATH until a new
# terminal opens, so "missing" here does not prove a failed install.
$finalState = [ordered]@{
    Gh        = Test-Command 'gh'
    Dotnet    = Test-Command 'dotnet'
    Scoop     = Test-Command 'scoop'
    Gcc       = Test-Command 'gcc'
    Cmake     = Test-Command 'cmake'
    Ninja     = Test-Command 'ninja'
    VulkanSdk = if ($env:VULKAN_SDK -and (Test-Path $env:VULKAN_SDK)) { $env:VULKAN_SDK } else { $null }
    Ollama    = Test-Command 'ollama'
}

Write-Section "Summary"

$installedCount = ($results | Where-Object { -not $_.Failed }).Count
$failedCount    = ($results | Where-Object { $_.Failed }).Count

if ($plan.Count -eq 0) {
    Write-Host "  Nothing to install — current state already covers the requested tier." -ForegroundColor Green
} else {
    Write-Host "  Installs attempted : $($plan.Count)" -ForegroundColor White
    Write-Host "  Succeeded          : $installedCount" -ForegroundColor Green
    if ($failedCount -gt 0) {
        Write-Host "  Failed             : $failedCount" -ForegroundColor Red
        foreach ($r in $results | Where-Object { $_.Failed }) {
            Write-Host "    - $($r.Name) : $($r.Error)" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "  Post-install state (this session, before reopening terminal):" -ForegroundColor White
foreach ($k in $finalState.Keys) {
    $v = $finalState[$k]
    if ($v) { Write-Good "$k : $v" } else { Write-Miss $k }
}
if ($plan.Count -gt 0) {
    Write-Host ""
    Write-Host "  Some freshly-installed tools may only appear in a new terminal." -ForegroundColor Yellow
    Write-Host "  Re-run with -DryRun in a fresh session to confirm full coverage." -ForegroundColor Yellow
}

Write-Section "Next steps"

Write-Host "  1. Open a new PowerShell terminal so VULKAN_SDK and any" -ForegroundColor White
Write-Host "     freshly-installed tool become visible." -ForegroundColor White
Write-Host ""
Write-Host "  2. Verify nothing is missing:" -ForegroundColor White
Write-Host "       scripts\commands\setup\bootstrap-dev-env.ps1 -DryRun" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  3. Build + run Deckle:" -ForegroundColor White
Write-Host "       scripts\commands\build-run.ps1" -ForegroundColor DarkGray
Write-Host "     or use the interactive launcher:" -ForegroundColor White
Write-Host "       scripts\deckle.ps1" -ForegroundColor DarkGray

if ($Full) {
    Write-Host ""
    Write-Host "  4. (-Full only) Clone whisper.cpp for native rebuilds:" -ForegroundColor White
    $repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $ScriptDir))
    $whisperRepo = [IO.Path]::GetFullPath((Join-Path $repoRoot '..\whisper.cpp'))
    Write-Host "       git clone https://github.com/ggerganov/whisper.cpp `"$whisperRepo`"" -ForegroundColor DarkGray
    Write-Host "     Then build whisper.cpp with Vulkan before publishing a native bundle." -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  5. (-Full only) Pull an Ollama model for the rewrite feature:" -ForegroundColor White
    Write-Host "       ollama pull llama3.2:3b      # ~2 GB, fast on CPU-only laptops" -ForegroundColor DarkGray
    Write-Host "       ollama pull phi3:mini        # ~2 GB, comparable" -ForegroundColor DarkGray
}
Write-Host ""

$summaryResult = if ($failedCount -gt 0) { 'Partial' } else { 'Success' }
$summarySentence = if ($DryRun) {
    "Development environment bootstrap was probed only; no install was run."
} elseif ($plan.Count -eq 0) {
    "Development environment already covered the requested tier."
} elseif ($failedCount -gt 0) {
    "Development environment bootstrap attempted $($plan.Count) install(s), with $failedCount failure(s)."
} else {
    "Development environment bootstrap completed $installedCount install step(s) for the requested tier."
}

Write-ActionSummary `
    -Workflow $Workflow `
    -Result $summaryResult `
    -Sentence $summarySentence `
    -Details ([ordered]@{
        Tier             = $(if ($Full) { 'Full' } else { 'Default' })
        'Dry run'        = $(if ($DryRun) { 'Yes' } else { 'No' })
        'Planned items'  = $plan.Count
        Succeeded        = $installedCount
        Failed           = $failedCount
        'Runtime assets' = $RuntimeAssetsStatus
    }) `
    -Next @(
        "Open a new PowerShell terminal."
        "Run scripts\commands\setup\bootstrap-dev-env.ps1 -DryRun to verify."
        "Run scripts\deckle.ps1 and choose Build & run."
    )
