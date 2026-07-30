# Mocksmith — Requirements & Design

Self-hosted (docker compose) web app for collecting, generating, refining, and cataloging web-design samples. Inputs (screenshots, images, URLs, text descriptions) are turned into interactive single-file sample pages by Claude; good results are saved, tagged, organized into collections, tweaked into variants, and exported as a complete handoff package for a human designer or an AI coding agent.

All decisions were resolved one-by-one in a grilling session on 2026-07-30 (raw Q&A in [`../../audit.md`](../../audit.md)).

## Decisions

| # | Decision | Resolution |
|---|----------|-----------|
| 1 | Generation locus | In-app via Claude API, behind an `IDesignGenerator` interface |
| 2 | Sample artifact | Single self-contained HTML file + CSS-custom-property token contract; vanilla-JS interactivity; sandboxed iframe preview |
| 3 | URL ingest | Manual screenshots (clipboard paste UX); URL stored as provenance; optionally passed to Claude's server-side `web_fetch` tool for HTML/CSS signal. No browser container in v1 |
| 4 | Refine loop | Conversational draft sessions (iteration history, step back, promote winner) **plus** a direct-edit side panel (click element in preview → typography/color/spacing controls) |
| 5 | Direct-edit scope | Rule/token level — editing an h1 edits the heading rule/token, all similar elements update |
| 6 | Post-save tweaks | Fork into **named variants**; variant name is an upsert key (same name = overwrite that variant); dashboard groups variants under parent |
| 7 | Handoff | Zip bundle (sample.html, design-tokens.json, AI-written design-brief.md, source screenshots, metadata) **plus** one-click "copy as agent prompt" markdown |
| 8 | Tagging | AI-suggested at save (rides in the generation call, fed existing vocabulary), user approves/edits as chips |
| 9 | Collections | Both from day one: smart (saved tag queries) **and** manual pin/exclude overrides per collection |
| 10 | Fan-out | 1–3 selector on request form, default 1; candidates side-by-side; pick one as active draft |
| 11 | Blazor model | .NET 10 Blazor Web App, Interactive Server render mode globally; no separate API layer |
| 12 | Database | SQLite via EF Core, file lives on bind-mounted `/data` (backup = copy the folder) |
| 13 | Auth | Single-user ASP.NET cookie login; username + password **hash** via env vars; Anthropic key env-var/secret only, never in DB |
| 14 | Default model | `claude-sonnet-5`, per-request override dropdown (Opus 4.8 / Haiku 4.5); usage/cost logged per generation |
| 15 | Name/home | **Mocksmith** — `JJWren/Mocksmith` |

## Architecture

### Solution layout

```
Mocksmith/
├── docker-compose.yml            # app service + ./data:/data bind mount
├── Dockerfile                    # multi-stage publish, non-root
├── src/
│   ├── Mocksmith/                # Blazor Web App (Interactive Server) — thin: pages, components, JS
│   │   └── wwwroot/js/bridge.js  # iframe bridge injected into samples (see Preview)
│   └── Mocksmith.Core/           # domain: entities, EF DbContext, services, generator abstraction
└── tests/
    └── Mocksmith.Tests/          # xUnit: token parsing, patch merge, export bundle, tag/query logic
```

### Data model (EF Core / SQLite)

- **Sample**: Id, Name, Summary, Description (original request text), SourceUrl, HtmlFile (relative path), TokensJson (cached manifest), Model, CreatedAt/UpdatedAt
- **Variant**: Id, SampleId, Name *(unique per sample — upsert key)*, HtmlFile (materialized standalone), PatchJson (override deltas for provenance)
- **Tag** (Name unique, kebab-case) + **SampleTag** join
- **Collection**: Id, Name, TagQuery (e.g. `dark AND dashboard`) + **CollectionPin** (SampleId, Mode: Include|Exclude)
- **DraftSession** → **DraftIteration** (Index, CandidateGroup for fan-out, InstructionText, HtmlFile, Model, IsActive)
- **InputAsset**: uploaded/pasted screenshots (file path, linked to session; carried onto saved Sample as provenance)
- **GenerationLog**: model, input/output tokens, estimated cost, duration, timestamps
- **Settings**: single row (default model, etc.)

Filesystem under `/data`: `mocksmith.db`, `samples/{id}/…`, `sessions/{id}/iter-{n}.html`, `assets/{id}.png`.

### The token contract (the load-bearing spec)

Every generated sample MUST contain:

1. `:root` CSS custom properties for all major design decisions — palette (`--color-primary`, `--color-bg`, `--color-surface`, `--color-text`, …), typography (`--font-heading`, `--font-body`, type scale), spacing unit, radii, shadows.
2. A machine-readable manifest block: `<script type="application/json" id="mocksmith-tokens">` enumerating each token with label + category — the app parses this to build the tweak panel and `design-tokens.json`.
3. Single-file constraints: no external network requests (system font stacks or embedded assets), vanilla JS only, semantic HTML.

The generator system prompt enforces this; a post-generation validator checks it (missing manifest → one automatic repair round-trip).

### Generation pipeline (`IDesignGenerator` → `ClaudeDesignGenerator`)

- Anthropic Messages API, streaming; request = system prompt (token contract + design guidance) + user content: description text, screenshot image blocks, and `web_fetch` server tool enabled when a URL is present.
- Structured return: `{ name, summary, tags[], html }` (tool-use/structured output) so tag suggestion and naming ride along free.
- Tag suggestions are vocabulary-aware: existing tag list injected into the prompt.
- Fan-out N: N parallel calls, one CandidateGroup per session.
- Refine turn: current HTML + instruction → full rewritten file (fine for single-file artifacts); every call logged to GenerationLog with cost.

### Preview & direct-edit panel

- Preview iframe uses `sandbox="allow-scripts"` **without** `allow-same-origin` — AI-generated HTML never runs in the app's origin (defends against prompt-injected JS).
- At render time the app injects `bridge.js` into the sample HTML. Parent ↔ frame speak via `postMessage`: hover/click element picking (overlay highlight), applying live patch styles, reporting computed styles for the panel.
- Edits are stored as a structured patch (selector → property → value) at **rule level** (decision #5). Applied live via the bridge; **baked into the HTML file on save** (merged into the `<style>` block / token values updated).
- Dashboard tiles: lazy-loaded scaled iframes (IntersectionObserver, capped concurrency). Snapshot thumbnails only if perf demands later.

### Workspaces & flows

- **New generation**: request form (description, clipboard-paste/upload screenshots, optional URL, model dropdown, fan-out 1–3) → draft session → candidates side-by-side → pick active → refine via chat + edit panel → **Save dialog** (name, summary, AI tag chips to approve) → Sample.
- **Edit saved sample**: same workspace; Save offers *overwrite original* or *save as variant* (existing variant name = overwrite that variant).
- **Dashboard**: filter bar (text search over name/summary + tag chips AND-filter), collection cards, sample tiles (name, summary, tags, variant count/grouping).
- **Handoff** (per sample or variant): zip download (`sample.html`, `design-tokens.json`, `design-brief.md` — AI-written style/typography/palette/components/provenance brief, source screenshots, `metadata.json`) + "Copy as agent prompt" button producing one markdown blob.

### Compose & ops

- One service; `./data:/data` bind mount; healthcheck endpoint; env: `ANTHROPIC_API_KEY`, `MOCKSMITH_USERNAME`, `MOCKSMITH_PASSWORD_HASH` (docs include a one-liner to produce the hash), `ASPNETCORE_URLS`.
- Cookie auth guards everything including sample files (served through an authorized endpoint, not bare static files).
- Backup story: stop, copy `/data`, start — the point of SQLite-on-bind-mount.

## Implementation milestones (GitHub issues M1–M8)

1. **Scaffold**: .NET solution (Mocksmith, Mocksmith.Core, Mocksmith.Tests), Dockerfile + compose, EF/SQLite wiring, cookie auth, healthcheck — the guarded CI build goes green on this PR.
2. **Catalog core**: entities/migrations, dashboard shell, sample tiles (iframe previews), tags, text+tag filtering, manual sample import (dev aid + generation-independent testing path).
3. **Generation**: `IDesignGenerator` + Claude client (streaming, structured output, cost logging), request form with clipboard paste, draft sessions, fan-out, conversational refine.
4. **Edit panel**: bridge.js, click-to-select, rule-level patch model, live apply, bake-on-save, save dialog with tag approval.
5. **Variants**: fork / save-as-variant with name-upsert, dashboard grouping.
6. **Collections**: smart tag queries + pin/exclude, collection cards.
7. **Handoff**: zip bundle builder, design-brief generation, copy-as-agent-prompt.
8. **Polish**: cost/usage view, README (deploy, backup, hash one-liner), release hygiene.

## Verification (across milestones)

- Unit tests: token-manifest parsing, patch merge/bake, variant upsert semantics, tag-query evaluation, export bundle contents.
- `docker compose up` → login → end-to-end loop: paste screenshot + description → generate → refine via chat → panel-edit an h1 (all similar elements update; bake persists after reload) → save with tag approval → fork a variant, re-save same name (overwrite) → smart collection picks it up → export zip and inspect → copy-as-prompt sanity check.
- Restart container and verify `/data` persistence; copy-folder backup/restore drill once.
