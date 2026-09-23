-- Apply ONCE after 002_local_users.sql, BEFORE deploying the role-enabled web app.
-- Previously every enabled account had administrator access; preserve that access.
ALTER TABLE LocalUser
    ADD COLUMN Role VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'ReadOnly';
UPDATE LocalUser SET Role = 'Admin' WHERE Enabled = 1;
-- New registrations explicitly use ReadOnly and Enabled = false.
