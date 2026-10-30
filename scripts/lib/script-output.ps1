# Semantic presentation for script output.

$script:OutputRoleColors = @{
    Body     = $null
    Category = [ConsoleColor]::Magenta
    Heading  = [ConsoleColor]::Cyan
    Action   = [ConsoleColor]::DarkYellow
    Muted    = [ConsoleColor]::DarkGray
    Success  = [ConsoleColor]::Green
    Warning  = [ConsoleColor]::Yellow
    Error    = [ConsoleColor]::Red
}

function Get-OutputColor {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Body', 'Category', 'Heading', 'Action', 'Muted', 'Success', 'Warning', 'Error')]
        [string]$Role
    )

    return $script:OutputRoleColors[$Role]
}

function New-OutputSegment {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)]
        [ValidateSet('Body', 'Category', 'Heading', 'Action', 'Muted', 'Success', 'Warning', 'Error')]
        [string]$Role
    )

    return [pscustomobject]@{
        Text = $Text
        Role = $Role
    }
}

function Write-OutputFragment {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)]
        [ValidateSet('Body', 'Category', 'Heading', 'Action', 'Muted', 'Success', 'Warning', 'Error')]
        [string]$Role,
        [switch]$NoNewline,
        [string[]]$Tags = @('DeckleScripts.Output'),
        [hashtable]$Metadata = @{}
    )

    $message = [System.Management.Automation.HostInformationMessage]::new()
    $message.Message = $Text
    $message.NoNewLine = [bool]$NoNewline
    $color = Get-OutputColor -Role $Role
    if ($null -ne $color) { $message.ForegroundColor = $color }
    $message | Add-Member -NotePropertyName OutputRole -NotePropertyValue $Role
    foreach ($entry in $Metadata.GetEnumerator()) {
        $message | Add-Member -NotePropertyName ([string]$entry.Key) -NotePropertyValue $entry.Value
    }
    Write-Information -MessageData $message -Tags $Tags -InformationAction Continue
}

function Write-OutputLine {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$Segments
    )

    if ($Segments.Count -eq 0) {
        Write-OutputFragment -Text '' -Role Body
        return
    }

    for ($index = 0; $index -lt $Segments.Count; $index++) {
        $segment = $Segments[$index]
        Write-OutputFragment `
            -Text ([string]$segment.Text) `
            -Role ([string]$segment.Role) `
            -NoNewline:($index -lt ($Segments.Count - 1))
    }
}

function Write-OutputText {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [ValidateSet('Body', 'Category', 'Heading', 'Action', 'Muted', 'Success', 'Warning', 'Error')]
        [string]$Role = 'Body'
    )

    Write-OutputLine -Segments @(
        New-OutputSegment -Text $Text -Role $Role
    )
}

function New-WorkflowOutput {
    param([Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Category)

    return [pscustomobject]@{
        Category = $Category
        Indent   = ' ' * ($Category.Length + 3)
    }
}

function Write-WorkflowStep {
    param(
        [Parameter(Mandatory)]$Output,
        [Parameter(Mandatory)][string]$Message
    )

    Write-OutputLine -Segments @()
    Write-OutputLine -Segments @(
        New-OutputSegment -Text "[$($Output.Category)] " -Role Category
        New-OutputSegment -Text $Message -Role Heading
    )
}

function Write-WorkflowMessage {
    param(
        [Parameter(Mandatory)]$Output,
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('Body', 'Action', 'Muted', 'Success', 'Warning', 'Error')]
        [string]$Role = 'Body'
    )

    Write-OutputLine -Segments @(
        New-OutputSegment -Text $Output.Indent -Role Body
        New-OutputSegment -Text $Message -Role $Role
    )
}

function Write-WorkflowAction {
    param(
        [Parameter(Mandatory)]$Output,
        [Parameter(Mandatory)][string]$Message
    )

    Write-WorkflowMessage -Output $Output -Message $Message -Role Action
}

function Write-WorkflowResult {
    param(
        [Parameter(Mandatory)]$Output,
        [Parameter(Mandatory)][string]$Message
    )

    Write-WorkflowMessage -Output $Output -Message $Message -Role Success
}
