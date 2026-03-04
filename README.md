# YtProducer

## Architecture Overview

`YtProducer` is a clean-architecture oriented skeleton for long-running content orchestration.

- `YtProducer.Api` is a minimal ASP.NET Core API orchestrator for playlist operations.
- `YtProducer.Worker` runs background polling for pending jobs and executes placeholder MCP calls.
- `YtProducer.Domain` contains pure domain entities and enums without EF Core dependencies.
- `YtProducer.Infrastructure` contains EF Core PostgreSQL persistence, service implementations, and queue polling abstractions.
- `YtProducer.Contracts` contains request/response DTOs used by API boundaries.
- `ui` is a Vite + React dashboard skeleton that displays playlists and status badges.

## Run With Docker

From the repository root:

```bash
cd docker
docker compose up --build
```

Services:

- API: `http://localhost:8080`
- PostgreSQL: `localhost:5432`
- Worker: background process attached to the same database

The PostgreSQL bootstrap script is located at `docker/postgres/init.sql`.

## Run Locally Without Docker

```bash
dotnet build YtProducer.sln
dotnet run --project src/YtProducer.Api
dotnet run --project src/YtProducer.Worker
```

UI:

```bash
cd ui
npm install
npm run dev
```

## Future Roadmap

- MCP integration for music/image/video generation pipelines.
- Retry policies with exponential backoff and dead-letter behavior.
- Distributed worker scaling with partitioned DB polling and lease-based locking.
