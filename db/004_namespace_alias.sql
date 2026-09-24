-- Apply ONCE after the existing schema, BEFORE deploying the alias-enabled web app.
ALTER TABLE NamespaceProfile
    ADD COLUMN Alias VARCHAR(128) NULL;
