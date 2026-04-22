# Docs site setup guide

This folder contains everything needed to serve the DaftAlerts documentation
as a [Jekyll](https://jekyllrb.com/) site using the
[just-the-docs](https://just-the-docs.com/) theme, deployed to GitHub Pages.

## Files in this bundle

| File | Goes to | Purpose |
|:---|:---|:---|
| `_config.yml`                        | `docs/_config.yml`                       | Jekyll + theme configuration |
| `Gemfile`                            | `docs/Gemfile`                           | Ruby gem dependencies for local dev |
| `index.md`                           | `docs/index.md`                          | Landing page (replaces `docs/INDEX.md`) |
| `.gitignore`                         | `docs/.gitignore`                        | Exclude build artifacts |
| `pages.yml`                          | `.github/workflows/pages.yml`            | GitHub Actions deploy workflow |
| `ARCHITECTURE.front-matter.example.md` | _reference only_                       | Shows the front matter to add to existing docs |

## One-time setup

### 1. Place the files

```bash
# From the repo root
mv _config.yml docs/
mv Gemfile docs/
mv index.md docs/
mv .gitignore docs/

mkdir -p .github/workflows
mv pages.yml .github/workflows/pages.yml
```

### 2. Add front matter to your existing docs

Your current `docs/` folder has: `ARCHITECTURE.md`, `DEPLOYMENT.md`,
`PARSER.md`, `API.md`, `DECISIONS.md`, `INDEX.md`.

- **Delete** `docs/INDEX.md` — it's replaced by `docs/index.md` (the new landing page).
- **Prepend front matter** to each of the others using this template:

```yaml
---
title: <Title>
nav_order: <number>
permalink: /<slug>/
description: "<short description>"
---
```

Specifically:

| File | Front matter |
|:---|:---|
| `ARCHITECTURE.md` | `title: Architecture`, `nav_order: 2`, `permalink: /architecture/` |
| `DEPLOYMENT.md`   | `title: Deployment`,   `nav_order: 3`, `permalink: /deployment/`   |
| `PARSER.md`       | `title: Parser`,       `nav_order: 4`, `permalink: /parser/`       |
| `API.md`          | `title: API reference`,`nav_order: 5`, `permalink: /api/`          |
| `DECISIONS.md`    | `title: Decisions`,    `nav_order: 6`, `permalink: /decisions/`    |

The `nav_order` values match the sidebar ordering; `permalink` values match
the links in `index.md` and keep URLs clean.

### 3. Enable GitHub Pages

On GitHub: **Settings → Pages → Build and deployment → Source**:
**GitHub Actions**.

That's it — the workflow in `.github/workflows/pages.yml` takes over.

### 4. First deploy

```bash
git add .
git commit -m "docs: set up Jekyll + just-the-docs site"
git push origin main
```

The workflow runs automatically on any push to `main` that touches `docs/`.
First run takes ~1–2 minutes. Subsequent deploys are ~30 seconds.

Site will be live at: **https://guibranco.github.io/daftalerts-api/**

## Local development

```bash
cd docs

# First time: install Ruby 3.3+ via rbenv / asdf / apt, then:
bundle install

# Serve locally with live reload
bundle exec jekyll serve --livereload

# Open http://localhost:4000/daftalerts-api/
```

The `/daftalerts-api/` suffix comes from `baseurl` in `_config.yml`.
If you want to serve at root locally, add `--baseurl ""` to the serve command.

## Adding a new page

Create a new `.md` file anywhere under `docs/`, with front matter:

```yaml
---
title: My new page
nav_order: 7
permalink: /my-new-page/
---
```

It will appear in the sidebar automatically, sorted by `nav_order`.

### Grouping pages

If you want nested sections (e.g., a "Guides" section with multiple pages),
use `parent:` in the front matter of the child pages:

```yaml
---
title: Adding a new email variant
parent: Parser
nav_order: 1
---
```

## Theme customisation

- **Colors & fonts** → create `docs/_sass/custom/setup.scss` and override variables
- **Callouts** already configured: use `{: .note }`, `{: .warning }`, `{: .important }`, `{: .new }`, `{: .highlight }` after a paragraph
- **Logo / favicon** → drop files into `docs/assets/images/` and update `_config.yml`
- **Footer** → edit `footer_content` in `_config.yml`

Full theme docs: <https://just-the-docs.com/>

## Troubleshooting

| Symptom | Fix |
|:---|:---|
| Build fails on `remote_theme` | Ensure `jekyll-remote-theme` is in `plugins:` in `_config.yml` |
| 404 on sub-pages | Check the `baseurl` matches your repo name exactly |
| Search box missing | `search_enabled: true` in `_config.yml`; requires theme v0.4+ |
| "Edit on GitHub" link broken | Check `gh_edit_branch` and `gh_edit_source` in `_config.yml` |
| Mermaid diagrams don't render | The `mermaid:` key in `_config.yml` enables them; ensure you're using triple-backtick ` ```mermaid ` fences |
| Local server shows stale content | `rm -rf _site .jekyll-cache && bundle exec jekyll serve` |

## Why just-the-docs?

Compared to the alternatives:

- **Minima** — too minimal; no sidebar nav, no search.
- **Minimal Mistakes** — gorgeous but heavier; more config than you need for five docs.
- **Cayman** — single page only; doesn't fit multi-page docs.
- **just-the-docs** — purpose-built for documentation: sidebar, search, dark mode, Mermaid, callouts, "edit this page" links. Used by Rust Analyzer, Supabase docs, and many OSS projects.
