# Portfolio Management API

## Scope
PortfolioApi is an application backend: an ASP.NET Core 8 REST API with EF Core and SQL Server.
It is not an infrastructure repository. The Dockerfile and docker-compose.yml only run the API
and its database locally.

A REST API built with ASP.NET Core on .NET 8. Investors create portfolios, record Buy and Sell
transactions against assets, and read back holdings, profit and loss, and allocation.
Data is stored in SQL Server through EF Core (code first, with migrations).

## Prerequisites

- .NET 8 SDK (`dotnet --list-sdks` should list an `8.0.x` entry)
- SQL Server LocalDB (installed with the Visual Studio "Data storage and processing" workload),
  or any other SQL Server instance
- The EF Core command-line tool:
  `dotnet tool install --global dotnet-ef --version 8.0.11`

## Connection string

Set in `appsettings.json` under `ConnectionStrings:DefaultConnection`:

```
Server=(localdb)\MSSQLLocalDB;Database=PortfolioDb;Trusted_Connection=True;TrustServerCertificate=True
```

It uses Windows authentication, so there is no password in the repository. To use another
SQL Server instance, change the `Server=` part.

## Create the database

The migrations are committed in `Migrations/`. From the folder that contains `PortfolioApi.csproj`:

```
dotnet ef database update
```

To start again from an empty database:

```
dotnet ef database drop --force
dotnet ef database update
```

## Run

```
dotnet run
```

The API listens on `http://localhost:5220`. Swagger is at `http://localhost:5220/swagger`.

`requests.http` contains the worked example (steps 1 to 12) followed by the error cases.
Set `@host` at the top of the file, then send the requests in order against an empty database.

## Tests

```
dotnet test
```
• Domain Feature Unit Metrics: Multi-threaded validation checks process calculations cleanly under deterministic constraints.
• Autonomous High-Fidelity Test Runs: The testing runtime automatically swaps out production connections with an isolated SQLite In-Memory Engine. Tests compile schema layers inside isolated memory space, meaning testing workflows execute without relying on active external database installations.
• Code Coverage Tracking: Integrated alongside coverlet.collector tracing parameter execution loops automatically exported inside reports to assert continuous quality checks.
• Architecture Validation Checkpoints: Includes a structural net-arch evaluation verification layer inside ArchitectureTests.cs. This prevents dependency decay, asserting that web routing layers or outer controllers never interact directly with core database entity models.

The xUnit tests cover the money calculations (average cost, realized P&L, position values,
the summary figures and rounding) against the numbers in the brief.

## Endpoints

| Method and route | Success | Errors |
|---|---|---|
| `POST /api/assets` | 201 | 400, 409 duplicate symbol |
| `GET /api/assets?type=&search=` | 200 | 400 unknown type |
| `GET /api/assets/{symbol}` | 200 | 404 |
| `PUT /api/assets/{symbol}/price` | 200 | 400, 404 |
| `POST /api/portfolios` | 201 | 400, 409 same name for the same owner |
| `GET /api/portfolios?ownerEmail=` | 200 | 400 |
| `GET /api/portfolios/{id}` | 200 | 404 |
| `DELETE /api/portfolios/{id}` | 204 | 404, 409 has transactions |
| `POST /api/portfolios/{id}/transactions` | 201 | 400, 404, 409 insufficient holdings or concurrent change |
| `GET /api/portfolios/{id}/transactions` | 200, paged, newest first | 400, 404 |
| `GET /api/portfolios/{id}/holdings` | 200 | 404 |
| `GET /api/portfolios/{id}/summary` | 200 | 404 |

## Project structure

```
Controllers/   HTTP only: bind the request, call a service, return a DTO
Services/      Business rules, validation and database access
Common/        PositionCalculator (Calculator) and MoneyMaths (the only rounding helper)
Data/          AppDbContext and the EF entities
DTOs/          Request and response shapes. Controllers never see an entity
Exceptions/    NotFoundException, ConflictException, RequestValidationException, GlobalExceptionHandler
Migrations/    EF Core migrations
evidence/      Acceptance run and test output
```

## Design decisions

1. **Layers.** Controllers do no logic. Services hold the rules, and every public method takes a
   `CancellationToken` that is passed to each EF call. Controllers return DTOs only.
2. **Money.** Everything is `decimal`. `MoneyMath` is the single place that rounds, using
   `MidpointRounding.AwayFromZero`. Values are rounded only when stored or returned, never in
   intermediate steps. Average cost is stored to 4 decimal places, other money to 2.
3. **Calculator.** `PositionCalculator` (Buy, Sell, Value) is pure code with no database access,
   so it is unit-tested directly against the worked example.
4. **Atomic sells.** A sell runs a guarded `ExecuteUpdateAsync` that only applies when the holding
   still has enough quantity and its `RowVersion` is unchanged. The holding update and the
   transaction row are saved in one database transaction. If the guard matches no row, the
   service retries up to 3 times and re-reads the holding. A request that then finds too little
   stock gets the 409 insufficient-holdings response, and one that keeps losing the race gets a
   409 concurrent-change response. A unique index on (PortfolioId, AssetId) covers two first
   buys racing.
5. **Single-query reads.** Holdings and summary load the portfolio, its holdings and their assets
   in one `SELECT` (`AsNoTracking` with a projection). A missing portfolio produces no row,
   which becomes the 404, so no second query is needed.
6. **Errors.** Services throw typed exceptions. One `GlobalExceptionHandler` turns them into
   `ProblemDetails`: 400 with validation errors, 404, 409, and a generic 500 with no stack
   trace. The exception is still logged on the server.
7. **Frozen prices.** A transaction stores the unit price it was executed at, so later price
   changes only affect market value and unrealized P&L. Realized P&L is kept on the holding rows.

## Limits

- A `pageSize` above 100 is rejected with a 400 instead of being clamped.
- Transaction history does not show `realizedPnl`, because it is stored per holding and not per
  transaction. It appears in the response of the sell itself.
- Concurrent conflicts are retried 3 times, then a 409 is returned.
- Asset price updates are last write wins, and there is no price history.
- There is no authentication or currency conversion, as set out in the brief.
- The database setup assumes LocalDB with Windows authentication.

## Evidence

The `evidence/` folder holds:

- `acceptance-run.txt`: the worked example and the error cases
- `holdings-sql.txt`: the single `SELECT` logged for the holdings endpoint
- `concurrency-test.txt`: simultaneous sells of one holding
- `paging-test.txt`: 25 transactions, page 2 with page size 10
- `500-response.txt` and `500-log.txt`: the 500 response with the database offline, and its log entry

`evidence-tests.ps1` produced the paging, concurrency and 500 output:

```
.\evidence-tests.ps1 -Test Paging
.\evidence-tests.ps1 -Test Concurrency
.\evidence-tests.ps1 -Test Stopped
```

