$ErrorActionPreference = 'Stop'
$ScriptsDir = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$LibDir = Join-Path $ScriptsDir 'lib'
Import-Module (Join-Path $LibDir 'agent-state-maintenance.psm1') -Force

function Assert-Equal($Expected, $Actual, [string]$Case) {
    if ($Expected -ne $Actual) { throw ($Case + ": expected $Expected, got $Actual") }
}

function Assert-True([bool]$Value, [string]$Case) {
    if (-not $Value) { throw ($Case + ': expected true') }
}

function Assert-Throws([scriptblock]$Action, [string]$Pattern, [string]$Case) {
    try {
        & $Action
    } catch {
        if ($_.Exception.Message -notmatch $Pattern) {
            throw ($Case + ": unexpected error: $($_.Exception.Message)")
        }
        return
    }
    throw ($Case + ': expected an error')
}

function New-TestStateDatabase([string]$Path, [string]$Trigger = '') {
    $source = @'
import sqlite3, sys
connection = sqlite3.connect(sys.argv[1])
for table in ["_sqlx_migrations", "backfill_state", "remote_control_enrollments",
              "thread_dynamic_tools", "thread_spawn_edges", "threads"]:
    connection.execute('CREATE TABLE "' + table + '" (id INTEGER)')
    connection.execute('INSERT INTO "' + table + '" VALUES (1)')
if sys.argv[2]:
    connection.executescript(sys.argv[2])
connection.commit()
connection.close()
'@
    $null = & python -c $source $Path $Trigger
    if ($LASTEXITCODE -ne 0) { throw 'Could not create a state database fixture.' }
}

$codexInput = [ordered]@{
    'active-workspace-roots' = @('D:\projects\deckle')
    'local-projects' = [ordered]@{ deckle = [ordered]@{ path = 'D:\projects\deckle' } }
    'selected-project' = 'deckle'
    model = 'gpt-test'
    'electron-persisted-atom-state' = [ordered]@{
        'prompt-history' = @('secret prompt')
        'thread-client-id-v1:abc' = 'client'
        'sidebar-collapsed-sections-v1' = @('old-project')
        'sidebar-width' = 312
    }
}
$codexClean = ConvertTo-CleanCodexGlobalState -Json ($codexInput | ConvertTo-Json -Depth 20) | ConvertFrom-Json -Depth 20
Assert-Equal 0 @($codexClean.'active-workspace-roots').Count 'Codex workspace roots are cleared'
Assert-Equal 0 @($codexClean.'local-projects'.PSObject.Properties).Count 'Codex projects are cleared'
Assert-Equal $null $codexClean.PSObject.Properties['selected-project'] 'Codex selected project is removed'
Assert-Equal $null $codexClean.'electron-persisted-atom-state'.PSObject.Properties['prompt-history'] 'Codex prompt history is removed'
Assert-Equal $null $codexClean.'electron-persisted-atom-state'.PSObject.Properties['thread-client-id-v1:abc'] 'Codex thread atom is removed'
Assert-Equal $null $codexClean.'electron-persisted-atom-state'.PSObject.Properties['sidebar-collapsed-sections-v1'] 'Codex collapsed project sections are removed'
Assert-Equal 312 $codexClean.'electron-persisted-atom-state'.'sidebar-width' 'Codex UI preference is preserved'
Assert-Equal 'gpt-test' $codexClean.model 'Codex model setting is preserved'

$claudeProfile = [ordered]@{
    oauthAccount = [ordered]@{ accountUuid = 'account-1' }
    mcpServers = [ordered]@{ figma = [ordered]@{ type = 'http' } }
    projects = [ordered]@{ 'D:\old-project' = [ordered]@{ trust = $true } }
}
$claudeClean = ConvertTo-CleanClaudeProfile -Json ($claudeProfile | ConvertTo-Json -Depth 20) | ConvertFrom-Json -Depth 20
Assert-Equal 0 @($claudeClean.projects.PSObject.Properties).Count 'Claude projects are cleared'
Assert-Equal 'account-1' $claudeClean.oauthAccount.accountUuid 'Claude account is preserved'
Assert-Equal 'http' $claudeClean.mcpServers.figma.type 'Claude global MCP is preserved'

$desktopConfig = [ordered]@{
    preferences = [ordered]@{
        launchPreviewPersistedWorkspaces = @('D:\old-project')
        launchPreviewSessionScopedSessions = @('session-1')
        'chillingSlothLocation.customPath' = 'D:\worktrees\deckle'
        epitaxyPrefs = [ordered]@{
            'epitaxy-folder-permission-mode.account-1' = [ordered]@{ 'D:\old-project' = 'allow' }
            'epitaxy-perm-mode-acks.account-1' = @('D:\old-project')
            'starred-local-code-sessions' = @('session-1')
            'starred-session-groups' = @('group-1')
            'starred-cowork-spaces' = @('space-1')
            'dframe-local-slice' = [ordered]@{
                homeProjectsPinnedOrder = @('D:\old-project')
                pinnedOrder = @('D:\old-project')
            }
            'ccd-sessions-filter' = [ordered]@{
                state = [ordered]@{ selectedProjects = @('D:\old-project') }
            }
            'cc-landing-worktree-enabled' = $true
            theme = 'dark'
        }
    }
}
$desktopClean = ConvertTo-CleanClaudeDesktopConfig -Json ($desktopConfig | ConvertTo-Json -Depth 20) | ConvertFrom-Json -Depth 20
Assert-Equal 0 @($desktopClean.preferences.epitaxyPrefs.'epitaxy-folder-permission-mode.account-1'.PSObject.Properties).Count 'Claude folder permissions are cleared'
Assert-Equal 0 @($desktopClean.preferences.epitaxyPrefs.'epitaxy-perm-mode-acks.account-1').Count 'Claude folder acknowledgements are cleared'
Assert-Equal 0 @($desktopClean.preferences.epitaxyPrefs.'ccd-sessions-filter'.state.selectedProjects).Count 'Claude selected projects are cleared'
Assert-Equal 0 @($desktopClean.preferences.epitaxyPrefs.'dframe-local-slice'.pinnedOrder).Count 'Claude project pins are cleared'
Assert-Equal 0 @($desktopClean.preferences.'launchPreviewPersistedWorkspaces').Count 'Claude launch workspaces are cleared'
Assert-Equal 'D:\worktrees\deckle' $desktopClean.preferences.'chillingSlothLocation.customPath' 'Claude worktree location is preserved'
Assert-Equal $true $desktopClean.preferences.epitaxyPrefs.'cc-landing-worktree-enabled' 'Claude worktree preference is preserved'
Assert-Equal 'dark' $desktopClean.preferences.epitaxyPrefs.theme 'Claude unrelated UI preference is preserved'

$toml = @'
model = "gpt-test"

[projects.'D:\old-project']
trust_level = "trusted"

[[skills.config]]
path = "D:\skills\global"
'@
$tomlClean = ConvertTo-CleanCodexConfig -Toml $toml
Assert-True ($tomlClean -notmatch 'old-project') 'Codex project trust is removed'
Assert-True ($tomlClean -match 'model = "gpt-test"') 'Codex general config is preserved'
Assert-True ($tomlClean -match '\[\[skills\.config\]\]') 'Codex skill config is preserved'
Assert-Throws { ConvertTo-CleanCodexConfig -Toml 'notes = """[projects.''D:\unsafe'']"""' } 'multiline TOML' 'Ambiguous multiline TOML is rejected'

Assert-Throws { Assert-AgentStatePath -Path 'D:\outside' -Root 'C:\safe' } 'escapes' 'Path boundary is enforced'
Assert-Throws { Assert-AgentProcessesStopped -ProcessNames @('explorer', 'ChatGPT') } 'ChatGPT' 'Active Codex blocks reset'
Assert-AgentProcessesStopped -ProcessNames @('explorer')

$broadRoot = Join-Path ([IO.Path]::GetTempPath()) "deckle-agent-broad-$([guid]::NewGuid())"
try {
    $null = New-Item -ItemType Directory -Path $broadRoot -Force
    Set-Content -LiteralPath (Join-Path $broadRoot 'config.toml') -Value 'model = "test"' -Encoding utf8NoBOM
    Assert-Throws {
        Get-AgentStateCleanupPlan -Scope Codex -UserProfileRoot $broadRoot -CodexHome $broadRoot
    } 'broad Codex state root' 'The whole user profile cannot become a cleanup root'
} finally {
    if (Test-Path -LiteralPath $broadRoot) { Remove-Item -LiteralPath $broadRoot -Recurse -Force }
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "deckle-agent-state-$([guid]::NewGuid())"
$profile = Join-Path $testRoot 'profile'
$codex = Join-Path $profile '.codex'
$claude = Join-Path $profile '.claude'
$desktop = Join-Path $profile 'AppData\Roaming\Claude'
$repoSentinel = Join-Path $testRoot 'repository\worktree.txt'
try {
    $null = New-Item -ItemType Directory -Path (Join-Path $codex 'sessions') -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $codex 'plugins\kept') -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $codex 'worktrees\kept') -Force
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $repoSentinel) -Force
    Set-Content -LiteralPath (Join-Path $codex 'sessions\session.jsonl') -Value 'session secret' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $codex 'auth.json') -Value '{"token":"keep"}' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $codex 'plugins\kept\config.json') -Value '{"enabled":true}' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $codex 'worktrees\kept\work.txt') -Value 'keep work' -Encoding utf8NoBOM
    Set-Content -LiteralPath $repoSentinel -Value 'keep repository' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $codex '.codex-global-state.json') -Value ($codexInput | ConvertTo-Json -Depth 20) -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $codex 'config.toml') -Value $toml -Encoding utf8NoBOM

    $sessionPath = Join-Path $codex 'sessions\session.jsonl'
    $statePath = Join-Path $codex '.codex-global-state.json'
    $sessionHash = (Get-FileHash -LiteralPath $sessionPath).Hash
    $stateHash = (Get-FileHash -LiteralPath $statePath).Hash
    $stateTime = (Get-Item -LiteralPath $statePath).LastWriteTimeUtc
    $planParameters = @{
        Scope = 'Codex'
        UserProfileRoot = $profile
        CodexHome = $codex
        ClaudeHome = $claude
        ClaudeDesktopData = $desktop
    }
    $plan = Get-AgentStateCleanupPlan @planParameters
    $preview = Invoke-AgentStateCleanupPlan -Plan $plan
    Assert-Equal $false $preview.Applied 'Preview reports no apply'
    Assert-Equal $sessionHash (Get-FileHash -LiteralPath $sessionPath).Hash 'Preview preserves session file'
    Assert-Equal $stateHash (Get-FileHash -LiteralPath $statePath).Hash 'Preview preserves mixed state file'
    Assert-Equal $stateTime (Get-Item -LiteralPath $statePath).LastWriteTimeUtc 'Preview preserves mixed state timestamp'

    $applied = Invoke-AgentStateCleanupPlan -Plan $plan -Apply -Confirmation 'RESET LOCAL AI SESSIONS' -RunningProcessNames @('explorer')
    Assert-Equal $true $applied.Applied 'Apply reports reset'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $codex 'sessions')) 'Session directory is removed'
    Assert-Equal '{"token":"keep"}' (Get-Content -Raw -LiteralPath (Join-Path $codex 'auth.json')).Trim() 'Codex auth is preserved'
    Assert-Equal '{"enabled":true}' (Get-Content -Raw -LiteralPath (Join-Path $codex 'plugins\kept\config.json')).Trim() 'Codex plugins are preserved'
    Assert-Equal 'keep work' (Get-Content -Raw -LiteralPath (Join-Path $codex 'worktrees\kept\work.txt')).Trim() 'Codex worktrees are preserved'
    Assert-Equal 'keep repository' (Get-Content -Raw -LiteralPath $repoSentinel).Trim() 'Repositories are not traversed'

    $afterState = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json -Depth 20
    Assert-Equal 'gpt-test' $afterState.model 'Apply preserves Codex model'
    Assert-True ((Get-Content -Raw -LiteralPath (Join-Path $codex 'config.toml')) -notmatch 'old-project') 'Apply removes project trust only'

    $secondPlan = Get-AgentStateCleanupPlan @planParameters
    $secondApply = Invoke-AgentStateCleanupPlan -Plan $secondPlan -Apply -Confirmation 'RESET LOCAL AI SESSIONS' -RunningProcessNames @('explorer')
    Assert-Equal 0 $secondApply.RemovedTargets 'Second apply has no file target to remove'
    Assert-Equal 0 $secondApply.ChangedStateFiles 'Second apply is idempotent'
} finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}

$claudeTestRoot = Join-Path ([IO.Path]::GetTempPath()) "deckle-agent-claude-$([guid]::NewGuid())"
try {
    $claudeProfileRoot = Join-Path $claudeTestRoot 'profile'
    $claudeHome = Join-Path $claudeProfileRoot '.claude'
    $claudeDesktop = Join-Path $claudeProfileRoot 'AppData\Roaming\Claude'
    $profilePath = Join-Path $claudeProfileRoot '.claude.json'
    $profileBackupPath = Join-Path $claudeProfileRoot '.claude.json.backup'
    $historyBackupPath = Join-Path $claudeHome 'backups\.claude.json.backup.1'
    foreach ($directory in @(
        (Join-Path $claudeHome 'projects\old-project\memory'),
        (Join-Path $claudeHome 'plugins\kept'),
        (Split-Path -Parent $historyBackupPath),
        (Join-Path $claudeDesktop 'claude-code-sessions'),
        (Join-Path $claudeDesktop 'Cache')
    )) {
        $null = New-Item -ItemType Directory -Path $directory -Force
    }
    Set-Content -LiteralPath (Join-Path $claudeHome 'settings.json') -Value '{"theme":"keep"}' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $claudeHome '.last-cleanup') -Value 'marker' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $claudeHome 'plugins\kept\config.json') -Value '{"enabled":true}' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $claudeHome 'projects\old-project\memory\MEMORY.md') -Value 'remove memory' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $claudeDesktop 'claude-code-sessions\session.json') -Value 'remove session' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $claudeDesktop 'Cache\entry') -Value 'remove cache path' -Encoding utf8NoBOM
    $profileJson = $claudeProfile | ConvertTo-Json -Depth 20
    foreach ($path in @($profilePath, $profileBackupPath, $historyBackupPath)) {
        Set-Content -LiteralPath $path -Value $profileJson -Encoding utf8NoBOM
    }
    Set-Content -LiteralPath (Join-Path $claudeDesktop 'claude_desktop_config.json') -Value ($desktopConfig | ConvertTo-Json -Depth 20) -Encoding utf8NoBOM

    $claudePlanParameters = @{
        Scope = 'Claude'
        UserProfileRoot = $claudeProfileRoot
        ClaudeHome = $claudeHome
        ClaudeDesktopData = $claudeDesktop
    }
    $claudePlan = Get-AgentStateCleanupPlan @claudePlanParameters
    $claudeResult = Invoke-AgentStateCleanupPlan -Plan $claudePlan -Apply -Confirmation 'RESET LOCAL AI SESSIONS' -RunningProcessNames @('explorer')
    Assert-Equal $true $claudeResult.Applied 'Claude cleanup applies without calling an installed executable'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $claudeHome 'projects')) 'Claude projects and automatic memory are removed'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $claudeDesktop 'claude-code-sessions')) 'Claude Desktop sessions are removed'
    Assert-Equal $false (Test-Path -LiteralPath (Join-Path $claudeDesktop 'Cache')) 'Claude Desktop path cache is removed'
    Assert-Equal '{"theme":"keep"}' (Get-Content -Raw -LiteralPath (Join-Path $claudeHome 'settings.json')).Trim() 'Claude settings are preserved'
    Assert-Equal 'marker' (Get-Content -Raw -LiteralPath (Join-Path $claudeHome '.last-cleanup')).Trim() 'Claude cleanup marker is preserved'
    Assert-Equal '{"enabled":true}' (Get-Content -Raw -LiteralPath (Join-Path $claudeHome 'plugins\kept\config.json')).Trim() 'Claude plugins are preserved'
    foreach ($path in @($profilePath, $profileBackupPath, $historyBackupPath)) {
        $cleanProfile = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json -Depth 20
        Assert-Equal 0 @($cleanProfile.projects.PSObject.Properties).Count 'Claude project entries are cleared from live and backup profiles'
        Assert-Equal 'account-1' $cleanProfile.oauthAccount.accountUuid 'Claude account is preserved in live and backup profiles'
    }
    Assert-Equal $true (Test-Path -LiteralPath (Split-Path -Parent $historyBackupPath)) 'Claude backup directory is preserved'
    $cleanDesktop = Get-Content -Raw -LiteralPath (Join-Path $claudeDesktop 'claude_desktop_config.json') | ConvertFrom-Json -Depth 20
    Assert-Equal 0 @($cleanDesktop.preferences.epitaxyPrefs.'epitaxy-folder-permission-mode.account-1'.PSObject.Properties).Count 'Claude Desktop project permissions are cleared on apply'
    Assert-Equal 'D:\worktrees\deckle' $cleanDesktop.preferences.'chillingSlothLocation.customPath' 'Claude Desktop worktree preference survives apply'
    $secondClaudePlan = Get-AgentStateCleanupPlan @claudePlanParameters
    $secondClaudeApply = Invoke-AgentStateCleanupPlan -Plan $secondClaudePlan -Apply -Confirmation 'RESET LOCAL AI SESSIONS' -RunningProcessNames @('explorer')
    Assert-Equal 0 $secondClaudeApply.RemovedTargets 'Second Claude apply has no file target to remove'
    Assert-Equal 0 $secondClaudeApply.ChangedStateFiles 'Second Claude apply is idempotent'
} finally {
    if (Test-Path -LiteralPath $claudeTestRoot) { Remove-Item -LiteralPath $claudeTestRoot -Recurse -Force }
}

$sqliteRoot = Join-Path ([IO.Path]::GetTempPath()) "deckle-agent-sqlite-$([guid]::NewGuid())"
try {
    $sqliteProfile = Join-Path $sqliteRoot 'profile'
    $sqliteCodex = Join-Path $sqliteProfile '.codex'
    $sqliteDir = Join-Path $sqliteCodex 'sqlite'
    $null = New-Item -ItemType Directory -Path $sqliteDir -Force
    $stateDatabase = Join-Path $sqliteCodex 'state_5.sqlite'
    $catalogDatabase = Join-Path $sqliteDir 'codex-dev.db'
    $createSource = @'
import sqlite3, sys
state, catalog = sys.argv[1], sys.argv[2]
connection = sqlite3.connect(state)
for table in ["_sqlx_migrations", "backfill_state", "external_agent_config_imports", "remote_control_enrollments",
              "thread_dynamic_tools", "thread_spawn_edges", "threads", "thread_sections", "agent_jobs",
              "agent_job_items"]:
    connection.execute('CREATE TABLE "' + table + '" (id INTEGER)')
    connection.execute('INSERT INTO "' + table + '" VALUES (1)')
connection.commit()
connection.close()
connection = sqlite3.connect(catalog)
for table in ["inbox_items", "automation_runs", "thread_timeline_ledger", "local_thread_catalog",
              "local_thread_catalog_sync_state", "local_thread_catalog_hosts", "automations",
              "local_app_server_feature_enablement"]:
    connection.execute('CREATE TABLE "' + table + '" (id INTEGER)')
    connection.execute('INSERT INTO "' + table + '" VALUES (1)')
connection.execute("CREATE TABLE local_thread_catalog_metadata (id INTEGER PRIMARY KEY, catalog_revision INTEGER)")
connection.execute("INSERT INTO local_thread_catalog_metadata VALUES (1, 9)")
connection.commit()
connection.close()
'@
    $null = & python -c $createSource $stateDatabase $catalogDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Could not create SQLite fixtures.' }

    $sqlitePlan = Get-AgentStateCleanupPlan -Scope Codex -UserProfileRoot $sqliteProfile -CodexHome $sqliteCodex
    $sqliteResult = Invoke-AgentStateCleanupPlan -Plan $sqlitePlan -Apply -Confirmation 'RESET LOCAL AI SESSIONS' -RunningProcessNames @('explorer')
    Assert-Equal 2 $sqliteResult.DatabaseResults.Count 'Both mixed Codex databases are cleaned'
    Assert-Equal '(0,)' ((& python -m sqlite3 $stateDatabase 'SELECT COUNT(*) FROM threads').Trim()) 'Codex thread table is empty'
    Assert-Equal '(1,)' ((& python -m sqlite3 $stateDatabase 'SELECT COUNT(*) FROM remote_control_enrollments').Trim()) 'Remote control enrollment is preserved'
    Assert-Equal '(0,)' ((& python -m sqlite3 $stateDatabase 'SELECT COUNT(*) FROM agent_job_items').Trim()) 'Agent job session items are empty'
    Assert-Equal '(1,)' ((& python -m sqlite3 $stateDatabase 'SELECT COUNT(*) FROM agent_jobs').Trim()) 'Agent job definitions are preserved'
    Assert-Equal '(0,)' ((& python -m sqlite3 $catalogDatabase 'SELECT COUNT(*) FROM automation_runs').Trim()) 'Automation runs are empty'
    Assert-Equal '(1,)' ((& python -m sqlite3 $catalogDatabase 'SELECT COUNT(*) FROM automations').Trim()) 'Automation definitions are preserved'
    Assert-Equal '(0,)' ((& python -m sqlite3 $catalogDatabase 'SELECT catalog_revision FROM local_thread_catalog_metadata WHERE id=1').Trim()) 'Catalog revision is reset'
} finally {
    if (Test-Path -LiteralPath $sqliteRoot) { Remove-Item -LiteralPath $sqliteRoot -Recurse -Force }
}

$protectedRoot = Join-Path ([IO.Path]::GetTempPath()) "deckle-agent-protected-$([guid]::NewGuid())"
try {
    $protectedCodex = Join-Path $protectedRoot '.codex'
    $null = New-Item -ItemType Directory -Path $protectedCodex -Force
    $protectedDatabase = Join-Path $protectedCodex 'state_5.sqlite'
    New-TestStateDatabase $protectedDatabase @'
CREATE TRIGGER mutate_protected AFTER DELETE ON threads
BEGIN
    UPDATE remote_control_enrollments SET id = id + 1;
END;
'@
    $protectedPlan = Get-AgentStateCleanupPlan -Scope Codex -UserProfileRoot $protectedRoot -CodexHome $protectedCodex
    Assert-Throws {
        Invoke-AgentStateCleanupPlan -Plan $protectedPlan -Apply -Confirmation 'RESET LOCAL AI SESSIONS' -RunningProcessNames @('explorer')
    } 'protected SQLite table changed' 'Protected-table trigger rolls back the database'
    Assert-Equal '(1,)' ((& python -m sqlite3 $protectedDatabase 'SELECT id FROM remote_control_enrollments').Trim()) 'Protected row is unchanged after rollback'
    Assert-Equal '(1,)' ((& python -m sqlite3 $protectedDatabase 'SELECT id FROM threads').Trim()) 'Target row is restored after protected-table rollback'
} finally {
    if (Test-Path -LiteralPath $protectedRoot) { Remove-Item -LiteralPath $protectedRoot -Recurse -Force }
}

$repopulateRoot = Join-Path ([IO.Path]::GetTempPath()) "deckle-agent-repopulate-$([guid]::NewGuid())"
try {
    $repopulateCodex = Join-Path $repopulateRoot '.codex'
    $null = New-Item -ItemType Directory -Path $repopulateCodex -Force
    $repopulateDatabase = Join-Path $repopulateCodex 'state_5.sqlite'
    New-TestStateDatabase $repopulateDatabase @'
CREATE TRIGGER repopulate_threads AFTER DELETE ON threads
BEGIN
    INSERT INTO threads VALUES (99);
END;
'@
    $repopulatePlan = Get-AgentStateCleanupPlan -Scope Codex -UserProfileRoot $repopulateRoot -CodexHome $repopulateCodex
    Assert-Throws {
        Invoke-AgentStateCleanupPlan -Plan $repopulatePlan -Apply -Confirmation 'RESET LOCAL AI SESSIONS' -RunningProcessNames @('explorer')
    } 'expected empty state' 'Target-table trigger rolls back the database'
    Assert-Equal '(1,)' ((& python -m sqlite3 $repopulateDatabase 'SELECT id FROM threads').Trim()) 'Original target row is restored after target postcondition failure'
} finally {
    if (Test-Path -LiteralPath $repopulateRoot) { Remove-Item -LiteralPath $repopulateRoot -Recurse -Force }
}

$driftRoot = Join-Path ([IO.Path]::GetTempPath()) "deckle-agent-drift-$([guid]::NewGuid())"
try {
    $driftCodex = Join-Path $driftRoot '.codex'
    $null = New-Item -ItemType Directory -Path $driftCodex -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $driftCodex 'sessions') -Force
    $driftSession = Join-Path $driftCodex 'sessions\keep-on-failure.jsonl'
    Set-Content -LiteralPath $driftSession -Value 'must survive failed preflight' -Encoding utf8NoBOM
    $driftDatabase = Join-Path $driftCodex 'state_5.sqlite'
    $driftSource = @'
import sqlite3, sys
connection = sqlite3.connect(sys.argv[1])
connection.execute("CREATE TABLE threads (id INTEGER)")
connection.commit()
connection.close()
'@
    $null = & python -c $driftSource $driftDatabase
    $driftPlan = Get-AgentStateCleanupPlan -Scope Codex -UserProfileRoot $driftRoot -CodexHome $driftCodex
    Assert-Throws {
        Invoke-AgentStateCleanupPlan -Plan $driftPlan -Apply -Confirmation 'RESET LOCAL AI SESSIONS' -RunningProcessNames @('explorer')
    } 'Unexpected SQLite schema' 'Unknown SQLite schema stops cleanup'
    Assert-Equal $true (Test-Path -LiteralPath $driftSession) 'Failed preflight writes nothing'
} finally {
    if (Test-Path -LiteralPath $driftRoot) { Remove-Item -LiteralPath $driftRoot -Recurse -Force }
}

$linkRoot = Join-Path ([IO.Path]::GetTempPath()) "deckle-agent-link-$([guid]::NewGuid())"
try {
    $linkCodex = Join-Path $linkRoot '.codex'
    $linkTarget = Join-Path $linkRoot 'external-sessions'
    $null = New-Item -ItemType Directory -Path $linkCodex -Force
    $null = New-Item -ItemType Directory -Path $linkTarget -Force
    Set-Content -LiteralPath (Join-Path $linkCodex 'installation_id') -Value 'test-installation' -Encoding utf8NoBOM
    $null = New-Item -ItemType Junction -Path (Join-Path $linkCodex 'sessions') -Target $linkTarget
    Assert-Throws {
        Get-AgentStateCleanupPlan -Scope Codex -UserProfileRoot $linkRoot -CodexHome $linkCodex
    } 'reparse point' 'Reparse-point cleanup target is rejected'
    Remove-Item -LiteralPath (Join-Path $linkCodex 'sessions') -Force
} finally {
    if (Test-Path -LiteralPath $linkRoot) { Remove-Item -LiteralPath $linkRoot -Recurse -Force }
}

$fakeRepoRoot = Join-Path ([IO.Path]::GetTempPath()) "deckle-agent-fake-repo-$([guid]::NewGuid())"
try {
    $fakeProfile = Join-Path $fakeRepoRoot 'profile'
    $fakeRepo = Join-Path $fakeRepoRoot 'repository'
    $null = New-Item -ItemType Directory -Path $fakeProfile -Force
    $null = New-Item -ItemType Directory -Path (Join-Path $fakeRepo 'projects\real-source') -Force
    Set-Content -LiteralPath (Join-Path $fakeRepo 'CLAUDE.md') -Value 'project instructions' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $fakeRepo 'settings.json') -Value '{"project":true}' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $fakeRepo 'projects\real-source\keep.txt') -Value 'repository work' -Encoding utf8NoBOM
    Assert-Throws {
        Get-AgentStateCleanupPlan -Scope Claude -UserProfileRoot $fakeProfile -ClaudeHome $fakeRepo
    } 'Unrecognized Claude state root' 'A repository cannot masquerade as Claude home'
    Assert-Equal 'repository work' (Get-Content -Raw -LiteralPath (Join-Path $fakeRepo 'projects\real-source\keep.txt')).Trim() 'Rejected repository content is untouched'
} finally {
    if (Test-Path -LiteralPath $fakeRepoRoot) { Remove-Item -LiteralPath $fakeRepoRoot -Recurse -Force }
}

$ancestorRoot = Join-Path ([IO.Path]::GetTempPath()) "deckle-agent-ancestor-$([guid]::NewGuid())"
$ancestorExternal = Join-Path ([IO.Path]::GetTempPath()) "deckle-agent-ancestor-external-$([guid]::NewGuid())"
$ancestorJunction = Join-Path $ancestorRoot 'profile\linked'
try {
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $ancestorJunction) -Force
    $ancestorCodex = Join-Path $ancestorExternal '.codex'
    $null = New-Item -ItemType Directory -Path (Join-Path $ancestorCodex 'sessions') -Force
    Set-Content -LiteralPath (Join-Path $ancestorCodex 'installation_id') -Value 'test-installation' -Encoding utf8NoBOM
    $null = New-Item -ItemType Junction -Path $ancestorJunction -Target $ancestorExternal
    Assert-Throws {
        Get-AgentStateCleanupPlan -Scope Codex -UserProfileRoot (Join-Path $ancestorRoot 'profile') -CodexHome (Join-Path $ancestorJunction '.codex')
    } 'reparse point ancestor' 'A reparse point above the agent root is rejected'
} finally {
    if (Test-Path -LiteralPath $ancestorJunction) { Remove-Item -LiteralPath $ancestorJunction -Force }
    if (Test-Path -LiteralPath $ancestorRoot) { Remove-Item -LiteralPath $ancestorRoot -Recurse -Force }
    if (Test-Path -LiteralPath $ancestorExternal) { Remove-Item -LiteralPath $ancestorExternal -Recurse -Force }
}

Write-Host 'agent-state-maintenance.tests.ps1: PASS' -ForegroundColor Green
