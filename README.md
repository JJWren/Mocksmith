# Mocksmith

> A self-hosted forge for web-design samples — collect inspiration, generate interactive design pages with Claude, refine them live, and hand the winner to a designer or AI agent.

[![CI](https://github.com/JJWren/Mocksmith/actions/workflows/ci.yml/badge.svg)](https://github.com/JJWren/Mocksmith/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/JJWren/Mocksmith?include_prereleases)](https://github.com/JJWren/Mocksmith/releases)

## What it does

- **Generate** — feed in screenshots (clipboard paste), reference URLs, and a text description; Claude produces a self-contained, interactive sample page (single HTML file with design tokens as CSS custom properties). Fan out 1–3 style directions per request.
- **Refine** — iterate conversationally ("darker header, more whitespace") in a draft session with full iteration history, or edit directly: click an element in the preview, adjust typography/color/spacing at rule level, and watch it update live.
- **Catalog** — a dashboard of sample tiles (name, summary, tags) with tag-driven filtering and search, smart collections (saved tag queries) plus manual pin/exclude.
- **Variants** — fork a saved sample into named variants (`Dark`, `Compact`, …); saving under an existing variant name overwrites that variant.
- **Hand off** — export a zip (`sample.html`, `design-tokens.json`, `design-brief.md`, source screenshots, `metadata.json`) or copy a single agent-ready prompt for Claude Code or a human designer.

## Stack

.NET 10 Blazor (Interactive Server) · EF Core + SQLite · Anthropic Claude API (Sonnet 5 default, per-request override) · Docker Compose — single container with a bind-mounted `/data` volume.

## Status

**v1 feature-complete** — all eight milestones (M1–M8) shipped via issue-driven PRs. Design
decisions and architecture live in [`aidlc-docs/`](aidlc-docs/inception/requirements/requirements.md).
In-app: `/usage` shows per-call and aggregate generation cost (subscription runs are marked as
such), `/settings` sets the default model.

## Running

```bash
# 1) Generate a password hash for the single-user login
docker compose run --rm mocksmith hash-password 'your-password'
#    (or without Docker: dotnet run --project src/Mocksmith -- hash-password 'your-password')

# 2) Provide env vars (shell or .env next to docker-compose.yml)
#    MOCKSMITH_USERNAME=you
#    MOCKSMITH_PASSWORD_HASH=<output of step 1>
#    ANTHROPIC_API_KEY=<key>   # needed from M3 (generation)

# 3) Build and start
docker compose up -d --build
```

The app listens on port 8080 and reports readiness at `/healthz`.

### Run from published images (no clone needed)

Each release publishes two images to GHCR, tagged with the release version and `latest`:

| Image | Contents |
|---|---|
| `ghcr.io/jjwren/mocksmith` | Standard image — `api` backend |
| `ghcr.io/jjwren/mocksmith-full` | Standard + Node + Claude Code CLI — enables the `claude-code` backend |

```bash
# Password hash without a checkout:
docker run --rm ghcr.io/jjwren/mocksmith:latest hash-password 'your-password'
```

Minimal compose file (swap in `mocksmith-full`, `MOCKSMITH_GENERATOR=claude-code`, and
`CLAUDE_CODE_OAUTH_TOKEN` for subscription-backed generation):

```yaml
services:
  mocksmith:
    image: ghcr.io/jjwren/mocksmith:latest
    restart: unless-stopped
    ports:
      - "8080:8080"
    environment:
      - ANTHROPIC_API_KEY=${ANTHROPIC_API_KEY:-}
      - MOCKSMITH_USERNAME=${MOCKSMITH_USERNAME:?}
      - MOCKSMITH_PASSWORD_HASH=${MOCKSMITH_PASSWORD_HASH:?}
      - ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
    volumes:
      - ./data:/data
```

The in-repo `docker-compose.yml` keeps building from source — use it for development
or to run unreleased changes.

### Generation backends

Generation runs through one of two interchangeable backends behind `IDesignGenerator`:

| Backend | Credential | How |
|---|---|---|
| `api` | `ANTHROPIC_API_KEY` | Anthropic Messages API (official SDK); per-call cost logged |
| `claude-code` | `CLAUDE_CODE_OAUTH_TOKEN` (from `claude setup-token`) or a logged-in local `claude` CLI | Claude Code CLI headless mode — rides your Claude subscription, no API billing |

`MOCKSMITH_GENERATOR=api|claude-code` selects explicitly; otherwise the app auto-detects
(API key first, then OAuth token / installed CLI). For the subscription backend in Docker,
use the **full** image variant, which bundles Node + the Claude Code CLI:

```bash
claude setup-token   # once, on any machine with your Claude login — put the token in .env
docker compose -f docker-compose.yml -f docker-compose.full.yml up -d --build
```

| Env var | Purpose |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key for the `api` backend (secret — env only, never stored in the DB) |
| `CLAUDE_CODE_OAUTH_TOKEN` | Subscription token for the `claude-code` backend (`claude setup-token`) |
| `MOCKSMITH_GENERATOR` | Backend override: `api` or `claude-code` (default: auto-detect) |
| `MOCKSMITH_GENERATION_TIMEOUT_SECONDS` | CLI generation timeout (default 600) |
| `MOCKSMITH_USERNAME` | Single-user login name |
| `MOCKSMITH_PASSWORD_HASH` | PBKDF2 hash from `hash-password` (never the plain password) |
| `MOCKSMITH_DATA_DIR` | Data directory (defaults to `/data` in the container) |
| `ASPNETCORE_URLS` | Bind address (defaults to `http://+:8080` in the container) |

## Local development

```bash
export MOCKSMITH_USERNAME=dev
export MOCKSMITH_PASSWORD_HASH=$(dotnet run --project src/Mocksmith -- hash-password 'dev')
dotnet run --project src/Mocksmith    # SQLite lands in src/Mocksmith/data/
dotnet test                           # unit tests
```

## Backup

The entire app state lives in `./data` (SQLite DB + sample HTML files + uploaded assets):

```bash
docker compose stop
cp -r data data-backup-$(date +%F)
docker compose start
```

Restore = put the copied folder back at `./data` and `docker compose up -d`. The full
stop → copy → destroy → restore → verify cycle was drilled successfully on 2026-07-30
(restored instance healthy, login and DB intact). Stopping first matters: it checkpoints
SQLite's WAL so the copy is a single consistent file.

## License

[MIT](LICENSE)
