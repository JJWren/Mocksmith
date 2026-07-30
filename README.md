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

Pre-scaffold. Work is issue-driven: see issues **M1–M8** for the v1 roadmap. Design decisions and architecture live in [`aidlc-docs/`](aidlc-docs/inception/requirements/requirements.md).

## Running (lands with M1)

```bash
docker compose up -d
```

| Env var | Purpose |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key (secret — env only, never stored in the DB) |
| `MOCKSMITH_USERNAME` | Single-user login name |
| `MOCKSMITH_PASSWORD_HASH` | Password hash for the single user (hash one-liner documented with M1) |
| `ASPNETCORE_URLS` | Bind address, e.g. `http://+:8080` |

## Backup

The entire app state lives in `./data` (SQLite DB + sample HTML files + uploaded assets). Backup = stop the container, copy the folder, start it again.

## License

[MIT](LICENSE)
