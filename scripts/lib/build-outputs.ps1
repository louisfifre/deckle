# Build output knowledge for the clean script: which folders are outputs of
# which language, which languages a repository uses, and the three locks a
# folder must pass before anything deletes it.
#
# A folder name alone never justifies a deletion (`build` or `dist` can hold
# hand-written files), so the knowledge here only nominates candidates; the
# locks, asked to git, decide.

# Markers are tracked file names that prove the language is used. Outputs are
# folder names; `*` is a wildcard on the folder name (egg-info folders carry
# the package name).
$script:BuildOutputLanguages = @(
    [pscustomobject]@{
        Name    = '.NET'
        Markers = @('*.sln', '*.csproj', '*.fsproj', '*.vbproj', 'Directory.Build.props')
        Outputs = @('bin', 'obj', 'artifacts', 'publish', 'TestResults')
    }
    [pscustomobject]@{
        Name    = 'Node'
        Markers = @('package.json')
        Outputs = @('node_modules', 'dist', 'build', '.next', 'coverage')
    }
    [pscustomobject]@{
        Name    = 'Python'
        Markers = @('pyproject.toml', 'setup.py', 'requirements.txt', '*.py')
        Outputs = @('__pycache__', '.pytest_cache', '.mypy_cache', '.ruff_cache', '.venv', 'venv', 'build', 'dist', '*.egg-info')
    }
    [pscustomobject]@{
        Name    = 'Rust'
        Markers = @('Cargo.toml')
        Outputs = @('target')
    }
)

function Get-BuildOutputLanguages {
    return @($script:BuildOutputLanguages)
}

function Format-BuildOutputSize {
    param([Parameter(Mandatory)][int64]$Bytes)

    if ($Bytes -ge 1GB) { return '{0:N1} GB' -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return '{0:N1} MB' -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return '{0:N1} KB' -f ($Bytes / 1KB) }
    return "$Bytes B"
}

function Get-TrackedFiles {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    # -z keeps unusual file names intact; no output means no tracked file.
    $raw = @(& git -C $RepositoryRoot ls-files -z 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw "git could not list the tracked files of '$RepositoryRoot'."
    }
    return @(($raw -join '') -split "`0" | Where-Object { $_ -ne '' })
}

function Get-PresentLanguages {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $leaves = @(Get-TrackedFiles -RepositoryRoot $RepositoryRoot | ForEach-Object {
        $_.Substring($_.LastIndexOf('/') + 1)
    })
    $present = foreach ($language in $script:BuildOutputLanguages) {
        $found = $false
        foreach ($marker in $language.Markers) {
            if (@($leaves | Where-Object { $_ -like $marker }).Count -gt 0) { $found = $true; break }
        }
        if ($found) { $language }
    }
    return @($present)
}

function Get-OutputLanguageNames {
    param(
        [Parameter(Mandatory)][string]$FolderName,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Languages
    )

    return @($Languages | Where-Object {
        foreach ($pattern in $_.Outputs) {
            if ($FolderName -like $pattern) { return $true }
        }
        return $false
    } | ForEach-Object Name)
}

function Get-BuildOutputCandidates {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Languages
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $candidates = [System.Collections.Generic.List[object]]::new()
    if ($Languages.Count -eq 0) { return @() }

    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($root)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        $children = try {
            [IO.DirectoryInfo]::new($directory).GetDirectories()
        } catch {
            # An unreadable folder cannot hold a candidate we could remove anyway.
            @()
        }
        foreach ($child in $children) {
            if ($child.Name -eq '.git') { continue }
            # A link may lead anywhere; neither follow it nor offer it for removal.
            if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }

            $names = @(Get-OutputLanguageNames -FolderName $child.Name -Languages $Languages)
            if ($names.Count -gt 0) {
                # A matched folder is one candidate; its content is not walked.
                $candidates.Add([pscustomobject]@{
                    Path         = $child.FullName
                    RelativePath = [IO.Path]::GetRelativePath($root, $child.FullName).Replace('\', '/')
                    Languages    = $names
                })
            } else {
                $pending.Push($child.FullName)
            }
        }
    }
    return @($candidates | Sort-Object RelativePath)
}

function Test-BuildOutputLocks {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$Path,
        # Passed by callers that test many paths, to ask git for the languages once.
        [object[]]$Languages
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $relative = [IO.Path]::GetRelativePath($root, $fullPath).Replace('\', '/')
    $inside = $relative -ne '.' -and $relative -ne '..' -and -not $relative.StartsWith('../') -and -not [IO.Path]::IsPathRooted($relative)
    if (-not $inside) {
        return [pscustomobject]@{
            KnownOutput = $false; GitIgnored = $false; NoTrackedFile = $false; Passed = $false
            RelativePath = $relative; FailedLock = 'KnownOutput'
        }
    }

    if ($null -eq $Languages) { $Languages = @(Get-PresentLanguages -RepositoryRoot $root) }
    $folderName = Split-Path -Leaf $fullPath
    $knownOutput = @(Get-OutputLanguageNames -FolderName $folderName -Languages $Languages).Count -gt 0

    # --no-index keeps the ignore lock about the rules alone; the tracked-file lock
    # answers the index. check-ignore takes plain paths, ls-files takes pathspecs,
    # so only the latter needs the literal switch.
    & git -C $root check-ignore --no-index -q -- $relative 2>$null
    $gitIgnored = $LASTEXITCODE -eq 0

    $tracked = @(& git --literal-pathspecs -C $root ls-files -- $relative 2>$null)
    if ($LASTEXITCODE -ne 0) {
        # When git cannot answer, the lock does not open.
        $noTrackedFile = $false
    } else {
        $noTrackedFile = $tracked.Count -eq 0
    }

    $failed = if (-not $knownOutput) { 'KnownOutput' }
              elseif (-not $gitIgnored) { 'GitIgnored' }
              elseif (-not $noTrackedFile) { 'NoTrackedFile' }
              else { $null }
    return [pscustomobject]@{
        KnownOutput   = $knownOutput
        GitIgnored    = $gitIgnored
        NoTrackedFile = $noTrackedFile
        Passed        = $null -eq $failed
        RelativePath  = $relative
        FailedLock    = $failed
    }
}

function Get-BuildOutputSize {
    param([Parameter(Mandatory)][string]$Path)

    $bytes = [int64]0
    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($Path)
    while ($pending.Count -gt 0) {
        $directory = [IO.DirectoryInfo]::new($pending.Pop())
        try {
            foreach ($file in $directory.GetFiles()) { $bytes += $file.Length }
            foreach ($child in $directory.GetDirectories()) {
                # Sizes never follow links, so a junction cannot inflate the figure.
                if (-not ($child.Attributes -band [IO.FileAttributes]::ReparsePoint)) { $pending.Push($child.FullName) }
            }
        } catch {
            # A folder that cannot be read counts for nothing.
        }
    }
    return $bytes
}

function Remove-BuildOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$Path
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if (-not $fullPath.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove '$fullPath': it is outside the repository."
    }
    $item = Get-Item -LiteralPath $fullPath -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Refusing to remove '$fullPath': it is a link."
    }
    # Links inside the folder are removed as links, never followed.
    Remove-Item -LiteralPath $fullPath -Recurse -Force
}
