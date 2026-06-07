# Testing SlackTube end-to-end

A staged guide: each stage works on its own, so you can stop after Stage A/B if you
just want a smoke test, or go all the way to a real Slack→YouTube upload in Stage C.

| Stage | What it proves | Needs a tunnel? | External keys |
|---|---|---|---|
| **A** | Stack boots, panel + API healthy, login works | no | none |
| **B** | Google/YouTube account connects, channel shows up | no | Google OAuth client |
| **C** | Slack message → Drive download → YouTube upload → live status | **yes** | Slack app + Google |

All config lives in the root **`.env`** (already created, gitignored). Defaults boot the
stack with no keys.

---

## Prerequisites

- **Docker** running (`docker info` works). `postgres:16-alpine` will be pulled if missing.
- A **Google account** that owns (or can create) a **YouTube channel** — required for upload.
- A **Slack workspace** where you can install an app (you’re an admin, or can self-approve).
- For Stage C: a tunnel that exposes `localhost:5080` over **public HTTPS** — `ngrok` or
  `cloudflared`. Slack’s servers POST to your backend, so localhost alone is unreachable.

---

## Stage A — boot + UI smoke (no keys)

```bash
docker compose up --build      # project "slacktube": db, redis, api, web
```

Then check:

| URL | Expect |
|---|---|
| http://localhost:5080/health | `{"status":"ok"}` |
| http://localhost:3000 | login page → sign in `admin` / `admin` |

After login you land on the dashboard; **Slack / Accounts / Mapping / Jobs / Settings**
tabs all render (counts are zero — nothing connected yet). EF migrations auto-apply on API
startup; Hangfire dashboard is at http://localhost:5080/hangfire (local-only).

Stop anytime with `docker compose down` (add `-v` to also wipe the Postgres volume).

---

## Stage B — connect a Google/YouTube account (no tunnel)

Google accepts `http://localhost` redirect URIs, so this stage needs **no tunnel**.

### 3.1 Create the Google OAuth client

1. https://console.cloud.google.com → create (or pick) a project.
2. **APIs & Services → Library** → enable **YouTube Data API v3** *and* **Google Drive API**.
3. **APIs & Services → OAuth consent screen**:
   - User type **External**, fill app name + your email.
   - **Scopes**: you can leave empty here (the app requests them at runtime).
   - **Test users**: add the Google account(s) you’ll connect. `youtube.upload` is a
     *sensitive* scope — without verification only listed test users can consent.
4. **APIs & Services → Credentials → Create credentials → OAuth client ID**:
   - Application type **Web application**.
   - **Authorized redirect URIs** → add exactly:
     `http://localhost:5080/google/oauth/callback`
   - Create → copy **Client ID** + **Client secret**.

### 3.2 Put the keys in `.env` and restart

```dotenv
GOOGLE_CLIENT_ID=xxxx.apps.googleusercontent.com
GOOGLE_CLIENT_SECRET=xxxx
```

```bash
docker compose up -d --build      # picks up the new env
```

### 3.3 Connect from the panel

Panel → **Accounts** tab → **Connect Google account** → consent (use a **test user**
account that has a YouTube channel) → it bounces back and the account appears with its
**channel title** and a per-account quota gauge. Repeat to add more accounts.

> Put one **real, small video** (e.g. an `.mp4`) in that account’s Drive for Stage C, and
> grab its share link: `https://drive.google.com/file/d/<FILE_ID>/view`.

---

## Stage C — full Slack → YouTube flow (tunnel required)

### 4.1 Start a tunnel to the backend

**ngrok** (a free static domain avoids re-editing config on every restart):

```bash
ngrok http 5080
# → Forwarding  https://your-name.ngrok-free.app -> http://localhost:5080
```

Copy the `https://…` URL. (cloudflared equivalent: `cloudflared tunnel --url http://localhost:5080`.)

### 4.2 Point the stack at the tunnel

In `.env`:

```dotenv
PUBLIC_BASE_URL=https://your-name.ngrok-free.app
```

This one var feeds the Slack + Google redirect URIs, `App__PublicBaseUrl`, **and** the web
bundle’s backend URL. It’s baked into the web image at build time, so rebuild:

```bash
docker compose up -d --build
```

> Because Google’s redirect URI must match exactly, also add
> `https://your-name.ngrok-free.app/google/oauth/callback` to the Google OAuth client’s
> Authorized redirect URIs (keep the localhost one too). Then reconnect Google in Stage B
> *through the tunnel*, or just connect it now after the Slack app is set up.

### 4.3 Create the Slack app

1. https://api.slack.com/apps → **Create New App → From scratch** → name it, pick your workspace.
2. **Basic Information** → copy into `.env`:
   ```dotenv
   SLACK_CLIENT_ID=...          # "App Credentials" → Client ID
   SLACK_CLIENT_SECRET=...      # Client Secret
   SLACK_SIGNING_SECRET=...     # Signing Secret
   ```
3. **OAuth & Permissions**:
   - **Redirect URLs** → Add → `https://your-name.ngrok-free.app/slack/oauth/callback` → Save.
   - **Bot Token Scopes** → add the 3 (public channels):
     `chat:write`, `channels:read`, `channels:history`.
     (For private channels also add `groups:read`, `groups:history` here **and** in `InstallScopes`
     in `SlackEndpoints.cs` + restore `private_channel` in `SlackClient.ListChannelsAsync`.)
4. **Event Subscriptions** → toggle **On**:
   - **Request URL** → `https://your-name.ngrok-free.app/slack/events`
     (must show **Verified** — the stack must be running so it can answer the challenge).
   - **Subscribe to bot events** → add `message.channels` (+ `message.groups` if you enabled private channels) → Save.
5. **Interactivity & Shortcuts** → toggle **On**:
   - **Request URL** → `https://your-name.ngrok-free.app/slack/interactivity` → Save.

> After editing scopes/events Slack may prompt to **reinstall** the app — that’s expected.

Apply the new keys:

```bash
docker compose up -d --build
```

### 4.4 Install + wire up in the panel

1. Panel → **Slack** tab → **Connect Slack** → approve the install → workspace appears.
2. In Slack, **invite the bot** to a test channel: `/invite @YourApp` (the bot only sees
   channels it has joined).
3. Back in the panel → **Slack** tab → **Refresh channels** → the channel shows up.
4. **Accounts** tab → make sure a Google account is connected (Stage B).
5. **Mapping** tab → map that **channel → Google account** → Save. (Creating the mapping
   posts the live status message into the channel.) The bot only acts in **mapped** channels.

### 4.5 Fire a real upload

Post this in the mapped channel (labels case-insensitive; **Description** must be last):

```
🎬 UPLOAD

Video: https://drive.google.com/file/d/<FILE_ID>/view
Tags: promo, june, launch
Description:
Test upload from SlackTube 🎉
```

What to watch:

- A **live status message** appears/updates in the channel: queue + a text progress bar,
  throttled during transfer, then “YouTube processing…”, then a **YouTube link** (video is
  uploaded **private**).
- Omit `Tags` or `Description` → you get an **“Upload anyway?”** confirm (Yes/No).
- While **queued** or **downloading** a **✖ Cancel** button works; once the YouTube upload
  starts it’s the point of no return (cancel replies “remove manually in YouTube Studio”).
- Panel → **Jobs** tab → the job appears with its state + YouTube URL; **Dashboard** counts
  update; the account’s **Uploads** gauge ticks up by 1 per upload (cap ~100/day per OAuth client);
  the separate units meter is unaffected by uploads.

---

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Slack **Request URL not verified** | Stack not running, or tunnel/URL wrong. `curl https://<tunnel>/health` must return ok. URL must end in `/slack/events`. |
| Bot doesn’t react to a message | Channel not **mapped**, or bot not **invited**, or you posted a bot/edited message (only plain user messages trigger). Re-check Mapping. |
| `invalid_state` after Connect Slack | Cookie lost — open the panel and the OAuth flow on the **same** browser/domain; don’t mix localhost and tunnel for the panel. |
| Google **“no refresh token”** | You consented before. Revoke at https://myaccount.google.com/permissions and reconnect (the app forces `prompt=consent`). |
| Google `redirect_uri_mismatch` | The redirect URI in the console must **exactly** equal `<PUBLIC_BASE_URL>/google/oauth/callback`. Add both localhost and tunnel variants. |
| `access_denied` on Google consent | The account isn’t a **Test user** on the consent screen. Add it. |
| Upload fails on quota | ~100 uploads/day per **OAuth client** (`videos.insert` daily bucket, shared across accounts on the same client). Resets at Pacific-time midnight. Add more projects to stack capacity. |
| Changed `PUBLIC_BASE_URL`, browser still hits old URL | The web bundle is built-time baked — you must `up --build`, not just restart. |
| ngrok URL changed after restart | Use an ngrok **static domain** (free tier has one) or a named cloudflared tunnel, then you won’t re-edit config + rebuild. |

---

## Quick reference

```bash
docker compose up --build         # start (foreground)
docker compose up -d --build      # start / apply .env changes (detached)
docker compose logs -f api        # backend logs (Slack/Google/job errors)
docker compose logs -f web        # panel logs
docker compose down               # stop   (add -v to wipe the DB volume)
```

| URL | What |
|---|---|
| http://localhost:3000 | Admin panel (`admin`/`admin`) |
| http://localhost:5080/health | API health |
| http://localhost:5080/hangfire | Job dashboard (local-only) |
