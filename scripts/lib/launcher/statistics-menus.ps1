# Interactive goal, custom specification, worktree, and review flow for statistics.

function Select-MaintenanceOption {
    param(
        [Parameter(Mandatory)][string]$Header,
        [Parameter(Mandatory)][object[]]$Items
    )

    try {
        return Select-Action -Header $Header -Items $Items -ClearScreen -BannerStyle Compact
    } catch {
        if ($_.Exception.Message -eq 'Cancelled') { return $null }
        throw
    }
}

function Select-MaintenanceScanGoal {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Repository', 'Context')]
        [string]$Kind
    )

    $descriptions = if ($Kind -eq 'Repository') {
        @(
            'Overview totals tracked files; Files to review applies size and line limits.'
            'Source metrics reads C#, XAML, and RESW; Custom lets you define the scope.'
        )
    } else {
        @(
            'Footprint measures tracked Markdown by loading mode and document type.'
            'Recent changes reports Git activity; Custom lets you define the scope.'
        )
    }
    return Show-Submenu `
        -Header "Deckle > Maintenance > $Kind" `
        -Rows @(@{ Prefix = 'Goal'; Items = @(Get-MaintenanceScanGoals -Kind $Kind) }) `
        -BannerStyle Compact `
        -ResultTitle 'What this controls' `
        -ResultLines $descriptions `
        -Interaction Select
}

function Set-CustomRepositoryFileSet {
    param([Parameter(Mandatory)]$Specification)

    $choice = Select-MaintenanceOption `
        -Header 'Deckle > Maintenance > Custom > Files' `
        -Items @(
            [pscustomobject]@{ Label = 'All tracked files';       Value = 'All' }
            [pscustomobject]@{ Label = 'Supported text files';   Value = 'Text' }
            [pscustomobject]@{ Label = 'C#, XAML, and RESW';     Value = 'Source' }
            [pscustomobject]@{ Label = 'Documentation files';   Value = 'Documentation' }
        )
    if ($null -ne $choice) { $Specification.FileSet = $choice }
}

function Set-CustomContextDocuments {
    param([Parameter(Mandatory)]$Specification)

    $choice = Select-MaintenanceOption `
        -Header 'Deckle > Maintenance > Custom > Documents' `
        -Items @(
            [pscustomobject]@{ Label = 'All tracked Markdown';         Value = 'all' }
            [pscustomobject]@{ Label = 'Automatic instructions';      Value = 'automatic' }
            [pscustomobject]@{ Label = 'On-demand references';        Value = 'on-demand' }
            [pscustomobject]@{ Label = 'AGENTS and CLAUDE';           Value = 'instructions' }
            [pscustomobject]@{ Label = 'Skills and skill references'; Value = 'skills' }
            [pscustomobject]@{ Label = 'README, context, and journal'; Value = 'project-context' }
            [pscustomobject]@{ Label = 'Decisions and research';      Value = 'decisions' }
        )
    if ($null -eq $choice) { return }
    $Specification.LoadingModes = @()
    $Specification.DocumentTypes = @()
    switch ($choice) {
        'automatic'       { $Specification.LoadingModes = @('Automatic instructions') }
        'on-demand'       { $Specification.LoadingModes = @('On-demand references') }
        'instructions'    { $Specification.DocumentTypes = @('AGENTS', 'CLAUDE') }
        'skills'          { $Specification.DocumentTypes = @('Skill', 'Skill reference') }
        'project-context' { $Specification.DocumentTypes = @('README', 'Context', 'Journal') }
        'decisions'       { $Specification.DocumentTypes = @('ADR', 'Research') }
    }
}

function Set-CustomScanScope {
    param([Parameter(Mandatory)]$Specification)

    $choice = Select-MaintenanceOption `
        -Header 'Deckle > Maintenance > Custom > Scope' `
        -Items @(
            [pscustomobject]@{ Label = 'Whole tracked repository'; Value = '' }
            [pscustomobject]@{ Label = 'src/';                     Value = 'src' }
            [pscustomobject]@{ Label = 'tests/';                   Value = 'tests' }
            [pscustomobject]@{ Label = 'scripts/';                 Value = 'scripts' }
            [pscustomobject]@{ Label = 'docs/';                    Value = 'docs' }
            [pscustomobject]@{ Label = 'One relative path…';       Value = '__path__'; Role = 'folder' }
        )
    if ($null -eq $choice) { return }
    if ($choice -eq '__path__') {
        $pathInput = Read-MenuText `
            -Header 'Deckle > Maintenance > Custom > Scope' `
            -Title 'One relative path' `
            -Lines @('Relative to the worktree; files and folders are accepted.', 'Absolute paths, .., .git, links, and junctions are rejected.') `
            -Label 'Path' `
            -Default $Specification.ScopePath
        if ($pathInput.Status -eq 'Submitted' -and -not [string]::IsNullOrWhiteSpace($pathInput.Value)) {
            $Specification.ScopePath = [string]$pathInput.Value
        }
        return
    }
    $Specification.ScopePath = $choice
}

function Set-CustomScanMeasures {
    param([Parameter(Mandatory)]$Specification)

    if ($Specification.Kind -eq 'Repository') {
        $choice = Select-MaintenanceOption `
            -Header 'Deckle > Maintenance > Custom > Measures' `
            -Items @(
                [pscustomobject]@{ Label = 'File count and size';       Value = 'totals' }
                [pscustomobject]@{ Label = 'Text lines and size';       Value = 'lines' }
                [pscustomobject]@{ Label = 'Source LOC and resources'; Value = 'source' }
                [pscustomobject]@{ Label = 'Files crossing limits';    Value = 'review' }
            )
        if ($null -eq $choice) { return }
        switch ($choice) {
            'totals' { $Specification.Measurements = @('Files', 'Bytes'); $Specification.Detail = 'Summary' }
            'lines'  { $Specification.Measurements = @('Files', 'Bytes', 'Lines'); $Specification.Detail = 'Summary' }
            'source' { $Specification.Measurements = @('Files', 'Bytes', 'Lines', 'SourceLines', 'ReswKeys'); $Specification.Detail = 'Summary' }
            'review' {
                $Specification.Measurements = @('Files', 'Bytes', 'Lines', 'SourceLines')
                $Specification.Detail = 'Findings'
                if ($Specification.ThresholdProfile -eq 'Off') { $Specification.ThresholdProfile = 'Standard' }
            }
        }
        return
    }

    $choice = Select-MaintenanceOption `
        -Header 'Deckle > Maintenance > Custom > Measures' `
        -Items @(
            [pscustomobject]@{ Label = 'Footprint';                  Value = 'footprint' }
            [pscustomobject]@{ Label = 'Recent Git activity';        Value = 'activity' }
            [pscustomobject]@{ Label = 'Documents crossing limits'; Value = 'review' }
        )
    if ($null -eq $choice) { return }
    switch ($choice) {
        'footprint' {
            $Specification.Measurements = @('Files', 'Bytes', 'Lines', 'Sections', 'EstimatedTokens')
            $Specification.ActivityDays = 0
            $Specification.Detail = 'Summary'
        }
        'activity' {
            $Specification.Measurements = @('Files', 'Bytes', 'Lines', 'EstimatedTokens', 'GitActivity')
            if ($Specification.ActivityDays -le 0) { $Specification.ActivityDays = 30 }
            $Specification.Detail = 'Findings'
        }
        'review' {
            $Specification.Measurements = @('Files', 'Bytes', 'Lines', 'Sections', 'EstimatedTokens')
            $Specification.ActivityDays = 0
            $Specification.Detail = 'Findings'
            if ($Specification.ThresholdProfile -eq 'Off') { $Specification.ThresholdProfile = 'Standard' }
        }
    }
}

function Set-CustomScanGrouping {
    param([Parameter(Mandatory)]$Specification)

    $items = if ($Specification.Kind -eq 'Repository') {
        @(
            [pscustomobject]@{ Label = 'File extension';   Value = 'Extension' }
            [pscustomobject]@{ Label = 'Top-level folder'; Value = 'TopFolder' }
            [pscustomobject]@{ Label = 'No grouping';      Value = 'None' }
        )
    } else {
        @(
            [pscustomobject]@{ Label = 'Loading mode';  Value = 'LoadingMode' }
            [pscustomobject]@{ Label = 'Document type'; Value = 'DocumentType' }
            [pscustomobject]@{ Label = 'No grouping';   Value = 'None' }
        )
    }
    $choice = Select-MaintenanceOption -Header 'Deckle > Maintenance > Custom > Grouping' -Items $items
    if ($null -ne $choice) { $Specification.GroupBy = $choice }
}

function Set-CustomScanThresholds {
    param([Parameter(Mandatory)]$Specification)

    $standard = Copy-MaintenanceScanSpecification -Specification $Specification
    $standard.ThresholdProfile = 'Standard'
    $sensitive = Copy-MaintenanceScanSpecification -Specification $Specification
    $sensitive.ThresholdProfile = 'Sensitive'
    $choice = Select-MaintenanceOption `
        -Header 'Deckle > Maintenance > Custom > Thresholds' `
        -Items @(
            [pscustomobject]@{ Label = 'Off';              Value = 'Off' }
            [pscustomobject]@{ Label = Get-MaintenanceThresholdLabel -Specification $standard;  Value = 'Standard' }
            [pscustomobject]@{ Label = Get-MaintenanceThresholdLabel -Specification $sensitive; Value = 'Sensitive' }
        )
    if ($null -ne $choice) {
        $Specification.ThresholdProfile = $choice
        if ($choice -ne 'Off') {
            $Specification.Detail = 'Findings'
        } elseif ($Specification.Measurements -notcontains 'GitActivity') {
            $Specification.Detail = 'Summary'
        }
    }
}

function Set-CustomContextPeriod {
    param([Parameter(Mandatory)]$Specification)

    $choice = Select-MaintenanceOption `
        -Header 'Deckle > Maintenance > Custom > Period' `
        -Items @(
            [pscustomobject]@{ Label = 'Last 7 days';  Value = 7 }
            [pscustomobject]@{ Label = 'Last 30 days'; Value = 30 }
            [pscustomobject]@{ Label = 'Last 90 days'; Value = 90 }
        )
    if ($null -ne $choice) { $Specification.ActivityDays = [int]$choice }
}

function Show-CustomScanEditor {
    param(
        [Parameter(Mandatory)]$Specification,
        [string[]]$ResultLines = @('Choose only what the result needs; the scan has not started.', 'Esc returns and discards this configuration.')
    )

    $selection = @{ Index = 1; PreferredColumn = 0 }
    while ($true) {
        $rows = @(
            @{ Prefix = 'Scope'; Items = @(
                @{ Label = Get-MaintenanceScopeLabel -ScopePath $Specification.ScopePath; Value = 'scope'; Role = 'folder' }
            ) }
            @{ Prefix = $(if ($Specification.Kind -eq 'Context') { 'Documents' } else { 'Files' }); Items = @(
                @{ Label = Get-MaintenanceFileSetLabel -Specification $Specification; Value = 'files'; Role = 'folder' }
            ) }
            @{ Prefix = 'Measures'; Items = @(
                @{ Label = @($Specification.Measurements) -join ', '; Value = 'measures'; Role = 'folder' }
            ) }
            @{ Prefix = 'Grouping'; Items = @(
                @{ Label = Get-MaintenanceGroupingLabel -GroupBy $Specification.GroupBy; Value = 'grouping'; Role = 'folder' }
            ) }
            @{ Prefix = 'Thresholds'; Items = @(
                @{ Label = Get-MaintenanceThresholdLabel -Specification $Specification; Value = 'thresholds'; Role = 'folder' }
            ) }
        )
        if ($Specification.Kind -eq 'Context' -and $Specification.Measurements -contains 'GitActivity') {
            $rows += @{ Prefix = 'Period'; Items = @(
                @{ Label = "Last $($Specification.ActivityDays) days"; Value = 'period'; Role = 'folder' }
            ) }
        }
        $rows += @{ Prefix = 'Next'; Items = @(
            @{ Label = 'Choose worktree…'; Value = 'continue'; Role = 'folder' }
        ) }

        $choice = Show-Submenu `
            -Header "Deckle > Maintenance > $($Specification.Kind) > Custom" `
            -Rows $rows `
            -BannerStyle Compact `
            -ResultTitle 'Scan definition' `
            -ResultLines $ResultLines `
            -Interaction Select `
            -SelectionState $selection
        if ($null -eq $choice) { return $null }
        switch ($choice) {
            'scope'      { Set-CustomScanScope -Specification $Specification }
            'files'      {
                if ($Specification.Kind -eq 'Repository') { Set-CustomRepositoryFileSet -Specification $Specification }
                else { Set-CustomContextDocuments -Specification $Specification }
            }
            'measures'   { Set-CustomScanMeasures -Specification $Specification }
            'grouping'   { Set-CustomScanGrouping -Specification $Specification }
            'thresholds' { Set-CustomScanThresholds -Specification $Specification }
            'period'     { Set-CustomContextPeriod -Specification $Specification }
            'continue'   { return $Specification }
        }
        $ResultLines = @('Choose only what the result needs; the scan has not started.', 'Esc returns and discards this configuration.')
    }
}

function Show-MaintenanceScanReview {
    param(
        [Parameter(Mandatory)]$Specification,
        [Parameter(Mandatory)][string]$Worktree
    )

    return Show-Submenu `
        -Header "Deckle > Maintenance > $($Specification.Kind) > Review" `
        -Rows @(@{ Prefix = 'Scan'; Items = @(
            @{ Label = 'Edit scan…'; Value = 'edit'; Role = 'folder' }
            @{ Label = 'Run scan';   Value = 'run' }
        ) }) `
        -BannerStyle Compact `
        -ResultTitle 'Review scan' `
        -ResultLines @(Get-MaintenanceScanReviewLines -Specification $Specification -Worktree $Worktree) `
        -Interaction Select
}

function Invoke-MaintenanceScanFlow {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Repository', 'Context')]
        [string]$Kind
    )

    $goal = Select-MaintenanceScanGoal -Kind $Kind
    if ($null -eq $goal) { return $null }
    $specification = New-MaintenanceScanSpecification -Kind $Kind -Goal $goal
    if ($goal -eq 'custom') {
        $specification = Show-CustomScanEditor -Specification $specification
        if ($null -eq $specification) { return $null }
    }

    try {
        $worktree = Get-WorktreeOrReturn
    } catch {
        return [pscustomobject]@{
            Succeeded = $false
            Title = "$Kind scan failed"
            Lines = @(Get-MaintenanceFailureLines -ErrorRecord $_)
        }
    }
    if ($null -eq $worktree) { return $null }

    while ($true) {
        try {
            $specification = Resolve-MaintenanceScanSpecification -Specification $specification -Worktree $worktree
        } catch {
            $message = $_.Exception.Message
            $editable = Copy-MaintenanceScanSpecification -Specification $specification
            $specification = Show-CustomScanEditor -Specification $editable -ResultLines @(
                'The selected scope cannot be used.'
                $message
            )
            if ($null -eq $specification) { return $null }
            continue
        }

        $review = Show-MaintenanceScanReview -Specification $specification -Worktree $worktree
        if ($null -eq $review) { return $null }
        if ($review -eq 'edit') {
            $editable = Copy-MaintenanceScanSpecification -Specification $specification
            $specification = Show-CustomScanEditor -Specification $editable
            if ($null -eq $specification) { return $null }
            continue
        }

        Show-MenuStatus `
            -Header "Deckle > Maintenance > $Kind" `
            -Title $specification.GoalLabel `
            -Lines @(
                "Scanning $(Get-MaintenanceScopeLabel -ScopePath $specification.ScopePath)…"
                'Tracked files only; links and junctions are not traversed.'
            )
        return Invoke-MaintenanceTargetedScan -Specification $specification -Worktree $worktree -LibDir $LibDir
    }
}
