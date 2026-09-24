# AutoVerify System

AutoVerify is an MVP foundation for automatic verification, validation, and anomaly detection across MariaDB databases and external APIs.

## Current Implementation

- React + TypeScript operations UI.
- ASP.NET Core 8.0 API with MVP endpoints.
- Temporary login credentials: `admin` / `admin!` for local verification only.
- MariaDB operational database for users, sources, scan lists, scan executions, results, product queries, audit events, and SeaweedFS storage metadata.
- SeaweedFS HTTP storage manager for JSON and image uploads, with local filesystem fallback when SeaweedFS is not configured.
- The C++ engine is parked under `src/engine` and is not part of the current application runtime or default build.

## Component Boundaries

The project is intentionally separated into independently buildable components:

- Backend: `src/backend/AutoVerify.Api`
- Frontend: `src/web`
- Algorithm engine: `src/engine`
- Contracts: `contracts/proto`

Upgrade rule:

- Frontend depends only on HTTP API contracts and `VITE_API_BASE_URL`.
- Backend owns MariaDB metadata, SeaweedFS upload, audit, and orchestration APIs.
- Algorithm engine owns compute-heavy C++ logic and should communicate through versioned contracts.
- Shared changes should go through `contracts/proto` or documented HTTP contracts, not direct code coupling.

## Local Development

Run the C# API. On its first successful startup, EF Core creates the `autoverify` MariaDB database (when the configured account has `CREATE DATABASE` permission), all required tables, indexes, and seed data.

```powershell
dotnet run --project src/backend/AutoVerify.Api/AutoVerify.Api.csproj --urls http://localhost:5058
```

Run the web application:

```powershell
Set-Location src/web
npm install
npm run dev
```

Open `http://localhost:5173` and sign in with `admin` / `admin!`.

Configure the frontend API URL with `src/web/.env`:

```text
VITE_API_BASE_URL=http://localhost:5058/api/v1
```

If the configured MariaDB account cannot create databases, create the database once:

```sql
CREATE DATABASE autoverify CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

The API creates all operational tables on startup through EF Core `EnsureCreated`, then inserts the local `admin` account and initial application data. Required tables include users, data sources, scan lists, executions, verification results, audit events, storage objects, product queries, and query profiles.

Default connection string in `src/backend/AutoVerify.Api/appsettings.json`:

```text
Server=localhost;Port=3306;Database=autoverify;User=root;Password=1111;CharSet=utf8mb4;Allow User Variables=True;
```

When SeaweedFS is not configured, uploaded JSON and image objects are stored locally under:

```text
src/backend/AutoVerify.Api/data/storage
```

Configure SeaweedFS HTTP upload by setting `SeaweedFs:BaseUrl` in `src/backend/AutoVerify.Api/appsettings.json` or through environment configuration. Example value:

```text
http://localhost:8888
```

Build backend only:

```powershell
.\scripts\build-backend.ps1 -Configuration Release
```

Release backend only:

```powershell
.\scripts\release-backend.ps1 -Configuration Release -Output artifacts\backend
```

Build frontend only:

```powershell
.\scripts\build-frontend.ps1 -ApiBaseUrl http://localhost:5058/api/v1
```

Release frontend only:

```powershell
.\scripts\release-frontend.ps1 -ApiBaseUrl http://localhost:5058/api/v1 -Output artifacts\frontend
```

Embed frontend into backend `wwwroot`:

```powershell
.\scripts\embed-frontend.ps1 -ApiBaseUrl http://localhost:5058/api/v1
dotnet publish src/backend/AutoVerify.Api/AutoVerify.Api.csproj -c Release -o artifacts\backend-embedded
```

Run the embedded backend + frontend package:

```powershell
.\artifacts\backend-embedded\AutoVerify.Api.exe --urls http://localhost:5058
```

Then open `http://localhost:5058`. API endpoints remain under `/api/v1`.

Build algorithm engine only (automatically discovers Visual Studio MSVC and CMake):

```powershell
.\scripts\build-engine.ps1 -Configuration Release
```

Release algorithm engine only:

```powershell
.\scripts\release-engine.ps1 -Configuration Release -Output artifacts\engine
```

Build the current application (C# API + React):

```powershell
.\scripts\build-all.ps1 -Configuration Release
```

Build the parked algorithm engine only when explicitly needed:

```powershell
.\scripts\build-all.ps1 -Configuration Release -IncludeEngine
```

The C++ build script uses CMake when available. If CMake is not installed, it falls back to direct compilation with `cl`, `g++`, or `clang++`.

Required native toolchain options:

- Visual Studio Build Tools with Desktop development with C++ and CMake.
- MinGW-w64 with `g++` available in PATH.
- LLVM with `clang++` available in PATH.

## API Endpoints

- `POST /api/v1/auth/login`
- `GET /api/v1/dashboard/summary`
- `GET /api/v1/dashboard/trends`
- `POST /api/v1/imports/preview`
- `POST /api/v1/scan-lists`
- `GET /api/v1/scan-lists`
- `POST /api/v1/scan-executions`
- `GET /api/v1/scan-executions`
- `GET /api/v1/scan-executions/{executionId}/results`
- `GET /api/v1/data-sources`
- `POST /api/v1/data-sources/{sourceId}/test`
- `POST /api/v1/storage/json`
- `POST /api/v1/storage/images`
- `GET /api/v1/storage`
- `POST /api/v1/product-queries`
- `GET /api/v1/product-queries`
- `POST /api/v1/product-queries/{queryId}/status`
- `POST /api/v1/query-profiles`
- `GET /api/v1/query-profiles`
- `POST /api/v1/query-profiles/{profileId}/render`
- `GET /api/v1/audit-events`
- `GET /api/v1/system/health`

## Query Customization

Machine, line, and concept-specific query behavior is stored in MariaDB through `QueryProfiles`.

Each query profile contains:

- `MachineCode`
- `LineCode`
- `Concept`
- `SourceSystem`
- `SqlTemplate`
- `ParameterSchemaJson`
- `TimeoutSeconds`
- `MaxConcurrency`
- `Notes`

SQL templates use `{{parameterName}}` placeholders. The preview endpoint renders placeholders as SQL literals for engineering review.

Example template:

```sql
SELECT product_id, status, updated_at
FROM products
WHERE product_id = {{productId}}
  AND line_code = {{lineCode}}
LIMIT 1
```

Current safety rules:

- Only `SELECT` or `WITH` templates are accepted.
- Mutating keywords such as `INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `TRUNCATE`, `CREATE`, `REPLACE`, `MERGE`, `CALL`, and `EXEC` are blocked.
- Execution is not performed by this API yet; current endpoint is for storage and safe render preview.
- Real execution should happen inside a worker with read-only source credentials, bound parameters, timeouts, and per-profile concurrency limits.

## Next Implementation Steps

1. Add EF Core migrations instead of `EnsureCreated` for controlled database evolution.
2. Add real JWT validation middleware and secure refresh-token cookies.
3. Add RabbitMQ scan orchestration and worker service.
4. Generate C# and C++ code from `contracts/proto/anomaly_engine.proto` and expose a real gRPC engine service.
5. Implement real MariaDB/API connectors with query templates, source concurrency limits, retries, and circuit breakers.
6. Add automated backend, frontend, and C++ tests in CI.
