# K3S Manager

A sysadmin console for a k3s cluster: ASP.NET Core 10 MVC, organised by namespace, with
worker-node management. MySQL holds the small amount of state Kubernetes does not
(settings, namespace metadata, audit trail).

## Layout

```
K3SManager.sln
Directory.Build.props        shared TFM, nullable, analysis level
Directory.Packages.props     every package version, in one place
db/001_schema.sql            MySQL schema + seed rows
Deployment/
  build-and-push-image.ps1   build, tag and push the ARM64 image
  docker-buildx-arm64.ps1    buildx builder check, dot-sourced by the above
  Dockerfile_Web_Arm64       build context is the solution root
  k3smanager.yaml            namespace, RBAC, Deployment, Service, IngressRoute
src/
  K3SManager.Core            models, service contracts, options - no dependencies
  K3SManager.Kubernetes      the API server client and every read/write on top of it
  K3SManager.Data            MySQL repositories, audit runner
  K3SManager.Web             MVC app: controllers, views, CSS
```

`Core` knows nothing about Kubernetes or MySQL. `Web` depends on the three below it and on no
client library directly, so swapping the persistence layer touches one project.

## Running it

1. Create the database and run the schema:

   ```sql
   CREATE DATABASE k3smanager CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
   ```

   ```bash
   mysql -u root -p k3smanager < db/001_schema.sql
   ```

2. Point CodedThought.Core at the database. Connections live in the `CoreSettings:Connections`
   section of `src/K3SManager.Web/appsettings.json`, which ships with an empty `ConnectionString`.
   Either put it in user-secrets for development, or set the environment variable, which is what
   the container does:

   ```bash
   export CoreSettings__Connections__MYSQL__ConnectionString="Server=localhost;Port=3306;Database=k3smanager;Uid=k3sm;Pwd=..."
   ```

   `AddCoreSettingsConfiguration` appends `appsettings.json` to the end of the configuration chain,
   so `Program.cs` re-applies user-secrets and environment variables after it - otherwise the file
   would outrank both. `Data:ConnectionName` selects which connection to use; leave it empty to take
   whichever one is marked `Primary`.

   Chorli-style Base64 encrypted connection strings are decrypted through
   `CodedThought.Core.Security.Encryption.DecryptString` using `AppSettings:EncryptionKey`
   from the same configuration. Plain connection strings still work unchanged.
   Keep this database encryption key separate from `Authentication:Jwt:SigningKey`.

3. Point it at the cluster. Either copy `/etc/rancher/k3s/k3s.yaml` off the server and set
   `Kubernetes:KubeConfigPath`, or run the app in the cluster and let it use its service account.
   When the kubeconfig says `127.0.0.1`, set `Kubernetes:ServerOverrideUrl` to the real address.

4. `dotnet run --project src/K3SManager.Web`

Local authentication requires a usable database configuration and the `LocalUser`
table. Follow [the authentication setup](Deployment/AUTHENTICATION.md) to configure
JWT signing and create an administrator before signing in.

## Building the image

Same shape as the other Deployment folders:

```powershell
# from anywhere
.\Deployment\build-and-push-image.ps1            # build, tag, push
.\Deployment\build-and-push-image.ps1 -Login     # docker login docker.io first
.\Deployment\build-and-push-image.ps1 -WhatIf    # show the tags without building
```

It reads the version, asserts a `linux/arm64` buildx builder, then builds
`k3smanager-build/k3smanager-web:<version>-arm64`, tags it
`codedthought/k3smanager-web:v<version>-arm64`, and pushes that.

One difference from the others: this solution keeps `<Version>` in `Directory.Build.props` so
every assembly is stamped from one place, so the script checks the project file first and falls
back to the props file. Bump the version there and the tag follows.

`Deployment/k3smanager.yaml` pins that image tag, so update it when you cut a new version.

### Deployment settings file

Like chorli, the deployment mounts a node-local settings file read-only at
`/app/appsettings.json`. Before deploying, copy `src/K3SManager.Web/appsettings.json`
to `/home/codedthought/k3s_manage/settings/web/appsettings.json` on every ARM64 node
where the pod can run. Configure its database connection for the cluster, and ensure
the container's app user can read the file. The host path uses `type: File`, so a
missing file prevents the pod from starting.

Leave `Kubernetes:KubeConfigPath` and `Kubernetes:ServerOverrideUrl` empty to use
the pod's service account. The database connection is read from `CoreSettings` in
the mounted settings file; the deployment does not reference a database Secret.
After replacing the settings file,
restart the deployment to ensure the pod uses the replacement:

```bash
sudo k3s kubectl apply -f Deployment/k3smanager.yaml
sudo k3s kubectl -n k3smanager rollout restart deployment/k3smanager
sudo k3s kubectl -n k3smanager rollout status deployment/k3smanager
```

## What it does

**Overview** - node readiness, pod phases, CPU and memory commitment against allocatable,
warning events, recent operator actions.

**Namespaces** - list with pod counts and resource requests, owner/environment metadata stored
locally, create with labels, resource quotas, per-namespace workloads, pods, services and events.
Scale, rolling-restart and pod delete from the namespace page. Delete is behind a settings flag
*and* requires the name to be typed.

**Nodes** - status, roles, kubelet version, capacity vs requests, live usage when metrics-server
is present, conditions, taints and labels. Cordon, uncordon, drain (real evictions, DaemonSet
pods left alone, PodDisruptionBudget refusals reported rather than forced), taint and label
editing, and node removal.

**Add a worker node** - there is no API for this: a node joins by running the k3s agent. The page
renders the exact `curl ... K3S_URL=... K3S_TOKEN=... sh -` command with your node name, labels
and taints. The token comes from `SystemSettings`, never from source or a config file.

**Settings** - the `SystemSettings` table, with secret rows masked, plus the audit trail.

## Notes on the implementation

- Every cluster call is async and takes a `CancellationToken` through to the API server.
- List reads go through one cache (`Kubernetes:CacheSeconds`, default 10) with single-flight per
  key, so one dashboard render is one set of list calls rather than one per widget. Any mutation
  invalidates it.
- Logging is structured throughout, JSON to stdout outside Development. Secret values are never
  logged and never written to an audit row.
- Mutations run through `IAuditedActionRunner`, which writes the audit row on success *and* on
  failure; a failed audit write never masks the original exception.
- The client is a singleton (it owns an `HttpClient`); services are scoped.
- `/healthz` is liveness, `/readyz` pings the API server.

### The data layer

Persistence goes through CodedThought.Core. `K3SManager.Data/Entities` carries the three
`[DataTable]`/`[DataColumn]` classes and the project declares `DataAwareAssemblyAttribute` in its
csproj so the ORM map picks them up.

The **host** owns the provider registration, because a provider's lifetime is a hosting decision:

```csharp
builder.Services.AddMemoryCache();
builder.Services.AddTransient<IDatabaseObject, K3SManagerMySqlProvider>();
```

Transient, not `AddCoreDataProvider`. That helper registers the provider as a singleton, and the
MySql provider holds one `MySqlConnection` on it - which would leave every request in the process
sharing a single connection. Transient gives each data store its own.

`CoreDataStore` is the only class that touches `GenericDataStore`. It is registered transient too,
so each repository owns one store, one provider and one connection, closed when the request scope
disposes it. It carries a per-instance gate for the case where one repository is called
concurrently (`NodeService.BuildJoinInstructionAsync` reads three settings under one
`Task.WhenAll`), and it dispatches to the thread pool because `GenericDataStore` is synchronous -
the request thread is released, a pool thread blocks on the socket. If the framework gains an async
surface, that class is the only file that changes.

`SystemSettings` has a natural string key, so `GenericDataStore.Save` would always resolve it as an
update; `NamespaceProfile` has an identity key but is addressed by namespace name. Both repositories
therefore read first and then call `SaveNew` or `SaveExisting` explicitly rather than `Save`.

## Risks worth knowing

- **Drain is genuinely disruptive.** It evicts pods; a single-replica workload goes down until it
  is rescheduled. On a three-node Pi cluster, draining one node can leave nothing with capacity.
- **Namespace delete is unrecoverable** and takes PVCs with it. Two gates, deliberately.
- **Single instance assumed.** The cluster read cache is per process, so behind two replicas two
  operators can briefly see different numbers. Nothing is cached across mutations, so no correctness
  issue - but if you scale out, drop `CacheSeconds` to 0 or move to a shared cache.
- **One connection per repository per request.** Transient providers mean a page that touches
  settings, namespace metadata and the audit trail opens three connections. That is what MySQL
  connection pooling is for, but keep it in mind if you ever raise the replica count.
- **Local administrator authentication.** Pages and actions require a validated JWT.
  See [authentication setup and session behavior](Deployment/AUTHENTICATION.md).
  All local accounts have administrator access, and the audit trail records their usernames.
- **RBAC is broad by necessity.** `Deployment/k3smanager.yaml` grants only the verbs the UI actually
  uses; trim it further if you disable features.

## Package versions

`Directory.Packages.props` is the only file carrying a version. `KubernetesClient` was pinned
without access to nuget.org and may need a bump. The three CodedThought versions were read from the
repo at `..\CodedThought.Core` (`10.0.1.3`, `10.0.1`, `1.0.1.3`); if they are not published yet,
`K3SManager.Data.csproj` carries the `ProjectReference` paths to use instead.
