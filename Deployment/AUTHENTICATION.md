# Local authentication and roles

Application pages require an Admin or ReadOnly JWT. Changes and administrative pages require Admin. The login and registration pages,
static assets, `/healthz` and `/readyz` are accessible without a token. Health probes
continue to work before the first administrator is created.

## Deploying the change

1. Apply `db/002_local_users.sql` to the existing K3SManager database. This creates
   the `LocalUser` table without modifying existing data. Then apply `db/003_user_roles.sql` once before deploying; see [User roles](user-roles.md). User reads and writes use
   CodedThought.Core's mapped entities, parameter collections and GenericDataStore.
2. Merge the `Authentication` section from `Deployment/authentication.example.json`
   into the mounted `/home/codedthought/k3s_manage/settings/web/appsettings.json` on
   every eligible worker. Preserve the existing Data, CoreSettings and Kubernetes
   sections. Generate a signing key with `openssl rand -base64 32` and replace the
   placeholder privately. Do not commit the generated key. Use the same key on all
   workers. Issuer and audience must remain consistent across instances.
3. Ensure `Data:Enabled` is true and the named/primary CodedThought.Core database
   connection is configured. Authentication now requires persistence; the old
   unauthenticated read-only mode is no longer available. Invalid/missing JWT
   configuration fails startup instead of exposing the application.
4. Build and deploy a new K3SManager.Web image. Use a new version/tag in
   `Directory.Build.props` and `Deployment/k3smanager.yaml` so `IfNotPresent` does not
   reuse an old image. Apply the deployment and wait for it to become ready.
5. Create an administrator from an interactive terminal in the running container:

   ```bash
   sudo k3s kubectl -n k3smanager exec -it deployment/k3smanager -- \
     dotnet K3SManager.Web.dll --create-admin
   ```

   This command prompts for a username and a password twice (password input is
   hidden). Passwords must contain 15–128 characters. Usernames allow 1–64 ASCII
   letters, numbers, periods, underscores and hyphens, and are normalized to upper
   case. Duplicate usernames are rejected. The command exits without starting a
   second web server. There are no default accounts. Accounts created through this
   operator command are enabled Admins; public registration creates disabled ReadOnly accounts.
6. Open `https://k3s.codedthought.info/account/login` and sign in.

For local development, configure the signing key through .NET user-secrets and use
the HTTPS launch URL. Create the account with:

```powershell
dotnet run --project src/K3SManager.Web -- --create-admin
```

The account command connects to whichever database is configured for that process.
It can also create additional administrators. The sign-in page includes a Register
link. Registration stores a hashed password through CodedThought.Core and sets
`Enabled = false` on the server, regardless of submitted form fields. New accounts
cannot sign in until an Admin enables them on the Users page. Admins can also assign roles there. Registration
uses HTTPS, antiforgery protection and the same rate limit as login. There is no self-service
password reset. To recover access, an operator can create a new administrator via
the same command.

## Session behavior

Passwords use ASP.NET Core's salted PBKDF2 password hasher with 210,000 iterations.
JWTs are signed with HS256 and validate signature, issuer, audience, expiration,
the user's current database role, enabled status, and security stamp. The
default lifetime is 30 minutes, configurable from 5–60 minutes, with 30 seconds of
clock skew. There is no refresh token; users sign in again after expiration.

The browser receives the JWT in a Secure, HttpOnly, SameSite=Strict `__Host-` cookie,
never localStorage or a URL. Unsafe MVC actions require antiforgery tokens. Tokens
can also be validated from the Authorization Bearer header; unsafe actions still
require antiforgery protection. Login attempts have a global limit of 10 per minute
per process, shared with registration. This deliberately avoids depending on spoofable forwarded IP headers;
it assumes the existing single-replica deployment. Additional replicas require a
shared/distributed limiter. A busy or attacked login endpoint can exhaust that
shared budget temporarily.

Sign out changes the user's security stamp in the database and expires the cookie,
invalidating all that user's sessions. Disabling a user also prevents subsequent
token use. The signing key can be rotated in the mounted settings followed by a
rollout restart, which signs out every user. Database availability is required for
login and authenticated requests; a database outage does not grant access.

Traefik must terminate HTTPS and forward the original scheme. Direct plain-HTTP
login is rejected. Retain the existing ingress configuration and ensure port 8080
is not separately exposed to untrusted clients.

## Verification

`dotnet test tests/K3SManager.Web.Tests` exercises the actual authentication pipeline
and controllers with a fake user repository. It does not contact MySQL or Kubernetes.
Apply the migration and verify administrator creation/sign-in against your real
database as the deployment smoke test.
