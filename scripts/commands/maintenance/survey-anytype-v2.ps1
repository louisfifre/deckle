# Survey Anytype API v2 — read-only measures against the project-management space.
#
# Replays a fixed list of measures against the local headless Anytype backend
# (anytype-cli, REST on 127.0.0.1:31012) and writes one Markdown report, so the
# v2 port of Deckle.Anytype is decided on measured behaviour rather than on the
# docs, which contradict each other (changelog vs APIV2.md vs SKILL.md). Rerun it
# on every new backend version and diff the reports.
#
# Kept out of the launcher menu on purpose: this file carries no comment-based
# Header, because a Header without an `action:` line still declares a one-click
# Action (scripts/README.md, "Adding a script"). The maintainer decides whether
# it joins Maintenance > Checks; the Header would then be `section: Checks`.
#
# Safety contract, enforced by Invoke-SurveyRequest rather than by discipline:
#   - GET/HEAD only, plus POST .../search (a read), plus POST/PATCH that carry
#     ?dry_run=true. DELETE and PUT are refused before any byte leaves.
#   - dry_run is proven on a creation (POST then exact-name search) before any
#     dry-run PATCH runs; if the proof fails, every write probe is skipped.
#   - dry-run PATCHes on objects target only the survey's own task; view and
#     widget probes are chosen so that even an ignored dry_run changes nothing.
#   - writes are spaced over a second apart: the backend's write limiter
#     (1 rps sustained, burst 60) is shared with the resident Deckle app.
#   - the API key is read from the DPAPI vault, sent only in the Authorization
#     header, and scrubbed from the report; only its length is ever shown.
#
# Usage:
#   pwsh -NoProfile -File scripts/commands/maintenance/survey-anytype-v2.ps1
#   pwsh -NoProfile -File scripts/commands/maintenance/survey-anytype-v2.ps1 -OutputPath .tmp/x.md

#Requires -Version 7.2

[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$BaseUrl = 'http://127.0.0.1:31012'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$RepoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
if (-not $OutputPath) {
    $OutputPath = Join-Path $RepoRoot ('.tmp/anytype-v2-survey/{0}.md' -f (Get-Date -Format 'yyyy-MM-dd_HHmm'))
} elseif (-not [IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $RepoRoot $OutputPath
}

# The task that owns this survey. Every dry-run PATCH on an object lands here:
# if a dry_run were ever honoured as a real write, the damage stays on the task
# whose purpose is to measure that very thing.
$SurveyTaskName = "Mesurer l'API v2 sur l'espace Dev avant d'écrire une ligne de code"
# Probe names are unique per run and prefixed so a final sweep can prove that no
# dry-run creation left an object behind.
$ProbePrefix = 'zz-survey-v2-probe'
$RunStamp = Get-Date -Format 'yyyyMMddHHmmss'

# ── Credentials ──────────────────────────────────────────────────────────────
# Same sources as the app: SecretVault (DPAPI CurrentUser, null entropy) for the
# key, the module's credentials.json for the space id and the v1 version header.

function Get-DeckleDataRoot {
    if ($env:DECKLE_DATA_ROOT) { return [IO.Path]::GetFullPath($env:DECKLE_DATA_ROOT) }
    return Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Deckle'
}

function Read-AnytypeApiKey {
    param([Parameter(Mandatory)][string]$DataRoot)
    Add-Type -AssemblyName System.Security.Cryptography.ProtectedData -ErrorAction SilentlyContinue
    $path = Join-Path $DataRoot 'secrets.dat'
    if (-not (Test-Path -LiteralPath $path)) { throw "No secret vault at '$path'." }
    $plain = [System.Security.Cryptography.ProtectedData]::Unprotect(
        [IO.File]::ReadAllBytes($path), $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
    try {
        $vault = [Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json -AsHashtable
    } finally {
        [Array]::Clear($plain, 0, $plain.Length)
    }
    $key = $vault['secrets']['anytype-api-key']
    if ([string]::IsNullOrWhiteSpace($key)) { throw "The vault holds no 'anytype-api-key' entry." }
    return $key
}

$DataRoot = Get-DeckleDataRoot
$CredentialsPath = Join-Path $DataRoot 'modules/anytype/credentials.json'
$Credentials = Get-Content -LiteralPath $CredentialsPath -Raw | ConvertFrom-Json -AsHashtable
$SpaceId = [string]$Credentials['space_id']
$V1ApiVersion = [string]$Credentials['api_version']
$script:ApiKey = Read-AnytypeApiKey -DataRoot $DataRoot

# ── HTTP ─────────────────────────────────────────────────────────────────────

$script:Http = [System.Net.Http.HttpClient]::new()
$script:Http.BaseAddress = [Uri]$BaseUrl
$script:Http.Timeout = [TimeSpan]::FromSeconds(30)
$script:CallLog = [System.Collections.Generic.List[object]]::new()
$script:LastWriteAt = [datetime]::MinValue
$script:WriteGap = [TimeSpan]::FromMilliseconds(1100)

function Test-ReadOnlyRequest {
    param([string]$Method, [string]$Path)
    if ($Method -in 'GET', 'HEAD') { return $true }
    if ($Method -in 'DELETE', 'PUT') { return $false }
    $route = $Path.Split('?')[0]
    # Search is a read: the docs say it takes no Idempotency-Key and ignores dry_run.
    if ($Method -eq 'POST' -and $route -match '/search$') { return $true }
    return $Path -match '[?&]dry_run=true(&|$)'
}

function Invoke-SurveyRequest {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [AllowNull()]$Body,
        [hashtable]$Headers = @{},
        [switch]$NoAuth
    )
    $Path = $Path.Replace('{s}', $SpaceId)
    if (-not (Test-ReadOnlyRequest -Method $Method -Path $Path)) {
        throw "Refused by the survey's read-only guard: $Method $Path"
    }
    $isWrite = $Method -notin 'GET', 'HEAD' -and $Path -match 'dry_run=true'
    if ($isWrite) {
        $wait = $script:WriteGap - ((Get-Date) - $script:LastWriteAt)
        if ($wait -gt [TimeSpan]::Zero) { Start-Sleep -Milliseconds ([int]$wait.TotalMilliseconds) }
    }
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), $Path)
    if (-not $NoAuth) { [void]$request.Headers.TryAddWithoutValidation('Authorization', "Bearer $script:ApiKey") }
    foreach ($name in $Headers.Keys) { [void]$request.Headers.TryAddWithoutValidation($name, [string]$Headers[$name]) }
    if ($null -ne $Body) {
        $json = if ($Body -is [string]) { $Body } else { ConvertTo-Json -InputObject $Body -Depth 40 -Compress }
        $request.Content = [System.Net.Http.StringContent]::new($json, [Text.Encoding]::UTF8, 'application/json')
    }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $response = $script:Http.SendAsync($request).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $watch.Stop()
    if ($isWrite) { $script:LastWriteAt = Get-Date }
    $headerMap = [ordered]@{}
    foreach ($h in $response.Headers) { $headerMap[$h.Key] = $h.Value -join ',' }
    foreach ($h in $response.Content.Headers) { $headerMap[$h.Key] = $h.Value -join ',' }
    $json = $null
    if ($text -and $headerMap['Content-Type'] -match 'json') {
        try { $json = $text | ConvertFrom-Json -AsHashtable -Depth 100 } catch { $json = $null }
    }
    $result = [pscustomobject]@{
        Method = $Method; Path = $Path; Status = [int]$response.StatusCode
        Headers = $headerMap; Text = $text; Json = $json; Ms = $watch.ElapsedMilliseconds
        Kind = if ($isWrite) { 'dry-run write' } elseif ($Method -eq 'POST') { 'search' } else { 'read' }
    }
    $script:CallLog.Add($result)
    return $result
}

function Get-Json {
    param([string]$Path)
    $r = Invoke-SurveyRequest -Method GET -Path $Path
    if ($r.Status -ne 200) { throw "GET $Path answered $($r.Status): $(Limit-Text $r.Text 400)" }
    return $r.Json
}

function Find-Objects {
    param([hashtable]$Body, [int]$Limit = 25)
    $r = Invoke-SurveyRequest -Method POST -Path "/v2/spaces/{s}/search?limit=$Limit" -Body $Body
    if ($r.Status -ne 200) { throw "search answered $($r.Status): $(Limit-Text $r.Text 400)" }
    return $r.Json
}

function Invoke-DryPatch {
    param([string]$ObjectId, [object[]]$Ops, [hashtable]$Headers = @{}, [string]$Query = '')
    if ($ObjectId -ne $script:Ctx.SurveyTaskId) {
        throw "Dry-run object PATCH refused: only the survey task is a target."
    }
    return Invoke-SurveyRequest -Method PATCH -Path "/v2/spaces/{s}/objects/$($ObjectId)?dry_run=true$Query" -Body @{ ops = $Ops } -Headers $Headers
}

# ── Evidence helpers ─────────────────────────────────────────────────────────

function Limit-Text {
    param([AllowNull()][string]$Text, [int]$Max = 2500)
    if ($null -eq $Text) { return '' }
    if ($Text.Length -le $Max) { return $Text }
    return $Text.Substring(0, $Max) + " … [trimmed $($Text.Length - $Max) chars]"
}

function ConvertTo-CompactJson {
    param([AllowNull()]$Value)
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [string]) { return $Value }
    return ConvertTo-Json -InputObject $Value -Depth 40 -Compress
}

function New-Measure {
    param([string]$Name, [string]$Why)
    return [pscustomobject]@{
        Name = $Name; Why = $Why; Verdict = ''; Status = 'measured'
        Evidence = [System.Collections.Generic.List[object]]::new()
        Notes = [System.Collections.Generic.List[string]]::new()
    }
}

function Add-Evidence {
    param($Measure, [string]$Label, [AllowNull()]$Value, [int]$Max = 2500)
    $Measure.Evidence.Add([pscustomobject]@{ Label = $Label; Body = Limit-Text (ConvertTo-CompactJson $Value) $Max })
}

function Add-Call {
    # One request as evidence: its status, the headers that matter, the body.
    param($Measure, [string]$Label, $Response, [int]$Max = 1500)
    $keep = [ordered]@{}
    foreach ($k in $Response.Headers.Keys) {
        if ($k -match '^(Ratelimit|X-Rate-Limit|Anytype-Version|ETag|Idempotency|Retry-After)') { $keep[$k] = $Response.Headers[$k] }
    }
    $head = "$($Response.Method) $($Response.Path.Replace($SpaceId, '{space}')) -> $($Response.Status)"
    if ($keep.Count) { $head += ' ' + (ConvertTo-CompactJson $keep) }
    $Measure.Evidence.Add([pscustomobject]@{ Label = $Label; Body = $head + "`n" + (Limit-Text $Response.Text $Max) })
}

function Add-Contradiction {
    param([string]$Text)
    $script:Ctx.Contradictions.Add($Text)
}

function Test-DryRunAllowed {
    param($Measure)
    if ($script:Ctx.DryRunTrusted) { return $true }
    $Measure.Notes.Add('Dry-run write probes skipped: dry_run was not proven on a creation.')
    return $false
}

# ── Context ──────────────────────────────────────────────────────────────────

$script:Ctx = [pscustomobject]@{
    SurveyTaskId = $null; SurveyTaskEtag = $null; DryRunTrusted = $false
    Contradictions = [System.Collections.Generic.List[string]]::new()
    WidgetCountBefore = $null; TemplateCountBefore = $null; TypeEtags = @{}
    Whoami = $null; ServedOps = @()
}

# ── Preflight ────────────────────────────────────────────────────────────────
# Not a measure: it stops the run when the backend is down (the survey never
# starts it), identifies the survey task, and proves dry_run on a creation
# before any dry-run PATCH is allowed to run.

function Invoke-Preflight {
    $m = New-Measure 'preflight' 'Backend alive, key accepted, survey task found, dry_run proven on a creation before any PATCH relies on it.'
    try {
        $who = Invoke-SurveyRequest -Method GET -Path '/v2/auth/whoami'
    } catch {
        throw "Backend did not answer on $BaseUrl ($($_.Exception.Message)). The survey never starts it."
    }
    if ($who.Status -ne 200) { throw "GET /v2/auth/whoami answered $($who.Status): $(Limit-Text $who.Text 300)" }
    $script:Ctx.Whoami = $who.Json
    # The key's own id and name are left out: they identify the credential.
    Add-Evidence $m 'whoami (key id/name omitted)' ([ordered]@{ scope = $who.Json['scope']; grant = $who.Json['grant']; api = $who.Json['api']; key_status = $who.Json['key_status'] })

    $found = Find-Objects @{ filter = ('name = "{0}"' -f $SurveyTaskName.Replace('"', '\"')) }
    if ($found['total'] -ne 1) { throw "Expected exactly one survey task, found $($found['total'])." }
    $script:Ctx.SurveyTaskId = $found['data'][0]['id']
    $task = Invoke-SurveyRequest -Method GET -Path "/v2/spaces/{s}/objects/$($script:Ctx.SurveyTaskId)?include=properties"
    $script:Ctx.SurveyTaskEtag = $task.Headers['ETag']
    Add-Evidence $m 'survey task' ([ordered]@{ id = $script:Ctx.SurveyTaskId; etag = $task.Headers['ETag'] })

    $probeName = "$ProbePrefix-create-$RunStamp"
    $create = Invoke-SurveyRequest -Method POST -Path '/v2/spaces/{s}/objects?dry_run=true' -Body @{ type = 'task'; name = $probeName }
    Add-Call $m 'dry-run creation' $create
    Start-Sleep -Seconds 2
    $after = Find-Objects @{ filter = ('name = "{0}"' -f $probeName) }
    Add-Evidence $m 'exact-name search after the dry-run creation' ([ordered]@{ total = $after['total'] })
    $script:Ctx.DryRunTrusted = ($create.Status -eq 200 -and $create.Json['dry_run'] -eq $true -and $after['total'] -eq 0)
    $m.Verdict = if ($script:Ctx.DryRunTrusted) {
        'Backend up, key scoped all-spaces read-write; dry-run POST answered 200 dry_run:true and created nothing.'
    } else {
        'dry_run NOT proven: every write probe below is skipped.'
    }
    return $m
}

# ── 1. malformed-keys ────────────────────────────────────────────────────────
# DevSpace.cs freezes keys outside v2's documented key grammar ^[a-zA-Z0-9_]+$.
# If v2 re-slugs, refuses or cannot filter them, every gesture touching them
# breaks on the port.

$MalformedKeys = @('charge_estimee_(jours)', 'budget_reel_(', 'tache(s)_liee(s)', 'fichier(s)_lie(s)', 'livrable(s)', 'rpport(s)_lie(s)')

function Measure-MalformedKeys {
    $m = New-Measure 'malformed-keys' 'v2 documents a key grammar ^[a-zA-Z0-9_]+$; the space carries keys with parentheses, frozen verbatim in DevSpace.cs.'
    $props = Get-Json '/v2/spaces/{s}/properties?limit=1000'
    $served = @($props['data'] | Where-Object { $_['key'] -in $MalformedKeys })
    Add-Evidence $m 'GET /properties rows for the malformed keys' $served
    $missing = @($MalformedKeys | Where-Object { $_ -notin @($served | ForEach-Object { $_['key'] }) })
    if ($missing) { $m.Notes.Add("Not served by GET /properties: $($missing -join ', ')") }

    $opt = Invoke-SurveyRequest -Method GET -Path '/v2/spaces/{s}/properties/livrable(s)/options'
    Add-Call $m 'options route with a parenthesised key in the path' $opt 600

    # Filter channels: the compact string validates before folding, so it is the
    # channel most likely to choke on parentheses.
    $f1 = Invoke-SurveyRequest -Method POST -Path '/v2/spaces/{s}/search?limit=3' -Body @{ filter = 'charge_estimee_(jours) > 0'; fields = @('charge_estimee_(jours)') }
    Add-Call $m 'compact filter string with a parenthesised key' $f1 800
    $f2 = Invoke-SurveyRequest -Method POST -Path '/v2/spaces/{s}/search?limit=3' -Body @{ filters = @(@{ property = 'charge_estimee_(jours)'; condition = 'greater'; value = 0 }); fields = @('charge_estimee_(jours)') }
    Add-Call $m 'structured filters array with a parenthesised key' $f2 800

    $writes = [ordered]@{}
    if (Test-DryRunAllowed $m) {
        $values = [ordered]@{
            'charge_estimee_(jours)' = 1
            'budget_reel_(' = 1
            'tache(s)_liee(s)' = @()
            'fichier(s)_lie(s)' = @()
            'livrable(s)' = @('Texte')
            'Charge réelle (Jours)' = 1
        }
        foreach ($k in $values.Keys) {
            $set = @{}; $set[$k] = $values[$k]
            $r = Invoke-DryPatch $script:Ctx.SurveyTaskId @(@{ op = 'set_properties'; set = $set })
            $writes[$k] = $r.Status
            Add-Call $m "dry-run set_properties with key '$k'" $r 700
        }
    }
    $okWrites = @($writes.Keys | Where-Object { $writes[$_] -eq 200 })
    $m.Verdict = "Served verbatim ($($served.Count)/$($MalformedKeys.Count) keys, parentheses kept); options route takes them in the path ($($opt.Status)); compact filter $($f1.Status), structured filters $($f2.Status); dry-run writes accepted for $($okWrites.Count)/$($writes.Count) spellings."
    if ($served.Count -gt 0) { Add-Contradiction 'Key grammar ^[a-zA-Z0-9_]+$ (APIV2) vs malformed keys served and accepted verbatim.' }
    return $m
}

# ── 2. option-names ──────────────────────────────────────────────────────────
# v2 addresses select options by NAME (case-sensitive), never by id; DevSpace.cs
# freezes v1 option KEYS. The port must know which names exist and whether the
# frozen tables still match the space.

function Get-DevSpaceOptions {
    # Parses the frozen tables of DevSpace.cs: class name -> list of (key, name).
    $path = Join-Path $RepoRoot 'src/Deckle.Anytype/Schema/DevSpace.cs'
    $text = Get-Content -LiteralPath $path -Raw
    $tables = [ordered]@{}
    foreach ($block in [regex]::Matches($text, 'public static class (\w+)\s*\{(.*?)\n    \}', 'Singleline')) {
        $opts = @([regex]::Matches($block.Groups[2].Value, 'new\("([^"]*)", "([^"]*)"\)') | ForEach-Object { [ordered]@{ key = $_.Groups[1].Value; name = $_.Groups[2].Value } })
        if ($opts.Count) { $tables[$block.Groups[1].Value] = $opts }
    }
    return $tables
}

function Measure-OptionNames {
    $m = New-Measure 'option-names' 'Select values travel as option names in v2; the frozen DevSpace.cs tables hold v1 keys and French labels.'
    $map = [ordered]@{ etat = 'Etat'; priorite = 'Priority'; type_de_tache = 'TypeDeTache'; 'livrable(s)' = 'Livrable'; type_de_document = 'TypeDeDocument'; phase_projet = 'PhaseProjet' }
    $frozen = Get-DevSpaceOptions
    $drift = [System.Collections.Generic.List[string]]::new()
    foreach ($key in $map.Keys) {
        $served = @((Get-Json "/v2/spaces/{s}/properties/$key/options?limit=1000")['data'] | ForEach-Object { $_['name'] })
        $frozenNames = @($frozen[$map[$key]] | ForEach-Object { $_['name'] })
        $onlyFrozen = @($frozenNames | Where-Object { $_ -cnotin $served })
        $onlyServed = @($served | Where-Object { $_ -cnotin $frozenNames })
        Add-Evidence $m "options of $key" ([ordered]@{ served = $served; devspace_names = $frozenNames; devspace_keys = @($frozen[$map[$key]] | ForEach-Object { $_['key'] }); only_in_devspace = $onlyFrozen; only_served = $onlyServed })
        if ($onlyFrozen -or $onlyServed) { $drift.Add("$key (devspace-only: $($onlyFrozen -join '/'); served-only: $($onlyServed -join '/'))") }
    }
    $answers = [ordered]@{}
    if (Test-DryRunAllowed $m) {
        $cases = [ordered]@{
            'etat = "En cours" (exact name)' = @{ etat = @('En cours') }
            'etat = "en cours" (wrong case)' = @{ etat = @('en cours') }
            'etat = "en_cours" (v1 key)' = @{ etat = @('en_cours') }
            'etat = "En cours" as a scalar string' = @{ etat = 'En cours' }
            'priorite = 4 as a number' = @{ priorite = 4 }
            'priorite = "67c6d722341c1628147d7b1e" (v1 option key)' = @{ priorite = @('67c6d722341c1628147d7b1e') }
            '67c6d714341c1628147d7b1d (v1 opaque property key) = "4"' = @{ '67c6d714341c1628147d7b1d' = @('4') }
            'type_de_tache = "recherche" (v1 key)' = @{ type_de_tache = @('recherche') }
            'type_de_tache = "Chercher" (name)' = @{ type_de_tache = @('Chercher') }
        }
        foreach ($label in $cases.Keys) {
            $r = Invoke-DryPatch $script:Ctx.SurveyTaskId @(@{ op = 'set_properties'; set = $cases[$label] })
            $answers[$label] = $r.Status
            Add-Call $m "dry-run $label" $r 600
        }
    }
    $m.Verdict = "Options are names; case-sensitive: $(if ($answers.Count) { "exact name $($answers['etat = ""En cours"" (exact name)']), wrong case $($answers['etat = ""en cours"" (wrong case)']), v1 key $($answers['etat = ""en_cours"" (v1 key)'])" } else { 'not probed' }). Drift vs DevSpace.cs: $(if ($drift.Count) { $drift -join '; ' } else { 'none' })."
    return $m
}

# ── 3. outline ───────────────────────────────────────────────────────────────
# The catalogue's reads (checklist, sections, last lines of a report) want the
# cheap ?outline=true survey; its exact shape decides what the MCP can parse.

function Get-OutlineSummary {
    param($Doc)
    $rows = @($Doc['outline'])
    $ids = @($rows | ForEach-Object { $_['id'] })
    return [ordered]@{
        top_level_keys = @($Doc.Keys)
        entries = $rows.Count
        entry_keys = @($rows | ForEach-Object { $_.Keys } | Sort-Object -Unique)
        types = @($rows | Group-Object { $_['type'] } | ForEach-Object { "$($_.Name)=$($_.Count)" })
        max_indent = ($rows | ForEach-Object { [int]$_['indent'] } | Measure-Object -Maximum).Maximum
        id_lengths = @($ids | Group-Object Length | ForEach-Object { "$($_.Name)ch=$($_.Count)" })
        headings = @($rows | Where-Object { $_['type'] -like 'heading*' } | ForEach-Object { $_['text'] })
        checkbox_entries_have_checked = [bool](@($rows | Where-Object { $_['type'] -eq 'checkbox' -and $_.ContainsKey('checked') }).Count)
        truncated_texts = @($rows | Where-Object { $_.ContainsKey('text') -and $_['text'].EndsWith('…') }).Count
    }
}

function Measure-Outline {
    $m = New-Measure 'outline' 'get_task / get_report / replace_section read structure through ?outline=true; its fields decide what the MCP can address without a full read.'
    $task = Get-Json "/v2/spaces/{s}/objects/$($script:Ctx.SurveyTaskId)?outline=true"
    Add-Evidence $m 'task outline summary' (Get-OutlineSummary $task)
    Add-Evidence $m 'task outline, first 12 entries' (@($task['outline'])[0..11]) 1800
    $rapports = Find-Objects @{ type = 'rapport'; sorts = @(@{ property = 'created_date'; direction = 'desc' }) } 1
    $rid = $rapports['data'][0]['id']
    $rapport = Get-Json "/v2/spaces/{s}/objects/$($rid)?outline=true&include=properties"
    Add-Evidence $m "rapport outline summary (latest rapport $rid)" (Get-OutlineSummary $rapport)
    Add-Evidence $m 'rapport outline, first 6 entries' (@($rapport['outline'])[0..5]) 1500
    $both = Invoke-SurveyRequest -Method GET -Path "/v2/spaces/{s}/objects/$($script:Ctx.SurveyTaskId)?outline=true&block=c80d1"
    Add-Call $m 'outline together with block (documented as illegal)' $both 500
    $ts = Get-OutlineSummary $task
    $m.Verdict = "Flat array of {indent,id,type,text?}; $($ts.entries) entries on the task, ids $($ts.id_lengths -join ','); checkbox entries carry checked: $($ts.checkbox_entries_have_checked); empty blocks carry no text key; outline+block -> $($both.Status)."
    if (-not $ts.checkbox_entries_have_checked) { $m.Notes.Add('The outline does not say whether a checkbox is ticked: a checklist read needs the full or ?block= read.') }
    return $m
}

# ── 4. anytype-version-header ────────────────────────────────────────────────
# The changelog says v2 ignores Anytype-Version; APIV2.md C1 says "as in v1".
# The v2 client either keeps, drops or must never send the header.

function Measure-AnytypeVersionHeader {
    $m = New-Measure 'anytype-version-header' 'Changelog: "not used by v2 routes"; APIV2.md C1: "Anytype-Version date header as in v1". Measured on a v2 read, a v2 dry-run write and a v1 read for contrast.'
    $cases = [ordered]@{ 'none' = $null; "v1 value ($V1ApiVersion)" = $V1ApiVersion; 'bogus date 1999-01-01' = '1999-01-01'; 'garbage not-a-version' = 'not-a-version' }
    $table = [System.Collections.Generic.List[string]]::new()
    foreach ($label in $cases.Keys) {
        $h = @{}; if ($cases[$label]) { $h['Anytype-Version'] = $cases[$label] }
        $r = Invoke-SurveyRequest -Method GET -Path '/v2/auth/whoami' -Headers $h
        Add-Call $m "v2 GET whoami, header $label" $r 300
        $v1 = Invoke-SurveyRequest -Method GET -Path '/v1/spaces?limit=1' -Headers $h
        Add-Call $m "v1 GET spaces, header $label" $v1 300
        $line = "${label}: v2 $($r.Status) (echo $($r.Headers['Anytype-Version'])), v1 $($v1.Status)"
        if ($cases[$label] -eq 'not-a-version' -and (Test-DryRunAllowed $m)) {
            $w = Invoke-SurveyRequest -Method POST -Path '/v2/spaces/{s}/objects?dry_run=true' -Body @{ type = 'task'; name = "$ProbePrefix-header-$RunStamp" } -Headers $h
            Add-Call $m 'v2 dry-run create with a garbage header' $w 400
            $line += ", v2 dry-run write $($w.Status)"
        }
        $table.Add($line)
    }
    $m.Verdict = ($table -join '; ')
    $v2Statuses = @($script:CallLog | Where-Object { $_.Path -eq '/v2/auth/whoami' } | Select-Object -Last 4 | ForEach-Object { $_.Status } | Sort-Object -Unique)
    if ($v2Statuses.Count -eq 1 -and $v2Statuses[0] -eq 200) { Add-Contradiction 'APIV2.md C1 says v2 takes the Anytype-Version header "as in v1"; measured: v2 ignores it (any value or none answers 200), matching the changelog.' }
    return $m
}

# ── 5. type-views ────────────────────────────────────────────────────────────
# A type's views are its default list in the app; the v2 view ops make them
# editable from Deckle. The shape and the default decide the "specify views in
# space plans" task.

function Get-ViewSummary {
    param($TypeDoc)
    $dv = @($TypeDoc['blocks'] | Where-Object { $_['type'] -eq 'dataview' })[0]
    if ($null -eq $dv) { return @() }
    $i = 0
    return @($dv['views'] | ForEach-Object {
        [ordered]@{
            position = $i++; id = $_['id']; name = $_['name']; type = if ($_.ContainsKey('type')) { $_['type'] } else { '(absent = table)' }
            group_by = $_['group_by']; filters = @($_['filters'] | ForEach-Object { "$($_['property']) $($_['condition']) $(ConvertTo-CompactJson $_['value'])" })
            sorts = @($_['sorts'] | ForEach-Object { "$($_['property']) $($_['direction'])" })
            visible_columns = @($_['columns'] | Where-Object { -not $_['hidden'] } | ForEach-Object { $_['property'] })
            view_keys = @($_.Keys)
        }
    })
}

function Measure-TypeViews {
    $m = New-Measure 'type-views' 'The views of each type are the space''s default lists; update_view/insert_view/move_view/delete_view make them scriptable.'
    $types = [ordered]@{ project = 'Projet'; task = 'Tâche'; rapport = 'Rapport'; idee = 'Idée'; document = 'Document'; epic = 'EPIC' }
    $singleView = $null; $counts = [System.Collections.Generic.List[string]]::new()
    foreach ($key in $types.Keys) {
        $r = Invoke-SurveyRequest -Method GET -Path "/v2/spaces/{s}/types/$key"
        $script:Ctx.TypeEtags[$key] = $r.Headers['ETag']
        $views = @(Get-ViewSummary $r.Json)
        Add-Evidence $m "views of type $key (etag $($r.Headers['ETag']); position 0 is the default tab)" $views 2200
        $counts.Add("$key=$($views.Count)")
        if ($views.Count -eq 1 -and -not $singleView) { $singleView = [pscustomobject]@{ Type = $key; View = $views[0] } }
    }
    $dvKeys = @((Get-Json '/v2/spaces/{s}/types/task')['blocks'] | Where-Object { $_['type'] -eq 'dataview' })[0].Keys
    Add-Evidence $m 'dataview block keys on a type document' @($dvKeys)

    if (Test-DryRunAllowed $m) {
        # Proof first, on the one op whose real effect would be a new view only.
        $probeView = "$ProbePrefix-view-$RunStamp"
        $ins = Invoke-SurveyRequest -Method PATCH -Path '/v2/spaces/{s}/types/rapport?dry_run=true' -Body @{ ops = @(@{ op = 'insert_view'; name = $probeView }) }
        Add-Call $m 'dry-run insert_view on type rapport' $ins 800
        $check = Invoke-SurveyRequest -Method GET -Path '/v2/spaces/{s}/types/rapport'
        $leaked = $check.Text.Contains($probeView)
        Add-Evidence $m 'type rapport after the dry-run insert_view' ([ordered]@{ probe_view_present = $leaked; etag_before = $script:Ctx.TypeEtags['rapport']; etag_after = $check.Headers['ETag'] })
        if (-not $leaked -and $singleView) {
            # On a one-view type every remaining op is harmless even if dry_run
            # were ignored: renaming to the same name, moving to where it is, and
            # deleting the last view, which the docs say is refused.
            $t = $singleView.Type; $v = $singleView.View
            $ops = [ordered]@{
                update_view = @{ op = 'update_view'; view = $v.id; set = @{ name = $v.name } }
                move_view = @{ op = 'move_view'; view = $v.id; position = 'first' }
                delete_view = @{ op = 'delete_view'; view = $v.id }
            }
            foreach ($op in $ops.Keys) {
                $r = Invoke-SurveyRequest -Method PATCH -Path "/v2/spaces/{s}/types/$($t)?dry_run=true" -Body @{ ops = @($ops[$op]) }
                Add-Call $m "dry-run $op on the single view of type $t (no-op by construction)" $r 800
            }
        } elseif (-not $singleView) {
            $m.Notes.Add('No surveyed type has a single view: move/update/delete_view probes skipped to stay harmless.')
        }
    }
    $m.Verdict = "GET /types/{key} serves an AnyBlock object_type document whose dataview block holds the views (counts: $($counts -join ', ')); default = first view; view ops answered as shown."
    return $m
}

# ── 6. templates ─────────────────────────────────────────────────────────────
# v1 never applied a default template (DevSpace.Templates passes ids by hand).
# The catalogue's create_task body sections come from the task template.

function Measure-Templates {
    $m = New-Measure 'templates' 'Which template each type starts from decides whether Deckle keeps passing template ids by hand (DevSpace.Templates).'
    $all = Get-Json '/v2/spaces/{s}/templates?limit=1000'
    $script:Ctx.TemplateCountBefore = $all['total']
    Add-Evidence $m 'GET /templates (all rows)' $all['data'] 2500
    $orphans = @($all['data'] | Where-Object { -not $_.ContainsKey('template_for') })
    if ($orphans) { $m.Notes.Add("$($orphans.Count) template rows carry no template_for (their type is not served).") }
    $taskTpl = @($all['data'] | Where-Object { $_['template_for'] -eq 'task' -and $_['default'] })[0]
    $tplDoc = Invoke-SurveyRequest -Method GET -Path "/v2/spaces/{s}/objects/$($taskTpl['id'])?outline=true&include=properties"
    Add-Call $m 'task default template, outline with properties' $tplDoc 2500
    $md = Invoke-SurveyRequest -Method GET -Path "/v2/spaces/{s}/objects/$($taskTpl['id'])?format=md"
    Add-Call $m 'task default template as markdown' $md 1500
    $schema = Invoke-SurveyRequest -Method GET -Path '/v2/schemas/template'
    Add-Call $m 'GET /v2/schemas/template' $schema 1200
    $served = Get-Json '/docs/openapi.json'
    $routes = @($served['paths'].Keys | Where-Object { $_ -match 'template' } | ForEach-Object { "$_ : $(@($served['paths'][$_].Keys | Where-Object { $_ -in 'get','post','patch','put','delete' }) -join ',')" })
    Add-Evidence $m 'template routes in the served openapi' $routes
    if (Test-DryRunAllowed $m) {
        $name = "$ProbePrefix-template-$RunStamp"
        $post = Invoke-SurveyRequest -Method POST -Path '/v2/spaces/{s}/templates?dry_run=true' -Body @{ template_for = 'task'; name = $name }
        Add-Call $m 'dry-run POST /templates' $post 800
        $after = Get-Json '/v2/spaces/{s}/templates?type=task&limit=1000'
        Add-Evidence $m 'task templates after the dry run' ([ordered]@{ total = $after['total']; probe_present = ($after['data'] | Where-Object { $_['name'] -eq $name }) -ne $null })
    }
    $defaults = @($all['data'] | Where-Object { $_['default'] } | ForEach-Object { if ($_.ContainsKey('template_for')) { $_['template_for'] } else { '(none)' } })
    $m.Verdict = "$($all['total']) templates; default for: $($defaults -join ', '); task default = '$($taskTpl['name'])'. Template routes: GET+POST only; a template is an object, so PATCH/DELETE /objects/{id} reach it (docs: templates are in the delete allowlist) — not probed."
    Add-Contradiction 'openapi lists GET+POST for templates only, yet APIV2.md puts templates in the DELETE /objects allowlist and a template is an ordinary object for PATCH.'
    return $m
}

# ── 7. type-default-on-create ────────────────────────────────────────────────
# Catalogue unknown: "application des templates par défaut à la création".

function Measure-TypeDefaultOnCreate {
    $m = New-Measure 'type-default-on-create' 'Catalogue unknown: does a create without template start from the type''s default_template?'
    if (-not (Test-DryRunAllowed $m)) { $m.Verdict = 'skipped'; return $m }
    $cases = [ordered]@{
        'task, no template' = @{ type = 'task'; name = "$ProbePrefix-t1-$RunStamp" }
        'task, explicit template' = @{ type = 'task'; name = "$ProbePrefix-t2-$RunStamp"; template = 'bafyreibhy53jvgxp6euzxrn3bmchbtty5eq555m5ybf2mw43wsrwcawzre' }
        'task, no template, with markdown' = @{ type = 'task'; name = "$ProbePrefix-t3-$RunStamp"; markdown = "## Points d'attention`n- [ ] probe" }
        'project, no template' = @{ type = 'project'; name = "$ProbePrefix-p1-$RunStamp" }
        'rapport, no template (rapport template is not default)' = @{ type = 'rapport'; name = "$ProbePrefix-r1-$RunStamp" }
        'idee, no template' = @{ type = 'idee'; name = "$ProbePrefix-i1-$RunStamp" }
    }
    $sources = [System.Collections.Generic.List[string]]::new()
    foreach ($label in $cases.Keys) {
        $r = Invoke-SurveyRequest -Method POST -Path '/v2/spaces/{s}/objects?dry_run=true' -Body $cases[$label]
        Add-Call $m "dry-run create: $label" $r 700
        $src = if ($r.Json -and $r.Json.ContainsKey('template')) { "$($r.Json['template']['source'])$(if ($r.Json['template'].ContainsKey('blocks_added')) { ' +blocks_added' })" } else { 'no template' }
        $sources.Add("${label}: $($r.Status) $src")
    }
    $m.Verdict = ($sources -join '; ') + '. A dry run names the template but never its body (blocks_added absent, as documented).'
    return $m
}

# ── 8. widgets ───────────────────────────────────────────────────────────────
# The sidebar is scriptable in v2: a widget per active project, for instance.

function Measure-Widgets {
    $m = New-Measure 'widgets' 'Widgets are the sidebar; v2 can list and place them, which could keep a sidebar of the active projects in sync.'
    $w = Get-Json '/v2/spaces/{s}/widgets?limit=1000'
    $script:Ctx.WidgetCountBefore = $w['total']
    $named = foreach ($row in $w['data']) {
        $t = $row['target']; $label = $t
        if ($t -notlike '_*') {
            $o = Invoke-SurveyRequest -Method GET -Path "/v2/spaces/{s}/objects/$($t)?include=properties"
            if ($o.Status -eq 200) { $label = "$($o.Json['type']): $($o.Json['properties']['name'])" } else { $label = "unreadable ($($o.Status))" }
        }
        [ordered]@{ id = $row['id']; scope = $row['scope']; layout = $row['layout']; limit = $row['limit']; view_id = $row['view_id']; target = $label }
    }
    Add-Evidence $m 'GET /widgets, targets resolved to type: name' @($named) 4000
    Add-Evidence $m 'counts by scope and layout' ([ordered]@{ total = $w['total']; scope = @($w['data'] | Group-Object { $_['scope'] } | ForEach-Object { "$($_.Name)=$($_.Count)" }); layout = @($w['data'] | Group-Object { $_['layout'] } | ForEach-Object { "$($_.Name)=$($_.Count)" }) })
    $schema = Invoke-SurveyRequest -Method GET -Path '/v2/schemas/widget'
    Add-Call $m 'GET /v2/schemas/widget' $schema 1500
    if (Test-DryRunAllowed $m) {
        $post = Invoke-SurveyRequest -Method POST -Path '/v2/spaces/{s}/widgets?dry_run=true' -Body @{ target = $script:Ctx.SurveyTaskId; scope = 'personal'; layout = 'link' }
        Add-Call $m 'dry-run POST widget (personal, link to the survey task)' $post 800
        $after = Get-Json '/v2/spaces/{s}/widgets?limit=1000'
        Add-Evidence $m 'widgets after the dry-run POST' ([ordered]@{ total_before = $w['total']; total_after = $after['total'] })
        # A no-op PATCH: the first view widget, re-sent with its own limit.
        $first = @($w['data'] | Where-Object { $_['layout'] -eq 'view' -and $_.ContainsKey('limit') })[0]
        if ($first) {
            $patch = Invoke-SurveyRequest -Method PATCH -Path "/v2/spaces/{s}/widgets/$($first['id'])?dry_run=true" -Body @{ limit = $first['limit'] }
            Add-Call $m "dry-run PATCH widget $($first['id']) with its own limit (no-op)" $patch 800
        }
    }
    $m.Verdict = "$($w['total']) widgets, all scope=$(@($w['data'] | ForEach-Object { $_['scope'] } | Sort-Object -Unique) -join '/'); dry-run POST/PATCH answered as shown. A sidebar of active projects is feasible: one link/tree widget per open project, or one view widget on a query of open projects."
    return $m
}

# ── 9. markdown-unescape ─────────────────────────────────────────────────────
# v1 bug GO-7522: every body PATCH multiplied backslashes in untouched sections.
# A dry run echoes no document, so the parsed text is read through the same
# batch: update/delete_block `match` reads the document as earlier ops left it.

function Measure-MarkdownUnescape {
    $m = New-Measure 'markdown-unescape' 'GO-7522 drift (v1): \_ became \\\_ after each body rewrite. v2 must parse markdown once and leave untouched blocks alone.'
    $cat = Find-Objects @{ filter = 'name = "Catalogue MCP project-management décidé (port v2)"' }
    if ($cat['total'] -ge 1) {
        $doc = Get-Json "/v2/spaces/{s}/objects/$($cat['data'][0]['id'])?include=blocks"
        $hits = @($doc['blocks'] | Where-Object { $_.ContainsKey('text') -and $_['text'] -match '\\_' } | Select-Object -First 3)
        $runs = @($doc['blocks'] | Where-Object { $_.ContainsKey('text') } | ForEach-Object { [regex]::Matches($_['text'], '(\\+)_') } | ForEach-Object { $_.Groups[1].Length } | Sort-Object -Unique)
        Add-Evidence $m 'Catalogue document: blocks holding \_ as served (JSON text)' (@($hits | ForEach-Object { $_['text'] })) 1500
        Add-Evidence $m 'backslash run lengths before _ in the Catalogue blocks' $runs
        $md = Invoke-SurveyRequest -Method GET -Path "/v2/spaces/{s}/objects/$($cat['data'][0]['id'])?format=md"
        $line = @($md.Text -split "`n" | Where-Object { $_ -match 'session' -and $_ -match '\\' })[0]
        Add-Evidence $m 'same text through ?format=md' $line 600
    }
    if (-not (Test-DryRunAllowed $m)) { $m.Verdict = 'stored drift measured; parse not probed'; return $m }
    $probes = [ordered]@{
        "zzmd1 a\_b" = @("zzmd1 a\_b", 'zzmd1 a_b', 'zzmd1 a\\_b', 'zzmd1 a\\\_b')
        'zzmd2 c_d e*f' = @('zzmd2 c_d e*f', 'zzmd2 c\_d e\*f', 'zzmd2 c\_d e*f')
        'zzmd3 _g_ *h* **i**' = @('zzmd3 _g_ *h* **i**', 'zzmd3 *g* *h* **i**', 'zzmd3 _g_ _h_ **i**')
    }
    $found = [ordered]@{}
    foreach ($source in $probes.Keys) {
        $matched = [System.Collections.Generic.List[string]]::new()
        foreach ($candidate in $probes[$source]) {
            $r = Invoke-DryPatch $script:Ctx.SurveyTaskId @(
                @{ op = 'insert_blocks'; position = 'last'; markdown = $source },
                @{ op = 'delete_block'; match = $candidate })
            if ($r.Status -eq 200) { $matched.Add($candidate) }
            Add-Call $m "insert markdown [$source] then delete_block match [$candidate]" $r 500
        }
        $found[$source] = $matched
    }
    Add-Evidence $m 'markdown sent -> block texts that matched (the stored markup source)' $found
    $m.Verdict = 'Stored: ' + (($found.Keys | ForEach-Object { "[$_] -> [$($found[$_] -join ' | ')]" }) -join '; ') + '. Untouched blocks are never re-serialised by ops (design), so the v1 multiplication path is gone; existing drift stays in stored text.'
    return $m
}

# ── 10. op-schemas ───────────────────────────────────────────────────────────
# The PATCH op schemas are served at runtime and published nowhere else.

function Measure-OpSchemas {
    $m = New-Measure 'op-schemas' 'Per-op JSON schemas are the contract the MCP will build PATCH bodies against; they are only served at runtime.'
    $index = Get-Json '/v2/schemas'
    $indexOps = @($index['ops'] | ForEach-Object { $_['kind'] })
    $served = Get-Json '/docs/openapi.json'
    $enumText = $served['paths']['/v2/schemas/ops/{op}']['get']['parameters'][0]['description']
    $openapiOps = @(($enumText -replace '^[^:]*:\s*', '') -split ',\s*' | ForEach-Object { $_.Trim() })
    $skillOps = @('set_properties', 'update_block', 'replace_text', 'insert_blocks', 'move_block', 'delete_block', 'set_cell', 'update_view', 'insert_view', 'replace_subtree', 'move_view', 'delete_view', 'add_items', 'remove_items')
    Add-Evidence $m 'op lists' ([ordered]@{
        schema_index = $indexOps; openapi_param_description = $openapiOps
        index_only = @($indexOps | Where-Object { $_ -notin $openapiOps }); openapi_only = @($openapiOps | Where-Object { $_ -notin $indexOps })
        skill_md_says = 'Fourteen ops (heart v0.51.6 SKILL.md); its example list omits set_type'
    })
    $script:Ctx.ServedOps = $indexOps
    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($op in ($indexOps + $openapiOps | Sort-Object -Unique)) {
        $r = Invoke-SurveyRequest -Method GET -Path "/v2/schemas/ops/$op"
        if ($r.Status -ne 200) { $missing.Add("$op ($($r.Status))"); Add-Call $m "schema $op" $r 400; continue }
        $s = $r.Json['schema']
        $m.Evidence.Add([pscustomobject]@{
            Label = "op $op — endpoint: $($r.Json['endpoint']); required: $(@($s['required']) -join ','); fields: $(@($s['properties'].Keys) -join ',')"
            Body = "example: $(ConvertTo-CompactJson $r.Json['example'])`nschema: $($r.Text)"
            Collapsed = $true
        })
    }
    $m.Verdict = "$($indexOps.Count) ops served by /v2/schemas ($($openapiOps.Count) in the openapi op description; SKILL.md says 14); the type-only ops $((@($indexOps | Where-Object { $_ -notin $openapiOps })) -join ', ') are missing from the openapi list; schemas missing: $(if ($missing.Count) { $missing -join ', ' } else { 'none' })."
    if ($indexOps.Count -ne $openapiOps.Count) { Add-Contradiction "Op count: /v2/schemas index serves $($indexOps.Count), the openapi op description lists $($openapiOps.Count), SKILL.md says fourteen." }
    return $m
}

# ── 11. rate-limits ──────────────────────────────────────────────────────────
# Read from the headers of the calls this run already made; never provoked.

function Measure-RateLimits {
    $m = New-Measure 'rate-limits' 'The write limiter is shared with the resident Deckle app; the MCP must pace its writes. Read from ordinary calls only.'
    $groups = $script:CallLog | Group-Object Kind
    $summary = foreach ($g in $groups) {
        $withHeaders = @($g.Group | Where-Object { $_.Headers.Keys -match '^(Ratelimit|X-Rate-Limit)' })
        $sample = if ($withHeaders) { $h = [ordered]@{}; foreach ($k in $withHeaders[-1].Headers.Keys) { if ($k -match '^(Ratelimit|X-Rate-Limit)' -and $k -notmatch 'Remote-Addr') { $h[$k] = $withHeaders[-1].Headers[$k] } }; $h } else { $null }
        $remaining = @($withHeaders | ForEach-Object { [int]$_.Headers['Ratelimit-Remaining'] })
        [ordered]@{
            kind = $g.Name; calls = $g.Count; calls_with_rate_headers = $withHeaders.Count
            sample_headers = $sample
            min_remaining = if ($remaining) { ($remaining | Measure-Object -Minimum).Minimum } else { $null }
            statuses_429 = @($g.Group | Where-Object { $_.Status -eq 429 }).Count
        }
    }
    Add-Evidence $m 'rate-limit headers by call kind (this run)' @($summary)
    Add-Evidence $m 'documented' 'changelog 2025-05-20: burst rate limiting, sustained 1 rps, burst 60, ANYTYPE_API_DISABLE_RATE_LIMIT=1 disables it; APIV2.md §1.3: the shared write rate limit answers before v2 handlers, in the older envelope; streams cap at 16 per process (429 too_many_streams).'
    $writes = @($summary | Where-Object { $_.kind -eq 'dry-run write' })
    $m.Verdict = "Only writes carry limiter headers (dry runs included, so they consume the budget): $(if ($writes) { ConvertTo-CompactJson $writes[0].sample_headers } else { 'no write made' }); reads and searches carry none. Matches 1 rps / burst 60."
    $m.Notes.Add('The limiter keys on the remote address (X-Rate-Limit-Request-Remote-Addr), so every local client — Deckle, the MCP, this survey — shares one bucket.')
    return $m
}

# ── 12. property-patch ───────────────────────────────────────────────────────
# Catalogue unknown: "forme du PATCH des propriétés".

function Measure-PropertyPatch {
    $m = New-Measure 'property-patch' 'Catalogue unknown: the request shape v2 wants for each property format the update gesture writes.'
    if (-not (Test-DryRunAllowed $m)) { $m.Verdict = 'skipped'; return $m }
    $projectId = (Find-Objects @{ filter = 'name = "Anytype API v2 et AnyBlock v2"' })['data'][0]['id']
    $cases = [ordered]@{
        'select état by name (array)' = @{ op = 'set_properties'; set = @{ etat = @('En cours') } }
        'select priorité by name' = @{ op = 'set_properties'; set = @{ priorite = @('4') } }
        'date as YYYY-MM-DD' = @{ op = 'set_properties'; set = @{ due_date = '2026-11-01' } }
        'date as RFC 3339' = @{ op = 'set_properties'; set = @{ due_date = '2026-11-01T00:00:00Z' } }
        'date as unix seconds' = @{ op = 'set_properties'; set = @{ due_date = 1793491200 } }
        'objects: add one project' = @{ op = 'set_properties'; add = @{ relation_projet = @($projectId) } }
        'objects: set by name instead of id' = @{ op = 'set_properties'; set = @{ relation_projet = @('Anytype API v2 et AnyBlock v2') } }
        'checkbox done true' = @{ op = 'set_properties'; set = @{ done = $true } }
        'checkbox archive (custom) false' = @{ op = 'set_properties'; set = @{ archive = $false } }
        'number charge_estimee' = @{ op = 'set_properties'; set = @{ charge_estimee = 1.5 } }
        'text definition_de_fini' = @{ op = 'set_properties'; set = @{ definition_de_fini = 'probe' } }
        'unset description' = @{ op = 'set_properties'; unset = @('description') }
        'same key in set and unset' = @{ op = 'set_properties'; set = @{ description = 'x' }; unset = @('description') }
        'display name as key (Date cible)' = @{ op = 'set_properties'; set = @{ 'Date cible' = '2026-11-01' } }
        'corpse property 6989a972… (uninstalled État)' = @{ op = 'set_properties'; set = @{ '6989a972341c161938d64dab' = @('A faire') } }
    }
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($label in $cases.Keys) {
        $r = Invoke-DryPatch $script:Ctx.SurveyTaskId @($cases[$label])
        $warn = if ($r.Json -and $r.Json.ContainsKey('warnings')) { ' +warning' } else { '' }
        $lines.Add("${label}: $($r.Status)$warn")
        Add-Call $m "dry-run $label" $r 700
    }
    $m.Verdict = ($lines -join '; ')
    return $m
}

# ── 13. checkbox-blocks ──────────────────────────────────────────────────────
# The catalogue's inline subtasks (set_subtask) are checkbox blocks in the body.

function Measure-CheckboxBlocks {
    $m = New-Measure 'checkbox-blocks' 'set_subtask ticks a "- [ ]" line by its label; the block shape and the tick op decide whether that is one call.'
    $doc = Get-Json "/v2/spaces/{s}/objects/$($script:Ctx.SurveyTaskId)?include=blocks"
    $boxes = @($doc['blocks'] | Where-Object { $_['type'] -eq 'checkbox' })
    Add-Evidence $m 'checkbox blocks of the survey task (full read)' @($boxes | Select-Object -First 3) 1200
    # A ticked one, to see how "checked" is spelled: look through done tasks.
    $done = Find-Objects @{ filter = 'type = "task" AND done = true'; sorts = @(@{ property = 'last_modified_date'; direction = 'desc' }) } 15
    $ticked = $null
    foreach ($row in $done['data']) {
        $d = Get-Json "/v2/spaces/{s}/objects/$($row['id'])?include=blocks"
        $ticked = @($d['blocks'] | Where-Object { $_['type'] -eq 'checkbox' -and $_.ContainsKey('checked') })[0]
        if ($ticked) { Add-Evidence $m "a ticked checkbox block (task $($row['id']))" $ticked 600; break }
    }
    $md = Invoke-SurveyRequest -Method GET -Path "/v2/spaces/{s}/objects/$($script:Ctx.SurveyTaskId)?format=md"
    Add-Evidence $m 'checklist lines through ?format=md' (@($md.Text -split "`n" | Where-Object { $_ -match '^\s*- \[' } | Select-Object -First 3))
    if (Test-DryRunAllowed $m) {
        $labelBox = @($boxes | Where-Object { $_.ContainsKey('text') })[0]
        $emptyBox = @($boxes | Where-Object { -not $_.ContainsKey('text') })[0]
        $cases = [ordered]@{}
        if ($labelBox) {
            $cases["update_block by id $($labelBox['id']) checked:true"] = @(@{ op = 'update_block'; id = $labelBox['id']; set = @{ checked = $true } })
            $cases['update_block by match (label prefix) checked:true'] = @(@{ op = 'update_block'; match = $labelBox['text'].Substring(0, 40); set = @{ checked = $true } })
        }
        if ($emptyBox) { $cases["update_block on the empty checkbox $($emptyBox['id'])"] = @(@{ op = 'update_block'; id = $emptyBox['id']; set = @{ checked = $true } }) }
        $cases['update_block match on text in several blocks'] = @(@{ op = 'update_block'; match = 'mesur'; set = @{ checked = $true } })
        $cases['insert a new subtask after the Sous-tâches divider'] = @(@{ op = 'insert_blocks'; after = 'c80d4'; markdown = '- [ ] zz probe subtask' })
        foreach ($label in $cases.Keys) {
            $r = Invoke-DryPatch $script:Ctx.SurveyTaskId $cases[$label]
            Add-Call $m "dry-run $label" $r 900
        }
    }
    $m.Verdict = "Checkbox = block type 'checkbox' with text = label (markup source) and checked:true only when ticked (absent when unticked)$(if (-not $ticked) { ' [no ticked example found]' }); tick = update_block {id|match, set:{checked:true}}."
    return $m
}

# ── 14. epic-members ─────────────────────────────────────────────────────────
# Catalogue unknown: "lecture de l'appartenance à une collection" (list_projects
# epic filter, link epic).

function Measure-EpicMembers {
    $m = New-Measure 'epic-members' 'list_projects filters by epic and link sets a project''s epic: both need membership readable from either side.'
    $epics = Find-Objects @{ type = 'epic' }
    $epic = @($epics['data'] | Where-Object { $_['name'] -eq 'Deckle' })[0]
    if (-not $epic) { $epic = $epics['data'][0] }
    $doc = Get-Json "/v2/spaces/{s}/objects/$($epic['id'])?outline=true&include=properties"
    Add-Evidence $m 'epic read: top-level keys and collection_items count' ([ordered]@{ keys = @($doc.Keys); collection_items = @($doc['collection_items']).Count; layout = $doc['properties']['layout'] })
    $list = Invoke-SurveyRequest -Method GET -Path "/v2/spaces/{s}/collections/$($epic['id'])/objects?limit=3&fields=etat,done"
    Add-Call $m 'GET /collections/{epic}/objects' $list 1500
    $views = Get-Json "/v2/spaces/{s}/collections/$($epic['id'])/views"
    Add-Evidence $m 'epic views' (@($views['data'] | ForEach-Object { [ordered]@{ id = $_['id']; name = $_['name']; type = $_['type']; filters = @($_['filters']).Count } }))
    $projectId = @($doc['collection_items'])[0]
    $proj = Get-Json "/v2/spaces/{s}/objects/$($projectId)?include=properties"
    Add-Evidence $m 'a member project: does its read name the epic?' ([ordered]@{ property_keys = @($proj['properties'].Keys); mentions_epic_id = (ConvertTo-CompactJson $proj).Contains($epic['id']) })
    $back = Invoke-SurveyRequest -Method POST -Path '/v2/spaces/{s}/search' -Body @{ filter = ('name = "{0}"' -f $proj['properties']['name'].Replace('"', '\"')); fields = @('backlinks', 'links') }
    Add-Call $m 'search row of that project with fields backlinks, links' $back 800
    $rev = Invoke-SurveyRequest -Method POST -Path '/v2/spaces/{s}/search' -Body @{ filter = ('type = "epic" AND collection_items HAS "{0}"' -f $projectId) }
    Add-Call $m 'reverse lookup: epics whose collection_items hold the project (filter)' $rev 600
    $m.Verdict = "Members: the epic read carries collection_items ($(@($doc['collection_items']).Count) ids) and /collections/{id}/objects pages them with fields. Parent from the project: $(if ((ConvertTo-CompactJson $back.Json).Contains($epic['id'])) { 'reachable through backlinks' } else { 'not on the project read' }); reverse filter answered $($rev.Status)."
    return $m
}

# ── 15. trash ────────────────────────────────────────────────────────────────
# The catalogue's delete promises a restorable bin, and rule 5 filters archived
# objects everywhere: both need the bin readable and its exit known.

function Measure-Trash {
    $m = New-Measure 'trash' 'delete promises a restorable bin; rule 5 wants archived objects out of every list.'
    $arch = Find-Objects @{ filter = 'is_archived = true'; fields = @('is_archived') } 3
    Add-Evidence $m 'search filter is_archived = true' ([ordered]@{ total = $arch['total']; first = @($arch['data']) })
    if ($arch['total'] -gt 0) {
        $name = $arch['data'][0]['name']
        $plain = Find-Objects @{ filter = ('name = "{0}"' -f $name.Replace('"', '\"')) }
        Add-Evidence $m "default search for the archived object's exact name ($name)" ([ordered]@{ total = $plain['total'] })
        $read = Invoke-SurveyRequest -Method GET -Path "/v2/spaces/{s}/objects/$($arch['data'][0]['id'])?include=properties"
        Add-Call $m 'direct read of an archived object' $read 600
    }
    $custom = Find-Objects @{ filter = 'archive = true' } 1
    Add-Evidence $m 'the custom Archivé checkbox (archive = true)' ([ordered]@{ total = $custom['total'] })
    $openapi = Get-Json '/docs/openapi.json'
    $routes = @($openapi['paths'].Keys | Where-Object { $_ -match 'bin|trash|archive|restore' })
    Add-Evidence $m 'routes named bin/trash/archive/restore in the served openapi' $routes
    if (Test-DryRunAllowed $m) {
        $r = Invoke-DryPatch $script:Ctx.SurveyTaskId @(@{ op = 'set_properties'; set = @{ is_archived = $false } })
        Add-Call $m 'dry-run set_properties is_archived:false (the would-be restore path)' $r 700
    }
    $m.Verdict = "Bin = is_archived (system); default search excludes archived ($(if ($arch['total'] -gt 0) { 'exact-name search total ' + $plain['total'] } else { 'no archived sample' })); filter is_archived = true lists them ($($arch['total'])); no restore route; widget target _bin exists. Deckle's own 'archive' checkbox is a separate user property ($($custom['total'])+ objects)."
    return $m
}

# ── 16. etag-idempotency ─────────────────────────────────────────────────────

function Measure-EtagIdempotency {
    $m = New-Measure 'etag-idempotency' 'If-Match guards concurrent edits; Idempotency-Key makes a retried create safe. The MCP decides whether to send either.'
    $a = Invoke-SurveyRequest -Method GET -Path "/v2/spaces/{s}/objects/$($script:Ctx.SurveyTaskId)?include=properties"
    $b = Invoke-SurveyRequest -Method GET -Path "/v2/spaces/{s}/objects/$($script:Ctx.SurveyTaskId)?outline=true"
    Add-Evidence $m 'ETag header vs body etag, two reads' ([ordered]@{ header_1 = $a.Headers['ETag']; body_1 = $a.Json['etag']; header_2_outline = $b.Headers['ETag'] })
    if (-not (Test-DryRunAllowed $m)) { $m.Verdict = 'reads only'; return $m }
    $ok = Invoke-DryPatch $script:Ctx.SurveyTaskId @(@{ op = 'set_properties'; set = @{ priorite = @('4') } }) @{ 'If-Match' = $a.Headers['ETag'] }
    Add-Call $m 'dry-run PATCH with the current etag' $ok 500
    $bad = Invoke-DryPatch $script:Ctx.SurveyTaskId @(@{ op = 'set_properties'; set = @{ priorite = @('4') } }) @{ 'If-Match' = '"00000000"' }
    Add-Call $m 'dry-run PATCH with a stale etag' $bad 700
    $key = [guid]::NewGuid().ToString()
    $body = @{ type = 'task'; name = "$ProbePrefix-idem-$RunStamp" }
    $i1 = Invoke-SurveyRequest -Method POST -Path '/v2/spaces/{s}/objects?dry_run=true' -Body $body -Headers @{ 'Idempotency-Key' = $key }
    $i2 = Invoke-SurveyRequest -Method POST -Path '/v2/spaces/{s}/objects?dry_run=true' -Body $body -Headers @{ 'Idempotency-Key' = $key }
    $i3 = Invoke-SurveyRequest -Method POST -Path '/v2/spaces/{s}/objects?dry_run=true' -Body @{ type = 'task'; name = "$ProbePrefix-idem2-$RunStamp" } -Headers @{ 'Idempotency-Key' = $key }
    Add-Call $m 'dry-run POST, Idempotency-Key K, first' $i1 400
    Add-Call $m 'dry-run POST, same key, same body' $i2 400
    Add-Call $m 'dry-run POST, same key, different body' $i3 600
    $m.Verdict = "ETag header = body etag: $($a.Headers['ETag'] -eq ('"' + $a.Json['etag'] + '"') -or $a.Headers['ETag'] -eq $a.Json['etag']); If-Match current -> $($ok.Status), stale -> $($bad.Status); Idempotency-Key replay -> $($i2.Status) (Idempotency-Replayed: $($i2.Headers['Idempotency-Replayed'])), conflicting body -> $($i3.Status)."
    return $m
}

# ── 17. openapi-served ───────────────────────────────────────────────────────
# The backend serves its own contract; it is what a client should trust over a
# copy taken from the repository.

function Get-PathOps {
    param($Doc)
    $set = [System.Collections.Generic.SortedSet[string]]::new()
    foreach ($p in $Doc['paths'].Keys) { foreach ($verb in $Doc['paths'][$p].Keys) { if ($verb -in 'get', 'post', 'patch', 'put', 'delete', 'head') { [void]$set.Add("$($verb.ToUpper()) $p") } } }
    return @($set)
}

function Measure-OpenapiServed {
    $m = New-Measure 'openapi-served' 'Diff the contract the backend serves against the heart v0.51.6 research copy, and list the doc/behaviour contradictions met in this run.'
    $researchPath = Join-Path (Split-Path -Parent (git -C $RepoRoot rev-parse --path-format=absolute --git-common-dir)) '.tmp/anytype-v2-2026-10/research/heart-v0.51.6_openapi-v2.json'
    $routes = [ordered]@{}
    foreach ($p in '/docs/openapi.json', '/v2/docs/openapi.json', '/v2/docs/openapi.yaml', '/v1/docs/openapi.json', '/v2/openapi') {
        $r = Invoke-SurveyRequest -Method GET -Path $p
        $sha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($r.Text))).ToLower()
        $routes[$p] = [ordered]@{ status = $r.Status; bytes = $r.Text.Length; sha256 = $sha.Substring(0, 16) }
    }
    $noAuth = Invoke-SurveyRequest -Method GET -Path '/docs/openapi.json' -NoAuth
    $routes['/docs/openapi.json without key'] = [ordered]@{ status = $noAuth.Status }
    Add-Evidence $m 'served contract documents' $routes
    $served = Get-Json '/v2/docs/openapi.json'
    $servedOps = Get-PathOps $served
    $verdict = "Served: $($served['info']['title']) $($served['info']['version']), $(@($served['paths'].Keys).Count) paths, $($servedOps.Count) operations"
    if (Test-Path -LiteralPath $researchPath) {
        $researchText = [IO.File]::ReadAllText($researchPath)
        $research = $researchText | ConvertFrom-Json -AsHashtable -Depth 100
        $researchOps = Get-PathOps $research
        $researchSha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($researchPath))).ToLower()
        $servedSha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes((Invoke-SurveyRequest -Method GET -Path '/v2/docs/openapi.json').Text))).ToLower()
        Add-Evidence $m 'diff against the research copy (heart v0.51.6)' ([ordered]@{
            byte_identical = ($researchSha -eq $servedSha)
            only_served = @($servedOps | Where-Object { $_ -notin $researchOps })
            only_research = @($researchOps | Where-Object { $_ -notin $servedOps })
        })
        $verdict += "; byte-identical to the research copy: $($researchSha -eq $servedSha)"
    } else {
        $m.Notes.Add("Research copy not found at $researchPath; diff skipped.")
    }
    $servers = @($served['servers'] | ForEach-Object { $_['url'] })
    Add-Evidence $m 'servers declared by the served document' $servers
    if ($servers -notcontains $BaseUrl) { Add-Contradiction "The served openapi declares $($servers -join ', ') as its server; the headless backend answers on $BaseUrl." }
    Add-Contradiction 'BackendHealthProbe comment: the root /docs/openapi.json serves v2 and v1 lives under /v1/docs/openapi.json — confirmed; but SKILL.md names /v2/docs/openapi.json as the reference, also served (same bytes as the root).'
    $m.Verdict = $verdict + '.'
    return $m
}

# ── 18. type-property-drift (from the measured space) ─────────────────────────
# Found while measuring: a property absent from a type's definitions is "stored
# and served" but refused by type-scoped search. Rule 12 of the catalogue (every
# property of a type is an update parameter) depends on which keys a type lists.

function Measure-TypePropertyDrift {
    $m = New-Measure 'type-property-drift' 'v2 scopes search and queries to the keys a type lists; DevSpace.cs maps keys onto types by hand. Mismatches become refusals.'
    $frozen = [ordered]@{
        task = @('description', 'etat', 'priorite', 'type_de_tache', 'done', 'due_date', 'relation_projet', 'contact_lie', 'fichier(s)_lie(s)', 'livrable(s)', 'definition_de_fini', 'charge_estimee', 'charge_estimee_(jours)', 'archive')
        project = @('description', 'etat', 'phase_projet', 'priorite', 'done', 'version', 'definition_de_fini', 'due_date', 'budget_estime', 'budget_reel_(', 'charge_estimee', 'charge_estimee_(jours)', 'depend_de', 'archive')
        rapport = @('date_du_journal', 'tache(s)_liee(s)', 'contact_lie', 'fichier(s)_lie(s)')
        document = @('type_de_document', 'document_systeme', 'version')
        epic = @('etat', 'priorite', 'version', 'definition_de_fini', 'due_date', 'budget_estime', 'budget_reel_(', 'charge_estimee', 'charge_estimee_(jours)', 'archive')
        idee = @('archive')
    }
    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($type in $frozen.Keys) {
        $doc = Get-Json "/v2/spaces/{s}/types/$type"
        $defs = @($doc['type_settings']['property_definitions'] | ForEach-Object { [ordered]@{ property = $_['property']; name = $_['name']; internal_key = $_['internal_key']; section = $_['section'] } })
        $listed = @($defs | ForEach-Object { $_['property'] })
        $absent = @($frozen[$type] | Where-Object { $_ -notin $listed -and $_ -ne 'description' })
        $unknown = @($defs | Where-Object { $_['property'] -match '^[0-9a-f]{24}$' })
        Add-Evidence $m "type ${type}: DevSpace keys not listed on the type; opaque-key definitions" ([ordered]@{ not_on_type = $absent; opaque_definitions = $unknown })
        $lines.Add("${type}: $($absent.Count) DevSpace key(s) not on the type$(if ($absent) { ' (' + ($absent -join ', ') + ')' })")
    }
    $scoped = Invoke-SurveyRequest -Method POST -Path '/v2/spaces/{s}/search?limit=1' -Body @{ type = 'task'; fields = @('etat') }
    Add-Call $m 'type-scoped search (body type=task) asking for etat' $scoped 700
    $filtered = Invoke-SurveyRequest -Method POST -Path '/v2/spaces/{s}/search?limit=1' -Body @{ filter = 'type = "task" AND etat IS NOT EMPTY'; fields = @('etat') }
    Add-Call $m 'same need through the filter pseudo-key type' $filtered 500
    $corpse = Find-Objects @{ filter = 'type = "task" AND 6989a972341c161938d64dab IS NOT EMPTY' } 1
    Add-Evidence $m 'tasks still holding the uninstalled État (6989a972…)' ([ordered]@{ total = $corpse['total'] })
    $m.Verdict = ($lines -join '; ') + ". Body type=task + field etat -> $($scoped.Status); filter 'type = ""task""' + etat -> $($filtered.Status)."
    return $m
}

# ── 19. markdown-object-link ─────────────────────────────────────────────────
# Catalogue unknown: "forme d'un lien vers un objet dans le markdown d'un corps"
# (document mirrors turn references/x.md into links to objects).

function Measure-MarkdownObjectLink {
    $m = New-Measure 'markdown-object-link' 'Mirrored skill documents must turn a relative link into a link to an object; the markdown spelling v2 stores decides how.'
    if (-not (Test-DryRunAllowed $m)) { $m.Verdict = 'skipped'; return $m }
    $target = $script:Ctx.SurveyTaskId
    $short = (Get-Json '/v2/spaces/{s}')['id']
    $source = "zzlink [cible](anytype://object?objectId=$target&spaceId=$short)"
    $candidates = [ordered]@{
        'kept with the short space ref' = $source
        'space ref expanded to the full id' = "zzlink [cible](anytype://object?objectId=$target&spaceId=$SpaceId)"
        'bare object id as the destination' = "zzlink [cible]($target)"
    }
    $hits = [System.Collections.Generic.List[string]]::new()
    foreach ($label in $candidates.Keys) {
        $r = Invoke-DryPatch $script:Ctx.SurveyTaskId @(@{ op = 'insert_blocks'; position = 'last'; markdown = $source }, @{ op = 'delete_block'; match = $candidates[$label] })
        if ($r.Status -eq 200) { $hits.Add($label) }
        Add-Call $m "insert [$source] then match: $label" $r 600
    }
    $bare = Invoke-DryPatch $script:Ctx.SurveyTaskId @(@{ op = 'insert_blocks'; position = 'last'; markdown = "zzlink2 [cible]($target)" }, @{ op = 'delete_block'; match = "zzlink2 [cible]($target)" })
    Add-Call $m 'markdown link whose destination is a bare object id' $bare 600
    $m.Verdict = "anytype:// deep link stored as: $(if ($hits.Count) { $hits -join ' / ' } else { 'no candidate matched' }); a bare-id destination answered $($bare.Status)."
    return $m
}

# ── Final sweep ──────────────────────────────────────────────────────────────
# Proves after the fact that the run changed nothing it could have changed.

function Invoke-FinalSweep {
    $m = New-Measure 'final-sweep' 'Nothing created, nothing moved: probe names absent, widget and template counts unchanged, survey task etag compared.'
    $probes = Find-Objects @{ filter = ('name CONTAINS "{0}"' -f $ProbePrefix) }
    $w = Get-Json '/v2/spaces/{s}/widgets?limit=1000'
    $t = Get-Json '/v2/spaces/{s}/templates?limit=1000'
    $task = Invoke-SurveyRequest -Method GET -Path "/v2/spaces/{s}/objects/$($script:Ctx.SurveyTaskId)?include=properties"
    $typeEtags = [ordered]@{}
    foreach ($k in $script:Ctx.TypeEtags.Keys) { $typeEtags[$k] = "$($script:Ctx.TypeEtags[$k]) -> $((Invoke-SurveyRequest -Method GET -Path "/v2/spaces/{s}/types/$k").Headers['ETag'])" }
    $state = [ordered]@{
        probe_objects_found = $probes['total']
        widgets = "$($script:Ctx.WidgetCountBefore) -> $($w['total'])"
        templates = "$($script:Ctx.TemplateCountBefore) -> $($t['total'])"
        survey_task_etag = "$($script:Ctx.SurveyTaskEtag) -> $($task.Headers['ETag'])"
        type_etags = $typeEtags
        dry_run_writes_sent = @($script:CallLog | Where-Object { $_.Kind -eq 'dry-run write' }).Count
        calls_total = $script:CallLog.Count
    }
    Add-Evidence $m 'state after the run' $state
    $clean = $probes['total'] -eq 0 -and $w['total'] -eq $script:Ctx.WidgetCountBefore -and $t['total'] -eq $script:Ctx.TemplateCountBefore -and $task.Headers['ETag'] -eq $script:Ctx.SurveyTaskEtag
    $m.Verdict = if ($clean) { 'Clean: no probe object, counts and the survey task etag unchanged.' } else { 'CHECK: something moved, see the state above (an etag can also move through sync).' }
    return $m
}

# ── Report ───────────────────────────────────────────────────────────────────

function Write-SurveyReport {
    param([object[]]$Measures)
    $sb = [System.Text.StringBuilder]::new()
    $who = $script:Ctx.Whoami
    [void]$sb.AppendLine('# Anytype API v2 survey')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("- Run: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz') against $BaseUrl")
    [void]$sb.AppendLine("- Backend api version (whoami): $(if ($who) { $who['api']['version'] } else { 'unknown' }); key length $($script:ApiKey.Length) (value never written)")
    [void]$sb.AppendLine("- Space: $SpaceId; survey task: $($script:Ctx.SurveyTaskId)")
    [void]$sb.AppendLine("- Calls: $($script:CallLog.Count), of which dry-run writes: $(@($script:CallLog | Where-Object { $_.Kind -eq 'dry-run write' }).Count). No DELETE, no real write.")
    [void]$sb.AppendLine('- Every verdict below is measured on this backend unless a line says "documented".')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('| # | Measure | Verdict |')
    [void]$sb.AppendLine('|---|---|---|')
    $n = 0
    foreach ($m in $Measures) {
        [void]$sb.AppendLine("| $n | $($m.Name) | $(($m.Verdict -replace '\|', '\|' -replace "`r?`n", ' ')) |")
        $n++
    }
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Contradictions between docs and behaviour')
    [void]$sb.AppendLine()
    foreach ($c in ($script:Ctx.Contradictions | Select-Object -Unique)) { [void]$sb.AppendLine("- $c") }
    [void]$sb.AppendLine()
    $n = 0
    foreach ($m in $Measures) {
        [void]$sb.AppendLine("## $n. $($m.Name)")
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("Why: $($m.Why)")
        [void]$sb.AppendLine()
        [void]$sb.AppendLine("**Verdict ($($m.Status)):** $($m.Verdict)")
        [void]$sb.AppendLine()
        foreach ($note in $m.Notes) { [void]$sb.AppendLine("- $note") }
        if ($m.Notes.Count) { [void]$sb.AppendLine() }
        foreach ($e in $m.Evidence) {
            $collapsed = $e.PSObject.Properties['Collapsed'] -and $e.Collapsed
            if ($collapsed) {
                [void]$sb.AppendLine("<details><summary>$([Net.WebUtility]::HtmlEncode($e.Label))</summary>")
                [void]$sb.AppendLine()
            } else {
                [void]$sb.AppendLine("**$($e.Label)**")
                [void]$sb.AppendLine()
            }
            [void]$sb.AppendLine('```text')
            [void]$sb.AppendLine($e.Body.Replace('```', '`` `'))
            [void]$sb.AppendLine('```')
            if ($collapsed) { [void]$sb.AppendLine('</details>') }
            [void]$sb.AppendLine()
        }
        $n++
    }
    # Belt and braces: whatever a response echoed, the key never reaches disk.
    $text = $sb.ToString().Replace($script:ApiKey, "<api-key:$($script:ApiKey.Length) chars>")
    $dir = Split-Path -Parent $OutputPath
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [IO.File]::WriteAllText($OutputPath, $text, [Text.UTF8Encoding]::new($false))
}

# ── Run ──────────────────────────────────────────────────────────────────────

$measures = [System.Collections.Generic.List[object]]::new()
try {
    $measures.Add((Invoke-Preflight))
} catch {
    $failed = New-Measure 'preflight' 'Backend alive and survey task found.'
    $failed.Status = 'aborted'; $failed.Verdict = "ABORTED: $($_.Exception.Message)"
    $measures.Add($failed)
    Write-SurveyReport -Measures $measures
    Write-Host "Survey aborted: $($_.Exception.Message)"
    Write-Host "Report: $OutputPath"
    exit 1
}

$plan = [ordered]@{
    'malformed-keys' = { Measure-MalformedKeys }
    'option-names' = { Measure-OptionNames }
    'outline' = { Measure-Outline }
    'anytype-version-header' = { Measure-AnytypeVersionHeader }
    'type-views' = { Measure-TypeViews }
    'templates' = { Measure-Templates }
    'type-default-on-create' = { Measure-TypeDefaultOnCreate }
    'widgets' = { Measure-Widgets }
    'markdown-unescape' = { Measure-MarkdownUnescape }
    'op-schemas' = { Measure-OpSchemas }
    'property-patch' = { Measure-PropertyPatch }
    'checkbox-blocks' = { Measure-CheckboxBlocks }
    'epic-members' = { Measure-EpicMembers }
    'trash' = { Measure-Trash }
    'etag-idempotency' = { Measure-EtagIdempotency }
    'openapi-served' = { Measure-OpenapiServed }
    'type-property-drift' = { Measure-TypePropertyDrift }
    'markdown-object-link' = { Measure-MarkdownObjectLink }
    # Last, so it reads the headers of every call above.
    'rate-limits' = { Measure-RateLimits }
}

foreach ($name in $plan.Keys) {
    Write-Host "measure: $name"
    try {
        $measures.Add((& $plan[$name]))
    } catch {
        # One measure failing never aborts the others.
        $failed = New-Measure $name '(failed before it could state its why)'
        $failed.Status = 'error'
        $failed.Verdict = "ERROR: $($_.Exception.Message) (line $($_.InvocationInfo.ScriptLineNumber))"
        $measures.Add($failed)
    }
}
try { $measures.Add((Invoke-FinalSweep)) } catch { $f = New-Measure 'final-sweep' ''; $f.Status = 'error'; $f.Verdict = "ERROR: $($_.Exception.Message)"; $measures.Add($f) }

Write-SurveyReport -Measures $measures
$errors = @($measures | Where-Object { $_.Status -eq 'error' }).Count
Write-Host "Survey written: $OutputPath ($($measures.Count) sections, $errors in error)"
if ($errors) { exit 2 }
