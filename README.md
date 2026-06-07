# SlackTube

Automated publishing of videos to YouTube from Slack. Someone posts a templated message
containing a Google Drive video link, tags, and a description; a bot downloads the video
from Drive, uploads it to YouTube as **private**, and reports back in Slack with a live,
updating queue + progress.

Slack workspaces are connected via **OAuth install**; multiple **Google/YouTube accounts**
can be connected; and a **mapping** routes each Slack channel to a specific account.

---

## Architecture

A single ASP.NET (.NET 10) process hosts both the HTTP API **and** the Hangfire worker.

```
Slack  ──/slack/events──────►  verify sig → dedup(event_id) → ACK<3s → enqueue ingest
       ──/slack/interactivity►  verify sig → cancel / confirm / decline buttons
       ──/slack/oauth/*────────►  OAuth v2 install → store workspace + ENCRYPTED bot token
Google ──/google/oauth/*─────►  consent → store ENCRYPTED refresh token (one per account)

Hangfire pipeline (durable, survives restarts):
  ① SlackIngestService  resolve channel→account mapping → parse → validate vs Drive →
                        confirm OR enqueue upload (tagged with the target Google account)
  ② UploadJobHandler    cancel-check → Drive download → quota guard → YouTube upload
                        → live Slack status (delete+repost on queue change, throttled
                          chat.update during transfer), all per the job's account/channel

Stores:  PostgreSQL (workspaces+channels, google accounts, mappings, jobs+history)
         Redis      (event_id dedup TTL, per-account daily quota, cancel flags, status ts)
```

| Layer | Tech |
|---|---|
| Backend | ASP.NET / .NET 10, C#, minimal APIs |
| Background jobs | Hangfire + PostgreSQL storage |
| DB | PostgreSQL via EF Core 10 (Npgsql) |
| Cache/queue state | Redis (StackExchange.Redis) |
| Google | `Google.Apis.YouTube.v3`, `Google.Apis.Drive.v3`, `Google.Apis.Auth` |
| Slack | raw `HttpClient` (signature verify, Web API, Block Kit) |
| Admin panel | Next.js 16 (App Router, TS) + **Bun** + shadcn/ui + Tailwind v4 |

### Repo layout

```
backend/
  SlackTube.slnx
  src/SlackTube.Api/
    Domain/           UploadJob, JobState machine, GoogleToken, AppSettings
    Data/             AppDbContext, design-time factory, Migrations/
    Configuration/    strongly-typed options
    Services/
      Secrets/        AES-GCM secret protector
      Settings/       settings store (DB ⊕ config fallback)
      Slack/          sig verify, template parser, Web API client, Block Kit, ingest, status
      Google/         credential factory, OAuth, Drive download, YouTube upload
      Jobs/           job service, worker handler, quota, dedup, cancel flags, progress
    Endpoints/        Slack, Google OAuth, Admin API
    Program.cs        composition root
  tests/SlackTube.Tests/   parser unit tests (xUnit)
web/                  Next.js admin panel (Bun)
scripts/dev-infra.sh  start/stop local Postgres + Redis
.env.example          backend config contract  (web/.env.example for the panel)
```

---

## Quick start — `docker compose up`

Brings up the whole stack as one project (`slacktube`): Postgres + Redis + the API + the
admin panel.

```bash
docker compose up --build
```

| Service | URL |
|---|---|
| Admin panel | http://localhost:3000 — login `admin` / `admin` |
| Backend API | http://localhost:5080 — `/health`, `/hangfire` |

Override the dev defaults with env vars (or a root `.env` that compose reads) before `up`:
`TOKEN_ENCRYPTION_KEY`, `ADMIN_USER`, `ADMIN_PASSWORD`, `BACKEND_ADMIN_TOKEN`,
`SESSION_SECRET`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `SLACK_SIGNING_SECRET`,
`SLACK_CLIENT_ID`, `SLACK_CLIENT_SECRET`. Slack + Google are then connected from the admin
panel. EF migrations auto-apply on API startup.

Stop with `docker compose down` (add `-v` to also drop the Postgres volume).

> The sections below cover running each piece **individually** (without Docker) — e.g. for
> backend development with hot reload.

---

## Prerequisites

- **.NET SDK 10** (`dotnet --version` → 10.x)
- **Bun** ≥ 1.3 (`bun --version`)
- **Docker** (for Postgres + Redis)
- `dotnet-ef` global tool (only if you generate new migrations): `dotnet tool install --global dotnet-ef`

---

## 1. Start Postgres + Redis

```bash
./scripts/dev-infra.sh start     # one container (slacktube-deps): Postgres :5432 + Redis :6379
./scripts/dev-infra.sh stop      # tear down
```

> For dev convenience both run in a **single container**: `postgres:16-alpine` as the base,
> with Redis added via `apk add redis` and run as a background daemon. The apk trick avoids
> pulling the official `redis` image (its Docker blob CDN hangs on some networks). In
> production run Postgres and Redis as **separate** services. `postgres:16-alpine` must be
> available locally (`docker pull postgres:16-alpine` if not).

Defaults match the connection strings in `appsettings.json`:
`Host=localhost;Database=slacktube;Username=slacktube;Password=slacktube` and `localhost:6379`.

---

## 2. Run the backend

```bash
cp .env.example .env            # then fill in the values you have (see below)
# minimal required to boot + use the admin API:
export TokenEncryption__Key='a-long-random-32+char-secret'
export Admin__ApiKey='choose-a-shared-token'

dotnet run --project backend/src/SlackTube.Api --no-launch-profile
# → http://localhost:5080   (EF migrations auto-apply on startup)
```

Useful endpoints: `GET /health`, Hangfire dashboard at `/hangfire` (local-request-only).

### Backend configuration (env or `.env`; ASP.NET maps `__` → nested keys)

| Key | Purpose |
|---|---|
| `ConnectionStrings__Postgres` | Postgres connection string |
| `ConnectionStrings__Redis` | Redis connection string |
| `TokenEncryption__Key` | **Required.** ≥16-char secret; AES key for encrypting tokens at rest |
| `Slack__SigningSecret` | App signing secret (env-only; verifies request signatures) |
| `Slack__ClientId` / `Slack__ClientSecret` / `Slack__RedirectUri` | Slack OAuth-install app (redirect = `…/slack/oauth/callback`) |
| `Google__ClientId`, `Google__ClientSecret` | Optional seed/fallback OAuth client — auto-migrated to a "Default (env)" project on startup. Manage projects in the **Projects** tab. |
| `Google__RedirectUri` | Global OAuth callback (`…/google/oauth/callback`); **every** project's OAuth client must register this exact URI |
| `Admin__Username`, `Admin__Password` | Single-admin login (also usable as the admin-API key) |
| `Admin__ApiKey` | Token the web panel sends as `X-Admin-Token` (falls back to `Admin__Password`) |
| `App__PublicBaseUrl` | Public URL of the backend (for OAuth redirect/links) |
| `App__AdminPanelUrl` | Web panel origin — OAuth callbacks bounce the browser back here |
| `App__TempDownloadDir` | Where Drive files are streamed before upload (cleaned up after each job) |
| `App__YouTubeDailyUploadLimit` | Per-project daily upload cap — `videos.insert` calls/PT-day (Google default **100**). The real upload gate. |
| `App__YouTubeDailyQuotaUnits` | Informational non-upload unit pool (10000) for list/search etc. — uploads do **not** draw from it. |

---

## 3. Run the admin panel

```bash
cd web
cp .env.example .env.local      # set ADMIN_USER/ADMIN_PASSWORD + BACKEND_ADMIN_TOKEN (= backend Admin__ApiKey)
bun install
bun run dev                     # → http://localhost:3000
```

Sign in, then: **Slack** tab → *Connect Slack* (OAuth install) and invite the bot to your
channels → **Projects** tab → *Add project* (one OAuth client per Cloud project) → **Accounts**
tab → *Connect Google account* (pick a project, one or more channels) → **Mapping** tab → route
each Slack channel to a Google account. The dashboard shows connection status + job history.

---

## 4. Slack app setup (OAuth install)

1. Create a Slack app. From **Basic Information** copy **Client ID**, **Client Secret**, and
   **Signing Secret** → `Slack__ClientId` / `Slack__ClientSecret` / `Slack__SigningSecret`.
2. **OAuth & Permissions** → Redirect URLs: add `https://<public-backend>/slack/oauth/callback`
   (matches `Slack__RedirectUri`). Bot token scopes (public channels):
   `chat:write,channels:read,channels:history`.
   (Add `groups:read,groups:history` — here, in `InstallScopes`, and in `SlackClient.ListChannelsAsync` — for private channels.)
3. **Event Subscriptions** → Request URL `https://<public-backend>/slack/events` → subscribe to
   bot events `message.channels` (+ `message.groups` if private channels are enabled).
4. **Interactivity & Shortcuts** → Request URL `https://<public-backend>/slack/interactivity`.
5. In the admin panel **Slack** tab click **Connect Slack** → approve the install. Invite the
   bot to each channel, then **Refresh channels** (the bot only sees channels it has joined).

> Local dev: expose `http://localhost:5080` with a tunnel (e.g. `ngrok http 5080`) and use the
> public URL for the Redirect URL + Request URLs.

---

## 5. YouTube projects + Google accounts

YouTube enforces uploads **per Google Cloud project (OAuth client)** — `videos.insert` has its own
daily bucket (default **~100 uploads/day**), separate from the ~10k-unit pool used by other endpoints.
SlackTube manages a **pool of OAuth clients** (one per Cloud project) from the **Projects** tab;
connecting the **same channel through several projects** stacks their buckets, and uploads
**rotate** to the next project when one is exhausted (N projects ⇒ N×100 uploads/day to one channel).

**Per Google Cloud project** (repeat for each project you want in the pool):

1. Enable **YouTube Data API v3** + **Google Drive API**.
2. Create an **OAuth client (Web application)**. **Every project must register the SAME Authorized
   redirect URI** — `https://<public-backend>/google/oauth/callback` (locally
   `http://localhost:5080/google/oauth/callback`) — because the callback URL is global.
3. Configure the **consent screen** with the same scopes: `youtube.upload`, `youtube.readonly`,
   `drive.readonly`. (`youtube.upload` is sensitive — production needs Google verification; for
   testing add the channel's Google account as a **Test User**.)
4. In the admin panel **Projects** tab → **Add project**: paste the **OAuth client id + secret**
   (the secret is stored **encrypted**, AES-256-GCM, and is never shown again). Enable/disable or
   delete projects there; a project can't be deleted while accounts still use it.

**Connect a channel:** **Accounts** tab → **Connect Google account** → pick **which project** to
consent with → choose the YouTube channel. Each consent adds a new account bound permanently to
that project (its refresh token can only be refreshed by its issuing client). Connect the **same
channel under a second project** to add another daily quota for it.

> Backward compatibility: a single-client deploy that still sets `Google__ClientId/Secret` is
> auto-migrated on startup to one seeded **"Default (env)"** project, with all existing accounts
> bound to it. Once UI projects exist, `Google__*` is just an inert seed/fallback.

---

## 6. Mapping (channel → account)

In the admin panel **Mapping** tab, pick a synced Slack channel + a connected Google account
and create a mapping:

- The bot acts only on messages in **mapped** channels (others are ignored).
- An upload posted in a channel goes to that channel's mapped account — its refresh token and
  its quota counter; the live status message is posted with that workspace's bot token.
- One Slack channel maps to exactly one account; an account may receive from many channels.

---

## Upload template

Post in a mapped channel (labels are case-insensitive; the multiline
**Description** must come last):

```
🎬 UPLOAD

Video: https://drive.google.com/file/d/<FILE_ID>/view
Tags: promo, june, launch
Description:
Any number of lines, links, emoji 🎉
```

- **Video** — accepts `/file/d/<id>/`, `?id=`, `&id=`, `/uc?id=`, a bare id, or a Slack
  `<url|label>` link. Missing/unparseable → hard stop (“no video link found”).
- **Tags** — comma-split, trimmed, de-duped; each ≤100 chars, total ≤~500 (over-limit →
  warned + trimmed).
- **Description** — everything after the label, multiline-safe.
- **Title** is **not** in the template — derived from the Drive file name.
- Missing Description **or** Tags → a Block Kit **“Upload anyway?”** confirm (Yes/No).

### Live status message

One Block Kit message **per mapped channel** shows that account's remaining quota, the active
job with a text progress bar, queued jobs (each with a ✖ Cancel button), and recently completed
jobs with their YouTube link. It is **deleted + reposted** when the queue changes (so it stays at the
bottom) and **edited in place** (throttled: ≥2.5 s **and** ≥5 %) during an active upload.
After 100 % bytes it shows “YouTube processing…”.

### Cancellation rule

Cancel is allowed only while **queued** or **downloading**. Once the YouTube upload starts
it is the **point of no return** — the bot never deletes/modifies the video; the cancel
button replies “already uploading/uploaded — remove manually in YouTube Studio”.

---

## Run the tests / build everything

```bash
dotnet build backend/SlackTube.slnx          # 0 warnings
dotnet test  backend/tests/SlackTube.Tests   # parser unit tests
cd web && bun run build                      # type-check + production build
```

---

## Assumptions & decisions (MVP)

- **Slack via raw `HttpClient`**, not SlackNet — full control over raw-body signature
  verification, the delete+repost status trick, and throttled `chat.update`.
- **Secret encryption = AES-256-GCM** keyed by `SHA-256(TokenEncryption__Key)` rather than
  ASP.NET Data Protection. The key is fully determined by the env secret, so encrypted
  values survive restarts/redeploys with no key-ring volume to mount. Changing the key makes
  previously stored secrets undecryptable.
- **Quota reset** is implicit: the Redis counter key embeds the **Pacific-Time date**, so it
  resets to 0 at PT midnight with no separate scheduler. Each upload charges **1 `videos.insert`
  call** against the project's daily upload bucket (cap = `YouTubeDailyUploadLimit`, default 100);
  the ~10k unit pool is a separate informational meter that uploads do not consume.
- **No duplicate uploads:** the worker disables Hangfire auto-retry (`Attempts = 0`). A job
  interrupted *after* the upload started is marked Failed with a “verify in YouTube Studio”
  note instead of being retried. Crashes before upload can be re-posted to retry. State lives
  in Postgres so restarts are recoverable.
- **Drive metadata is fetched at ingest** to validate access early and learn the filename
  (so queued jobs show a real name) — no YouTube quota cost.
- **Category** defaults to `22` (People & Blogs); **title** is capped at 100 chars;
  uploads set `notifySubscribers = false`.
- **Admin auth (MVP, harden later):** the panel validates credentials against its own env and
  sets an HMAC-signed httpOnly session cookie; the backend admin API is guarded by a shared
  `X-Admin-Token`; the Hangfire dashboard is local-request-only. None of these are production
  hardened.
- **Admin dashboard stats** come from a dedicated `GET /api/admin/dashboard` endpoint
  (workspace/account counts, uploads today + last-24h, errors-24h, aggregate quota units).
  Tradeoff: "uploads today" uses the **UTC** day boundary (not PT, unlike the quota counter),
  and the dashboard quota gauge is an **aggregate** (sum of per-account used units; cap =
  per-account cap × account count) — per-account bars live on the Accounts tab. Admin quota is
  shown in **units**; the Slack live status message stays human-friendly ("≈N uploads").
- `GET /api/admin/jobs` takes `status` / `page` / `pageSize` and returns `{ items, total }`
  (server-side filter + pagination); the Jobs tab drives it via URL search params.
- EF migrations are applied automatically on backend startup.

## Out of scope

Editing/deleting YouTube videos from the bot · thumbnail/category/playlist/visibility fields
(parser is built to extend, not implemented) · other social networks · the SignalR live
dashboard · comment-bridge's SlackNet / EncryptedStringConverter / multi-user auth.
