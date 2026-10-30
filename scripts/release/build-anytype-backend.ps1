<#
.SYNOPSIS
    Build the Anytype backend
.DESCRIPTION
    Rebuilds one anytype-cli release tag from source for Windows with the
    tantivy-go cross-allocator free fixed, then packages it in the layout of
    the official asset. The fix applies nothing once upstream carries it.
.NOTES
    section: Release
    order: 130
    action: Build Anytype backend |
#>

# Why Deckle builds its own backend. Since v0.3.7 the official Windows asset
# of anytype-cli links msvcrt.dll: the release workflow moved to Ubuntu's
# mingw, whose default CRT is msvcrt. tantivy-go frees Go-allocated C strings
# through its Rust `string_free`, so a `malloc` from msvcrt's private heap is
# freed on the process heap and the serve dies with 0xC0000374 at account
# open. The fix is upstream as anyproto/tantivy-go#25 and the regression as
# anyproto/anytype-cli#63; neither has an answer. Until a release carries
# them, this script rebuilds the tag with two deliberate deviations:
#
#   - the tantivy-go sites listed in $PatchSites free with `C.free`, which
#     pairs with the `C.CString` malloc whatever CRT the binary links;
#   - the local mingw toolchain links the UCRT, its default, like the
#     official 0.3.6 asset and the desktop helper.
#
# Both deviations stay inert when upstream fixes the bug: the UCRT build is
# correct with or without the patch, and the patch step applies nothing when
# no site matches, so the binary is then the plain tag. A partial match is
# refused outright: the sites moved and someone has to audit them again
# before a binary ships.

[CmdletBinding()]
param(
    # anytype-cli release tag to rebuild, without the leading v.
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.5.0',

    # Output directory, holding the zip, its sha256 sidecar, the build
    # report and the work tree. Defaults to
    # <repo>\artifacts\anytype-backend-v<Version>\.
    [string]$OutDir,

    # Redo the clone, the module copy and the binary instead of reusing
    # what a previous run left under the work tree.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$ScriptDir = $PSScriptRoot
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) 'lib'
. (Join-Path $LibDir 'action-summary.ps1')

$Repo = Split-Path -Parent (Split-Path -Parent $ScriptDir)
$Output = New-WorkflowOutput -Category 'anytype-backend'

# The 16 sites where tantivy-go v1.0.6 frees a `C.CString` result through
# `string_free`. Each entry names a Go file of the module and the variables
# it frees that way; the count is the number of `C.string_free(<var>)` calls
# expected in that file for those variables. Every other `string_free` in
# the module frees a Rust-allocated buffer and is correct as it is.
$PatchSites = @(
    @{ File = 'internal/libinit.go'; Vars = @('cInitVal');                     Count = 1 }
    @{ File = 'document.go';         Vars = @('cFieldValue');                  Count = 2 }
    @{ File = 'fastfield.go';        Vars = @('cQuery');                       Count = 2 }
    @{ File = 'schemabuilder.go';    Vars = @('cName', 'cTokenizer');          Count = 2 }
    @{ File = 'tantivycontext.go';   Vars = @('cPath', 'cQuery', 'cTokenizerName', 'cLang'); Count = 9 }
)
$ExpectedSiteCount = ($PatchSites | Measure-Object -Property Count -Sum).Sum

$Tag = "v$Version"
$AssetName = "anytype-cli-$Tag-windows-amd64.zip"
if (-not $OutDir) { $OutDir = Join-Path $Repo "artifacts\anytype-backend-$Tag" }
$Work = Join-Path $OutDir 'work'
$Clone = Join-Path $Work 'anytype-cli'
$TantivyLibDir = Join-Path $Work 'tantivy-lib'
$PatchedModule = Join-Path $Work 'tantivy-go'
$Exe = Join-Path $Work 'anytype.exe'
$Report = [System.Collections.Generic.List[string]]::new()

function Add-Report {
    param([string]$Line)
    $Report.Add($Line)
}

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$Exe,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory
    )
    $previous = Get-Location
    # stderr is captured as text: a native tool writing progress there is
    # not an error, its exit code is.
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        if ($WorkingDirectory) { Set-Location -LiteralPath $WorkingDirectory }
        $lines = @(& $Exe @Arguments 2>&1 | ForEach-Object { "$_" })
        if ($LASTEXITCODE -ne 0) {
            throw "$Exe $($Arguments -join ' ') exited with $LASTEXITCODE`n$($lines -join "`n")"
        }
        # The comma keeps a one-line output an array, so [0] is the line
        # and not its first character.
        return ,$lines
    } finally {
        Set-Location $previous
    }
}

# ---------------------------------------------------------------------------
Write-WorkflowStep -Output $Output -Message "Toolchain"

# The scoop mingw comes first on PATH so `gcc` is the GCC the recipe was
# proven with, not whichever one another tool put there.
$ScoopRoot = if ($env:SCOOP) { $env:SCOOP } else { Join-Path $env:USERPROFILE 'scoop' }
$ScoopMingw = Join-Path $ScoopRoot 'apps\mingw\current\bin'
if (Test-Path -LiteralPath $ScoopMingw) { $env:PATH = "$ScoopMingw;$env:PATH" }

foreach ($tool in 'go', 'git', 'gcc', 'tar') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool is not on PATH; run setup/bootstrap-dev-env.ps1 (Go, git, scoop mingw)."
    }
}
$ObjDump = Get-Command objdump -ErrorAction SilentlyContinue
$Gh = Get-Command gh -ErrorAction SilentlyContinue

$goVersion = (Invoke-Native go @('version'))[0]
$gccVersion = (Invoke-Native gcc @('--version'))[0]
Write-WorkflowMessage -Output $Output -Message "go     : $goVersion"
Write-WorkflowMessage -Output $Output -Message "gcc    : $gccVersion ($((Get-Command gcc).Source))"
Add-Report "go: $goVersion"
Add-Report "gcc: $gccVersion ($((Get-Command gcc).Source))"

# The build runs at low priority like every other Deckle build; children
# inherit the class.
[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'

# ---------------------------------------------------------------------------
Write-WorkflowStep -Output $Output -Message "Source $Tag"

if ($Force -and (Test-Path -LiteralPath $Work)) {
    Write-WorkflowAction -Output $Output -Message "removing $Work"
    Remove-Item -LiteralPath $Work -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $Work | Out-Null

if (-not (Test-Path -LiteralPath (Join-Path $Clone 'go.mod'))) {
    Write-WorkflowAction -Output $Output -Message "cloning anyproto/anytype-cli at $Tag"
    if (Test-Path -LiteralPath $Clone) { Remove-Item -LiteralPath $Clone -Recurse -Force }
    Invoke-Native git @('clone', '--quiet', '--depth', '1', '--branch', $Tag,
        'https://github.com/anyproto/anytype-cli', $Clone) | Out-Null
} else {
    # A reused clone is reset to the tag so a previous run's replace
    # directive never leaks into a build that should not carry it.
    Invoke-Native git @('checkout', '--quiet', '--', 'go.mod', 'go.sum') -WorkingDirectory $Clone | Out-Null
}
$Commit = (Invoke-Native git @('rev-parse', '--short', 'HEAD') -WorkingDirectory $Clone)[0]
$DescribedTag = (Invoke-Native git @('describe', '--tags', '--exact-match') -WorkingDirectory $Clone)[0]
if ($DescribedTag -ne $Tag) { throw "clone is at $DescribedTag, expected $Tag" }
Write-WorkflowMessage -Output $Output -Message "commit : $Commit ($Tag)"
Add-Report "anytype-cli: $Tag $Commit"

# GOTOOLCHAIN=local keeps the build on the Go installed here. A tag that
# needs a newer Go fails here, by name, instead of downloading one silently.
$env:GOTOOLCHAIN = 'local'
Write-WorkflowAction -Output $Output -Message "go mod download"
Invoke-Native go @('mod', 'download') -WorkingDirectory $Clone | Out-Null

$TantivyVersion = (Invoke-Native go @('list', '-m', '-f', '{{.Version}}', 'github.com/anyproto/tantivy-go') -WorkingDirectory $Clone)[0]
$TantivyCacheDir = (Invoke-Native go @('list', '-m', '-f', '{{.Dir}}', 'github.com/anyproto/tantivy-go') -WorkingDirectory $Clone)[0]
Write-WorkflowMessage -Output $Output -Message "tantivy-go : $TantivyVersion"
Add-Report "tantivy-go: $TantivyVersion"

# ---------------------------------------------------------------------------
Write-WorkflowStep -Output $Output -Message "Tantivy static library $TantivyVersion"

# Same source as the official Makefile's download-tantivy target: the
# prebuilt Rust archive published with the tantivy-go release.
$LibArchive = Join-Path $TantivyLibDir "windows-amd64-$TantivyVersion.tar.gz"
$LibExtracted = Join-Path $TantivyLibDir "x-$TantivyVersion"
$LibFile = Join-Path $LibExtracted 'libtantivy_go.a'
if (-not (Test-Path -LiteralPath $LibFile)) {
    New-Item -ItemType Directory -Force -Path $TantivyLibDir, $LibExtracted | Out-Null
    $url = "https://github.com/anyproto/tantivy-go/releases/download/$TantivyVersion/windows-amd64.tar.gz"
    Write-WorkflowAction -Output $Output -Message "downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $LibArchive
    Invoke-Native tar @('-xzf', $LibArchive, '-C', $LibExtracted) | Out-Null
    if (-not (Test-Path -LiteralPath $LibFile)) { throw "the tantivy archive holds no libtantivy_go.a" }
}
$LibHash = (Get-FileHash -LiteralPath $LibArchive -Algorithm SHA256).Hash.ToLowerInvariant()
Add-Report "tantivy lib archive sha256: $LibHash"

# The release publishes no checksum file; GitHub's per-asset digest is the
# only reference, read through gh when it is here.
$LibVerdict = 'unverified (gh absent)'
if ($Gh) {
    try {
        $assets = ((Invoke-Native gh @('release', 'view', $TantivyVersion, '-R', 'anyproto/tantivy-go', '--json', 'assets')) -join "`n") | ConvertFrom-Json
        $asset = $assets.assets | Where-Object name -eq 'windows-amd64.tar.gz'
        $published = ("$($asset.digest)" -replace '^sha256:', '').ToLowerInvariant()
        if (-not $published) { $LibVerdict = 'unverified (no digest published)' }
        elseif ($published -eq $LibHash) { $LibVerdict = 'matches the published digest' }
        else { throw "tantivy archive digest mismatch: local $LibHash, published $published" }
    } catch {
        if ($_.Exception.Message -like '*digest mismatch*') { throw }
        $LibVerdict = "unverified ($($_.Exception.Message.Split("`n")[0]))"
    }
}
Write-WorkflowMessage -Output $Output -Message "archive : $LibVerdict"
Add-Report "tantivy lib archive: $LibVerdict"

# ---------------------------------------------------------------------------
Write-WorkflowStep -Output $Output -Message "tantivy-go free patch"

# The module cache is read-only and shared with every other Go build on the
# machine, so the patch is applied to a private copy and wired in through a
# replace directive on the clone's go.mod alone.
if ($Force -or -not (Test-Path -LiteralPath (Join-Path $PatchedModule 'go.mod'))) {
    if (Test-Path -LiteralPath $PatchedModule) { Remove-Item -LiteralPath $PatchedModule -Recurse -Force }
    Copy-Item -LiteralPath $TantivyCacheDir -Destination $PatchedModule -Recurse
    Get-ChildItem -LiteralPath $PatchedModule -Recurse -Force | ForEach-Object {
        if ($_.Attributes -band [IO.FileAttributes]::ReadOnly) { $_.Attributes = $_.Attributes -bxor [IO.FileAttributes]::ReadOnly }
    }
} else {
    # A reused copy starts again from the cache so the patch is counted on
    # pristine sources, never on top of itself.
    foreach ($site in $PatchSites) {
        Copy-Item -LiteralPath (Join-Path $TantivyCacheDir $site.File) -Destination (Join-Path $PatchedModule $site.File) -Force
        Set-ItemProperty -LiteralPath (Join-Path $PatchedModule $site.File) -Name IsReadOnly -Value $false
    }
}

$Applied = 0
$SiteLines = @()
foreach ($site in $PatchSites) {
    $path = Join-Path $PatchedModule $site.File
    if (-not (Test-Path -LiteralPath $path)) {
        $SiteLines += "$($site.File): file absent (0/$($site.Count))"
        continue
    }
    $text = [IO.File]::ReadAllText($path)
    $names = ($site.Vars | ForEach-Object { [regex]::Escape($_) }) -join '|'
    $pattern = "C\.string_free\(($names)\)"
    $count = [regex]::Matches($text, $pattern).Count
    $SiteLines += "$($site.File): $count/$($site.Count)"
    if ($count -eq 0) { continue }
    $text = [regex]::Replace($text, $pattern, 'C.free(unsafe.Pointer($1))')
    if ($text -notmatch '"unsafe"') {
        # cgo files open with `import "C"`, which must stay alone on its
        # line; the unsafe import joins the file's own import, whichever
        # form it takes.
        if ($text -match '(?m)^import \(\r?$') {
            $text = [regex]::new('(?m)^import \(\r?\n').Replace($text, "import (`n`t`"unsafe`"`n", 1)
        } else {
            $text = [regex]::new('(?m)^import "(?!C")([^"]+)"\r?$').Replace($text, "import (`n`t`"`$1`"`n`t`"unsafe`"`n)", 1)
        }
        if ($text -notmatch '"unsafe"') { throw "could not add the unsafe import to $($site.File)" }
    }
    [IO.File]::WriteAllText($path, $text)
    $Applied += $count
}
foreach ($line in $SiteLines) { Write-WorkflowMessage -Output $Output -Message $line -Role Muted }
Add-Report "patch sites: $Applied/$ExpectedSiteCount"
$SiteLines | ForEach-Object { Add-Report "  $_" }

$Patched = $false
if ($Applied -eq 0) {
    Write-WorkflowMessage -Output $Output -Message "no site matches: tantivy-go $TantivyVersion no longer needs the patch, building the plain tag" -Role Success
} elseif ($Applied -eq $ExpectedSiteCount) {
    $Patched = $true
    $replacePath = $PatchedModule -replace '\\', '/'
    Invoke-Native go @('mod', 'edit', '-replace', "github.com/anyproto/tantivy-go=$replacePath") -WorkingDirectory $Clone | Out-Null
    Write-WorkflowMessage -Output $Output -Message "$Applied sites patched, go.mod replaces tantivy-go with the copy" -Role Success
    # The diff is kept beside the binary so a reader can see exactly what
    # differs from the tag without rebuilding anything.
    $diffPath = Join-Path $OutDir 'tantivy-go.patch'
    & git diff --no-index --src-prefix=a/tantivy-go/ --dst-prefix=b/tantivy-go/ -- $TantivyCacheDir $PatchedModule 2>$null |
        Out-File -LiteralPath $diffPath -Encoding utf8
} else {
    throw "tantivy-go $TantivyVersion matches $Applied of $ExpectedSiteCount patch sites; the sites moved, audit them before building"
}

# ---------------------------------------------------------------------------
Write-WorkflowStep -Output $Output -Message "Build"

if ($Force -or -not (Test-Path -LiteralPath $Exe) -or $Patched) {
    $env:CGO_ENABLED = '1'
    $env:CC = 'gcc'
    $env:GOOS = 'windows'
    $env:GOARCH = 'amd64'
    # Only the library search path; no -mcrtdll, so the toolchain's default
    # CRT (UCRT) is linked. The official Makefile passes the same -L.
    $env:CGO_LDFLAGS = "-L$($LibExtracted -replace '\\', '/')"
    $buildTime = [DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss')
    $gitState = if ($Patched) { 'dirty' } else { 'clean' }
    $ldflags = "-s -w " +
        "-X 'github.com/anyproto/anytype-cli/core.Version=$Tag' " +
        "-X 'github.com/anyproto/anytype-cli/core.Commit=$Commit' " +
        "-X 'github.com/anyproto/anytype-cli/core.BuildTime=$buildTime deckle' " +
        "-X 'github.com/anyproto/anytype-cli/core.GitState=$gitState'"
    Write-WorkflowAction -Output $Output -Message "go build -p 4 -trimpath -tags noheic (UCRT)"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    if (Test-Path -LiteralPath $Exe) { Remove-Item -LiteralPath $Exe -Force }
    $buildLines = Invoke-Native go @('build', '-p', '4', '-trimpath', '-tags', 'noheic', '-ldflags', $ldflags, '-o', $Exe, '.') -WorkingDirectory $Clone
    foreach ($line in $buildLines) {
        if ("$line".Trim()) { Write-WorkflowMessage -Output $Output -Message "$line" -Role Muted }
    }
    Write-WorkflowMessage -Output $Output -Message ("built in {0:N0} s" -f $sw.Elapsed.TotalSeconds)
    Add-Report "built: $buildTime UTC, $([math]::Round($sw.Elapsed.TotalSeconds)) s"
} else {
    Write-WorkflowMessage -Output $Output -Message "reusing $Exe (use -Force to rebuild)" -Role Muted
}

# ---------------------------------------------------------------------------
Write-WorkflowStep -Output $Output -Message "Verification"

$Problems = @()

# `go version -m` reads the module list baked into the binary: the proof
# that the replace directive reached the link, not only go.mod.
# Invoke-Native hands its lines back as one array object; wrapping it in
# @() again would nest that array, so it is taken as it comes.
$embedded = Invoke-Native go @('version', '-m', $Exe)
$tantivyIndex = -1
for ($i = 0; $i -lt $embedded.Count; $i++) {
    if ("$($embedded[$i])" -match 'github\.com/anyproto/tantivy-go') { $tantivyIndex = $i; break }
}
$heartLine = $embedded | Where-Object { $_ -match 'github\.com/anyproto/anytype-heart' } | Select-Object -First 1
if ($tantivyIndex -lt 0) { throw "go version -m lists no tantivy-go module" }
# A replaced module takes two lines: the dep line, then `=> <path> (devel)`.
$tantivyLine = "$($embedded[$tantivyIndex])".Trim()
$replaceLine = if ($tantivyIndex + 1 -lt $embedded.Count -and "$($embedded[$tantivyIndex + 1])" -match '^\s*=>') { "$($embedded[$tantivyIndex + 1])".Trim() } else { '' }
Add-Report "embedded: $tantivyLine $replaceLine".Trim()
Add-Report "embedded: $("$heartLine".Trim())"
if ($Patched -and -not $replaceLine) { $Problems += 'the binary does not carry the tantivy-go replace' }
if (-not $Patched -and $replaceLine) { $Problems += 'the binary carries a replace it should not' }
Write-WorkflowMessage -Output $Output -Message "$tantivyLine $replaceLine".Trim() -Role Muted

# The import table is the CRT proof: the UCRT shows as api-ms-win-crt-*,
# the msvcrt build as msvcrt.dll. objdump comes with the mingw install.
if ($ObjDump) {
    $objdumpLines = Invoke-Native $ObjDump.Source @('-p', $Exe)
    $imports = @($objdumpLines | ForEach-Object { if ("$_" -match 'DLL Name: (.+)$') { $Matches[1].Trim() } } | Sort-Object -Unique)
    Add-Report "imports: $($imports -join ', ')"
    Write-WorkflowMessage -Output $Output -Message "imports: $($imports -join ', ')" -Role Muted
    if ($imports -contains 'msvcrt.dll') { $Problems += 'the binary links msvcrt.dll; the toolchain is not the UCRT mingw' }
    if (-not ($imports | Where-Object { $_ -like 'api-ms-win-crt-*' })) { $Problems += 'no UCRT import found' }
} else {
    Add-Report "imports: not read (objdump absent)"
    Write-WorkflowMessage -Output $Output -Message "objdump absent: CRT not verified" -Role Warning
}

# ---------------------------------------------------------------------------
Write-WorkflowStep -Output $Output -Message "Package"

# Same layout as the official asset: anytype.exe at the archive root, which
# is where BackendInstallation.InstallFromZipAsync looks for it. The license
# texts ride beside it because this asset is redistributed: the MIT notices
# of anytype-cli and tantivy-go must travel with every copy, and heart's
# Any Source Available License requires its text to reach any third party
# who receives the software. They come from the exact module versions the
# binary links, never from a copy kept in this repository.
$HeartDir = (Invoke-Native go @('list', '-m', '-f', '{{.Dir}}', 'github.com/anyproto/anytype-heart') -WorkingDirectory $Clone)[0]
$Licenses = @(
    @{ Name = 'LICENSE.anytype-cli.md';   Source = Join-Path $Clone 'LICENSE.md' }
    @{ Name = 'LICENSE.anytype-heart.md'; Source = Join-Path $HeartDir 'LICENSE.md' }
    @{ Name = 'LICENSE.tantivy-go.txt';   Source = Join-Path $TantivyCacheDir 'LICENSE' }
)
$Staging = Join-Path $Work 'package'
if (Test-Path -LiteralPath $Staging) { Remove-Item -LiteralPath $Staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Staging | Out-Null
Copy-Item -LiteralPath $Exe -Destination (Join-Path $Staging 'anytype.exe')
foreach ($license in $Licenses) {
    if (-not (Test-Path -LiteralPath $license.Source)) { throw "license text missing: $($license.Source)" }
    Copy-Item -LiteralPath $license.Source -Destination (Join-Path $Staging $license.Name)
    Add-Report "license: $($license.Name) <- $($license.Source)"
}
$ZipPath = Join-Path $OutDir $AssetName
if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
Compress-Archive -Path (Join-Path $Staging '*') -DestinationPath $ZipPath -CompressionLevel Optimal
$ZipHash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$ZipSize = (Get-Item -LiteralPath $ZipPath).Length
"$ZipHash  $AssetName" | Out-File -LiteralPath "$ZipPath.sha256" -Encoding ascii -NoNewline
Add-Report "asset: $AssetName"
Add-Report "asset sha256: $ZipHash"
Add-Report "asset bytes: $ZipSize"
Add-Report "exe bytes: $((Get-Item -LiteralPath $Exe).Length)"
$Report | Out-File -LiteralPath (Join-Path $OutDir 'build-report.txt') -Encoding utf8

$result = if ($Problems.Count -eq 0) { 'Success' } else { 'Failed' }
$sentence = if ($Patched) { "anytype-cli $Tag rebuilt with the tantivy-go free patch." } else { "anytype-cli $Tag rebuilt from the plain tag; the patch no longer applies." }
if ($Problems.Count -gt 0) { $sentence = "anytype-cli $Tag was built but failed verification: $($Problems -join '; ')." }
Write-ActionSummary -Workflow 'Build Anytype backend' -Result $result -Sentence $sentence -Details ([ordered]@{
    'Asset'        = $ZipPath
    'SHA-256'      = $ZipHash
    'Bytes'        = $ZipSize
    'Patch'        = if ($Patched) { "$Applied/$ExpectedSiteCount sites" } else { 'not needed' }
    'tantivy-go'   = $TantivyVersion
    'Report'       = Join-Path $OutDir 'build-report.txt'
}) -Next @(
    'Smoke-test the serve on a fresh DATA_PATH and spare ports before pinning it.'
    'Pin the asset in BackendInstallation.CurrentBundle with this SHA-256 and size.'
)
if ($result -ne 'Success') { exit 1 }
