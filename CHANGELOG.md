# Changelog

All notable changes to this project are documented here. Format loosely follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versioning is [SemVer](https://semver.org/).

## [Unreleased]

## [0.1.0] — Initial release

### Added
- Clean-architecture solution across 5 source projects (Domain, Application, Infrastructure, Api, EmailIngest).
- SQLite-backed persistence with hand-written `InitialCreate` migration.
- `Property`, `FilterPreset`, `RawEmail`, `GeocodeCache` entities.
- `Eircode` value object and `BerRank` ordinal helper (registered as a SQLite scalar function).
- Daft.ie email parser handling: Eircode extraction with graceful degradation, Outlook SafeLinks unwrapping with `originalsrc` preference, BER rating extraction from image URLs, property type detection from subject and body.
- REST API with token auth, full filter/sort/paging, bulk status actions, stats, filter-preset CRUD, health endpoints, Swagger UI (dev).
- Background workers: GeocodingWorker (60s), RetentionCleanupWorker (daily), ParseRetryWorker (30 min).
- Hybrid geocoding: Google primary, Nominatim fallback, SHA-256 cache keys, 365-day TTL.
- EmailIngest console app for Postfix SMTP piping, self-contained single-file publish.
- Docker multi-stage build, docker-compose with nginx TLS termination.
- `deploy/install.sh` for host-side Postfix wiring.
- systemd unit template for Mode B deployments.
- GitHub Actions CI workflow (build, test w/ coverage, Docker image on tag, single-file artifact on tag).
- Extensive test coverage: Domain (Eircode, BerRank), Application (validators, status transitions), Infrastructure (parser with 6 `.eml` fixtures, repository, geocoding fallback chain), Api (integration tests via `WebApplicationFactory<Program>`).
- Docs: README with Mermaid architecture diagram, ARCHITECTURE, DEPLOYMENT, PARSER, API, DECISIONS, postfix-setup.
