<#
.SYNOPSIS
    Apply a schema manifest or a space plan to an Anytype space, or read a space into a plan
.DESCRIPTION
    With -Plan, builds a whole space from one plan file, in creation order:
    properties and options, types with their panel and default template,
    collections, queries, templates with their in-body views, type views, pages
    and, when asked for, objects and the sidebar. It is idempotent and -Check
    only reads. A plan is checked against itself first (a view on a property
    nobody declared is refused before any write). Ten minutes after a write the
    script checks again (-RecheckAfterMinutes): a sort or filter written without
    an id reads right at once and collapses when the object is loaded again.
    -Stages names what a run covers: schema, structure (the default pair),
    objects, widgets. The sidebar (widgets) is the space owner's: run that stage
    with the owner's key, not the bot's.

    With -ReadTo, reads the space (-Space) into a plan file, the reverse
    command: -Pages names the pages to read with their body. Objects are never
    read. The file holds the structure of a real space: keep it out of the
    repository.

    Without -Plan, brings an Anytype space to a schema manifest through the running Deckle's
    schema-admin surface, with no model in the loop: it previews, shows what
    would change, waits for a typed yes, applies, then previews again and
    succeeds only when nothing is left to do.

    With -InviteLink it first makes Deckle's bot ask to join the space, waits
    until a person has accepted it in the Anytype app, and points the alias
    at that space in spaces.json; -Pilot also makes it the space the piloting
    commands aim at, in credentials.json. Both files are copied beside
    themselves before they change.

    Run it from a terminal: it asks a question, so the menu does not offer it.
    A person still creates the space and accepts the bot, since the bot cannot
    invite. Exit codes: 0 the space matches the manifest, 1 a call failed,
    2 the preview holds a conflict or the answer was not yes, 3 actions remain
    after the apply.
.EXAMPLE
    pwsh -NoProfile -File scripts/maintenance/apply-anytype-schema.ps1 -Space home -ReadTo D:\anytype\space-builder\plans\home.plan.json -Pages 'Accueil'
.EXAMPLE
    pwsh -NoProfile -File scripts/maintenance/apply-anytype-schema.ps1 -Space home-test -Plan D:\anytype\space-builder\plans\home.plan.json
.EXAMPLE
    pwsh -NoProfile -File scripts/maintenance/apply-anytype-schema.ps1 -Space home-test -Plan D:\anytype\space-builder\plans\home.plan.json -Check
.EXAMPLE
    pwsh -NoProfile -File scripts/maintenance/apply-anytype-schema.ps1 -Space pm -Manifest src/Deckle.Anytype/Schema/pm-schema-manifest.json
.EXAMPLE
    pwsh -NoProfile -File scripts/maintenance/apply-anytype-schema.ps1 -Space home -ReadTo D:\anytype\space-builder\plans\home.plan.json -Pages 'Accueil'
.EXAMPLE
    pwsh -NoProfile -File scripts/maintenance/apply-anytype-schema.ps1 -Space home-test -Plan D:\anytype\space-builder\plans\home.plan.json
.EXAMPLE
    pwsh -NoProfile -File scripts/maintenance/apply-anytype-schema.ps1 -Space home-test -Plan D:\anytype\space-builder\plans\home.plan.json -Check
.EXAMPLE
    pwsh -NoProfile -File scripts/maintenance/apply-anytype-schema.ps1 -Space pm -Manifest src/Deckle.Anytype/Schema/pm-schema-manifest.json -InviteLink 'anytype://invite/?cid=CID&key=KEY' -Pilot
.NOTES
    section: Maintenance
    order: 300
#>

[CmdletBinding()]
param(
    # The space alias, as spaces.json names it.
    [Parameter(Mandatory)]
    [string]$Space,
    # Path of the schema manifest, a JSON file.
    [string]$Manifest,
    # Path of a space plan, a JSON file: the manifest's grammar plus panels, views,
    # templates, collections, queries, pages, widgets and objects.
    [string]$Plan,
    # With -Plan: which parts a run covers. Default: schema and structure.
    [ValidateSet('schema', 'structure', 'objects', 'widgets')]
    [string[]]$Stages = @('schema', 'structure'),
    # With -Plan: read only. Exit 0 when the space is the plan, 3 when it is not.
    [switch]$Check,
    # With -Plan: after a write, wait this long and check again. 0 skips it.
    [ValidateRange(0, 120)]
    [int]$RecheckAfterMinutes = 10,
    # Reads the space into a plan file instead of writing anything.
    [string]$ReadTo,
    # With -ReadTo: names of the pages to read with their body.
    [string[]]$Pages = @(),
    # An invite link of the space, copied from the Anytype app. When given, the
    # bot asks to join and the alias is pointed at that space.
    [string]$InviteLink,
    # With -InviteLink: also make it the space of the piloting commands.
    [switch]$Pilot,
    # How long to wait for a person to accept the bot.
    [ValidateRange(1, 120)]
    [int]$AcceptTimeoutMinutes = 15,
    # The data root holding modules\anytype; defaults to DECKLE_DATA_ROOT, else
    # the app's default under LOCALAPPDATA.
    [string]$DataRoot
)

$ErrorActionPreference = 'Stop'

$Endpoint = 'http://127.0.0.1:33255/mcp'
$TokenVariable = 'DECKLE_MCP_TOKEN_SCHEMA_ADMIN'

function Write-Step([string]$Text) { Write-Host "`n[$Text]" -ForegroundColor Cyan }

function Stop-With([int]$Code, [string]$Text) {
    Write-Host "`n$Text" -ForegroundColor Red
    exit $Code
}

# One tool call on the schema-admin surface. The surface answers a JSON-RPC
# result as server-sent events; the last data line carries it.
function Invoke-SchemaTool([string]$Name, [hashtable]$Arguments) {
    $body = @{
        jsonrpc = '2.0'; id = 1; method = 'tools/call'
        params  = @{ name = $Name; arguments = $Arguments }
    } | ConvertTo-Json -Depth 64 -Compress
    $headers = @{ Authorization = "Bearer $script:Token"; Accept = 'application/json, text/event-stream' }
    try {
        $response = Invoke-WebRequest $Endpoint -Method Post -Headers $headers `
            -ContentType 'application/json; charset=utf-8' `
            -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 600
    }
    catch {
        Stop-With 1 "Deckle's schema-admin surface did not answer at $Endpoint ($($_.Exception.Message)). Is Deckle running?"
    }
    $line = $response.Content -split "`n" | Where-Object { $_ -like 'data: *' } | Select-Object -Last 1
    $json = if ($line) { $line.Substring(6) } else { $response.Content }
    $answer = $json | ConvertFrom-Json -Depth 64
    if ($answer.error) { Stop-With 1 "$Name was refused: $($answer.error.message)" }
    $text = ($answer.result.content | ForEach-Object { $_.text }) -join "`n"
    [pscustomobject]@{
        IsError    = [bool]$answer.result.isError
        Text       = $text
        Structured = $answer.result.structuredContent
    }
}

function Get-Preview {
    $preview = Invoke-SchemaTool 'schema_preview' @{ space = $Space; manifest = $script:ManifestObject }
    if ($preview.IsError -or -not $preview.Structured) { Stop-With 1 "schema_preview failed:`n$($preview.Text)" }
    $preview.Structured
}

function Show-Preview($Preview) {
    $Preview.actions | Group-Object kind | Sort-Object Count -Descending |
        ForEach-Object { Write-Host ('  {0,4}  {1}' -f $_.Count, $_.Name) }
    # The actions a reader wants by name: what is created, renamed or detached.
    $Preview.actions |
        Where-Object { $_.kind -in 'create_type', 'rename', 'detach_property', 'create_section' } |
        ForEach-Object { Write-Host "        $($_.kind) · $($_.key) · $($_.name)" -ForegroundColor DarkGray }
}

function Show-Skipped($Preview) {
    if (-not $Preview.skipped_conflicts) { return }
    Write-Host "`nLeft as it is in the space, to change by hand in the app if wanted:" -ForegroundColor Yellow
    $Preview.skipped_conflicts | ForEach-Object { Write-Host "  - $_" }
}

# Replaces one JSON string value in a Deckle settings file, after copying the
# file beside itself. The text is edited in place so nothing else moves.
function Set-JsonValue([string]$Path, [string]$Property, [string]$Value) {
    $text = [IO.File]::ReadAllText($Path)
    $pattern = '("' + [regex]::Escape($Property) + '"\s*:\s*")[^"]*(")'
    if ($text -notmatch $pattern) { return $false }
    $backup = "$Path.bak-$(Get-Date -Format 'yyyy-MM-dd-HHmmss')"
    Copy-Item -LiteralPath $Path -Destination $backup
    $replaced = [regex]::new($pattern).Replace($text, { param($m) $m.Groups[1].Value + $Value + $m.Groups[2].Value }, 1)
    [IO.File]::WriteAllText($Path, $replaced, [Text.UTF8Encoding]::new($false))
    Write-Host "  $(Split-Path $Path -Leaf): $Property -> $Value (copy: $(Split-Path $backup -Leaf))"
    return $true
}

# ── Inputs ───────────────────────────────────────────────────────────────────

$script:Token = [Environment]::GetEnvironmentVariable($TokenVariable, 'User')
if (-not $script:Token) { Stop-With 1 "The user variable $TokenVariable is empty: Deckle mints it when its gateway starts." }

if (-not $Manifest -and -not $Plan -and -not $ReadTo) { Stop-With 1 'Give -Manifest, -Plan or -ReadTo.' }
if ($Manifest -and $Plan) { Stop-With 1 'Give -Manifest or -Plan, not both.' }
if ($Manifest) {
    $manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
    $script:ManifestObject = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 64
}

if (-not $DataRoot) {
    $DataRoot = if ($env:DECKLE_DATA_ROOT) { $env:DECKLE_DATA_ROOT } else { Join-Path $env:LOCALAPPDATA 'Deckle' }
}
$moduleFolder = Join-Path $DataRoot 'modules\anytype'

# ── Join, when an invite link is given ───────────────────────────────────────

if ($InviteLink) {
    Write-Step 'Join the space'
    $versions = Join-Path $env:LOCALAPPDATA 'Programs\Deckle Providers\Anytype\versions'
    $cli = Get-ChildItem -LiteralPath $versions -Filter 'anytype.exe' -Recurse -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Directory.Name } | Select-Object -Last 1
    if (-not $cli) { Stop-With 1 "No anytype.exe under $versions." }

    $joined = & $cli.FullName space join $InviteLink 2>&1 | Out-String
    Write-Host $joined.Trim()
    if ($joined -notmatch "to space '([^']+)'") {
        Stop-With 1 'The join request did not name the space it was sent to.'
    }
    $spaceId = $Matches[1]

    Write-Host "`nIn the Anytype app, open the members of that space and accept the bot as an editor."
    $deadline = (Get-Date).AddMinutes($AcceptTimeoutMinutes)
    $active = $false
    while ((Get-Date) -lt $deadline) {
        $list = & $cli.FullName space list 2>&1 | Out-String
        if ($list -split "`n" | Where-Object { $_ -like "*$spaceId*" -and $_ -match '\bActive\b' }) { $active = $true; break }
        Start-Sleep -Seconds 5
    }
    if (-not $active) { Stop-With 1 "The bot was not accepted within $AcceptTimeoutMinutes minutes." }
    Write-Host 'The bot is a member.' -ForegroundColor Green

    Write-Step 'Point Deckle at the space'
    $spacesFile = Join-Path $moduleFolder 'spaces.json'
    if (-not (Set-JsonValue $spacesFile $Space $spaceId)) {
        Stop-With 1 "spaces.json has no alias '$Space': add it there first, then run again without -InviteLink."
    }
    if ($Pilot) {
        if (-not (Set-JsonValue (Join-Path $moduleFolder 'credentials.json') 'space_id' $spaceId)) {
            Stop-With 1 'credentials.json has no space_id.'
        }
        Write-Host '  The piloting commands follow at the next start of Deckle, which replaces its gateway.' -ForegroundColor Yellow
    }
}

# ── Read a space into a plan ─────────────────────────────────────────────────

if ($ReadTo) {
    Write-Step "Read '$Space' into a plan"
    $read = Invoke-SchemaTool 'space_plan_read' @{ space = $Space; pages = @($Pages) }
    if ($read.IsError) { Stop-With 1 "space_plan_read failed:`n$($read.Text)" }
    [IO.File]::WriteAllText($ReadTo, $read.Text, [Text.UTF8Encoding]::new($false))
    $plan = $read.Text | ConvertFrom-Json -Depth 64
    Write-Host ("  {0} properties, {1} types, {2} templates, {3} collections, {4} queries -> {5}" -f `
        @($plan.properties).Count, @($plan.types).Count, @($plan.templates).Count, @($plan.collections).Count, @($plan.queries).Count, $ReadTo)
    foreach ($note in @($plan.reader_notes)) { Write-Host "  note: $note" -ForegroundColor Yellow }
    Write-Host 'The file holds a real space''s structure: keep it out of the repository.' -ForegroundColor Yellow
    exit 0
}

# ── Build, check or re-check a space from a plan ─────────────────────────────

if ($Plan) {
    $planPath = (Resolve-Path -LiteralPath $Plan).Path
    $planObject = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json -Depth 64

    function Get-PlanPreview {
        $answer = Invoke-SchemaTool 'space_plan_preview' @{ space = $Space; plan = $planObject; stages = @($Stages) }
        if ($answer.IsError -or -not $answer.Structured) { Stop-With 1 "space_plan_preview failed:`n$($answer.Text)" }
        $answer.Structured
    }

    Write-Step "Preview '$Space' against $(Split-Path $planPath -Leaf) ($($Stages -join ', '))"
    $planPreview = Get-PlanPreview
    if (@($planPreview.problems).Count) {
        Write-Host 'The plan is invalid:' -ForegroundColor Red
        $planPreview.problems | ForEach-Object { Write-Host "  - $_" }
        Stop-With 2 'Nothing was written: fix the plan first.'
    }
    if (@($planPreview.conflicts).Count) {
        Write-Host 'Conflicts:' -ForegroundColor Red
        $planPreview.conflicts | ForEach-Object { Write-Host "  - $_" }
        Stop-With 2 'Nothing was written: a conflict has to be settled first.'
    }
    foreach ($note in @($planPreview.notes)) { Write-Host "  left as it is: $note" -ForegroundColor Yellow }
    $count = @($planPreview.actions).Count
    if ($count -eq 0) { Write-Host "`nNothing to do: the space matches the plan." -ForegroundColor Green; exit 0 }

    $planPreview.actions | Group-Object kind | Sort-Object Count -Descending |
        ForEach-Object { Write-Host ('  {0,4}  {1}' -f $_.Count, $_.Name) }
    if ($Check) {
        $planPreview.actions | Select-Object -First 40 | ForEach-Object { Write-Host "        $($_.kind) · $($_.key)" -ForegroundColor DarkGray }
        Stop-With 3 "The space differs from the plan ($count action(s))."
    }

    $answer = Read-Host "`nApply these $count action(s) to '$Space'? (yes/no)"
    if ($answer -notin 'yes', 'y', 'oui', 'o') { Stop-With 2 'Nothing was written.' }

    Write-Step 'Apply'
    $applied = Invoke-SchemaTool 'space_plan_apply' @{
        space = $Space; preview_id = $planPreview.preview_id; plan = $planObject; stages = @($Stages); confirm = $true
    }
    Write-Host $applied.Text
    if ($applied.IsError) { Stop-With 1 'The apply stopped on an error; what it wrote before is listed above.' }

    Write-Step 'Verify'
    $after = Get-PlanPreview
    if (@($after.actions).Count) {
        $after.actions | Select-Object -First 40 | ForEach-Object { Write-Host "  $($_.kind) · $($_.key)" }
        Stop-With 3 'The space does not match the plan yet.'
    }
    Write-Host 'The space matches the plan right after the write.' -ForegroundColor Green

    if ($RecheckAfterMinutes -gt 0) {
        # Why wait: id-less sorts and filters collapse when the object is loaded again.
        Write-Step "Check again in $RecheckAfterMinutes minute(s)"
        Start-Sleep -Seconds ($RecheckAfterMinutes * 60)
        $later = Get-PlanPreview
        if (@($later.actions).Count) {
            $later.actions | Select-Object -First 40 | ForEach-Object { Write-Host "  $($_.kind) · $($_.key)" }
            Stop-With 3 "After $RecheckAfterMinutes minute(s) the space no longer matches the plan."
        }
        Write-Host "Still the plan after $RecheckAfterMinutes minute(s)." -ForegroundColor Green
    }
    exit 0
}

# ── Preview, confirm, apply, verify ──────────────────────────────────────────

Write-Step "Preview '$Space' against $(Split-Path $manifestPath -Leaf)"
$preview = Get-Preview

if ($preview.conflicts) {
    Write-Host 'Conflicts:' -ForegroundColor Red
    $preview.conflicts | ForEach-Object { Write-Host "  - $_" }
    Stop-With 2 'Nothing was written: a conflict has to be settled first.'
}
if ($preview.actions.Count -eq 0) {
    Show-Skipped $preview
    Write-Host "`nNothing to do: the space matches the manifest." -ForegroundColor Green
    exit 0
}

Write-Host "$($preview.actions.Count) action(s):"
Show-Preview $preview
Show-Skipped $preview

$answer = Read-Host "`nApply these $($preview.actions.Count) action(s) to '$Space'? (yes/no)"
if ($answer -notin 'yes', 'y', 'oui', 'o') { Stop-With 2 'Nothing was written.' }

Write-Step 'Apply'
$applied = Invoke-SchemaTool 'schema_apply' @{
    space = $Space; preview_id = $preview.preview_id; manifest = $script:ManifestObject; confirm = $true
}
Write-Host $applied.Text
if ($applied.IsError) { Stop-With 1 'The apply stopped on an error; what it wrote before is listed above.' }

Write-Step 'Verify'
$after = Get-Preview
if ($after.actions.Count -gt 0) {
    Write-Host "$($after.actions.Count) action(s) remain:" -ForegroundColor Red
    Show-Preview $after
    Stop-With 3 'The space does not match the manifest yet.'
}
Show-Skipped $after
Write-Host "`nDone: the space matches the manifest." -ForegroundColor Green
exit 0
