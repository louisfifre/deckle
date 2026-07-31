# Semantic presentation for Deckle script output.

$script:DeckleOutputColors = @{
    Body     = $null
    Category = [ConsoleColor]::Magenta
    Heading  = [ConsoleColor]::Cyan
    Action   = [ConsoleColor]::DarkYellow
    Muted    = [ConsoleColor]::DarkGray
    Success  = [ConsoleColor]::Green
    Warning  = [ConsoleColor]::Yellow
    Error    = [ConsoleColor]::Red
}

function Get-DeckleOutputColor {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Body', 'Category', 'Heading', 'Action', 'Muted', 'Success', 'Warning', 'Error')]
        [string]$Role
    )

    return $script:DeckleOutputColors[$Role]
}

function New-DeckleOutputSegment {
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

function Write-DeckleOutputFragment {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)]
        [ValidateSet('Body', 'Category', 'Heading', 'Action', 'Muted', 'Success', 'Warning', 'Error')]
        [string]$Role,
        [switch]$NoNewline,
        [string[]]$Tags = @('Deckle.Output'),
        [hashtable]$Metadata = @{}
    )

    $message = [System.Management.Automation.HostInformationMessage]::new()
    $message.Message = $Text
    $message.NoNewLine = [bool]$NoNewline
    $color = Get-DeckleOutputColor -Role $Role
    if ($null -ne $color) { $message.ForegroundColor = $color }
    $message | Add-Member -NotePropertyName DeckleRole -NotePropertyValue $Role
    foreach ($entry in $Metadata.GetEnumerator()) {
        $message | Add-Member -NotePropertyName ([string]$entry.Key) -NotePropertyValue $entry.Value
    }
    Write-Information -MessageData $message -Tags $Tags -InformationAction Continue
}

function Write-DeckleOutputLine {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$Segments
    )

    if ($Segments.Count -eq 0) {
        Write-DeckleOutputFragment -Text '' -Role Body
        return
    }

    for ($index = 0; $index -lt $Segments.Count; $index++) {
        $segment = $Segments[$index]
        Write-DeckleOutputFragment `
            -Text ([string]$segment.Text) `
            -Role ([string]$segment.Role) `
            -NoNewline:($index -lt ($Segments.Count - 1))
    }
}

function Write-DeckleOutputText {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [ValidateSet('Body', 'Category', 'Heading', 'Action', 'Muted', 'Success', 'Warning', 'Error')]
        [string]$Role = 'Body'
    )

    Write-DeckleOutputLine -Segments @(
        New-DeckleOutputSegment -Text $Text -Role $Role
    )
}

function New-DeckleWorkflowOutput {
    param([Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Category)

    return [pscustomobject]@{
        Category = $Category
        Indent   = ' ' * ($Category.Length + 3)
    }
}

function Write-DeckleWorkflowStep {
    param(
        [Parameter(Mandatory)]$Output,
        [Parameter(Mandatory)][string]$Message
    )

    Write-DeckleOutputLine -Segments @()
    Write-DeckleOutputLine -Segments @(
        New-DeckleOutputSegment -Text "[$($Output.Category)] " -Role Category
        New-DeckleOutputSegment -Text $Message -Role Heading
    )
}

function Write-DeckleWorkflowMessage {
    param(
        [Parameter(Mandatory)]$Output,
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('Body', 'Action', 'Muted', 'Success', 'Warning', 'Error')]
        [string]$Role = 'Body'
    )

    Write-DeckleOutputLine -Segments @(
        New-DeckleOutputSegment -Text $Output.Indent -Role Body
        New-DeckleOutputSegment -Text $Message -Role $Role
    )
}

function Write-DeckleWorkflowAction {
    param(
        [Parameter(Mandatory)]$Output,
        [Parameter(Mandatory)][string]$Message
    )

    Write-DeckleWorkflowMessage -Output $Output -Message $Message -Role Action
}

function Write-DeckleWorkflowResult {
    param(
        [Parameter(Mandatory)]$Output,
        [Parameter(Mandatory)][string]$Message
    )

    Write-DeckleWorkflowMessage -Output $Output -Message $Message -Role Success
}
