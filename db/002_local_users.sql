-- Apply to the same database as 001_schema.sql before deploying JWT authentication.
-- Accounts are created with the application's --create-admin command, never plaintext SQL passwords.
CREATE TABLE IF NOT EXISTS LocalUser (
    UserName VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    PasswordHash VARCHAR(512) NOT NULL,
    SecurityStamp VARCHAR(64) NOT NULL,
    Enabled BOOL NOT NULL DEFAULT 1,
    PRIMARY KEY (UserName)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;
