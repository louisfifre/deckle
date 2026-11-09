#Requires -Version 7
<#
.SYNOPSIS
Scans the workspace (Claude Code harness, skills, project repos) and writes data.js for the graph page.

.DESCRIPTION
Read-only. Builds nodes (files, skills, MCP servers, groups) and edges (symlinks, @imports,
hook commands, script calls) and tags every node with the prisms (tabs of the page) it appears
in, with its parent group and label for each prism. Three prisms:
  harness - what Claude Code loads, and from where;
  scripts - every script, grouped by repository and folder, with the calls between them;
  copies  - files and skills that exist in several places, with their content versions.
Run it again whenever the workspace changes, then reload index.html.
#>
[CmdletBinding()]
param(
    [string[]]$ProjectRoots = @(Get-ChildItem 'D:\projects' -Directory | ForEach-Object FullName),
    [string]$SkillsRepo = 'D:\skills',
    [string]$Out = (Join-Path $PSScriptRoot 'data.js')
)

$ErrorActionPreference = 'Stop'
$UserClaude = Join-Path $HOME '.claude'
$UserAgents = Join-Path $HOME '.agents'
$ScriptPattern = '\.(ps1|psm1|py|sh|mjs|cmd|bat|ahk)$'
$ExcludedPattern = '(^|/)(tests/fixtures|node_modules|\[mattpocock\])/'

$nodes = [ordered]@{}
$edges = [System.Collections.Generic.List[object]]::new()
$edgeKeys = [System.Collections.Generic.HashSet[string]]::new()

# ---------------------------------------------------------------- helpers

function Get-NodeId([string]$Path) {
    'f:' + ([IO.Path]::GetFullPath($Path).TrimEnd('\').ToLowerInvariant() -replace '\\', '/')
}

function Get-DisplayPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if ($full.StartsWith($HOME, [StringComparison]::OrdinalIgnoreCase)) { return '~' + $full.Substring($HOME.Length) }
    $full
}

function Add-Node {
    param([string]$Id, [string]$Label, [string]$Kind, [string]$Prism, [string]$Parent,
          [string]$Path, [string]$Desc, [string]$PrismLabel, [string]$Variant)
    if (-not $nodes.Contains($Id)) {
        $nodes[$Id] = [ordered]@{ id = $Id; label = $Label; kind = $Kind; path = ''; desc = ''
                                  parents = [ordered]@{}; labels = [ordered]@{}; variant = '' }
    }
    $n = $nodes[$Id]
    if ($Path -and -not $n.path) { $n.path = $Path }
    if ($Desc -and -not $n.desc) { $n.desc = $Desc }
    if ($Prism) {
        $n.parents[$Prism] = $Parent
        if ($PrismLabel) { $n.labels[$Prism] = $PrismLabel }
    }
    if ($Variant) { $n.variant = $Variant }
    $Id
}

function Add-Group([string]$Id, [string]$Label, [string]$Prism, [string]$Parent, [string]$Desc) {
    Add-Node -Id $Id -Label $Label -Kind 'group' -Prism $Prism -Parent $Parent -Desc $Desc
}

function Add-Edge([string]$From, [string]$To, [string]$Kind, [string]$Label) {
    if (-not $From -or -not $To -or $From -eq $To) { return }
    if ($edgeKeys.Add("$From|$To|$Kind")) {
        $edges.Add([ordered]@{ source = $From; target = $To; kind = $Kind; label = $Label })
    }
}

function Get-TextHash([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $text = [Text.Encoding]::UTF8.GetString($bytes) -replace "`r`n", "`n"
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).Substring(0, 12)
}

function Get-FolderHash([string]$Dir) {
    $root = [IO.Path]::GetFullPath($Dir).TrimEnd('\')
    $parts = Get-ChildItem -LiteralPath $root -Recurse -File -Force |
        Sort-Object FullName |
        ForEach-Object { $_.FullName.Substring($root.Length).ToLowerInvariant() + '=' + (Get-TextHash $_.FullName) }
    $joined = [Text.Encoding]::UTF8.GetBytes(($parts -join "`n"))
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($joined)).Substring(0, 12)
}

# First meaningful line that says what a file is: SKILL.md description, .SYNOPSIS,
# leading comment or docstring, or the first heading of a Markdown file.
function Get-Description([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '' }
    $lines = @(Get-Content -LiteralPath $Path -TotalCount 40 -ErrorAction SilentlyContinue)
    $leaf = Split-Path $Path -Leaf
    $isMd = $leaf -match '\.md$'
    if ($isMd -and $lines.Count -and $lines[0] -eq '---') {
        $end = [Array]::IndexOf([string[]]$lines, '---', 1)
        $front = if ($end -gt 0) { $lines[1..($end - 1)] } else { @() }
        $d = $front | Where-Object { $_ -match '^description:\s*(.+)$' } | Select-Object -First 1
        if ($d) { return Limit-Text (($d -replace '^description:\s*', '').Trim('"', "'", ' ')) }
    }
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*\.SYNOPSIS') {
            $next = $lines[($i + 1)..($lines.Count - 1)] | Where-Object { $_.Trim() } | Select-Object -First 1
            if ($next) { return Limit-Text $next.Trim() }
        }
    }
    # Leading comment block: its first sentence, which may span several comment lines.
    $text = ''
    $inFront = $isMd -and $lines.Count -and $lines[0] -eq '---'
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $t = $lines[$i].Trim()
        if ($inFront) { if ($i -gt 0 -and $t -eq '---') { $inFront = $false }; continue }
        if (-not $text -and (-not $t -or $t -match '^(#!|#Requires|<#|#>|\[CmdletBinding|param\b|using |Set-StrictMode|\$ErrorActionPreference|@echo)')) { continue }
        $m = [regex]::Match($t, '^(#+|//|"""|''''''|::|rem\s)\s*(.*)$')
        if ($m.Success -and -not ($isMd -and $text)) {
            $c = $m.Groups[2].Value.Trim('"', "'", '#', ' ')
            if (-not $text) { $c = $c -replace ('^' + [regex]::Escape($leaf) + '\s*[-:–]\s*'), '' }
            if (-not $c) { if ($text) { break } else { continue } }
            $text = ($text + ' ' + $c).Trim()
            if ($isMd -or $text -match '[.!?:]$' -or $text.Length -gt 200) { break }
            continue
        }
        if (-not $text -and $isMd) { $text = $t }
        break
    }
    if ($text.Length -gt 120) { $text = ($text -split '(?<=[.!?])\s+')[0] }
    Limit-Text $text
}

function Limit-Text([string]$Text) {
    if ($Text.Length -gt 240) { return $Text.Substring(0, 237) + '…' }
    $Text
}

function Resolve-LinkTarget([IO.FileSystemInfo]$Item) {
    if (-not $Item.LinkType) { return $null }
    $target = @($Item.Target)[0]
    if (-not [IO.Path]::IsPathRooted($target)) { $target = Join-Path (Split-Path $Item.FullName -Parent) $target }
    [IO.Path]::GetFullPath($target).TrimEnd('\')
}

# ---------------------------------------------------------------- prism: harness

$H = 'harness'
$HarnessRoots = [ordered]@{}
function Register-HarnessRoot([string]$Path, [string]$Id, [string]$Label, [string]$Parent, [string]$Desc) {
    $HarnessRoots[[IO.Path]::GetFullPath($Path).TrimEnd('\')] = $Id
    Add-Group $Id $Label $H $Parent $Desc | Out-Null
}

Add-Group 'h:global' 'Toutes les sessions' $H '' 'Ce que Claude Code charge quel que soit le dossier ouvert.' | Out-Null
Add-Group 'h:projects' 'Par projet' $H '' 'Ce que Claude Code ajoute quand une session s''ouvre dans un de ces dossiers.' | Out-Null
Add-Group 'h:mcp' 'Serveurs MCP' $H '' 'Les serveurs déclarés, et l''adresse ou la commande vers laquelle chacun pointe.' | Out-Null
Register-HarnessRoot $UserClaude 'h:user' '~/.claude' 'h:global' 'Réglages, hooks et liens que Claude Code lit à chaque session.'
Register-HarnessRoot $UserAgents 'h:agents' '~/.agents' 'h:global' 'Dossier commun avec Codex ; il ne contient que des liens vers D:.'
Register-HarnessRoot 'D:\instructions' 'h:instr' 'D:\instructions' 'h:global' 'Source des instructions globales.'
Register-HarnessRoot (Join-Path $SkillsRepo 'global') 'h:skills-global' 'D:\skills\global — chargés partout' 'h:global' 'Skills que toutes les sessions voient.'
Register-HarnessRoot (Join-Path $SkillsRepo 'projects') 'h:skills-lib' 'D:\skills\projects — bibliothèque' 'h:projects' 'Skills rangés pour les projets : un projet les recopie ou les lie.'
foreach ($root in $ProjectRoots) {
    $name = Split-Path $root -Leaf
    Register-HarnessRoot $root "h:proj:$name" $name 'h:projects' "Dossier $root"
}

function Get-HarnessParent([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $best = $null
    foreach ($root in $HarnessRoots.Keys) {
        if ($full.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) -or $full -ieq $root) {
            if (-not $best -or $root.Length -gt $best.Length) { $best = $root }
        }
    }
    if ($best) { $HarnessRoots[$best] } else { 'h:global' }
}

function Get-FileKind([string]$Path) {
    $leaf = Split-Path $Path -Leaf
    if ($leaf -eq 'SKILL.md' -or (Test-Path -LiteralPath (Join-Path $Path 'SKILL.md'))) { return 'skill' }
    if ($leaf -match $ScriptPattern) { return 'script' }
    if ($leaf -match '\.json$') { return 'config' }
    if ($leaf -match '\.md$') { return 'context' }
    if ((Get-Item -LiteralPath $Path -Force).PSIsContainer) { return 'folder' }
    'file'
}

# Adds a file or folder of the harness, follows its symlink chain, and answers the node id.
# A folder that is itself a registered root answers its group, so a link can point at a group.
function Add-HarnessItem([string]$Path, [string]$Kind, [string]$Label) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (-not (Test-Path -LiteralPath $full)) { return $null }
    $item = Get-Item -LiteralPath $full -Force
    $target = Resolve-LinkTarget $item
    if (-not $target -and $HarnessRoots.Contains($full)) { return $HarnessRoots[$full] }
    if (-not $Kind) { $Kind = if ($target) { 'link' } else { Get-FileKind $full } }
    if (-not $Label) { $Label = $item.Name }
    $desc = if ($target) { "Lien symbolique vers $(Get-DisplayPath $target)" }
            elseif ($Kind -eq 'skill' -and $item.PSIsContainer) { Get-Description (Join-Path $full 'SKILL.md') }
            else { Get-Description $full }
    $id = Add-Node -Id (Get-NodeId $full) -Label $Label -Kind $Kind -Prism $H -Parent (Get-HarnessParent $full) `
                   -Path (Get-DisplayPath $full) -Desc $desc
    if ($target) {
        $targetId = Add-HarnessItem $target
        Add-Edge $id $targetId 'link' 'lien symbolique'
    }
    $id
}

# @imports in CLAUDE.md / AGENTS.md
function Add-Imports([string]$ContextFile, [string]$FromId) {
    if (-not (Test-Path -LiteralPath $ContextFile -PathType Leaf)) { return }
    foreach ($line in Get-Content -LiteralPath $ContextFile) {
        if ($line -match '^\s*@(\S+)\s*$') {
            $ref = $Matches[1] -replace '^~', $HOME
            if (-not [IO.Path]::IsPathRooted($ref)) { $ref = Join-Path (Split-Path ([IO.Path]::GetFullPath($ContextFile)) -Parent) $ref }
            $toId = Add-HarnessItem $ref
            Add-Edge $FromId $toId 'imports' 'importe'
        }
    }
}

$mcpEndpoints = @{}
function Add-McpServers([object]$Servers, [string]$FromId, [string]$EdgeLabel) {
    if (-not $Servers) { return }
    foreach ($p in $Servers.PSObject.Properties) {
        $s = $p.Value
        $where = if ($s.url) { $s.url } else { (@($s.command) + @($s.args)) -join ' ' }
        $mcpId = Add-Node -Id "mcp:$($p.Name)" -Label $p.Name -Kind 'mcp' -Prism $H -Parent 'h:mcp' `
                          -Desc "Serveur MCP « $($p.Name) » ($($s.type ?? 'stdio')) : $where"
        Add-Edge $FromId $mcpId 'declares' $EdgeLabel
        $epKey = $where.ToLowerInvariant()
        if (-not $mcpEndpoints.ContainsKey($epKey)) {
            $epLabel = if ($s.url) { ([uri]$s.url).Authority } else { "commande $($s.command)" }
            $epDesc = if ($s.url) { "Adresse $($s.url). Plusieurs noms qui pointent ici parlent au même serveur, chacun avec son propre jeton." } else { "Programme lancé par Claude Code : $where" }
            $mcpEndpoints[$epKey] = Add-Node -Id "ep:$epKey" -Label $epLabel -Kind 'service' -Prism $H -Parent 'h:mcp' -Desc $epDesc
        }
        Add-Edge $mcpId $mcpEndpoints[$epKey] 'points' 'pointe vers'
    }
}

$hookEdges = [System.Collections.Generic.List[object]]::new()
function Add-Hooks([string]$SettingsFile, [string]$FromId) {
    if (-not (Test-Path -LiteralPath $SettingsFile)) { return }
    $json = Get-Content -LiteralPath $SettingsFile -Raw | ConvertFrom-Json
    if (-not $json.hooks) { return }
    foreach ($event in $json.hooks.PSObject.Properties) {
        foreach ($matcherGroup in @($event.Value)) {
            foreach ($hook in @($matcherGroup.hooks)) {
                $label = if ($matcherGroup.matcher) { "hook $($event.Name) ($($matcherGroup.matcher))" } else { "hook $($event.Name)" }
                $found = [regex]::Matches([string]$hook.command, '[A-Za-z]:[\\/][^\s"'']+|~[\\/][^\s"'']+')
                foreach ($m in $found) {
                    $p = $m.Value -replace '^~', $HOME
                    $toId = Add-HarnessItem $p 'script'
                    if ($toId) { Add-Edge $FromId $toId 'runs' $label; $hookEdges.Add(@($FromId, $toId)) }
                }
            }
        }
    }
}

# User level: settings, instructions, skills, hooks, MCP, scheduled tasks.
$userSettings = Join-Path $UserClaude 'settings.json'
$userSettingsId = Add-HarnessItem $userSettings 'config'
Add-Hooks $userSettings $userSettingsId
$claudeMdId = Add-HarnessItem (Join-Path $UserClaude 'CLAUDE.md') 'context'
Add-Imports (Join-Path $UserClaude 'CLAUDE.md') $claudeMdId
$userSkillsId = Add-HarnessItem (Join-Path $UserClaude 'skills')

$claudeJson = Join-Path $HOME '.claude.json'
if (Test-Path $claudeJson) {
    $cj = Get-Content $claudeJson -Raw | ConvertFrom-Json
    $cjId = Add-Node -Id (Get-NodeId $claudeJson) -Label '.claude.json' -Kind 'config' -Prism $H -Parent 'h:user' `
                     -Path (Get-DisplayPath $claudeJson) -Desc 'État de Claude Code ; sa clé mcpServers déclare les serveurs MCP valables partout.'
    Add-McpServers $cj.mcpServers $cjId 'déclare'
}
foreach ($f in Get-ChildItem (Join-Path $UserClaude 'mcp-parked') -Filter *.json -ErrorAction SilentlyContinue) {
    $id = Add-HarnessItem $f.FullName 'config'
    $nodes[$id].desc = 'Serveur MCP mis de côté : déclaré ici, il n''est pas chargé.'
    $wrapped = [pscustomobject]@{ $f.BaseName = (Get-Content $f.FullName -Raw | ConvertFrom-Json) }
    Add-McpServers $wrapped $id 'garé'
}
foreach ($f in Get-ChildItem (Join-Path $UserClaude 'hooks'), (Join-Path $UserClaude 'scripts') -File -ErrorAction SilentlyContinue) {
    Add-HarnessItem $f.FullName | Out-Null
}
foreach ($d in Get-ChildItem (Join-Path $UserClaude 'scheduled-tasks') -Directory -ErrorAction SilentlyContinue) {
    $desc = Get-Description (Join-Path $d.FullName 'SKILL.md')
    Add-Node -Id (Get-NodeId $d.FullName) -Label $d.Name -Kind 'task' -Prism $H -Parent 'h:user' `
             -Path (Get-DisplayPath $d.FullName) -Desc ("Tâche planifiée. " + $desc) | Out-Null
}

# Skills folders: global (loaded everywhere) and the project library.
foreach ($root in (Join-Path $SkillsRepo 'global'), (Join-Path $SkillsRepo 'projects')) {
    foreach ($d in Get-ChildItem $root -Directory -Force) {
        if ($d.Name -eq '.system') {
            Add-Node -Id (Get-NodeId $d.FullName) -Label '.system (Codex)' -Kind 'skill' -Prism $H -Parent (Get-HarnessParent $d.FullName) `
                     -Path (Get-DisplayPath $d.FullName) -Desc 'Skills système de Codex, rangés un niveau plus bas ; ils n''apparaissent pas dans la liste des skills de Claude Code.' | Out-Null
            continue
        }
        Add-HarnessItem $d.FullName 'skill' | Out-Null
    }
}

# Project level.
$projectSkillDirs = [System.Collections.Generic.List[object]]::new()
foreach ($root in $ProjectRoots) {
    foreach ($leaf in 'CLAUDE.md', 'AGENTS.md') {
        $p = Join-Path $root $leaf
        if (Test-Path $p) { $id = Add-HarnessItem $p 'context'; Add-Imports $p $id }
    }
    $mcpJson = Join-Path $root '.mcp.json'
    if (Test-Path $mcpJson) {
        $id = Add-HarnessItem $mcpJson 'config'
        Add-McpServers (Get-Content $mcpJson -Raw | ConvertFrom-Json).mcpServers $id 'déclare'
    }
    foreach ($leaf in 'settings.json', 'settings.local.json') {
        $p = Join-Path $root ".claude\$leaf"
        if (Test-Path $p) { $id = Add-HarnessItem $p 'config'; Add-Hooks $p $id }
    }
    $launch = Join-Path $root '.claude\launch.json'
    if (Test-Path $launch) {
        $launchId = Add-HarnessItem $launch 'config'
        $nodes[$launchId].desc = 'Serveurs de prévisualisation que l''app Claude peut démarrer.'
        foreach ($cfg in (Get-Content $launch -Raw | ConvertFrom-Json).configurations) {
            foreach ($arg in @($cfg.runtimeArgs)) {
                if ($arg -match $ScriptPattern) {
                    $sid = Add-HarnessItem (Join-Path $root $arg) 'script'
                    if ($sid) { Add-Edge $launchId $sid 'runs' "aperçu $($cfg.name)"; $hookEdges.Add(@($launchId, $sid)) }
                }
            }
        }
    }
    $claudeSkills = Join-Path $root '.claude\skills'
    if (Test-Path $claudeSkills) { Add-HarnessItem $claudeSkills | Out-Null }
    foreach ($skillsRoot in (Join-Path $root '.agents\skills'), $claudeSkills) {
        if (-not (Test-Path $skillsRoot)) { continue }
        $item = Get-Item $skillsRoot -Force
        if ($item.LinkType) { continue }
        $groupId = Add-Group ('h:pskills:' + (Get-NodeId $skillsRoot)) ((Split-Path $root -Leaf) + '/' + ($skillsRoot.Substring($root.Length + 1) -replace '\\', '/')) $H "h:proj:$(Split-Path $root -Leaf)" 'Skills propres au projet.'
        $HarnessRoots[[IO.Path]::GetFullPath($skillsRoot)] = $groupId
        foreach ($d in Get-ChildItem $skillsRoot -Directory -Force) {
            Add-HarnessItem $d.FullName 'skill' | Out-Null
            $projectSkillDirs.Add($d)
        }
    }
}

# ---------------------------------------------------------------- prism: scripts

$S = 'scripts'
$scriptFiles = [System.Collections.Generic.List[object]]::new()   # @{ Repo; Rel; Full; Id }

function Add-ScriptFile([string]$RepoName, [string]$RepoGroup, [string]$Rel, [string]$Full) {
    $dir = Split-Path $Rel -Parent
    $dirKey = if ($dir) { $dir -replace '\\', '/' } else { '(racine)' }
    $dirGroup = Add-Group "s:$RepoName/$dirKey" $dirKey $S $RepoGroup ''
    $id = Add-Node -Id (Get-NodeId $Full) -Label (Split-Path $Full -Leaf) -Kind 'script' -Prism $S -Parent $dirGroup `
                   -Path (Get-DisplayPath $Full) -Desc (Get-Description $Full)
    $scriptFiles.Add([pscustomobject]@{ Repo = $RepoName; Rel = ($Rel -replace '\\', '/'); Full = $Full; Id = $id })
}

$repos = @($ProjectRoots) + $SkillsRepo
foreach ($root in $repos) {
    if (-not (Test-Path (Join-Path $root '.git'))) { continue }
    $name = Split-Path $root -Leaf
    $files = @(git -C $root ls-files --cached --others --exclude-standard 2>$null) |
        Where-Object { $_ -match $ScriptPattern -and $_ -notmatch $ExcludedPattern } | Sort-Object -Unique
    if (-not $files) { continue }
    $repoGroup = Add-Group "s:$name" $name $S '' "Dépôt $root — $($files.Count) scripts"
    foreach ($rel in $files) {
        $full = Join-Path $root $rel
        if (Test-Path -LiteralPath $full) { Add-ScriptFile $name $repoGroup $rel $full }
    }
}

# Harness scripts and the files that run them.
$userGroup = Add-Group 's:~claude' '~/.claude' $S '' 'Scripts du harnais utilisateur.'
foreach ($f in Get-ChildItem (Join-Path $UserClaude 'hooks'), (Join-Path $UserClaude 'scripts') -File -ErrorAction SilentlyContinue) {
    if ($f.Name -match $ScriptPattern) { Add-ScriptFile '~claude' $userGroup $f.FullName.Substring($UserClaude.Length + 1) $f.FullName }
}
foreach ($pair in $hookEdges) {
    $from = $nodes[$pair[0]]
    $fromPath = $from.path -replace '^~', $HOME
    $repoGroup = if ($fromPath.StartsWith($UserClaude, [StringComparison]::OrdinalIgnoreCase)) { 's:~claude' }
                 else { $p = ($ProjectRoots | Where-Object { $fromPath.StartsWith($_ + '\', [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1); if ($p) { 's:' + (Split-Path $p -Leaf) } else { 's:~claude' } }
    Add-Node -Id $pair[0] -Prism $S -Parent $repoGroup | Out-Null
}

# Calls between scripts: file names written in a script, and Python imports.
$byRepoName = @{}
foreach ($s in $scriptFiles) {
    $key = "$($s.Repo)|$((Split-Path $s.Full -Leaf).ToLowerInvariant())"
    if (-not $byRepoName.ContainsKey($key)) { $byRepoName[$key] = [System.Collections.Generic.List[object]]::new() }
    $byRepoName[$key].Add($s)
}
function Find-Callee([object]$Caller, [string]$FileName) {
    $c = $byRepoName["$($Caller.Repo)|$($FileName.ToLowerInvariant())"]
    if (-not $c) { return $null }
    $c = @($c | Where-Object { $_.Id -ne $Caller.Id })
    if ($c.Count -eq 0) { return $null }
    if ($c.Count -eq 1) { return $c[0] }
    $sameDir = @($c | Where-Object { (Split-Path $_.Full -Parent) -eq (Split-Path $Caller.Full -Parent) })
    if ($sameDir.Count -eq 1) { return $sameDir[0] }
    # Closest by shared folder prefix; a tie is left unlinked rather than guessed.
    $scored = @($c | ForEach-Object {
        $a = $Caller.Rel.Split('/'); $b = $_.Rel.Split('/'); $k = 0
        while ($k -lt [Math]::Min($a.Count, $b.Count) - 1 -and $a[$k] -eq $b[$k]) { $k++ }
        [pscustomobject]@{ S = $_; K = $k }
    } | Sort-Object K -Descending)
    if ($scored.Count -ge 2 -and $scored[0].K -eq $scored[1].K) { return $null }
    $scored[0].S
}
foreach ($s in $scriptFiles) {
    $text = Get-Content -LiteralPath $s.Full -Raw -ErrorAction SilentlyContinue
    if (-not $text) { continue }
    $names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($m in [regex]::Matches($text, '[\w.-]+\.(ps1|psm1|py|sh|mjs|cmd|bat)\b')) { [void]$names.Add($m.Value) }
    if ($s.Full -match '\.py$') {
        foreach ($m in [regex]::Matches($text, '(?m)^\s*(?:from\s+([\w.]+)\s+import|import\s+([\w.,\s]+?)(?:\s+as\s+\w+)?\s*$)')) {
            $mods = if ($m.Groups[1].Success) { @($m.Groups[1].Value) } else { $m.Groups[2].Value -split ',' }
            foreach ($mod in $mods) { $first = ($mod.Trim() -split '[.\s]')[0]; if ($first) { [void]$names.Add("$first.py") } }
        }
    }
    if ($s.Full -match '\.(ps1|psm1)$') {
        foreach ($m in [regex]::Matches($text, 'Import-Module\s+[^\r\n]*?([\w-]+)(?:\.psm1)?[''"]?\s*(?:-|$)', 'Multiline')) { [void]$names.Add("$($m.Groups[1].Value).psm1") }
    }
    foreach ($n in $names) {
        $callee = Find-Callee $s $n
        if ($callee) { Add-Edge $s.Id $callee.Id 'calls' 'appelle' }
    }
}

# ---------------------------------------------------------------- prism: copies

$C = 'copies'
$letters = 'ABCDEFGHIJ'
function Add-CopyFamily([string]$Key, [string]$Title, [object[]]$Members) {
    # Members: @{ Id; Label; Hash }. Version A is the most common content, then B, C...
    $versions = $Members | Group-Object Hash | Sort-Object Count -Descending
    $letterOf = @{}; $i = 0
    foreach ($v in $versions) { $letterOf[$v.Name] = [string]$letters[[Math]::Min($i, 9)]; $i++ }
    $state = if ($versions.Count -eq 1) { 'identiques' } else { "$($versions.Count) versions" }
    $groupId = Add-Group "c:$Key" $Title $C '' "$($Members.Count) copies, $state."
    $nodes[$groupId].variant = if ($versions.Count -eq 1) { 'same' } else { 'diverged' }
    foreach ($m in $Members) {
        $l = $letterOf[$m.Hash]
        Add-Node -Id $m.Id -Prism $C -Parent $groupId -PrismLabel "$($m.Label) · $l" -Variant $l | Out-Null
    }
}

$byName = $scriptFiles | Group-Object { (Split-Path $_.Full -Leaf).ToLowerInvariant() } | Where-Object Count -ge 2
foreach ($g in $byName) {
    $members = foreach ($s in $g.Group) {
        $dir = Split-Path $s.Rel -Parent
        $lastDir = if ($dir) { ' › ' + (Split-Path $dir -Leaf) } else { '' }
        [pscustomobject]@{ Id = $s.Id; Label = ($s.Repo + $lastDir); Hash = (Get-TextHash $s.Full) }
    }
    Add-CopyFamily "script/$($g.Name)" (Split-Path $g.Group[0].Full -Leaf) $members
}

$skillDirs = @(Get-ChildItem (Join-Path $SkillsRepo 'global'), (Join-Path $SkillsRepo 'projects') -Directory) + $projectSkillDirs |
    Where-Object { $_.Name -ne '.system' }
foreach ($g in ($skillDirs | Group-Object { $_.Name.ToLowerInvariant() } | Where-Object Count -ge 2)) {
    $members = foreach ($d in $g.Group) {
        [pscustomobject]@{ Id = (Get-NodeId $d.FullName); Label = (Get-DisplayPath (Split-Path $d.FullName -Parent)); Hash = (Get-FolderHash $d.FullName) }
    }
    Add-CopyFamily "skill/$($g.Name)" "skill $($g.Group[0].Name)" $members
}

# ---------------------------------------------------------------- write

$data = [ordered]@{
    generated = (Get-Date -Format 'yyyy-MM-dd HH:mm')
    nodes     = @($nodes.Values)
    edges     = @($edges)
}
$json = $data | ConvertTo-Json -Depth 8 -Compress
Set-Content -LiteralPath $Out -Value "window.WORKSPACE = $json;" -Encoding utf8
Write-Host "Wrote $Out : $($nodes.Count) nodes, $($edges.Count) edges."
