-- K3S Manager - MySQL 8 schema
--
-- Run against a database you created beforehand, e.g.
--   CREATE DATABASE k3smanager CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
--   mysql -u root -p k3smanager < 001_schema.sql
--
-- No credentials appear in this file or anywhere in source. The connection string
-- is supplied through configuration (ctSettings.json or the matching environment
-- variable) and every other secret lives in SystemSettings.
--
-- MySQL 8 notes:
--   * Boolean columns are declared BOOL rather than TINYINT(1). Both store a
--     tinyint(1), but an explicit display width raises warning 1681
--     (ER_WARN_DEPRECATED_INTEGER_DISPLAY_WIDTH) on 8.0.17 and later. BOOL keeps
--     the tinyint(1) that MySqlConnector uses to detect booleans.
--   * The seed INSERT uses INSERT IGNORE rather than ON DUPLICATE KEY UPDATE ...
--     VALUES(), which is deprecated from 8.0.20. Re-running the script therefore
--     leaves existing rows alone instead of resetting their descriptions.
--   * Timestamps are written by the application in UTC. UTC_TIMESTAMP() is used
--     for the seed rows so they do not pick up the server's local time zone.

SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS SystemSettings (
    SettingKey      VARCHAR(128)    NOT NULL,
    SettingValue    TEXT            NULL,
    Description     VARCHAR(512)    NULL,
    IsSecret        BOOL            NOT NULL DEFAULT 0,
    UpdatedUtc      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (SettingKey)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS NamespaceProfile (
    Id              BIGINT          NOT NULL AUTO_INCREMENT,
    NamespaceName   VARCHAR(253)    NOT NULL,
    Owner           VARCHAR(128)    NULL,
    Description     VARCHAR(512)    NULL,
    Environment     VARCHAR(32)     NULL,
    IsPinned        BOOL            NOT NULL DEFAULT 0,
    CreatedUtc      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedUtc      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (Id),
    UNIQUE KEY UX_NamespaceProfile_Name (NamespaceName)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS AuditLog (
    Id              BIGINT          NOT NULL AUTO_INCREMENT,
    Action          VARCHAR(64)     NOT NULL,
    TargetKind      VARCHAR(64)     NOT NULL,
    TargetName      VARCHAR(253)    NOT NULL,
    TargetNamespace VARCHAR(253)    NULL,
    Detail          TEXT            NULL,
    ActorName       VARCHAR(128)    NOT NULL,
    ActorAddress    VARCHAR(64)     NULL,
    Succeeded       BOOL            NOT NULL DEFAULT 0,
    Error           TEXT            NULL,
    CreatedUtc      DATETIME        NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (Id),
    KEY IX_AuditLog_CreatedUtc (CreatedUtc DESC),
    KEY IX_AuditLog_Target (TargetKind, TargetName)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

-- Seed rows. Secret and URL values are deliberately empty; fill them in from the
-- Settings page. Existing rows are left untouched on a re-run.
INSERT IGNORE INTO SystemSettings (SettingKey, SettingValue, Description, IsSecret, UpdatedUtc) VALUES
    ('Cluster.DisplayName',        'k3s cluster', 'Name shown in the header and page titles.',                               0, UTC_TIMESTAMP()),
    ('K3s.ServerUrl',              NULL,          'https://<server>:6443 - used to render the agent join command.',          0, UTC_TIMESTAMP()),
    ('K3s.NodeToken',              NULL,          'Contents of /var/lib/rancher/k3s/server/node-token on the k3s server.',   1, UTC_TIMESTAMP()),
    ('K3s.InstallChannel',         'stable',      'INSTALL_K3S_CHANNEL value for the join command (stable, latest, v1.31).', 0, UTC_TIMESTAMP()),
    ('Ui.RefreshSeconds',          '30',          'Dashboard auto-refresh interval. 0 disables it.',                         0, UTC_TIMESTAMP()),
    ('Safety.AllowNamespaceDelete','false',       'Namespace deletion is refused unless this is true.',                      0, UTC_TIMESTAMP()),
    ('Safety.AllowNodeDrain',      'true',        'Cordon and drain are refused unless this is true.',                       0, UTC_TIMESTAMP());

-- Verify:
--   SHOW TABLES;
--   SELECT SettingKey, IsSecret FROM SystemSettings ORDER BY SettingKey;
