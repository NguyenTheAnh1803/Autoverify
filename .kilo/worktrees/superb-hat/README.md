# AutoVerify System

AutoVerify is an MVP foundation for automatic verification, validation, and anomaly detection across MariaDB databases and external APIs.

## Current Implementation

- React + TypeScript operations UI.
- ASP.NET Core 8.0 API with MVP endpoints.
- Temporary login credentials: `admin` / `admin!`.
- MariaDB operational database for users, sources, scan lists, scan executions, results, product queries, audit events, and SeaweedFS storage metadata.
- SeaweedFS HTTP storage manager for JSON and image uploads, with local filesystem fallback when SeaweedFS is not configured.
- C++20 anomaly engine skeleton with deterministic threshold scoring.
- Protocol Buffer contract placeholder for the future gRPC engine boundary.

## Local Development

Run the API:

```powershell
dotnet run --project src/backend/AutoVerify.Api/AutoVerify.Api.csproj --urls http://localhost:5057
```

Run the web application:

```powershell
Set-Location src/web
npm install
npm run dev
```

Open `http://localhost:5173` and sign in with `admin` / `admin!`.

Create the MariaDB database and user before running the API:

```sql
CREATE DATABASE autoverify CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'autoverify'@'localhost' IDENTIFIED BY 'autoverify-pass';
GRANT ALL PRIVILEGES ON autoverify.* TO 'autoverify'@'localhost';
FLUSH PRIVILEGES;
```

The API creates the initial tables on startup through EF Core `EnsureCreated`.

Default connection string in `src/backend/AutoVerify.Api/appsettings.json`:

```text
Server=localhost;Port=3306;Database=autoverify;User=autoverify;Password=autoverify-pass;CharSet=utf8mb4;Allow User Variables=True;
```

When SeaweedFS is not configured, uploaded JSON and image objects are stored locally under:

```text
src/backend/AutoVerify.Api/data/storage
```

Configure SeaweedFS HTTP upload by setting `SeaweedFs:BaseUrl` in `src/backend/AutoVerify.Api/appsettings.json` or through environment configuration. Example value:

```text
http://localhost:8888
```

Build the C++ engine:

```powershell
.\scripts\build-engine.ps1 -Configuration Release
```

Build everything:

```powershell
.\scripts\build-all.ps1 -Configuration Release
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
- `GET /api/v1/audit-events`
- `GET /api/v1/system/health`

## Next Implementation Steps

1. Add EF Core migrations instead of `EnsureCreated` for controlled database evolution.
2. Add real JWT validation middleware and secure refresh-token cookies.
3. Add RabbitMQ scan orchestration and worker service.
4. Generate C# and C++ code from `contracts/proto/anomaly_engine.proto` and expose a real gRPC engine service.
5. Implement real MariaDB/API connectors with query templates, source concurrency limits, retries, and circuit breakers.
6. Add automated backend, frontend, and C++ tests in CI.
