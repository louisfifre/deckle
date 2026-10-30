<#
.SYNOPSIS
    Update Deckle Scripts
.DESCRIPTION
    Installs or updates the launcher, its library and the chosen packs from a
    source repository at a version tag. The preview shows what would change;
    -Apply copies it. The install never overwrites a file it does not own, and a
    file edited locally since its installation stops the update unless -Force.
.NOTES
    order: 900
    action: Check for updates |
    action: Update Deckle Scripts | -Apply | Replaces the installed launcher, library and packs with the selected version.
#>

#Requires -Version 7.0

[CmdletBinding()]
param(
    [string]$Target,
    [string]$Source,
    [string]$Version,
    [string[]]$Packs,
    [switch]$Apply,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
# In an installed copy this script replaces itself and the library while it
# runs. That is safe: PowerShell has already parsed the files it executes.
$LibDir = Join-Path $PSScriptRoot 'lib'
. (Join-Path $LibDir 'action-summary.ps1')
. (Join-Path $LibDir 'version.ps1')
. (Join-Path $LibDir 'manifest.ps1')

$Workflow = 'Update Deckle Scripts'

trap {
    Write-ActionSummary `
        -Workflow $Workflow `
        -Result Failed `
        -Sentence 'Updating Deckle Scripts failed.' `
        -Details ([ordered]@{
            Error = $_.Exception.Message
        })
    throw
}

function Get-NormalizedPath {
    param([Parameter(Mandatory)][string]$Path)

    return [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Get-OwnRepositoryRoot {
    # Asked to git, not derived from folder names: the installed copy may sit in
    # a worktree or a submodule.
    $top = @(& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or $top.Count -eq 0) {
        throw "This script is not inside a git repository, so the repository to update is unknown. Name it with -Target (and the source with -Source)."
    }
    return Get-NormalizedPath -Path ([string]$top[0])
}

function Test-WorkingTreeSource {
    param([Parameter(Mandatory)][string]$Path)

    # A bare repository has tags but no files: it is read like a remote.
    $bare = @(& git -C $Path rev-parse --is-bare-repository 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "'$Path' is a folder but not a git repository, so it carries no version."
    }
    return ([string]$bare[0]).Trim() -ne 'true'
}

function Get-SourceVersionTags {
    param([Parameter(Mandatory)][string]$Url)

    $lines = @(& git ls-remote --tags $Url 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git could not list the tags of '$Url': $($lines -join ' ')"
    }
    # The anchored pattern drops the peeled entries (refs/tags/vX.Y.Z^{}) that
    # an annotated tag adds beside itself.
    $tags = foreach ($line in $lines) {
        if ([string]$line -match 'refs/tags/(v\d+\.\d+\.\d+)$') {
            [pscustomobject]@{ Tag = $Matches[1]; Version = ConvertTo-SemanticVersion -Text $Matches[1] }
        }
    }
    return @($tags | Sort-Object -Property Version)
}

function Copy-SourceAtTag {
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][string]$Tag,
        [Parameter(Mandatory)][string]$Destination
    )

    $log = @(& git clone --quiet --depth 1 --branch $Tag $Url $Destination 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git could not clone '$Url' at ${Tag}: $($log -join ' ')"
    }
}

function Resolve-SourceCheckout {
    param(
        [Parameter(Mandatory)][string]$Source,
        [string]$Version,
        [Parameter(Mandatory)][string]$Destination
    )

    $requested = $null
    if ($Version) {
        $requested = ConvertTo-SemanticVersion -Text $Version
        if ($null -eq $requested) { throw "'$Version' is not a version. Use X.Y.Z, with or without the leading v." }
    }

    $isLocal = Test-Path -LiteralPath $Source -PathType Container
    if ($isLocal -and (Test-WorkingTreeSource -Path $Source)) {
        if ($null -eq $requested) {
            # No version asked: the working tree is read as it is, so a maintainer
            # tries a change before tagging it.
            return [pscustomobject]@{ Root = $Source; Version = (Get-RepositoryVersion -Path $Source) }
        }
        $tag = "v$requested"
        Copy-SourceAtTag -Url $Source -Tag $tag -Destination $Destination
        return [pscustomobject]@{ Root = $Destination; Version = [string]$requested }
    }

    $tags = @(Get-SourceVersionTags -Url $Source)
    if ($tags.Count -eq 0) { throw "'$Source' has no version tag vX.Y.Z." }
    if ($null -eq $requested) {
        $chosen = $tags[-1]
    } else {
        $chosen = $tags | Where-Object { $_.Version -eq $requested } | Select-Object -First 1
        if ($null -eq $chosen) {
            throw "'$Source' has no tag v$requested. It has: $((@($tags | ForEach-Object Tag)) -join ', ')."
        }
    }
    Copy-SourceAtTag -Url $Source -Tag $chosen.Tag -Destination $Destination
    return [pscustomobject]@{ Root = $Destination; Version = [string]$chosen.Version }
}

function Get-OfferedPacks {
    param([Parameter(Mandatory)][string]$SourceRoot)

    $folders = Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'scripts') -Directory -Force |
        Where-Object { $_.Name -notin @('lib', 'tests') -and -not $_.Name.StartsWith('.') }
    return @(Sort-OrdinalStrings -Value @($folders | ForEach-Object Name))
}

function Get-OwnedFiles {
    param(
        [Parameter(Mandatory)][string]$SourceRoot,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Packs
    )

    $scriptsRoot = Join-Path $SourceRoot 'scripts'
    $paths = [System.Collections.Generic.List[string]]::new()
    foreach ($name in @('menu.ps1', 'update.ps1', 'check-scripts.ps1')) {
        $path = Join-Path $scriptsRoot $name
        if (Test-Path -LiteralPath $path -PathType Leaf) { $paths.Add($path) }
    }
    $folders = @(Join-Path $scriptsRoot 'lib') + @($Packs | ForEach-Object { Join-Path $scriptsRoot $_ })
    foreach ($folder in $folders) {
        if (-not (Test-Path -LiteralPath $folder -PathType Container)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -File -Force) { $paths.Add($file.FullName) }
    }

    $files = [ordered]@{}
    foreach ($path in $paths) {
        $relative = [IO.Path]::GetRelativePath($SourceRoot, $path).Replace('\', '/')
        $files[$relative] = $path
    }
    return $files
}

function Get-TargetFilePath {
    param(
        [Parameter(Mandatory)][string]$TargetRoot,
        [Parameter(Mandatory)][string]$Relative
    )

    return Join-Path $TargetRoot $Relative.Replace('/', [IO.Path]::DirectorySeparatorChar)
}

function New-UpdatePlan {
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary]$SourceFiles,
        [Parameter(Mandatory)][string]$TargetRoot,
        [Parameter(Mandatory)][System.Collections.IDictionary]$InstalledFiles
    )

    $plan = [System.Collections.Generic.List[object]]::new()
    function Add-Entry([string]$Path, [string]$State, [string]$SourcePath, [string]$Fingerprint, [bool]$Removal) {
        $plan.Add([pscustomobject]@{
            Path = $Path; State = $State; Source = $SourcePath; Fingerprint = $Fingerprint; Removal = $Removal
        })
    }

    foreach ($relative in (Sort-OrdinalStrings -Value @($SourceFiles.Keys))) {
        $sourcePath = $SourceFiles[$relative]
        $sourcePrint = Get-ContentFingerprint -Path $sourcePath
        $targetPath = Get-TargetFilePath -TargetRoot $TargetRoot -Relative $relative
        if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
            Add-Entry $relative 'Add' $sourcePath $sourcePrint $false
            continue
        }
        $targetPrint = Get-ContentFingerprint -Path $targetPath
        if ($targetPrint -eq $sourcePrint) {
            Add-Entry $relative 'Keep' $sourcePath $sourcePrint $false
        } elseif (-not $InstalledFiles.Contains($relative)) {
            Add-Entry $relative 'Conflict' $sourcePath $sourcePrint $false
        } elseif ($targetPrint -eq $InstalledFiles[$relative]) {
            Add-Entry $relative 'Update' $sourcePath $sourcePrint $false
        } else {
            Add-Entry $relative 'Modified' $sourcePath $sourcePrint $false
        }
    }

    # What the manifest owns but the selected source no longer offers: a file
    # removed upstream, or one of a pack that was deselected.
    foreach ($relative in (Sort-OrdinalStrings -Value @($InstalledFiles.Keys))) {
        if ($SourceFiles.Contains($relative)) { continue }
        $targetPath = Get-TargetFilePath -TargetRoot $TargetRoot -Relative $relative
        if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) { continue }
        $state = if ((Get-ContentFingerprint -Path $targetPath) -eq $InstalledFiles[$relative]) { 'Remove' } else { 'Modified' }
        Add-Entry $relative $state '' '' $true
    }
    return @($plan)
}

function Write-UpdatePlan {
    param(
        [Parameter(Mandatory)]$Output,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Plan
    )

    $roles = @{ Add = 'Success'; Update = 'Action'; Remove = 'Warning'; Modified = 'Warning'; Conflict = 'Error'; Keep = 'Muted' }
    foreach ($state in @('Add', 'Update', 'Remove', 'Modified', 'Conflict', 'Keep')) {
        $entries = @($Plan | Where-Object State -eq $state)
        if ($entries.Count -eq 0) { continue }
        Write-WorkflowMessage -Output $Output -Message "$state ($($entries.Count))" -Role $roles[$state]
        if ($state -eq 'Keep') { continue }
        foreach ($entry in $entries) {
            Write-WorkflowMessage -Output $Output -Message "  $($entry.Path)" -Role $roles[$state]
        }
    }
}

function Remove-EmptyParentFolders {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string]$StopAt
    )

    # A deselected pack leaves its folder empty; the scripts folder itself stays.
    $folder = Split-Path -Parent $FilePath
    while ($folder -and (Get-NormalizedPath -Path $folder) -ne (Get-NormalizedPath -Path $StopAt) -and
        (Test-Path -LiteralPath $folder -PathType Container) -and
        @(Get-ChildItem -LiteralPath $folder -Force).Count -eq 0) {
        Remove-Item -LiteralPath $folder -Force
        $folder = Split-Path -Parent $folder
    }
}

function Join-Names {
    param([string[]]$Value)
    return ($Value -join ', ')
}

# --- Resolve the target, the source and the packs ---------------------------

$output = New-WorkflowOutput -Category 'update'

if ($Target) {
    if (-not (Test-Path -LiteralPath $Target -PathType Container)) { throw "The target '$Target' is not a folder." }
    $TargetRoot = Get-NormalizedPath -Path (Resolve-Path -LiteralPath $Target).Path
    $null = & git -C $TargetRoot rev-parse --is-inside-work-tree 2>&1
    if ($LASTEXITCODE -ne 0) { throw "The target '$TargetRoot' is not inside a git repository." }
} else {
    $TargetRoot = Get-OwnRepositoryRoot
}

$manifest = Read-InstallManifest -RepositoryRoot $TargetRoot
$installedFiles = Get-InstallManifestFiles -Manifest $manifest

if (-not $Source) {
    $Source = if ($manifest -and $manifest.source) { [string]$manifest.source } else { Get-OwnRepositoryRoot }
}
# A path is recorded absolute: a relative one would mean something else from
# the next working directory.
if (Test-Path -LiteralPath $Source -PathType Container) {
    $Source = Get-NormalizedPath -Path (Resolve-Path -LiteralPath $Source).Path
    if ($Source -ieq $TargetRoot) { throw 'The source and the target are the same repository. Improvements are made in the source and reach other repositories by update.' }
}

$TempRoot = Join-Path ([IO.Path]::GetTempPath()) ("deckle-scripts-update-" + [guid]::NewGuid())
try {
    Write-WorkflowStep -Output $output -Message 'Resolve the source'
    $checkout = Resolve-SourceCheckout -Source $Source -Version $Version -Destination $TempRoot
    $offered = @(Get-OfferedPacks -SourceRoot $checkout.Root)

    # -File passes a comma list as one string; both spellings are accepted.
    $requestedPacks = @($Packs | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($requestedPacks.Count -gt 0) {
        $unknown = @($requestedPacks | Where-Object { $_ -notin $offered })
        if ($unknown.Count -gt 0) {
            throw "Unknown pack(s): $(Join-Names $unknown). The source offers: $(Join-Names $offered)."
        }
        $selectedPacks = $requestedPacks
    } elseif ($manifest) {
        $selectedPacks = @($manifest.packs | Where-Object { $_ -in $offered })
        $dropped = @($manifest.packs | Where-Object { $_ -notin $offered })
        if ($dropped.Count -gt 0) {
            Write-WorkflowMessage -Output $output -Role Warning -Message "The source no longer offers: $(Join-Names $dropped). Their files will be removed."
        }
    } else {
        $selectedPacks = $offered
    }
    $selectedPacks = @(Sort-OrdinalStrings -Value @($selectedPacks | Select-Object -Unique))

    $sourceFiles = Get-OwnedFiles -SourceRoot $checkout.Root -Packs $selectedPacks
    foreach ($required in @('scripts/menu.ps1', 'scripts/update.ps1')) {
        if (-not $sourceFiles.Contains($required)) { throw "The source '$Source' has no $required, so it is not a Deckle Scripts source." }
    }
    $installedVersion = if ($manifest) { [string]$manifest.version } else { 'not installed' }

    Write-WorkflowMessage -Output $output -Message "Source: $Source"
    Write-WorkflowMessage -Output $output -Message "Source version: $($checkout.Version)"
    Write-WorkflowMessage -Output $output -Message "Installed version: $installedVersion"
    Write-WorkflowMessage -Output $output -Message "Packs: $(Join-Names $selectedPacks)"

    # --- Plan ---------------------------------------------------------------

    Write-WorkflowStep -Output $output -Message 'Plan'
    $plan = @(New-UpdatePlan -SourceFiles $sourceFiles -TargetRoot $TargetRoot -InstalledFiles $installedFiles)
    Write-UpdatePlan -Output $output -Plan $plan
    $count = { param([string]$State) @($plan | Where-Object State -eq $State).Count }
    $conflicts = @($plan | Where-Object State -eq 'Conflict')
    $modified = @($plan | Where-Object State -eq 'Modified')
    $blocked = $conflicts.Count -gt 0 -or ($modified.Count -gt 0 -and -not $Force)

    # What the manifest holds once the plan is applied.
    $newFiles = [ordered]@{}
    foreach ($entry in ($plan | Where-Object { -not $_.Removal })) { $newFiles[$entry.Path] = $entry.Fingerprint }
    $filesUnchanged = @($plan | Where-Object { $_.State -ne 'Keep' }).Count -eq 0
    $manifestUnchanged = $manifest -and
        [string]$manifest.version -eq $checkout.Version -and
        [string]$manifest.source -eq $Source -and
        (Join-Names @($manifest.packs)) -eq (Join-Names $selectedPacks) -and
        ($installedFiles.Count -eq $newFiles.Count) -and
        -not @($newFiles.Keys | Where-Object { -not $installedFiles.Contains($_) -or $installedFiles[$_] -ne $newFiles[$_] })
    $upToDate = $filesUnchanged -and $manifestUnchanged

    $added = & $count 'Add'
    $updated = (& $count 'Update') + $(if ($Force) { @($modified | Where-Object { -not $_.Removal }).Count } else { 0 })
    $removed = (& $count 'Remove') + $(if ($Force) { @($modified | Where-Object Removal).Count } else { 0 })
    $details = [ordered]@{
        Target  = $TargetRoot
        Source  = $Source
        Version = if ($installedVersion -eq $checkout.Version) { $checkout.Version } else { "$installedVersion -> $($checkout.Version)" }
        Packs   = $selectedPacks
        Added   = [string]$added
        Updated = [string]$updated
        Removed = [string]$removed
        Kept    = [string](& $count 'Keep')
    }

    if ($upToDate) {
        Write-ActionSummary -Workflow $Workflow -Result Skipped -Sentence "Deckle Scripts is already up to date at version $($checkout.Version)." -Details $details
        return
    }

    # --- Preview ends here --------------------------------------------------

    if (-not $Apply) {
        if ($blocked) {
            Write-ActionSummary -Workflow $Workflow -Result Partial `
                -Sentence 'Preview: the update would stop on files it must not overwrite. Nothing was changed.' `
                -Details $details `
                -Next @(
                    'A file it does not own is never overwritten: move or delete it.',
                    'A file edited locally is overwritten only with -Force; improvements belong in the source repository.'
                )
        } else {
            Write-ActionSummary -Workflow $Workflow -Result Success `
                -Sentence "Preview: $added to add, $updated to update, $removed to remove. Nothing was changed." `
                -Details $details `
                -Next @('Run again with -Apply to perform it.')
        }
        return
    }

    # --- Apply --------------------------------------------------------------

    if ($blocked) {
        $reasons = @()
        if ($conflicts.Count -gt 0) {
            $reasons += "$($conflicts.Count) path(s) already exist and are not owned by Deckle Scripts, so the install never overwrites them: $(Join-Names @($conflicts.Path)). Move or delete them, then run again."
        }
        if ($modified.Count -gt 0 -and -not $Force) {
            $reasons += "$($modified.Count) file(s) were edited locally since their installation: $(Join-Names @($modified.Path)). Make the improvement in the source repository, or run again with -Force to overwrite them."
        }
        throw "Nothing was changed. $($reasons -join ' ')"
    }

    Write-WorkflowStep -Output $output -Message 'Apply'
    $scriptsFolder = Join-Path $TargetRoot 'scripts'
    foreach ($entry in $plan) {
        $writes = $entry.State -in @('Add', 'Update', 'Remove') -or ($entry.State -eq 'Modified' -and $Force)
        if (-not $writes) { continue }
        $destination = Get-TargetFilePath -TargetRoot $TargetRoot -Relative $entry.Path
        if ($entry.Removal) {
            Remove-Item -LiteralPath $destination -Force
            Remove-EmptyParentFolders -FilePath $destination -StopAt $scriptsFolder
        } else {
            $null = New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
            Copy-Item -LiteralPath $entry.Source -Destination $destination -Force
        }
    }

    Write-InstallManifest -RepositoryRoot $TargetRoot -Manifest ([pscustomobject]@{
        source      = $Source
        version     = $checkout.Version
        packs       = $selectedPacks
        installedAt = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        files       = $newFiles
    })
    Write-WorkflowResult -Output $output -Message "Applied: $added added, $updated updated, $removed removed, manifest written"

    $sentence = if ($manifest) {
        "Updated Deckle Scripts to version $($checkout.Version)."
    } else {
        "Installed Deckle Scripts $($checkout.Version) into $TargetRoot."
    }
    Write-ActionSummary -Workflow $Workflow -Result Success -Sentence $sentence -Details $details
} finally {
    if (Test-Path -LiteralPath $TempRoot) { Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
