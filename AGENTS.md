# AGENTS.md

ASP.NET Core 10 minimal API for a TV/movie tracker, backed by **Oracle** via **Dapper**. Single project: `MediaTracker.Api`. Schema: `database/schema.sql`.

## Commands

- **`dotnet build` / `dotnet test` from the repo root FAILS** (`MSB1003`) — there is no `.sln`. Always target the project:
  - `dotnet build MediaTracker.Api`
  - `dotnet run --project MediaTracker.Api`
  - or run from inside `MediaTracker.Api/`
- Run URL: `http://localhost:5165`; Swagger UI at `/swagger` (Development only, per `launchSettings.json` + `Program.cs:44-49`).
- No test project, no linter, no formatter, no `.editorconfig`, `Directory.Build.props`, `global.json`, or CI. **A successful build is the only automated verification available.** Don't claim tests pass.
- DB container: `docker start media-tracker-db` — it does **not** auto-start on reboot; check `docker ps -a`. Port 1521, service `FREEPDB1`, app user `media_app`.

### Required local setup (not in the repo — repo is public)
Secrets live in user-secrets, never `appsettings.json`. `UserSecretsId` is already set in the csproj, so `init` is done — only `set` is needed:
```
dotnet user-secrets set "ConnectionStrings:OracleDb" "User Id=media_app;Password=...;Data Source=localhost:1521/FREEPDB1"
dotnet user-secrets set "Tmdb:ApiKey" "<tmdb-v3-key>"
```
Both are read via `IConfiguration` (`GetConnectionString("OracleDb")`, `config["Tmdb:ApiKey"]`). A missing value is a `null` deref at runtime, not a startup error.

## Architecture

Per-entity three-layer slice, no exceptions:
```
Models/<Entity>.cs                          plain class mirroring the table
Repositories/I<Entity>Repository.cs        interface
Repositories/<Entity>Repository.cs         Dapper + Oracle SQL
Endpoints/<Entity>Endpoints.cs             static class, Map<Entity>Endpoints(this WebApplication), app.MapGroup(...)
```
Adding an entity means touching exactly: Model, `I<Entity>Repository`, `<Entity>Repository`, `<Entity>Endpoints`, then **two lines in `Program.cs`** (`AddScoped<I<Entity>Repository, EntityRepository>()` and `app.Map<Entity>Endpoints();`).

Constraints to preserve:
- **Dapper, not EF Core. No MVC controllers. Minimal APIs only.** Deliberate choice for Oracle compatibility.
- Repositories take `IConfiguration` and open a **new `OracleConnection` per method**. There is no `IDbConnectionFactory`/DbContext — don't introduce one.
- Endpoints return `Results.*` and bind repositories from DI. Media type is `'MOVIE'`/`'SHOW'` in the DB, but TMDB endpoints take `"movie"`/`"tv"` and translate (see `Endpoints/TmdbEndpoints.cs:30`).
- Nested routes are real: `/media/{mediaId}/seasons/{seasonId}/episodes`.

## Oracle SQL rules (silent breakage if ignored)

- Bind parameters are `:Name`, **not** `@Name`.
- New IDs: `RETURNING <col> INTO :NewId` plus a `DynamicParameters` entry with `ParameterDirection.Output`, then `parameters.Get<int>("NewId")` (`Repositories/MediaRepository.cs:41-53`).
- Upserts use `MERGE INTO ... USING (SELECT :p AS col FROM dual) ... WHEN MATCHED/NOT MATCHED` — the one-row-per-media tables (`watch_status`, `ratings`).
- **Every SELECT aliases columns to PascalCase** (`media_id AS MediaId`). Oracle returns uppercase column names, so these aliases are what make Dapper mapping work — not cosmetic.
- FKs are all `ON DELETE CASCADE`; no manual child cleanup. Timestamps use `SYSDATE`.
- `database/schema.sql` is plain `CREATE TABLE` with no drops — it is not idempotent, re-running it errors out.

## Gotchas

- Swashbuckle is added by hand (.NET 10 template ships none). `AddEndpointsApiExplorer()` and `AddSwaggerGen()` go together in `Program.cs:38-39` — drop one and Swagger generation fails.
- The named `"Tmdb"` HttpClient overrides `ConfigurePrimaryHttpMessageHandler` to force IPv4 (`Program.cs:18-35`) because IPv6-first DNS made TMDB calls hang ~100s on the author's machine. Keep it unless you've verified IPv6 works.
- `MediaTracker.Api.http` still calls `/weatherforecast/` — stale, that endpoint is gone.
- `README.md` is a 2-line stub (writing a real one is Stage E in the plan).

## Workflow

- **`.agents/plans/Main-Plan.md` is the single source of truth** for project status, completed/pending work, architecture rationale, and gotchas. Read it before starting a task. `.agents/` and `skills-lock.json` are gitignored, so the plan is local-only — don't expect it in a fresh clone.
- Next task = first unchecked box under **Pending Work** (currently Stage A: `EpisodeProgress` model/repository/endpoints — the table exists in the schema with no code behind it). Implement, tick the checkbox, update the status table.
- No migration tool; schema edits go directly into `database/schema.sql`.
- Commits are small, present-tense imperative, one per completed checkbox, message taken from the plan's suggested text (e.g. `Add EpisodeProgress model, repository, and endpoints`). Don't batch whole stages into one commit — readable incremental history is the point. Don't commit unless asked.
- Local skills in `.agents/skills/` (`dotnet-backend-patterns`, `dotnet-best-practices`, `dotnet-design-pattern-review`) are available and gitignored.
