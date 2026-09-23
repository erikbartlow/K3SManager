# User roles

Before deploying this version, apply `db/003_user_roles.sql` once to the application database after `002_local_users.sql`. Back up the database first. The migration adds Role and preserves Admin access for existing enabled users; existing disabled users become ReadOnly. Do not rerun the UPDATE later, as it is only intended to migrate the original accounts.

Deploy the rebuilt application and sign in again. Existing JWTs use the old role name and are rejected. Admins can use **Users** to enable registrations and assign **Admin** or **ReadOnly**. New registrations are disabled and ReadOnly; the interactive `--create-admin` command creates enabled Admins.

ReadOnly can view the cluster, namespaces, nodes, workloads, and logs. Cluster changes, joining workers, settings, and user administration require Admin. ReadOnly can sign out. Changes to a user's role or enabled status revoke their existing sessions. Admins cannot disable or demote themselves.

Database access continues through CodedThought.Core, with DbType.String mappings.
