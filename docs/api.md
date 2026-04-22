---
title: API reference
layout: default
nav_order: 5
permalink: /api/
description: "REST endpoint reference for DaftAlerts — authentication, filtering by routing key and price range, pagination, bulk operations, filter presets, and example requests and responses."
---

# API reference

Base URL: `https://daftalerts.example.com/api` (or `http://localhost:5080/api` in development).

All endpoints except `/health*` and (in dev) `/swagger*` require:

```
Authorization: Bearer <token>
```

Errors are returned as RFC 7807 ProblemDetails (`application/problem+json`). Validation failures come back as `ValidationProblemDetails` with per-field errors in the `errors` dictionary.

Rate limit: 300 requests per minute per IP. Excess returns 429.

---

## `GET /api/properties`

List properties with filter, sort, paging.

### Query parameters

| Parameter | Type | Default | Notes |
|---|---|---|---|
| `status` | `inbox \| approved \| recycled` | required | |
| `page` | int | 1 | 1-based |
| `pageSize` | int | 24 | max 100 |
| `search` | string | — | matches `Address` or `Notes` (case-insensitive contains) |
| `routingKeys` | csv string | — | e.g. `D01,D02,D04`. Validated against Eircode routing-key pattern. |
| `minBeds` | int | — | |
| `maxBeds` | int | — | |
| `minBaths` | int | — | |
| `minPrice` | decimal | — | |
| `maxPrice` | decimal | — | |
| `propertyTypes` | csv string | — | subset of `House,Apartment,Studio,Shared,Other` |
| `berMin` | string | — | e.g. `C3` — matches properties with BER C3 or better (or null BER) |
| `sortBy` | `receivedAt \| price \| beds` | `receivedAt` | |
| `sortDir` | `asc \| desc` | `desc` | |

### Response

```json
{
  "items": [
    {
      "id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
      "daftId": "6546017",
      "daftUrl": "https://www.daft.ie/for-rent/house-herbert-lane-mews-dublin-2/6546017",
      "address": "Herbert Lane Mews, Dublin 2, D02KC86",
      "eircode": "D02KC86",
      "routingKey": "D02",
      "priceMonthly": 2850.00,
      "currency": "EUR",
      "beds": 2,
      "baths": 2,
      "propertyType": "House",
      "berRating": "C1",
      "mainImageUrl": "https://media.daft.ie/listings/6546017/hero.jpg",
      "latitude": 53.33,
      "longitude": -6.25,
      "status": "inbox",
      "receivedAt": "2026-04-15T09:12:30Z",
      "approvedAt": null,
      "recycledAt": null,
      "notes": null
    }
  ],
  "total": 42,
  "page": 1,
  "pageSize": 24
}
```

---

## `GET /api/properties/{id}`

Returns a single `PropertyDto`, or 404 if not found.

---

## `PATCH /api/properties/{id}`

Update status and/or notes.

### Request

```json
{
  "status": "approved",
  "notes": "Great location, close to the Luas"
}
```

Both fields are optional. If `status` transitions to `approved` or `recycled`, the corresponding `ApprovedAt` / `RecycledAt` timestamp is set. Transitioning back to `inbox` (via bulk `restore`) clears both.

### Response

The updated `PropertyDto`, or `400` with validation errors, or `404` if not found.

---

## `POST /api/properties/bulk`

Batch approve/recycle/restore.

### Request

```json
{
  "ids": ["<guid>", "<guid>"],
  "action": "approve"
}
```

`action` is one of `approve`, `recycle`, `restore`. Up to 500 ids per call.

### Response

```json
{ "updated": 2 }
```

---

## `GET /api/stats`

```json
{
  "inboxCount": 12,
  "approvedCount": 5,
  "recycledCount": 38,
  "avgApprovedPrice": 2620.00,
  "medianApprovedPrice": 2500.00
}
```

Averages and medians are computed only over `approved` properties.

---

## `GET /api/presets`

Returns all filter presets, with the default first.

```json
[
  {
    "id": "<guid>",
    "name": "Dublin central rentals",
    "routingKeys": ["D01","D02","D04","D06","D08"],
    "minBeds": 1, "maxBeds": 3, "minBaths": 1,
    "minPrice": null, "maxPrice": 3500.00,
    "propertyTypes": ["House","Apartment"],
    "berMin": null,
    "isDefault": true,
    "createdAt": "2026-04-15T00:00:00Z"
  }
]
```

---

## `POST /api/presets`

Create a preset.

### Request

```json
{
  "name": "My preset",
  "routingKeys": ["D02"],
  "minBeds": 2, "maxBeds": 3, "minBaths": 1,
  "minPrice": null, "maxPrice": 3000.00,
  "propertyTypes": ["Apartment"],
  "berMin": "C3",
  "isDefault": false
}
```

Returns `201 Created` with the full `FilterPresetDto` and a `Location` header.

## `PUT /api/presets/{id}`

Same body shape, replaces the preset. Returns the updated DTO or 404.

## `DELETE /api/presets/{id}`

Returns 204 on success, 404 if not found.

---

## Health endpoints (no auth)

- `GET /health` — liveness. Always returns 200 if the process is running.
- `GET /health/ready` — readiness. 200 when the DB is reachable and the geocoding worker has run within the last 5 minutes.

---

## Example: approving in two calls

```sh
TOKEN=xxx
BASE=https://daftalerts.example.com/api

# List inbox
curl -s -H "Authorization: Bearer $TOKEN" \
    "$BASE/properties?status=inbox&routingKeys=D02,D04&minBeds=2&maxPrice=3000"

# Approve a single listing
curl -s -X PATCH -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json" \
    -d '{"status":"approved","notes":"worth a viewing"}' \
    "$BASE/properties/<guid>"
```
