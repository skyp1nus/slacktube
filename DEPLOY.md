# SlackTube — production deploy

SlackTube runs on the shared Hetzner host alongside comment-bridge, behind one shared Caddy and
one shared Postgres + Redis. This app's stack owns **no** infrastructure of its own.

```
                         ┌──────────────────────── /opt/shared ───────────────────────┐
  Internet ──► :80/:443 ─┤ shared-caddy-1   postgres (alias)   redis (alias)           │
                         └───────┬───────────────┬───────────────┬─────────────────────┘
                                 │ web network    │               │
        slacktube.danielhub.dev  │                │               │
           /slack/* /google/* ───┼──► slacktube-backend-1:8080 ───┤ (Postgres: db slacktube)
           everything else ──────┼──► slacktube-frontend-1:3000   └ (Redis: dedup/quota/cancel)
                                 │           │ BFF server-side: BACKEND_URL → backend-1:8080
```

## Topology & routing

| Path on `slacktube.danielhub.dev` | Goes to | Why |
|---|---|---|
| `/slack/*`  | `slacktube-backend-1:8080` | Slack Events/Interactivity/OAuth hit the backend directly |
| `/google/*` | `slacktube-backend-1:8080` | Google OAuth start + redirect |
| everything else (`/`, `/login`, `/api/*`, …) | `slacktube-frontend-1:3000` | Next.js panel **and its BFF** |

> **`/api/*` is the frontend, not the backend.** SlackTube's `/api/admin/[...path]`, `/api/login`,
> `/api/logout` are Next.js route handlers (the BFF). The admin proxy forwards to the backend
> *server-side* with `X-Admin-Token`; the browser never talks to the backend admin API directly.
> This is the one intentional deviation from the comment-bridge template (there `/api/*` → backend).

## Two ways the web tier reaches the backend

1. **Server-side BFF** — `BACKEND_URL=http://slacktube-backend-1:8080` (runtime env). The admin
   proxy + auth use this; it stays on the internal `web` network.
2. **Browser OAuth nav** — `NEXT_PUBLIC_BACKEND_URL=https://slacktube.danielhub.dev` (**build-arg**,
   inlined). The "Connect Google/Slack" buttons are client components that set
   `window.location` to `${NEXT_PUBLIC_BACKEND_URL}/google|slack/oauth/start`. Baked at build in
   `ci.yml`; a runtime value cannot override an inlined one.

## CI/CD

- `.github/workflows/ci.yml` — `backend` (dotnet restore/build/test on `SlackTube.slnx`, .NET 10),
  `frontend` (bun build), and `docker` (push `ghcr.io/skyp1nus/slacktube/{backend,frontend}`).
  The `docker` job runs **only on `main`**; on `dev`/PRs the first two jobs just gate correctness.
- `.github/workflows/deploy.yml` — fires after CI succeeds on `main`. scp's the compose + Caddy
  snippet to `/opt/slacktube`, pulls + `up -d`, copies `slacktube.caddy` into `/opt/shared/sites/`
  and hot-reloads the shared Caddy.

> `dev` is currently 17 commits ahead of `main`. The `docker`/`deploy` jobs only run on `main`, so
> nothing ships until you merge `dev → main`.

## GitHub Secrets (repo: skyp1nus/slacktube)

Reuse the same values as comment-bridge:

| Secret | Purpose |
|---|---|
| `DEPLOY_HOST` | Hetzner host/IP (SSH as `deploy`) |
| `DEPLOY_SSH_KEY` | private key for the `deploy` user |

`GITHUB_TOKEN` is automatic (GHCR push). Make the two new GHCR packages **public**, or the server
needs `docker login ghcr.io` once (a PAT with `read:packages`).

## Server `/opt/slacktube/.env`

See [`deploy/.env.prod.example`](deploy/.env.prod.example) for the full annotated list. Generate
fresh secrets — never reuse the dev values:

```sh
openssl rand -base64 48   # TokenEncryption__Key, SESSION_SECRET
openssl rand -hex 32      # Admin__ApiKey
```

`Password` in `ConnectionStrings__Postgres` must equal `SLACKTUBE_DB_PASSWORD` from `/opt/shared/.env`.

## First-time bring-up (after the shared stack exists)

The shared stack (`/opt/shared`) and the `web` network must already be up — see the infra repo
runbook. Then:

```sh
sudo mkdir -p /opt/slacktube && cd /opt/slacktube
# put .env here (from deploy/.env.prod.example), then let the deploy workflow ship the rest,
# or for a manual first run:
#   docker compose -f docker-compose.prod.yml pull && docker compose -f docker-compose.prod.yml up -d
#   install -D -m 644 slacktube.caddy /opt/shared/sites/slacktube.caddy
#   docker exec shared-caddy-1 caddy reload --config /etc/caddy/Caddyfile
```

The backend runs EF `Migrate()` on boot and self-heals interrupted jobs. If the shared Postgres
isn't reachable yet it will crash-loop (`restart: unless-stopped`) until it is — by design.

## External config to register manually

| Where | Value |
|---|---|
| Google Cloud console → Authorized redirect URI | `https://slacktube.danielhub.dev/google/oauth/callback` |
| Slack app → OAuth redirect URL | `https://slacktube.danielhub.dev/slack/oauth/callback` |
| Slack app → Event Subscriptions Request URL | `https://slacktube.danielhub.dev/slack/events` |
| Slack app → Interactivity Request URL | `https://slacktube.danielhub.dev/slack/interactivity` |

## Notes / optional hardening

- **No volumes.** Secrets are encrypted with a key derived from `TokenEncryption__Key`, so there is
  no Data Protection key-ring to persist. The temp download dir is ephemeral scratch.
- **Forwarded headers (optional).** Behind TLS-terminating Caddy, `HttpRequest.IsHttps` is `false`,
  so OAuth state cookies are set without `Secure` (still function over HTTPS). Add
  `UseForwardedHeaders` if you want `Secure` honored. Not a blocker.
- **Memory.** `next start` (320m) and the .NET backend with workstation GC (640m) fit the budget for
  single-user traffic. If the frontend is ever tight, switch the web Dockerfile to Next standalone
  output (`output: 'standalone'` + `bun server.js`, as comment-bridge does).
