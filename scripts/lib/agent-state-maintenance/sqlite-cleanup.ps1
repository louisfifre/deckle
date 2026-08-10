Set-StrictMode -Version Latest

function Get-SqlitePython {
    $command = Get-Command python -ErrorAction SilentlyContinue
    if (-not $command) { throw 'Python 3 is required to clean mixed Codex SQLite databases safely.' }
    return $command.Source
}

function Invoke-AgentStateSqlite {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][ValidateSet('State', 'Catalog')][string]$Kind,
        [switch]$Apply
    )

    $python = Get-SqlitePython
    $source = @'
import hashlib, json, sqlite3, sys

path, kind, apply_flag = sys.argv[1], sys.argv[2], sys.argv[3] == "true"
required_target = {
    "State": ["thread_dynamic_tools", "thread_spawn_edges", "threads"],
    "Catalog": ["inbox_items", "automation_runs", "thread_timeline_ledger", "local_thread_catalog",
                "local_thread_catalog_sync_state", "local_thread_catalog_hosts",
                "local_thread_catalog_metadata"],
}[kind]
optional_target = {
    "State": ["thread_sections", "agent_job_items"],
    "Catalog": [],
}[kind]
required_preserved = {
    "State": ["_sqlx_migrations", "backfill_state", "remote_control_enrollments"],
    "Catalog": ["automations", "local_app_server_feature_enablement"],
}[kind]
optional_preserved = {
    "State": ["external_agent_config_imports", "agent_jobs"],
    "Catalog": [],
}[kind]

def normalize(value):
    if isinstance(value, bytes):
        return {"bytes": value.hex()}
    return value

def digest_table(connection, table):
    rows = []
    for row in connection.execute('SELECT * FROM "' + table.replace('"', '""') + '"'):
        rows.append(json.dumps([normalize(value) for value in row], sort_keys=True, separators=(",", ":")))
    rows.sort()
    return hashlib.sha256("\n".join(rows).encode()).hexdigest()

uri = "file:" + path.replace("\\", "/") + "?mode=ro" if not apply_flag else path
connection = sqlite3.connect(uri, uri=not apply_flag)
try:
    if apply_flag:
        connection.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        connection.execute("PRAGMA secure_delete=ON")
        connection.execute("BEGIN IMMEDIATE")
    tables = {row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='table'")}
    missing = [name for name in required_target + required_preserved if name not in tables]
    if missing:
        raise RuntimeError("Unexpected SQLite schema; missing: " + ", ".join(missing))
    known = set(required_target + optional_target + required_preserved + optional_preserved + ["sqlite_sequence"])
    unknown = sorted(tables - known)
    if unknown:
        raise RuntimeError("Unexpected SQLite schema; unclassified tables: " + ", ".join(unknown))
    target = required_target + [name for name in optional_target if name in tables]
    preserved = required_preserved + [name for name in optional_preserved if name in tables]
    before = {name: connection.execute('SELECT COUNT(*) FROM "' + name + '"').fetchone()[0] for name in target}
    protected_before = {name: digest_table(connection, name) for name in preserved}
    after = before
    protected_after = protected_before
    if apply_flag:
        for name in target:
            if kind == "Catalog" and name == "local_thread_catalog_metadata":
                connection.execute("DELETE FROM local_thread_catalog_metadata")
                connection.execute("INSERT INTO local_thread_catalog_metadata (id, catalog_revision) VALUES (1, 0)")
            else:
                connection.execute('DELETE FROM "' + name + '"')
        after = {name: connection.execute('SELECT COUNT(*) FROM "' + name + '"').fetchone()[0] for name in target}
        protected_after = {name: digest_table(connection, name) for name in preserved}
        expected = {name: 1 if kind == "Catalog" and name == "local_thread_catalog_metadata" else 0 for name in target}
        if after != expected:
            raise RuntimeError("A target SQLite table did not reach its expected empty state.")
        if kind == "Catalog":
            metadata = connection.execute("SELECT id, catalog_revision FROM local_thread_catalog_metadata").fetchall()
            if metadata != [(1, 0)]:
                raise RuntimeError("Catalog metadata did not reach its expected reset state.")
        if protected_before != protected_after:
            raise RuntimeError("A protected SQLite table changed.")
        connection.commit()
        connection.execute("VACUUM")
        connection.execute("PRAGMA wal_checkpoint(TRUNCATE)")
except Exception:
    if apply_flag:
        connection.rollback()
    raise
finally:
    connection.close()
print(json.dumps({"before": before, "after": after, "protected": protected_before == protected_after}, separators=(",", ":")))
'@
    $applyValue = if ($Apply) { 'true' } else { 'false' }
    $output = @(& $python -c $source $Path $Kind $applyValue 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "SQLite cleanup failed for $Kind database: $($output -join ' ')" }
    return (($output -join [Environment]::NewLine) | ConvertFrom-Json)
}
