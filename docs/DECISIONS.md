---
title: Decisions,
nav_order: 6, 
permalink: /decisions/
---

# Architecture Decisions

Short, informal ADRs for choices that aren't self-evident from the code.

## 001 — SQLite over Postgres

**Context.** Single-tenant personal tool, single machine, low write volume (a few emails a day).

**Decision.** Use SQLite with EF Core. File lives at `/var/lib/daftalerts/daftalerts.db` in production.

**Consequences.**
- Backups are `cp` or `sqlite3 .backup`.
- No separate DB server to manage.
- The `berrank` scalar function is registered per connection via a `DbConnectionInterceptor` — this works on SQLite but wouldn't port trivially to Postgres (would need a SQL function defined at migration time). Accepted trade-off.

## 002 — Bearer token, not full identity

**Context.** One user ever.

**Decision.** A single shared secret in `Auth:ApiToken`. Middleware validates `Authorization: Bearer <token>` with constant-time compare. No ASP.NET Identity, no JWT, no refresh tokens.

**Consequences.** To rotate the token, edit config and restart. Good enough for a personal tool behind TLS.

## 003 — `Property` entities are mutable, DTOs are records

**Context.** EF Core needs mutable entities for change tracking; DTOs should be value-like.

**Decision.**
- Domain entities (`Property`, `FilterPreset`, `RawEmail`, `GeocodeCache`) are classes with settable properties.
- Application DTOs are `record`s.

**Consequences.** The language-level split between "tracked by EF" and "carried across boundaries" is clear. Mutating a DTO is a compile error.

## 004 — Currency is a plain string property, not a value object

**Context.** All Daft.ie pricing is euro. The user lives in Ireland. There is zero realistic chance of a multi-currency use case.

**Decision.** `Property.Currency` is a `string` defaulting to `"EUR"`. If the hypothetical second currency appears, promote to a value object.

## 005 — `DaftId` is the only hard-required parse field

**Context.** Real-world emails are messy. Parsing has to handle variants.

**Decision.** The parser returns `null` only when `DaftId` can't be found (which would break idempotency) or when the price is missing (which would mean we probably didn't get a listing email at all). Every other field is allowed to degrade to `null` or a sensible default.

**Consequences.** A property with no Eircode still shows up in the inbox — the user sees it and either approves it or recycles it. Frontend filters that depend on `RoutingKey` will silently exclude such properties; that's acceptable.

## 006 — BER-rank as a SQLite scalar function

**Context.** The `berMin` filter compares against a string like `"C3"` and needs to match rows with a "better or equal" rating. SQL LIKE won't do it; a CASE expression would be ugly.

**Decision.** Register a `berrank(ber)` scalar function on every SQLite connection that maps the rating string to an integer ordinal, then use it in LINQ via an EF `DbFunction` marker method. Filter becomes `WHERE BerRating IS NULL OR berrank(BerRating) <= <minRank>`.

**Consequences.** Works only on SQLite (noted in ADR 001). In tests, the function is registered the same way so filter tests are real queries.

## 007 — Single-file self-contained publish for EmailIngest

**Context.** Postfix pipes mail to a command. The command needs to run without depending on whatever runtime is (or isn't) installed on the host.

**Decision.** Publish EmailIngest as `SelfContained=true; PublishSingleFile=true` for `linux-x64`. Drops a single ~70 MB binary at `/opt/daftalerts/ingest/DaftAlerts.EmailIngest`.

**Consequences.** The ingest binary does not need the host .NET runtime. It does need to be rebuilt and redeployed when dependencies or the runtime change — accepted trade-off.

## 008 — Geocoding happens asynchronously, not inline

**Context.** Postfix expects piped commands to return fast (a few seconds); geocoding can take a second or more per request, especially on fallback.

**Decision.** The ingest path only persists the raw email and the parsed property. Geocoding is done by `GeocodingWorker`, which runs every 60s, pulls up to 20 properties with `Latitude IS NULL`, and writes back lat/lng.

**Consequences.** Properties briefly appear without coordinates. The frontend shows them on the list but not on the map until geocoding catches up. This is fine — most lookups resolve within a minute of ingestion.

## 009 — Google first, Nominatim fallback; cache for a year

**Context.** Eircodes are extremely accurate on Google, less so on Nominatim. Google is paid but cheap at this volume. Nominatim is free but has a 1 req/sec policy limit.

**Decision.** Try Google first (if API key configured), fall back to Nominatim, cache any successful result by SHA-256 of `(lowercased address | eircode)` for 365 days.

**Consequences.** A re-alert of the same listing hits the cache, not the network. If both providers fail, the property stays ungeocoded and the worker will retry next iteration.

## 010 — `originalsrc` preferred over `href` for listing URLs

**Context.** Outlook wraps all links in its SafeLinks service. The original URL is preserved in the `originalsrc` attribute on `<a>` tags.

**Decision.** For each `<a>`, check `originalsrc` first, then `href`. If the chosen URL contains `safelinks.protection.outlook.com`, decode the `url` query parameter.

**Consequences.** Handles mail routed through Exchange/Outlook. Other clients (Gmail, Fastmail) don't wrap links, so `href` is used directly.

## 011 — Manual EF migration commit

**Context.** We can't run `dotnet ef migrations add` in the Claude authoring environment (no network access to NuGet to restore the design package).

**Decision.** Hand-write the initial migration's `Up()` and `Down()` methods based on the model configuration. Commit it to the repo.

**Consequences.** Future schema changes should be generated via `dotnet ef migrations add` on a dev machine in the usual way. The hand-written initial migration is functionally identical to what the tool would produce, but lacks the companion `ModelSnapshot` file — the first `dotnet ef migrations add <next>` locally will regenerate the snapshot.

## 012 — ID-based idempotency, not content-based dedup

**Context.** Receiving the same listing email twice would create duplicate `RawEmail` rows.

**Decision.** Deduplicate by SHA-256 of `Message-Id`. If the header is missing, fall back to SHA-256 of `Date + From + Subject`.

**Consequences.** A well-formed Daft alert always has a unique `Message-Id`, so dedup is essentially free. The fallback handles the edge case of a malformed or relayed mail that lost the header.
