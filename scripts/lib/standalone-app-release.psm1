# The release shape of the standalone apps (Deckle Folders, Deckle Mail),
# shared by publish-app.ps1 and its tests.
#
# A standalone app ships as a private draft GitHub release in the deckle
# repository, under a tag of its own (folders-vX.Y.Z, mail-vX.Y.Z), carrying a
# portable ZIP of the app and its sha256 sidecar, no installer. The tag never
# has the vX.Y.Z form: Deckle's installer and updater read only exact vX.Y.Z
# releases (src/Deckle.Install/ReleaseResolver.cs), so an app release never
# reaches them. The number is Deckle's: both apps read the vX.Y.Z tag of their
# build (ReadVersionFromGit in their csproj).

$ErrorActionPreference = 'Stop'

$script:StandaloneApps = [ordered]@{
    folders = [pscustomobject]@{ Project = 'Deckle.Folders.App'; Assembly = 'DeckleFolders'; Title = 'Deckle Folders' }
    mail    = [pscustomobject]@{ Project = 'Deckle.Mail.App';    Assembly = 'DeckleMail';    Title = 'Deckle Mail' }
}

function Get-StandaloneAppNames {
    return [string[]]@($script:StandaloneApps.Keys)
}

# Everything a release of one app is named after: project, exe, archive, tag.
function Get-StandaloneAppRelease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$App,
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Version
    )

    if (-not $script:StandaloneApps.Contains($App)) {
        throw "-App is deckle, $((Get-StandaloneAppNames) -join ' or '), not '$App'."
    }
    if ($Version -cnotmatch '^\d+\.\d+\.\d+$') {
        throw "Version '$Version' is not MAJOR.MINOR.PATCH"
    }
    $entry = $script:StandaloneApps[$App]
    $zipName = "$($entry.Assembly)-v$Version.zip"
    return [pscustomobject]@{
        App      = $App
        Title    = $entry.Title
        Csproj   = Join-Path $RepoRoot "src\$($entry.Project)\$($entry.Project).csproj"
        ExeName  = "$($entry.Assembly).exe"
        # EnableMsixTooling makes the publish emit <assembly>.pri, which carries
        # the compiled XAML: without it the app starts windowless.
        PriName  = "$($entry.Assembly).pri"
        Version  = $Version
        Tag      = "$App-v$Version"
        ZipName  = $zipName
        ShaName  = "$zipName.sha256"
        DirName  = "$($entry.Assembly)-v$Version"
    }
}

# The dotnet arguments that publish one app as a self-contained folder, passed
# to build-locked.ps1 after its own (build, Release, x64, /m:4). The Publish
# target runs through `dotnet build` so the build takes the machine-wide lock;
# _IsPublishing is what `dotnet publish` would set, and the csproj gates
# SelfContained on it. RuntimeIdentifierOverride rather than a plain RID: the
# WinAppSDK PRI-merge reason sits in publish-app.ps1 beside the Deckle publish.
function Get-StandaloneAppPublishArguments {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$PublishDir)

    $dir = $PublishDir.TrimEnd('\') + '\'
    return [string[]]@(
        '/t:Publish',
        '/p:_IsPublishing=true',
        '/p:RuntimeIdentifierOverride=win-x64',
        '/p:SelfContained=true',
        "/p:PublishDir=$dir",
        '/v:m'
    )
}

# The gh arguments that upload one app as a draft. A draft is visible only to
# the repository's collaborators and GitHub creates its tag only when it is
# published, so this call writes no ref. Making it public is a separate act,
# done on GitHub, never by this script: no --latest, no --draft=false.
function Get-StandaloneAppDraftArguments {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][psobject]$Release,
        [Parameter(Mandatory)][string]$OwnerRepo,
        [Parameter(Mandatory)][string]$HeadSha,
        [Parameter(Mandatory)][string]$ZipPath,
        [Parameter(Mandatory)][string]$ShaPath,
        [Parameter(Mandatory)][string]$NotesPath
    )

    return [string[]]@(
        'release', 'create', $Release.Tag, $ZipPath, $ShaPath,
        '--repo', $OwnerRepo,
        '--title', "$($Release.Title) v$($Release.Version)",
        '--target', $HeadSha,
        '--draft',
        '--notes-file', $NotesPath
    )
}

function Get-StandaloneAppDraftNotes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][psobject]$Release,
        [Parameter(Mandatory)][string]$HeadSha
    )

    return @"
$($Release.Title), private preview built from Deckle v$($Release.Version) (commit $($HeadSha.Substring(0, 12))).

A portable folder, no installer: unzip $($Release.ZipName) anywhere and run $($Release.ExeName). It carries its own .NET and Windows App SDK runtimes. $($Release.ShaName) holds the archive's sha256.
"@
}

Export-ModuleMember -Function Get-StandaloneAppNames, Get-StandaloneAppRelease, Get-StandaloneAppPublishArguments, Get-StandaloneAppDraftArguments, Get-StandaloneAppDraftNotes
