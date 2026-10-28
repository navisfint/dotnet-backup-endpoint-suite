namespace VaultGuard.Common.Data;

/// <summary>系统一（备份还原）数据库建表脚本。</summary>
public static class BackupSchema
{
    public const string Ddl = """
        CREATE TABLE IF NOT EXISTS users (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            username      TEXT NOT NULL UNIQUE,
            password_hash TEXT NOT NULL,
            salt          TEXT NOT NULL,
            display_name  TEXT NOT NULL DEFAULT '',
            created_at    TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS hosts (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            name            TEXT NOT NULL,
            address         TEXT NOT NULL,
            os_type         TEXT NOT NULL DEFAULT 'windows',
            username        TEXT NOT NULL DEFAULT '',
            password_cipher TEXT NOT NULL DEFAULT '',
            remark          TEXT NOT NULL DEFAULT '',
            last_status     TEXT NOT NULL DEFAULT '未检测',
            last_check_at   TEXT NULL,
            created_at      TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS backup_jobs (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            name       TEXT NOT NULL,
            host_id    INTEGER NOT NULL,
            sources    TEXT NOT NULL DEFAULT '',
            repo_path  TEXT NOT NULL DEFAULT '',
            mode       INTEGER NOT NULL DEFAULT 0,
            excludes   TEXT NOT NULL DEFAULT '',
            enabled    INTEGER NOT NULL DEFAULT 1,
            created_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS backup_points (
            id                 INTEGER PRIMARY KEY AUTOINCREMENT,
            job_id             INTEGER NOT NULL,
            job_name           TEXT NOT NULL DEFAULT '',
            host_name          TEXT NOT NULL DEFAULT '',
            mode               INTEGER NOT NULL DEFAULT 0,
            sources            TEXT NOT NULL DEFAULT '',
            started_at         TEXT NOT NULL,
            finished_at        TEXT NULL,
            status             TEXT NOT NULL DEFAULT '进行中',
            copied_count       INTEGER NOT NULL DEFAULT 0,
            copied_bytes       INTEGER NOT NULL DEFAULT 0,
            total_count        INTEGER NOT NULL DEFAULT 0,
            total_bytes        INTEGER NOT NULL DEFAULT 0,
            manifest_path      TEXT NOT NULL DEFAULT '',
            message            TEXT NOT NULL DEFAULT '',
            elapsed_seconds    REAL NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS restore_records (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            point_id    INTEGER NOT NULL,
            files       TEXT NOT NULL DEFAULT '',
            target      TEXT NOT NULL DEFAULT '',
            file_count  INTEGER NOT NULL DEFAULT 0,
            total_bytes INTEGER NOT NULL DEFAULT 0,
            operator    TEXT NOT NULL DEFAULT '',
            status      TEXT NOT NULL DEFAULT '成功',
            created_at  TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS op_logs (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            actor      TEXT NOT NULL DEFAULT '',
            action     TEXT NOT NULL DEFAULT '',
            target     TEXT NOT NULL DEFAULT '',
            detail     TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_points_job   ON backup_points(job_id, started_at DESC);
        CREATE INDEX IF NOT EXISTS idx_points_time  ON backup_points(started_at DESC);
        CREATE INDEX IF NOT EXISTS idx_jobs_host    ON backup_jobs(host_id);
        """;
}

/// <summary>系统二（终端安全）数据库建表脚本。</summary>
public static class EndpointSchema
{
    public const string Ddl = """
        CREATE TABLE IF NOT EXISTS users (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            username      TEXT NOT NULL UNIQUE,
            password_hash TEXT NOT NULL,
            salt          TEXT NOT NULL,
            display_name  TEXT NOT NULL DEFAULT '',
            created_at    TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS agents (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            hostname     TEXT NOT NULL UNIQUE,
            ip_address   TEXT NOT NULL DEFAULT '',
            os_version   TEXT NOT NULL DEFAULT '',
            agent_token  TEXT NOT NULL DEFAULT '',
            agent_version TEXT NOT NULL DEFAULT 'V1.0',
            status       TEXT NOT NULL DEFAULT '离线',
            allowed      INTEGER NOT NULL DEFAULT 1,
            last_seen_at TEXT NULL,
            created_at   TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS policies (
            id                INTEGER PRIMARY KEY AUTOINCREMENT,
            agent_id          INTEGER NOT NULL UNIQUE,
            no_control_panel  INTEGER NOT NULL DEFAULT 0,
            disable_usb       INTEGER NOT NULL DEFAULT 0,
            audit_paths       TEXT NOT NULL DEFAULT '',
            updated_at        TEXT NOT NULL,
            applied_at        TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS audit_events (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            agent_id    INTEGER NOT NULL,
            hostname    TEXT NOT NULL DEFAULT '',
            event_type  TEXT NOT NULL DEFAULT '',
            path        TEXT NOT NULL DEFAULT '',
            target_path TEXT NOT NULL DEFAULT '',
            user_name   TEXT NOT NULL DEFAULT '',
            occurred_at TEXT NOT NULL,
            received_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS op_logs (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            actor      TEXT NOT NULL DEFAULT '',
            action     TEXT NOT NULL DEFAULT '',
            target     TEXT NOT NULL DEFAULT '',
            detail     TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_events_host ON audit_events(hostname, occurred_at DESC);
        CREATE INDEX IF NOT EXISTS idx_events_time ON audit_events(occurred_at DESC);
        CREATE INDEX IF NOT EXISTS idx_events_type ON audit_events(event_type);
        """;
}
