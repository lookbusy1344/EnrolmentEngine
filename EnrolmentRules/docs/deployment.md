# Deployment

The `EnrolmentRules.Web` front-end ships as a container. The image bundles the .NET 10
runtime, so a host does **not** need native .NET 10 support — it only needs to run an
OCI/Docker container. That is the key to "free .NET 10 hosting": containerise, then pick any
container host.

- **Image:** framework-dependent, ReadyToRun publish on
  `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (~141 MB — see
  [Base image / OS](#base-image--os)).
- **Listens on:** `8080` (`ASPNETCORE_HTTP_PORTS=8080`, the .NET image default). Plain HTTP —
  terminate TLS at a proxy or the platform's ingress.
- **User:** non-root (`$APP_UID` from the runtime image).
- **State:** in-memory session only (anonymous facts editing). No database, no volumes.
  Sessions are per-instance and do not survive a restart — this constrains your scaling policy,
  so read [Session state and scaling](#session-state-and-scaling) before deploying.

The `Dockerfile`, `.dockerignore`, and `compose.yaml` live in the `EnrolmentRules/` folder.
**The build context must be that folder** — the Web project pulls in sibling projects, the
shared `Directory.Build.props`, and the `workflows/` + `data/` trees, all under
`EnrolmentRules/`.

> **Monorepo note.** In the public GitHub repo, this project sits in a top-level
> `EnrolmentRules/` subfolder. Every git-connected provider below therefore needs its **root /
> source directory set to `EnrolmentRules`** so it finds this Dockerfile.

### Base image / OS

The runtime stage is **Ubuntu 24.04 "Noble" chiseled**
(`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`): a distroless-style image with no
shell and no package manager, non-root by default. The build stage stays on the default-OS
SDK tag (`mcr.microsoft.com/dotnet/sdk:10.0`) — the publish is RID-targeted, so the two
stages need not share a distro. The architecture matches wherever you build —
`linux/arm64` on Apple silicon, `linux/amd64` on a typical x86-64 server.

The image is chosen for size and cold start. Measured on this app (5 runs, `docker run` →
first HTTP 200 on `/`; the ~150 ms of container-create overhead is included and is
irreducible):

| Variant | Cold start | Image |
|---|---|---|
| `aspnet:10.0` (Debian 13 slim), no R2R | ~600 ms | 267 MB |
| `aspnet:10.0-noble-chiseled`, no R2R | ~600 ms | 136 MB |
| `aspnet:10.0-alpine` + R2R | ~530 ms | 140 MB |
| **`aspnet:10.0-noble-chiseled` + R2R** *(current)* | **~510 ms** | **141 MB** |
| `runtime-deps:10.0-noble-chiseled`, self-contained composite R2R | ~420 ms | 175 MB |

Three results drive the choice. Chiseled and Alpine are within 1 MB of each other, so
Alpine buys no size and its musl allocator starts measurably slower. The base swap alone
gives size but **zero** startup gain — [ReadyToRun](#readytorun) is what cuts cold start.
Self-contained composite R2R is faster still, but costs 34 MB and pins the runtime into
the image (patching .NET then means a rebuild, not a base-image pull).

**No ICU.** Chiseled and Alpine both ship without ICU and therefore default to
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true`. This app is safe under it — it formats and
compares with `InvariantCulture` and ordinal rules throughout, and does no timezone or
localization work. Code that needs real culture data must move to `-chiseled-extra`
(170 MB, ICU included), which also un-sets the invariant default.

Chiseled/Alpine images have no package manager or shell, so add tools in the **build**
stage, not the runtime stage, and debug via `docker logs` rather than `docker exec`. After
changing the base, rebuild and re-run the §1 smoke test.

### Frontend build tooling (Node/pnpm)

`EnrolmentRules.Web.csproj`'s `BuildClientApp` MSBuild target runs `pnpm install`/`pnpm run
build` for the Vue `ClientApp/` during `dotnet restore`/`build`/`publish` — the .NET SDK image
has neither Node nor pnpm, so the build stage installs both before `COPY . .`:

- **Node** is fetched as a plain `nodejs.org` release tarball (`linux-x64`/`linux-arm64`
  matching `$TARGETARCH`) and unpacked into `/usr/local`, rather than added via NodeSource's
  apt repo — the SDK image ships no `gnupg`, which that repo's key-import step needs.
- **pnpm** comes from Corepack, which ships inside the Node tarball. `corepack enable` wires up
  a `pnpm` shim that resolves the exact version pinned by `ClientApp/package.json`'s
  `"packageManager"` field — the version `pnpm-lock.yaml` was generated with — rather than
  whatever `npm i -g pnpm` would fetch on a given day.

Both are build-stage only; the runtime stage never sees Node, pnpm, or `ClientApp/`.

### ReadyToRun

The publish passes `-p:PublishReadyToRun=true` with an explicit `-r <RID>` (derived from
the build's `TARGETARCH`), pre-compiling the app's IL to native code so the JIT does less
work on the cold-start path. Two constraints worth knowing before editing the `Dockerfile`:

- **`PublishReadyToRun` must also be set on `dotnet restore`**, not just `publish` —
  otherwise the crossgen2 package is never restored and publish fails with `NETSDK1094`.
- **Trimming must stay off.** RulesEngine compiles the `workflows/` lambda expressions by
  reflection at runtime; a trimmed assembly graph would break rule evaluation. R2R alone
  does not trim, which is why it is safe here.

Because the publish is RID-targeted it emits an apphost, so the `ENTRYPOINT` invokes
`./EnrolmentRules.Web` directly rather than `dotnet EnrolmentRules.Web.dll`.

---

## 1. Run locally (OrbStack)

OrbStack is a drop-in Docker engine on macOS; the standard `docker` CLI talks to it, so every
command below is plain Docker. Make sure OrbStack is running (`orb start`, or launch the app).

From the `EnrolmentRules/` folder:

```bash
docker build -t enrolment-web .
docker run --rm -p 8080:8080 enrolment-web
```

Open <http://localhost:8080>. Stop with `Ctrl-C`.

The footer reports `0.1.0+unknown` unless you pass the commit in. The build can't derive it
itself: the git root is the *parent* monorepo directory while the build context is this folder,
so no `.git` is ever inside the context.

```bash
docker build --build-arg SOURCE_REVISION_ID="$(git rev-parse --short=10 HEAD)" -t enrolment-web .
```

Or with Compose (builds and runs `compose.yaml`):

```bash
docker compose up -d --build     # start detached
docker compose logs -f           # expect: "Now listening on: http://[::]:8080"
docker compose down              # stop and remove
```

OrbStack also gives every container a direct hostname: with Compose the service is reachable at
`http://web.enrolmentrules.orb.local` without a port map. Development environment (detailed
errors): `docker run --rm -p 8080:8080 -e ASPNETCORE_ENVIRONMENT=Development enrolment-web`.

---

## 2. Publish the image to a registry

Managed platforms that build from your Git repo (§3) don't need this. You need a registry when
deploying to your **own server** (§4) or pushing a locally built image. GitHub Container
Registry (GHCR) is the natural choice here — the repo already lives on GitHub.

```bash
# One-off: log in with a GitHub Personal Access Token that has write:packages
echo "$GITHUB_TOKEN" | docker login ghcr.io -u lookbusy1344 --password-stdin

# Build, tag, push (repo/user names lower-cased for GHCR)
docker build -t ghcr.io/lookbusy1344/enrolment-web:latest .
docker push ghcr.io/lookbusy1344/enrolment-web:latest
```

For multi-arch (e.g. building on an Apple-silicon Mac for an x86-64 server), use Buildx so the
server pulls the right architecture:

```bash
docker buildx build --platform linux/amd64,linux/arm64 \
  -t ghcr.io/lookbusy1344/enrolment-web:latest --push .
```

A GHCR package is private by default; make it public (or grant the server a read token) so the
host can pull it. Building **CI-side** instead — a GitHub Actions workflow that runs the two
commands above on push — is the usual way to keep this hands-off; not included here.

---

## 3. Managed container platforms

All build and run the image for you; you don't manage a server, and they terminate HTTPS at
their ingress. Free tiers and UIs change — confirm current details. Overview, then recipes:

| Host | Deploy style | Notes |
|---|---|---|
| **Google Cloud Run** | `gcloud run deploy --source .` or a pushed image | Generous always-free tier, scales to zero. Default request port is `8080` — no port config needed. |
| **Render** | Connect GitHub repo, Docker runtime | Free web-service tier (sleeps on inactivity). Set root dir `EnrolmentRules`; inject port (below). |
| **Railway** | Connect GitHub repo, Dockerfile auto-detected | Trial credits then usage-based. Set root dir `EnrolmentRules`; injects `PORT`. |
| **Fly.io** | `fly launch` / `fly deploy` from this folder | Small always-on allowance; card required. Global Anycast + automatic TLS. |
| **DigitalOcean App Platform** | Connect repo, Dockerfile detected | Set source dir `EnrolmentRules`, HTTP port `8080`. Managed TLS + domain. |
| **Azure Container Apps** | `az containerapp up` or a pushed image | Monthly free grant, scales to zero. Set target port `8080`. |

### Session state and scaling

**Read this before choosing a host or a scaling policy.** The web front-end keeps its
anonymous facts editing in an **in-memory session, per instance, with no database**, and the
DataProtection keys that encrypt the session cookie are ephemeral (hence the
`Storing keys in a directory ... may not be persisted outside of the container` warning in the
container logs). Two consequences follow, and neither is a bug to be fixed in the app as it
stands:

- **Scaling out loses sessions.** A user's next request can land on an instance that has never
  seen their session, discarding a part-filled form. Pin to a single instance
  (`--max-instances 1` on Cloud Run, one replica elsewhere) and/or enable sticky sessions
  (`--session-affinity`, best-effort).
- **Scaling to zero loses sessions.** When the last instance is destroyed on idle, in-memory
  sessions die *and* the DataProtection keys regenerate, so cookies issued before the shutdown
  can no longer be decrypted. Users who idle out start over. Instance pinning cannot fix this —
  it is inherent to scale-to-zero plus in-memory state.

This trades against cold start, and the trade is worth making deliberately:

| Goal | Setting | Cost |
|---|---|---|
| Cheapest; demo/low traffic | scale to zero (`--min-instances 0`) | ~1-2 s first hit after idle; sessions dropped on scale-down. |
| No cold starts, sessions survive idle | `--min-instances 1` | Billed continuously; leaves the always-free tier. |

The cheapest way to have no cold start is to never go cold. If the app ever needs sessions to
survive restarts and scale-out, the fix is upstream of hosting: back `ISession` and
DataProtection with a shared store (e.g. Redis), after which both constraints disappear and
multi-instance scaling becomes safe.

> **Cold start expectations.** The image is tuned for it (see [Base image / OS](#base-image--os)),
> but a platform's cold start is *its* scheduling and image pull **plus** the app's ~0.5 s
> startup. On Cloud Run expect roughly 1-2 s for the first hit after idle, not ~0.5 s.

### Port conventions (read once)

This image listens on `8080`, which satisfies Cloud Run, Fly, DO App Platform, and Azure
Container Apps as-is. Some hosts (Render, Railway) instead inject the port to bind as a `PORT`
env var, and ASP.NET Core does **not** read `PORT` automatically. On those, add a service env
var so Kestrel binds the expected port:

```
ASPNETCORE_HTTP_PORTS=8080     # then point the platform's routing at 8080
```

Render and Railway route to a port you can set; keeping everything on `8080` (set both the
platform's target port and `ASPNETCORE_HTTP_PORTS` to `8080`) is the least surprising. No
Dockerfile change is ever needed.

### Google Cloud Run

The reference deployment. Install the CLI (`brew install --cask gcloud-cli`), then
`gcloud auth login`. With a billing-enabled project selected, from `EnrolmentRules/`:

```bash
gcloud services enable run.googleapis.com cloudbuild.googleapis.com artifactregistry.googleapis.com

gcloud run deploy enrolment-web \
  --source . \
  --region europe-west2 \
  --allow-unauthenticated \
  --max-instances 1 \
  --session-affinity
```

`--source .` builds the Dockerfile with Cloud Build, pushes to Artifact Registry, and deploys.
Cloud Run routes to `8080` by default, so no port flag is needed. The command prints the public
HTTPS URL. `--max-instances 1` and `--session-affinity` are not optional decoration — see
[Session state and scaling](#session-state-and-scaling). `--allow-unauthenticated` makes the
service **public to anyone with the URL**; drop it for a private service.

`--source .` builds on Cloud Build, which does not set BuildKit's `TARGETARCH`. The
`Dockerfile`'s `${TARGETARCH:-amd64}` default resolves the R2R RID to `linux-x64` there, which
is correct for Cloud Build — no buildx-specific setup needed.

#### Use the deploy script, or the footer reads `0.1.0+unknown`

**Prefer `scripts/deploy-cloudrun.sh` over calling `gcloud run deploy --source .` by hand.** It
runs exactly the command above, but first writes the commit to `.sourcerevision` so the build can
stamp it. Deploying by hand works, but the running service then can't name the build it came from.

```bash
./scripts/deploy-cloudrun.sh              # SERVICE= and REGION= override the defaults
```

The reason it needs a file at all is worth knowing before you try to "simplify" it away:

- **The build can't see git.** The git root is the *parent* monorepo directory while the Docker
  build context is `EnrolmentRules/`, so no `.git` is ever inside the context. This is not
  something `.dockerignore` controls — the checkout is simply above the context root.
- **`--source .` can't pass a build arg.** Cloud Build gets a bare source tree. The
  `--set-build-env-vars` family is documented for *buildpacks* builds, not for feeding `ARG`
  values into a Dockerfile build. So the `Dockerfile`'s `SOURCE_REVISION_ID` arg — fine for a
  `docker build` you invoke yourself (§1, §2) — is unreachable here.

A file carried in the build context is therefore the only channel, which is what
`Directory.Build.props`' `StampGitCommit` reads when git fails. The script deletes it on exit,
and `.gitignore` ignores it.

> **`.gcloudignore` must keep existing.** With no `.gcloudignore`, gcloud auto-generates one from
> `.gitignore` — which lists `.sourcerevision`. That would drop the very file the build needs and
> silently stamp every deploy `unknown` again. The committed `.gcloudignore` exists to make the
> upload set explicit rather than inherited.

To deploy a pre-built image instead: `gcloud run deploy enrolment-web --image ghcr.io/...
--region europe-west2 --allow-unauthenticated` (mirror the image into Artifact Registry first
if Cloud Run can't pull from GHCR).

Useful follow-ups:

```bash
gcloud run services logs read enrolment-web --region europe-west2 --limit 50
gcloud run services delete enrolment-web --region europe-west2     # tear down
```

Signing up with the free trial credit? Read [Cost after the free trial](#cost-after-the-free-trial)
— the service **stops** when the trial ends unless you act.

#### Cost after the free trial

New accounts get a Free Trial credit (~£227 / $300) valid for **90 days**. The credit is not
what keeps this app free, and the trial ending is not a soft landing:

> "All resources you created during the trial are stopped. Further, any data you stored in
> services like Compute Engine is marked for deletion and might be lost."

If you do not upgrade to a paid Cloud Billing account within the 30-day grace period that
follows, "your Free Trial resources are **permanently deleted**". So when the credit runs out
or the 90 days elapse — **whichever comes first** — the demo goes down and is eventually
deleted. **Upgrading to a paid account is what keeps it running**, and doing so does not
consume the credit any faster: you keep unused credit until it expires, and you keep Free
Tier access.

Once upgraded, this app should sit inside the **Always Free** tier, which does not expire.
The relevant monthly allowances:

| Service | Always Free allowance | This app |
|---|---|---|
| Cloud Run requests | 2,000,000 / month | A demo will not approach this. |
| Cloud Run CPU | 180,000 vCPU-seconds | At 1 vCPU, ~50 hours of *request-processing* time. Scale-to-zero means idle costs nothing. |
| Cloud Run memory | 360,000 GiB-seconds | At 512 MiB, ~200 hours of active time. |
| Artifact Registry | 0.5 GB storage | The pushed image is ~56 MB compressed; each retained revision adds. |
| Cloud Build | 2,500 build-minutes (`e2-standard-2`) | Each `--source .` deploy costs a few minutes. |

Two caveats mean "free" here means *pennies*, not a guaranteed zero:

- **`europe-west2` (London) is a Tier 2 pricing region, and the free tier is "applied as a
  spending based discount using Tier 1 pricing".** Tier 2 rates are higher, so that fixed
  discount buys proportionally *less* usage than the headline allowances above. There is still
  ample headroom for a demo, but the margin is narrower than the table implies. Deploying to
  **`europe-west1` (Belgium)** — Tier 1, ~20 ms further from UK users — gets the full value of
  the free tier. `europe-west3` (Frankfurt) and `europe-west6` (Zurich) are also Tier 2;
  `europe-west4` (Netherlands), `europe-west9` (Paris) and `europe-north1` (Finland) are Tier 1.
- **The free egress allowance is "1 GB of outbound data transfer from North America per
  month"** — North America only. Egress from a European region is billed from the first byte
  (order of $0.12/GB). For a small page served to modest traffic this is pennies per month, but
  it is not zero.

Keep it cheap: leave `--min-instances 0` (the default) so idle time is not billed; prune old
Artifact Registry images so storage stays under 0.5 GB; and set a budget alert
(Billing → Budgets & alerts) rather than trusting any of the above to stay current. To stop
all spend, delete the service (`gcloud run services delete`) — the Artifact Registry images
outlive it and must be deleted separately.

#### Two gotchas that will bite you

**1. Cloud Build permission denied on a new project.** The first `--source .` deploy into a
fresh project fails with:

```
PERMISSION_DENIED: Build failed because the default service account is missing required
IAM permissions ... IAM permission denied for service account <N>-compute@developer.gserviceaccount.com
```

New projects no longer auto-grant the default compute service account the Editor role, so
Cloud Build cannot read the source it just uploaded. Grant the builder role once per project,
then re-run the deploy:

```bash
gcloud projects add-iam-policy-binding <project-id> \
  --member="serviceAccount:$(gcloud projects describe <project-id> --format='value(projectNumber)')-compute@developer.gserviceaccount.com" \
  --role=roles/cloudbuild.builds.builder --condition=None
```

**2. `gcloud` authenticated as the wrong Google identity.** If `gcloud projects list` **and**
`gcloud billing accounts list` both return empty while the console plainly shows a project and
free credit, gcloud is authenticated as a different Google account than the browser. A trial
signup always auto-creates "My First Project", so an identity that owns a trial can never
return empty for both — that combination is the signature of an identity mismatch, not of
propagation delay (which shows a *pending* account) or of missing permissions (which returns
`403`, not an empty list). Fix with `gcloud auth login`, taking care to pick the account shown
in the console's top-right chip, then confirm with `gcloud config get-value account`.

### Render

1. **New → Web Service**, connect the GitHub repo.
2. **Runtime: Docker**. **Root Directory: `EnrolmentRules`** (so it finds the Dockerfile).
3. Add env var `ASPNETCORE_HTTP_PORTS=8080` and set the service's port to `8080`.
4. Create. Render builds the image, deploys, and gives an `https://<name>.onrender.com` URL.
   The free instance sleeps after inactivity — first request after idle is slow.

### Railway

1. **New Project → Deploy from GitHub repo**; Railway detects the Dockerfile.
2. In **Settings → Source**, set the **Root Directory to `EnrolmentRules`**.
3. Add env var `ASPNETCORE_HTTP_PORTS=8080` (Railway injects `PORT`; this pins Kestrel to a
   known port and you target that in networking settings), then **Generate Domain**.

### Fly.io

From `EnrolmentRules/` with `flyctl` installed and authenticated:

```bash
fly launch --no-deploy        # detects the Dockerfile, writes fly.toml (pick app name/region)
```

Edit `fly.toml` so the HTTP service targets Kestrel's port:

```toml
[http_service]
  internal_port = 8080
  force_https = true
  auto_stop_machines = true    # scale to zero when idle
  min_machines_running = 0
```

Then `fly deploy`. Fly provisions a `*.fly.dev` hostname with automatic TLS.

### DigitalOcean App Platform

**Create App → GitHub repo → Dockerfile** detected. Set the component's **Source Directory to
`EnrolmentRules`** and **HTTP Port to `8080`**. Deploy; DO builds the image, serves it over
managed HTTPS, and can attach a custom domain.

---

## 4. Deploy on your own Linux server (VPS)

For a plain Ubuntu/Debian box — a DigitalOcean Droplet, Hetzner Cloud, Linode, AWS Lightsail,
or bare metal. The pattern: install Docker, run the container under Compose with a restart
policy, and put a reverse proxy in front for a domain + HTTPS.

### 4.1 Install Docker (once, on the server)

```bash
curl -fsSL https://get.docker.com | sh          # official convenience script
sudo usermod -aG docker "$USER"                  # run docker without sudo; re-login after
```

### 4.2 Get the app onto the server

**Option A — pull a pushed image (recommended).** After §2, on the server:

```bash
mkdir -p ~/enrolment && cd ~/enrolment
# only if the GHCR package is private:
echo "$GITHUB_TOKEN" | docker login ghcr.io -u lookbusy1344 --password-stdin
docker pull ghcr.io/lookbusy1344/enrolment-web:latest
```

**Option B — registry-free transfer** (build on your Mac, ship the tarball over SSH):

```bash
# on your machine, building for the server's architecture:
docker buildx build --platform linux/amd64 -t enrolment-web:latest --load .
docker save enrolment-web:latest | gzip | ssh user@server 'gunzip | docker load'
```

**Option C — build on the server.** `git clone` the repo, `cd EnrolmentRules`, and use the
`compose.yaml`'s `build:` path. Needs the .NET SDK layer to build there (more RAM/CPU); Option A
is lighter on the server.

### 4.3 Run it durably with Compose

Put a `compose.yaml` on the server. For a pulled image, replace the `build: .` line with the
image reference:

```yaml
services:
  web:
    image: ghcr.io/lookbusy1344/enrolment-web:latest
    restart: unless-stopped        # survives crashes and host reboots
    ports:
      - "127.0.0.1:8080:8080"      # bind to loopback; the proxy (4.4) faces the internet
    environment:
      ASPNETCORE_ENVIRONMENT: Production
```

```bash
docker compose up -d
docker compose logs -f            # confirm "Now listening on: http://[::]:8080"
```

`restart: unless-stopped` plus Docker's own systemd unit means the container comes back after a
reboot — no separate systemd unit for the app is needed. (If you'd rather not use Compose,
`docker run -d --restart unless-stopped -p 127.0.0.1:8080:8080 ghcr.io/...` is the equivalent.)

### 4.4 Reverse proxy + automatic HTTPS (Caddy)

The container serves plain HTTP. Caddy is the shortest path to a real domain with automatic
Let's Encrypt certificates. Point your domain's DNS `A` record at the server first, then add
Caddy as a second Compose service:

```yaml
services:
  web:
    image: ghcr.io/lookbusy1344/enrolment-web:latest
    restart: unless-stopped
    expose:
      - "8080"                     # visible to Caddy on the internal network, not published
    environment:
      ASPNETCORE_ENVIRONMENT: Production

  caddy:
    image: caddy:2
    restart: unless-stopped
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data           # persists issued certificates
    depends_on:
      - web

volumes:
  caddy_data:
```

`Caddyfile` (replace the hostname):

```
enrolment.example.com {
    reverse_proxy web:8080
}
```

`docker compose up -d`, then browse to `https://enrolment.example.com` — Caddy obtains and
renews the certificate automatically. (nginx or Traefik work equally well if you already run
one; Caddy just needs the least config.)

### 4.5 Updating

```bash
docker compose pull        # fetch the new image tag
docker compose up -d       # recreate only what changed
docker image prune -f      # reclaim old layers
```

For hands-off updates, a scheduled `docker compose pull && up -d` (cron) or a tool like
Watchtower works; keep the single-replica caveat in mind, since a redeploy drops active
sessions.

---

## Notes and troubleshooting

- **`dotnet run` vs the container.** Running the project source with `dotnet run` sets the
  content root to the *source* directory, where `workflows/`/`data/` don't exist, and fails at
  startup. The container runs the *published* output where those trees are copied alongside the
  DLL, so `ContentRootPath` resolves them — the same reason `README`/`CLAUDE.md` tell you to run
  the compiled binary locally rather than `dotnet run`.
- **Client-side libs.** `wwwroot/lib/` is gitignored and `.dockerignore`d; the LibMan build
  target restores it inside the image during publish. No manual `libman restore` needed.
- **Vue assets.** `wwwroot/app/` is likewise gitignored and `.dockerignore`d; the
  `BuildClientApp` MSBuild target rebuilds it from `ClientApp/` (source + `pnpm-lock.yaml`)
  during the same publish, using the Node/pnpm toolchain described above. No compiled frontend
  output is ever checked in or expected to already exist in the build context.
- **Image size.** Framework-dependent image (shares the aspnet runtime layer). For a
  smaller/standalone image, switch to a self-contained, trimmed publish — not done here because
  it complicates the build for little benefit on these hosts.
- **Health checks.** No dedicated health endpoint; a platform or proxy health check can hit `/`
  (returns `200`).
- **Architecture mismatch.** An image built on Apple silicon (`arm64`) won't run on an x86-64
  (`amd64`) server. Build with `--platform linux/amd64` or a multi-arch Buildx image (§2/§4.2).
