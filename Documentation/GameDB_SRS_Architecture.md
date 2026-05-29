# GameDB — Software Requirements Specification & High-Level Architecture

**Version:** 1.0  
**Stack:** ASP.NET Core 9 · EF Core · PostgreSQL · Razor Pages  
**Date:** 2025

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Functional Requirements](#2-functional-requirements)
3. [Non-Functional Requirements](#3-non-functional-requirements)
4. [Data Model & ERD](#4-data-model--erd)
5. [System Architecture](#5-system-architecture)
6. [Backend Folder Structure](#6-backend-folder-structure)
7. [API Endpoints](#7-api-endpoints)
8. [Steam Integration Flow](#8-steam-integration-flow)
9. [Admin Panel Workflow](#9-admin-panel-workflow)
10. [MVP Scope](#10-mvp-scope)
11. [Future Expansion Plan (GOG / Epic Ready)](#11-future-expansion-plan-gog--epic-ready)

---

## 1. Project Overview

### 1.1 Purpose

GameDB is a web platform that aggregates Steam game metadata, pricing history, and user library data into a unified catalog with filtering, wishlists, and price-drop alerts — similar in spirit to a simplified SteamDB + GG.deals.

### 1.2 Goals

| # | Goal |
|---|------|
| G1 | Provide a browsable, filterable catalog of Steam games |
| G2 | Allow users to connect their Steam account and import their library |
| G3 | Allow users to maintain a wishlist and set price/discount alerts |
| G4 | Provide an admin panel to manage data and trigger imports |
| G5 | Be architecturally extensible to GOG and Epic Games Store |

### 1.3 Scope (Phase 1)

- Steam only as a data source
- Web-only (no mobile app)
- No real-time websockets
- No microservices
- Runs locally or in Docker (single host)

---

## 2. Functional Requirements

### 2.1 Guest (Unauthenticated)

| ID | Requirement |
|----|-------------|
| FR-G1 | Browse paginated game catalog |
| FR-G2 | Filter by name, genre, tag, price, discount, release date, Metacritic score, single/multiplayer, controller support, Steam Deck compatibility, free/paid |
| FR-G3 | Sort catalog by name, price, discount %, release date, Metacritic score |
| FR-G4 | Full-text search across game name and description |
| FR-G5 | View game detail page (metadata, screenshots, system requirements, current price) |

### 2.2 Registered User

| ID | Requirement |
|----|-------------|
| FR-U1 | Register with email + password |
| FR-U2 | Log in with email + password (cookie-based auth) |
| FR-U3 | Connect Steam account via OpenID login |
| FR-U4 | Import owned games from Steam library (IGetOwnedGames) |
| FR-U5 | View personal library (owned games) |
| FR-U6 | Add/remove games to/from wishlist manually |
| FR-U7 | Create price alert for a game (target price OR target discount %) |
| FR-U8 | View and manage active alerts |
| FR-U9 | Receive alert notification (in-app banner; email is post-MVP) |

### 2.3 Admin

| ID | Requirement |
|----|-------------|
| FR-A1 | View full game catalog with admin tools |
| FR-A2 | Trigger full Steam catalog import (batch, non-blocking) |
| FR-A3 | Trigger single-game metadata refresh |
| FR-A4 | Trigger price data refresh (ITAD) |
| FR-A5 | View import logs (timestamp, type, status, record count, error message) |
| FR-A6 | View games with incomplete data (missing description, genres, etc.) |
| FR-A7 | Edit game metadata manually |
| FR-A8 | Delete a game from catalog |

---

## 3. Non-Functional Requirements

| Category | Requirement |
|----------|-------------|
| Performance | Catalog page loads < 500 ms with pagination (50 items/page) |
| Caching | MemoryCache for catalog pages (TTL 5 min) and game detail pages (TTL 15 min) |
| Scalability | Stateless API layer; DB connection pooling via EF Core |
| Security | ASP.NET Identity for local auth; Steam OpenID for Steam login; HTTPS enforced |
| Authorization | Role-based: `User`, `Admin` |
| Data freshness | Game metadata refreshed on-demand or via scheduled BackgroundService (nightly) |
| Indexing | PostgreSQL indexes on `AppId`, `Name` (GIN for FTS), `GenreIds`, `Tags`, `Price`, `DiscountPercent`, `ReleaseDate` |
| Extensibility | Provider abstraction allows adding GOG/Epic without touching core domain |

---

## 4. Data Model & ERD

### 4.1 Tables

#### `Games`
```
Games
├── Id              SERIAL PRIMARY KEY
├── AppId           INT NOT NULL UNIQUE          -- Steam AppID
├── Name            VARCHAR(255) NOT NULL
├── Slug            VARCHAR(255) UNIQUE          -- URL-friendly name
├── Description     TEXT
├── ShortDescription TEXT
├── HeaderImage     VARCHAR(512)
├── ReleaseDate     DATE
├── IsFree          BOOLEAN NOT NULL DEFAULT FALSE
├── MetacriticScore SMALLINT                     -- nullable
├── SteamDeckSupport VARCHAR(20)                 -- 'Verified'|'Playable'|'Unsupported'|null
├── ControllerSupport VARCHAR(20)                -- 'full'|'partial'|null
├── WindowsRequirements TEXT                     -- JSON blob
├── LinuxRequirements  TEXT
├── MacRequirements    TEXT
├── DataStatus      VARCHAR(20) NOT NULL DEFAULT 'Partial'  -- 'Full'|'Partial'|'Missing'
├── LastSyncedAt    TIMESTAMPTZ
├── CreatedAt       TIMESTAMPTZ NOT NULL DEFAULT NOW()
└── UpdatedAt       TIMESTAMPTZ NOT NULL DEFAULT NOW()
```

#### `Genres`
```
Genres
├── Id    SERIAL PRIMARY KEY
└── Name  VARCHAR(100) NOT NULL UNIQUE
```

#### `GameGenres` (join)
```
GameGenres
├── GameId   INT FK → Games.Id
└── GenreId  INT FK → Genres.Id
PRIMARY KEY (GameId, GenreId)
```

#### `Tags`
```
Tags
├── Id    SERIAL PRIMARY KEY
└── Name  VARCHAR(100) NOT NULL UNIQUE
```

#### `GameTags` (join)
```
GameTags
├── GameId  INT FK → Games.Id
├── TagId   INT FK → Tags.Id
└── Weight  INT DEFAULT 0   -- Steam vote count
PRIMARY KEY (GameId, TagId)
```

#### `GamePrices`
```
GamePrices
├── Id               SERIAL PRIMARY KEY
├── GameId           INT NOT NULL FK → Games.Id UNIQUE
├── Currency         CHAR(3) NOT NULL DEFAULT 'USD'
├── CurrentPrice     DECIMAL(10,2)
├── OriginalPrice    DECIMAL(10,2)
├── DiscountPercent  SMALLINT NOT NULL DEFAULT 0
├── IsOnSale         BOOLEAN NOT NULL DEFAULT FALSE
├── LowestPrice      DECIMAL(10,2)              -- from ITAD
├── LowestPriceDate  DATE
├── LastUpdatedAt    TIMESTAMPTZ NOT NULL DEFAULT NOW()
└── Source           VARCHAR(20) NOT NULL DEFAULT 'ITAD'
```

#### `Users`
```
Users
├── Id              UUID PRIMARY KEY DEFAULT gen_random_uuid()
├── Email           VARCHAR(255) NOT NULL UNIQUE
├── PasswordHash    VARCHAR(512)
├── SteamId         VARCHAR(20) UNIQUE           -- nullable
├── SteamProfileUrl VARCHAR(512)
├── AvatarUrl       VARCHAR(512)
├── Role            VARCHAR(20) NOT NULL DEFAULT 'User'
├── IsActive        BOOLEAN NOT NULL DEFAULT TRUE
├── CreatedAt       TIMESTAMPTZ NOT NULL DEFAULT NOW()
└── LastLoginAt     TIMESTAMPTZ
```

#### `UserGames`
```
UserGames
├── Id          SERIAL PRIMARY KEY
├── UserId      UUID FK → Users.Id
├── GameId      INT FK → Games.Id
├── Status      VARCHAR(20) NOT NULL    -- 'Owned'|'Wishlist'|'Ignored'
├── AddedAt     TIMESTAMPTZ NOT NULL DEFAULT NOW()
└── PlaytimeMinutes INT DEFAULT 0       -- from Steam import
UNIQUE (UserId, GameId)
```

#### `Alerts`
```
Alerts
├── Id               SERIAL PRIMARY KEY
├── UserId           UUID FK → Users.Id
├── GameId           INT FK → Games.Id
├── TargetPrice      DECIMAL(10,2)     -- nullable
├── TargetDiscount   SMALLINT          -- nullable (e.g. 50 = 50%)
├── IsActive         BOOLEAN NOT NULL DEFAULT TRUE
├── TriggeredAt      TIMESTAMPTZ       -- nullable; set when fired
├── CreatedAt        TIMESTAMPTZ NOT NULL DEFAULT NOW()
└── CONSTRAINT check_alert CHECK (TargetPrice IS NOT NULL OR TargetDiscount IS NOT NULL)
```

#### `ExternalGameMappings`
```
ExternalGameMappings
├── Id           SERIAL PRIMARY KEY
├── GameId       INT FK → Games.Id
├── ProviderName VARCHAR(50) NOT NULL    -- 'Steam'|'GOG'|'Epic'|'ITAD'
├── ExternalId   VARCHAR(100) NOT NULL
├── ExternalUrl  VARCHAR(512)
└── CreatedAt    TIMESTAMPTZ NOT NULL DEFAULT NOW()
UNIQUE (ProviderName, ExternalId)
```

#### `ImportLogs`
```
ImportLogs
├── Id          SERIAL PRIMARY KEY
├── Type        VARCHAR(50) NOT NULL   -- 'FullCatalogImport'|'GameMetaRefresh'|'PriceRefresh'|'LibraryImport'
├── Status      VARCHAR(20) NOT NULL   -- 'Running'|'Completed'|'Failed'
├── StartedAt   TIMESTAMPTZ NOT NULL
├── FinishedAt  TIMESTAMPTZ
├── RecordsProcessed INT DEFAULT 0
├── RecordsFailed    INT DEFAULT 0
├── TriggeredBy VARCHAR(100)           -- 'System'|user email
└── ErrorMessage TEXT
```

### 4.2 ERD (Text Diagram)

```
Users ──────────────┬────────────── UserGames ──── Games ──────┬── GameGenres ── Genres
                    │                   │              │         ├── GameTags   ── Tags
                    └── Alerts ─────────┘              │         └── GamePrices
                                                        └── ExternalGameMappings
                                               ImportLogs (standalone audit log)
```

### 4.3 PostgreSQL Indexes

```sql
-- Catalog filtering performance
CREATE INDEX idx_games_name_gin ON games USING GIN (to_tsvector('english', name));
CREATE INDEX idx_games_release_date ON games (release_date DESC);
CREATE INDEX idx_games_metacritic ON games (metacritic_score DESC NULLS LAST);
CREATE INDEX idx_games_is_free ON games (is_free);
CREATE INDEX idx_games_deck_support ON games (steam_deck_support);

-- Price filtering
CREATE INDEX idx_game_prices_current ON game_prices (current_price, discount_percent);
CREATE INDEX idx_game_prices_discount ON game_prices (discount_percent DESC);

-- User lookups
CREATE INDEX idx_user_games_user_id ON user_games (user_id, status);
CREATE INDEX idx_alerts_user_active ON alerts (user_id) WHERE is_active = TRUE;
CREATE INDEX idx_alerts_game_active ON alerts (game_id) WHERE is_active = TRUE;

-- External mappings
CREATE INDEX idx_ext_mapping_provider ON external_game_mappings (provider_name, external_id);
```

---

## 5. System Architecture

### 5.1 Overview

```
┌─────────────────────────────────────────────────────────────┐
│                        Browser / Client                      │
└──────────────────────────────┬──────────────────────────────┘
                               │ HTTP
┌──────────────────────────────▼──────────────────────────────┐
│              ASP.NET Core Web Application                    │
│  ┌─────────────────┐   ┌──────────────────────────────────┐ │
│  │  Razor Pages /  │   │         Web API Controllers      │ │
│  │  MVC Views      │   │  (for AJAX / Admin API calls)    │ │
│  └────────┬────────┘   └────────────────┬─────────────────┘ │
│           │                             │                    │
│  ┌────────▼─────────────────────────────▼─────────────────┐ │
│  │                   Application Layer                     │ │
│  │  GameService  |  UserService  |  AlertService          │ │
│  │  ImportService  |  PriceService  |  AdminService       │ │
│  └────────────────────────┬────────────────────────────────┘ │
│                           │                                   │
│  ┌────────────────────────▼────────────────────────────────┐ │
│  │              External Provider Abstraction              │ │
│  │  IExternalGameProvider                                  │ │
│  │    ├── SteamGameProvider                                │ │
│  │    ├── (future) GogGameProvider                         │ │
│  │    └── (future) EpicGameProvider                        │ │
│  │  IExternalPriceProvider                                 │ │
│  │    └── ItadPriceProvider                                │ │
│  └────────────────────────┬────────────────────────────────┘ │
│                           │                                   │
│  ┌────────────────────────▼────────────────────────────────┐ │
│  │              Data / Infrastructure Layer                │ │
│  │  AppDbContext (EF Core) → PostgreSQL                    │ │
│  │  IMemoryCache (catalog + detail pages)                  │ │
│  └─────────────────────────────────────────────────────────┘ │
│                                                               │
│  ┌──────────────────────────────────────────────────────────┐ │
│  │           Background Services (.NET BackgroundService)   │ │
│  │  ├── PriceRefreshBackgroundService  (nightly)            │ │
│  │  └── AlertCheckerBackgroundService  (every 15 min)       │ │
│  └──────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
           │                          │
    ┌──────▼──────┐           ┌───────▼───────┐
    │  Steam API  │           │   ITAD API    │
    │  (OpenID +  │           │  (prices)     │
    │  Store API) │           └───────────────┘
    └─────────────┘
```

### 5.2 Layer Responsibilities

| Layer | Responsibility |
|-------|---------------|
| **Razor Pages / MVC** | Rendering, form handling, user session, model binding |
| **API Controllers** | JSON endpoints for AJAX catalog filters, admin triggers |
| **Application Services** | Business logic, orchestration, no EF Core leakage |
| **Provider Abstraction** | Adapters to external APIs; returns domain DTOs |
| **Data Layer** | EF Core DbContext, repositories (thin), migrations |
| **Background Services** | Scheduled tasks — price refresh, alert checking |

### 5.3 Authentication Flow

```
Local Login:  Browser → ASP.NET Identity (cookie) → Role Claims
Steam Login:  Browser → /auth/steam → SteamOpenIdMiddleware
              → Validate OpenID response → Link SteamId to User → Cookie issued
```

### 5.4 Caching Strategy

| Cache Key | TTL | Invalidated By |
|-----------|-----|----------------|
| `catalog:page:{hash_of_filters}` | 5 min | Admin game edit/delete |
| `game:detail:{appId}` | 15 min | Admin refresh or import |
| `genres:all` | 60 min | Game edit |
| `tags:popular` | 60 min | Game edit |

All caches use `IMemoryCache` — no Redis required.

---

## 6. Backend Folder Structure

```
GameDB/
├── GameDB.Web/                          # ASP.NET Core entry point
│   ├── Program.cs
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── Pages/                           # Razor Pages
│   │   ├── Index.cshtml                 # Home / catalog
│   │   ├── Game/
│   │   │   └── Detail.cshtml            # /game/{slug}
│   │   ├── Library/
│   │   │   └── Index.cshtml             # User library
│   │   ├── Wishlist/
│   │   │   └── Index.cshtml
│   │   ├── Alerts/
│   │   │   └── Index.cshtml
│   │   ├── Auth/
│   │   │   ├── Login.cshtml
│   │   │   ├── Register.cshtml
│   │   │   └── SteamCallback.cshtml
│   │   └── Admin/
│   │       ├── Index.cshtml             # Dashboard
│   │       ├── Games/Index.cshtml       # Game list
│   │       ├── Games/Edit.cshtml
│   │       ├── Logs/Index.cshtml
│   │       └── Incomplete/Index.cshtml  # Games with missing data
│   ├── Controllers/                     # API-style controllers
│   │   ├── CatalogApiController.cs      # AJAX filter/search endpoint
│   │   ├── AdminApiController.cs        # Trigger imports, refresh
│   │   ├── AlertApiController.cs
│   │   └── AuthController.cs            # Steam OpenID callback
│   ├── ViewModels/
│   │   ├── CatalogViewModel.cs
│   │   ├── GameDetailViewModel.cs
│   │   ├── CatalogFilterModel.cs
│   │   └── ...
│   └── Middleware/
│       └── SteamOpenIdMiddleware.cs
│
├── GameDB.Application/                  # Business logic
│   ├── Services/
│   │   ├── GameService.cs
│   │   ├── CatalogService.cs            # Filter + search orchestration
│   │   ├── UserService.cs
│   │   ├── LibraryService.cs
│   │   ├── WishlistService.cs
│   │   ├── AlertService.cs
│   │   ├── ImportService.cs             # Orchestrates Steam import
│   │   ├── PriceService.cs
│   │   └── AdminService.cs
│   ├── DTOs/
│   │   ├── GameDto.cs
│   │   ├── GameSummaryDto.cs
│   │   ├── CatalogFilterDto.cs
│   │   ├── PriceDto.cs
│   │   ├── UserDto.cs
│   │   └── ImportResultDto.cs
│   └── Interfaces/
│       ├── IGameService.cs
│       ├── ICatalogService.cs
│       ├── IImportService.cs
│       ├── IAlertService.cs
│       └── IPriceService.cs
│
├── GameDB.Domain/                       # Pure domain models (no dependencies)
│   ├── Entities/
│   │   ├── Game.cs
│   │   ├── Genre.cs
│   │   ├── Tag.cs
│   │   ├── GamePrice.cs
│   │   ├── User.cs
│   │   ├── UserGame.cs
│   │   ├── Alert.cs
│   │   ├── ExternalGameMapping.cs
│   │   └── ImportLog.cs
│   ├── Enums/
│   │   ├── UserGameStatus.cs            # Owned, Wishlist, Ignored
│   │   ├── ProviderName.cs              # Steam, GOG, Epic, ITAD
│   │   ├── DataStatus.cs                # Full, Partial, Missing
│   │   └── ImportType.cs
│   └── ValueObjects/
│       └── SteamAppId.cs
│
├── GameDB.Infrastructure/               # EF Core + External providers
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   ├── Configurations/              # IEntityTypeConfiguration<T>
│   │   │   ├── GameConfiguration.cs
│   │   │   ├── UserConfiguration.cs
│   │   │   ├── AlertConfiguration.cs
│   │   │   └── ...
│   │   ├── Migrations/
│   │   └── Repositories/               # Optional thin repos
│   │       ├── GameRepository.cs
│   │       └── UserRepository.cs
│   ├── ExternalProviders/
│   │   ├── Abstractions/
│   │   │   ├── IExternalGameProvider.cs
│   │   │   ├── IExternalPriceProvider.cs
│   │   │   └── ExternalGameData.cs      # Shared DTO
│   │   ├── Steam/
│   │   │   ├── SteamGameProvider.cs
│   │   │   ├── SteamLibraryProvider.cs
│   │   │   ├── SteamApiClient.cs        # HTTP client
│   │   │   └── SteamModels/             # API response models
│   │   │       ├── SteamAppDetailsResponse.cs
│   │   │       └── SteamOwnedGamesResponse.cs
│   │   └── ITAD/
│   │       ├── ItadPriceProvider.cs
│   │       ├── ItadApiClient.cs
│   │       └── ItadModels/
│   ├── BackgroundServices/
│   │   ├── PriceRefreshBackgroundService.cs
│   │   └── AlertCheckerBackgroundService.cs
│   └── Caching/
│       └── CatalogCacheService.cs
│
└── GameDB.Tests/                        # xUnit test project
    ├── Unit/
    │   ├── GameServiceTests.cs
    │   ├── AlertServiceTests.cs
    │   └── SteamProviderTests.cs
    └── Integration/
        └── CatalogFilterTests.cs
```

---

## 7. API Endpoints

All endpoints return JSON. Razor Pages handle HTML rendering. API endpoints are consumed by JavaScript on the page (AJAX filters, admin triggers).

### 7.1 Catalog (Public)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/catalog` | Filtered, paginated game list |
| GET | `/api/catalog/{appId}` | Single game detail |
| GET | `/api/catalog/genres` | List of all genres |
| GET | `/api/catalog/tags` | Popular tags |

**GET /api/catalog — Query Parameters:**

```
page          int       default 1
pageSize      int       default 50, max 100
q             string    full-text search
genre         int[]     genre IDs
tag           string[]  tag names
minPrice      decimal
maxPrice      decimal
minDiscount   int       0–100
isFree        bool
releasedAfter date
releasedBefore date
minMetacritic int
singlePlayer  bool
multiPlayer   bool
controllerSupport  string   full|partial
steamDeckSupport   string   Verified|Playable
sortBy        string    name|price|discount|releaseDate|metacritic
sortDir       string    asc|desc
```

**Response:**
```json
{
  "items": [
    {
      "appId": 570,
      "name": "Dota 2",
      "slug": "dota-2",
      "headerImage": "https://...",
      "isFree": true,
      "releaseDate": "2013-07-09",
      "genres": ["Action","Strategy"],
      "currentPrice": 0.00,
      "discountPercent": 0,
      "metacriticScore": null,
      "steamDeckSupport": "Verified"
    }
  ],
  "totalCount": 1200,
  "page": 1,
  "pageSize": 50,
  "totalPages": 24
}
```

### 7.2 User Library & Wishlist

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/library` | User's owned games |
| POST | `/api/library/import` | Import Steam library (async trigger) |
| GET | `/api/wishlist` | User's wishlist |
| POST | `/api/wishlist/{appId}` | Add game to wishlist |
| DELETE | `/api/wishlist/{appId}` | Remove from wishlist |

### 7.3 Alerts

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/alerts` | List user alerts |
| POST | `/api/alerts` | Create alert |
| PUT | `/api/alerts/{id}` | Update alert |
| DELETE | `/api/alerts/{id}` | Delete alert |

**POST /api/alerts Body:**
```json
{
  "appId": 1091500,
  "targetPrice": 19.99,
  "targetDiscount": null
}
```

### 7.4 Admin

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/admin/import/full` | Admin | Start full Steam catalog import |
| POST | `/api/admin/import/game/{appId}` | Admin | Refresh single game |
| POST | `/api/admin/prices/refresh` | Admin | Trigger ITAD price refresh |
| GET | `/api/admin/logs` | Admin | Paginated import logs |
| GET | `/api/admin/games/incomplete` | Admin | Games with DataStatus != Full |
| PUT | `/api/admin/games/{id}` | Admin | Edit game metadata |
| DELETE | `/api/admin/games/{id}` | Admin | Delete game |
| GET | `/api/admin/stats` | Admin | Dashboard stats (total games, last sync, alert count) |

### 7.5 Auth

| Method | Path | Description |
|--------|------|-------------|
| POST | `/auth/register` | Email registration |
| POST | `/auth/login` | Email login |
| POST | `/auth/logout` | Logout |
| GET | `/auth/steam` | Redirect to Steam OpenID |
| GET | `/auth/steam/callback` | Steam OpenID return URL |

---

## 8. Steam Integration Flow

### 8.1 Full Catalog Import

The biggest challenge: Steam has ~100k+ apps. A naive full import takes hours. The solution is a **two-phase import**:

**Phase 1 — App List Seeding (fast, ~30 sec)**

```
1. GET https://steamspy.com/api.php?request=all
   → Returns dictionary of appid → { appid, name } — ~100k items

2. For each app:
   - If Games.AppId does not exist → INSERT with Name + AppId, DataStatus = 'Missing'
   - If exists → skip (or update name if changed)
   → Bulk upsert using EF Core 8 ExecuteUpdate or raw SQL for performance
   → ~5–10 sec total
```

**Phase 2 — Metadata Fill (background, batched)**

```
1. Select top N games WHERE DataStatus = 'Missing' ORDER BY some_priority
   (Priority: games with high tag weight, popular names, manual admin trigger)

2. For each batch of 5 (Steam rate limit: ~200 req/5min):
   GET https://store.steampowered.com/api/appdetails?appids={appid}&cc=us&l=en

3. Parse response:
   - If success: update all fields, DataStatus = 'Full', LastSyncedAt = NOW()
   - If not a game (DLC, video, etc.): mark DataStatus = 'Missing' with note
   - If error: log, increment retry count

4. Sleep 1.5 sec between requests to respect rate limit
5. Background service runs this in chunks of 50/request cycle
```

**ImportService pseudo-code:**
```csharp
public async Task RunFullImportAsync(CancellationToken ct)
{
    var log = await CreateImportLog(ImportType.FullCatalogImport);
    try
    {
        // Phase 1
        var allApps = await _steamSpy.GetAppListAsync(ct);
        await _gameRepository.BulkSeedAppsAsync(allApps, ct);
        
        // Phase 2 runs separately as BackgroundService, not blocking this call
        log.Status = ImportStatus.Completed;
    }
    catch (Exception ex)
    {
        log.Status = ImportStatus.Failed;
        log.ErrorMessage = ex.Message;
    }
    finally { await UpdateImportLog(log); }
}
```

### 8.2 Single Game Refresh

```
1. GET /api/appdetails?appids={appId}&cc=us&l=en
2. Map SteamAppDetailsResponse → Game entity
3. Upsert genres/tags (create if not exist)
4. Update GamePrices if price data present in response
5. Update ExternalGameMappings to ensure Steam entry exists
6. Set DataStatus = 'Full', LastSyncedAt = NOW()
```

### 8.3 Steam OpenID Authentication

```
1. User clicks "Connect Steam"
2. Redirect to:
   https://steamcommunity.com/openid/login
   ?openid.mode=checkid_setup
   &openid.return_to=https://gamedb.local/auth/steam/callback
   &openid.realm=https://gamedb.local
   &openid.claimed_id=http://specs.openid.net/auth/2.0/identifier_select

3. Steam redirects back to /auth/steam/callback with openid params
4. Server-side validation:
   - POST to https://steamcommunity.com/openid/login with openid.mode=check_authentication
   - Parse SteamID from openid.claimed_id: https://steamcommunity.com/openid/id/{steamId64}
5. If user is already logged in → link SteamId to existing User
   If not logged in → create new User with SteamId (no password required)
6. Issue auth cookie
```

**Dependency:** `AspNet.Security.OpenId.Steam` NuGet package simplifies this significantly.

### 8.4 User Library Import

```
1. User clicks "Import Steam Library"
2. GET https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/
   ?key={STEAM_API_KEY}
   &steamid={user.SteamId}
   &include_appinfo=1
   &include_played_free_games=1

3. For each game in response:
   a. Find or create Game by AppId (Phase 1 seed if missing)
   b. Upsert UserGame: UserId + GameId, Status = Owned, PlaytimeMinutes = playtime_forever

4. Log result to ImportLogs (Type = LibraryImport)
```

### 8.5 ITAD Price Refresh

```
ITAD API: https://api.isthereanydeal.com/

Flow:
1. GET /games/lookup/v1 → resolve Steam AppIds to ITAD game IDs
2. GET /games/prices/v3 → get current prices + historical lowest
3. Update GamePrices table
4. Check Alerts: for each active alert where (currentPrice <= targetPrice OR discountPercent >= targetDiscount)
   → Set Alert.TriggeredAt = NOW(), IsActive = FALSE
   → Add notification record (or simple in-app flag on User)
```

---

## 9. Admin Panel Workflow

### 9.1 Dashboard (Admin Index)

Displays:
- Total games in catalog
- Games with `DataStatus = 'Full'` vs `'Partial'` vs `'Missing'` (counts + bar)
- Last full import timestamp
- Active alert count
- Quick action buttons: **[Run Full Import]**, **[Refresh Prices]**

### 9.2 Game Management

**Games list page:**
- Table: Name, AppId, DataStatus, LastSyncedAt, Price, Actions
- Filter by DataStatus, Genre
- Search by name
- Per-row actions: **[Refresh]**, **[Edit]**, **[Delete]**

**Edit game page:**
- Form with all metadata fields
- Read-only AppId
- Save triggers cache invalidation for that game

### 9.3 Import Trigger Flow

```
Admin clicks [Run Full Import]
→ POST /api/admin/import/full
→ ImportService.RunFullImportAsync() starts in background (Task.Run, not awaited)
→ Response: 202 Accepted + { logId: 123 }
→ Admin page polls GET /api/admin/logs/123 every 3 sec to show progress
→ When log.Status = Completed/Failed → show result toast
```

### 9.4 Logs View

- Table: Type, Status, StartedAt, Duration, ProcessedCount, FailedCount, TriggeredBy
- Status badge: green/red/yellow
- Click row to expand ErrorMessage

### 9.5 Incomplete Games View

- Games where `DataStatus != 'Full'`
- Columns: AppId, Name, DataStatus, LastSyncedAt
- Bulk action: **[Refresh All Incomplete]**
- Admin can trigger individual refresh per row

---

## 10. MVP Scope

The MVP is intentionally scoped to deliver a working product without over-building.

### 10.1 MVP Includes

| Feature | Notes |
|---------|-------|
| Steam app list seeding | Full ~100k app bulk insert |
| Metadata fill (background) | Top ~2k popular games initially |
| Basic catalog + filters | Genre, price, discount, free/paid, search |
| Game detail page | Name, description, genres, tags, price, header image |
| User registration + login | Email/password via ASP.NET Identity |
| Steam OpenID connect | Link Steam account |
| Steam library import | Owned games only |
| Wishlist (manual add) | User adds from game detail page |
| Price alerts | Target price or discount% |
| Admin: import trigger | Full import + single game refresh |
| Admin: logs view | Import log table |
| Admin: edit/delete game | Manual metadata correction |
| ITAD price data | Current price + discount% per game |

### 10.2 MVP Excludes (Post-MVP)

| Feature | Reason Deferred |
|---------|----------------|
| Email notifications for alerts | Needs SMTP setup; in-app is enough for MVP |
| Price history charts | Needs historical data accumulation |
| Screenshots/video on detail page | API available but adds storage complexity |
| Social features (reviews, ratings) | Scope creep |
| GOG / Epic integration | Architecture is ready, not time |
| Advanced analytics | Low priority |

### 10.3 MVP Delivery Estimate

| Component | Effort |
|-----------|--------|
| DB schema + EF migrations | 1 day |
| Steam import pipeline | 2 days |
| Catalog with filters | 2 days |
| Auth + Steam OpenID | 1.5 days |
| Library + wishlist | 1 day |
| Alerts | 1 day |
| Admin panel | 1.5 days |
| ITAD price integration | 1 day |
| UI polish + testing | 1 day |
| **Total** | **~12 days** |

---

## 11. Future Expansion Plan (GOG / Epic Ready)

### 11.1 Provider Abstraction Interfaces

The core abstraction that makes multi-platform possible without refactoring:

```csharp
// GameDB.Infrastructure/ExternalProviders/Abstractions/

public interface IExternalGameProvider
{
    string ProviderName { get; }  // "Steam" | "GOG" | "Epic"

    /// Fetch metadata for a single game by external ID
    Task<ExternalGameData?> GetGameByExternalIdAsync(string externalId, CancellationToken ct);

    /// Fetch a page of the provider's catalog (for import)
    Task<IEnumerable<ExternalGameSummary>> GetCatalogPageAsync(int page, int pageSize, CancellationToken ct);

    /// Fetch user's owned games
    Task<IEnumerable<ExternalOwnedGame>> GetOwnedGamesAsync(string externalUserId, CancellationToken ct);
}

public interface IExternalPriceProvider
{
    string ProviderName { get; }
    Task<IEnumerable<ExternalPriceData>> GetPricesAsync(IEnumerable<string> externalIds, CancellationToken ct);
}

// Shared DTO — all providers map to this
public class ExternalGameData
{
    public string ExternalId { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public string? HeaderImageUrl { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public bool IsFree { get; set; }
    public List<string> Genres { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public int? MetacriticScore { get; set; }
    public SystemRequirements? Requirements { get; set; }
    public ProviderName Provider { get; set; }
}
```

### 11.2 Registration Pattern (DI)

```csharp
// Program.cs — adding a new provider = one line
builder.Services.AddScoped<IExternalGameProvider, SteamGameProvider>();
// Future:
// builder.Services.AddScoped<IExternalGameProvider, GogGameProvider>();
// builder.Services.AddScoped<IExternalGameProvider, EpicGameProvider>();

// ImportService resolves all registered providers:
public class ImportService
{
    private readonly IEnumerable<IExternalGameProvider> _providers;
    // Iterates all providers — zero code changes needed for new platform
}
```

### 11.3 ExternalGameMappings Design

Every game in the `Games` table is platform-agnostic. The link to external platforms lives in `ExternalGameMappings`:

```
Game (Id=1001, Name="Cyberpunk 2077")
  ├── ExternalGameMapping { ProviderName="Steam", ExternalId="1091500" }
  ├── ExternalGameMapping { ProviderName="GOG",   ExternalId="1423049311" }  ← future
  └── ExternalGameMapping { ProviderName="ITAD",  ExternalId="cyberpunk-2077" }
```

This allows:
- Deduplication: same game, multiple storefronts
- Price comparison across platforms (ITAD supports this natively)
- Cross-platform library merging in future

### 11.4 GOG Integration Sketch

When GOG support is added, the only new code required is:

```
GameDB.Infrastructure/ExternalProviders/GOG/
├── GogGameProvider.cs          ← implements IExternalGameProvider
├── GogApiClient.cs             ← HTTP client for api.gog.com
└── GogModels/
    └── GogProductResponse.cs   ← maps to ExternalGameData
```

`ImportService`, `CatalogService`, `GameService` — **zero changes**.

### 11.5 User Account Extensibility

```csharp
// Future: Users table gets additional provider links
// Option A: Separate table (preferred)
ExternalUserAccounts
├── UserId        UUID FK → Users
├── ProviderName  VARCHAR(20)   -- 'Steam'|'GOG'|'Epic'
├── ExternalUserId VARCHAR(100)
└── AccessToken   VARCHAR(512)  -- encrypted

// Option B: Simple columns (fine for 2-3 providers)
Users.GogUserId   VARCHAR(50)
Users.EpicUserId  VARCHAR(50)
```

Option A is already reflected in the architecture intention; for MVP we keep `SteamId` directly on `Users` and migrate to a join table when adding GOG.

### 11.6 Migration Path

```
Phase 1 (MVP):     Steam only — catalog, library, prices, alerts
Phase 2:           GOG integration — add GogGameProvider, GOG OAuth
Phase 3:           Epic integration — Epic Games Store API (limited public API)
Phase 4:           Cross-platform price comparison pages
Phase 5:           Unified user library across all platforms
```

---

## Appendix A — Key NuGet Packages

| Package | Use |
|---------|-----|
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | ASP.NET Identity |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | PostgreSQL EF Core provider |
| `AspNet.Security.OpenId.Steam` | Steam OpenID authentication |
| `Microsoft.Extensions.Http` | IHttpClientFactory for API clients |
| `Microsoft.Extensions.Caching.Memory` | IMemoryCache |
| `Serilog.AspNetCore` | Structured logging |

## Appendix B — Environment Variables

```env
# appsettings.Development.json / environment
ConnectionStrings__DefaultConnection=Host=localhost;Database=gamedb;Username=...;Password=...
Steam__ApiKey=YOUR_STEAM_WEB_API_KEY
Steam__OpenId__ReturnUrl=https://localhost:5001/auth/steam/callback
Steam__OpenId__Realm=https://localhost:5001
ITAD__ApiKey=YOUR_ITAD_API_KEY
```

## Appendix C — Docker Compose (Optional)

```yaml
version: '3.9'
services:
  db:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: gamedb
      POSTGRES_USER: gamedb
      POSTGRES_PASSWORD: gamedb_dev
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data

  web:
    build: .
    ports:
      - "5000:8080"
    environment:
      ConnectionStrings__DefaultConnection: "Host=db;Database=gamedb;Username=gamedb;Password=gamedb_dev"
      Steam__ApiKey: "${STEAM_API_KEY}"
      ITAD__ApiKey: "${ITAD_API_KEY}"
    depends_on:
      - db

volumes:
  pgdata:
```

---

*Document end. Version 1.0 — GameDB SRS & Architecture.*
