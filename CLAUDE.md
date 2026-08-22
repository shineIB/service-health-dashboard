# CLAUDE.md — Service Health & Deployment Dashboard

## Vad det här projektet är

Ett portfolio-projekt som ska visa att jag kan bygga och driva ett
mikrotjänst-system, inte bara skriva enskilda API:er.

Systemet består av 2–3 .NET-mikrotjänster som körs i Kubernetes lokalt
(minikube eller k3s), plus en dashboard som visar hälsa, versioner och
deploy-status för varje tjänst i realtid.

**Målgrupp:** rekryterare och tech leads som tittar på GitHub-repot.
Det betyder att README, arkitekturdiagram och "kör det själv på 5 minuter"
är lika viktigt som koden.

## Domän (förslag — ändra om något bättre dyker upp)

En liten e-handelskärna, tillräckligt konkret för att domänlogiken ska kännas
äkta men inte så stor att den äter tid från infrastrukturen:

| Tjänst | Ansvar |
|---|---|
| `orders-service` | Skapa och hämta ordrar. Validerar mot inventory innan order accepteras. |
| `inventory-service` | Lagersaldo per artikel. Reserverar och släpper saldo. |
| `notifications-service` | Konsumerar order-events och loggar/"skickar" bekräftelser. |

Kommunikation: orders → inventory synkront via HTTP.
orders → notifications asynkront via events.

**Öppen fråga:** meddelandebuss. Börja med in-memory / direkt HTTP och byt
till RabbitMQ (eller NATS) när tjänst 1 och 2 står. Bygg inte bussen först.

## Teknikval

- **.NET 9**, ASP.NET Core Minimal APIs
- **EF Core + PostgreSQL** (en databas per tjänst — inte delad)
- **Docker** — en Dockerfile per tjänst, multi-stage build
- **Kubernetes** — minikube lokalt, rena YAML-manifest (ingen Helm i steg 1)
- **Health checks** — `/health/live` och `/health/ready` via
  `Microsoft.Extensions.Diagnostics.HealthChecks`, kopplade till
  liveness/readiness probes i k8s
- **OpenTelemetry** för traces och metrics
- **Dashboard** — React + Vite + TypeScript, pollar eller lyssnar på
  ett aggregerande API. (Alternativ: Blazor, om jag hellre vill hålla
  allt i .NET — bestäms när tjänsterna finns.)
- **CI** — GitHub Actions: build, test, bygg image

## Prioritetsordning

Bygg i den här ordningen. Gå inte vidare förrän steget innan faktiskt kör.

1. **Första tjänsten** — `orders-service` som Web API med riktig men enkel
   domänlogik, EF Core mot Postgres, health checks, unit-tester.
2. **Dockerisera den** — Dockerfile + docker-compose för lokal körning med
   Postgres.
3. **Andra tjänsten** — `inventory-service`, samma mönster.
   Orders anropar inventory. Hantera fel när inventory är nere
   (timeout, retry, fallback).
4. **Kubernetes** — manifest för båda tjänsterna: Deployment, Service,
   ConfigMap, Secret, probes. Kör i minikube.
5. **Dashboard** — aggregerings-API som frågar varje tjänsts health-endpoint,
   plus frontend som visar status, version och senaste deploy.
6. **Observability** — OpenTelemetry, strukturerad loggning, ev. Prometheus
   + Grafana.
7. **Tredje tjänsten + events** — `notifications-service` och en riktig
   meddelandebuss.
8. **CI/CD** — GitHub Actions som bygger och pushar images, ev. auto-deploy.

## Kodkonventioner

- En lösning (`.sln`) i roten, en mapp per tjänst under `src/`,
  tester under `tests/`.
- Varje tjänst: `Api` / `Domain` / `Infrastructure` — håll domänen fri från
  EF- och ASP.NET-beroenden.
- Async hela vägen, `CancellationToken` genom kedjan.
- Inga magiska strängar för config — använd typade options-klasser.
- Nullable reference types på, warnings as errors.
- Tester: xUnit + FluentAssertions. Integrationstester med
  `WebApplicationFactory` och Testcontainers.

## Så vill jag att du jobbar

- **Förklara valen.** Det här är ett lärprojekt — när du väljer ett mönster,
  säg kort varför och vad alternativet hade varit.
- **Små steg.** Kör och verifiera efter varje steg innan du bygger vidare.
- **Fråga hellre än gissa** när ett vägval påverkar arkitekturen.
- **Inga stora hopp framåt** — bygg inte steg 4 medan vi är på steg 1.
- Uppdatera det här dokumentet när vi fattar beslut som ändrar planen.

## Status

**Steg 1 — klart (kod), inte verifierat mot riktig Postgres än.**

- `orders-service` skapad i `src/OrdersService/` med `Api` / `Domain` / `Infrastructure`.
- Domänmodell: `Order` (aggregate root) + `OrderLine`, med tillståndsmaskin
  Pending → Confirmed / Cancelled. Validering i domänen kastar `DomainException`.
- `IOrderRepository` i Domain, EF Core-implementation (`OrderRepository` +
  `OrdersDbContext`, Npgsql-provider) i Infrastructure. `OrderLine` är mappad
  som owned collection (inget eget primärnyckel-koncept i domänen).
- Endpoints: `POST/GET /orders`, `GET /orders/{id}`, `POST /orders/{id}/confirm`,
  `POST /orders/{id}/cancel`.
- Health checks: `/health/live` (inga beroenden — alltid frisk om processen kör)
  och `/health/ready` (kollar Postgres-anslutning via `AddDbContextCheck`).
- `Directory.Build.props` i roten sätter `Nullable`, `TreatWarningsAsErrors`
  m.m. för hela lösningen istället för i varje `.csproj`.
- Initial EF-migration (`InitialCreate`) genererad men **inte körd** — ingen
  Postgres-instans finns lokalt än (kommer med docker-compose i steg 2).
- Tester: 13 domän-unit-tester (xUnit + FluentAssertions) + 1 API-smoke-test
  (`WebApplicationFactory`) mot `/health/live`. Inga Testcontainers-baserade
  integrationstester mot riktig Postgres än — det är naturligt att lägga till
  i steg 2 när Docker/Postgres finns lokalt.
**Beslut — migrationer vid uppstart:**
Migrationer körs INTE automatiskt vid uppstart som permanent lösning —
det tävlar mellan repliker i Kubernetes (flera pods som kör
`Database.Migrate()` samtidigt mot samma databas är en race condition).
Istället, styrt av flaggan `Database:RunMigrationsOnStartup`:

- **Steg 2 (docker-compose):** flaggan `true` som default lokalt —
  `orders-service` migrerar sig själv vid uppstart mot Postgres-containern.
- **Steg 4 (Kubernetes):** flaggan `false` — migrationen körs istället som
  ett separat Job eller en initContainer, en gång per deploy, inte per pod.

**Steg 2 — klart och verifierat.**

- Dockerfile (multi-stage, repo-root som build context för att nå hela
  projekt-referens-grafen) + `docker-compose.yml` med Postgres.
- Verifierat mot riktig Postgres-container:
  - `docker compose up --build` bygger och startar båda containrarna.
  - Migrationen `InitialCreate` körs automatiskt vid uppstart
    (styrt av `Database__RunMigrationsOnStartup=true` i compose).
  - `/health/live`, `/health/ready` och `/version` svarar 200.
  - Full flow mot riktig databas: `POST /orders` → 201, `GET /orders`
    returnerar den sparade ordern.
  - Stoppar man Postgres-containern går `/health/ready` till 503 Unhealthy
    medan `/health/live` fortsätter svara 200 Healthy — bekräftar att
    liveness inte är beroende av databasen.
- **Fixat:** `POST /orders` kastade en okontrollerad `ArgumentNullException`
  (500) om `items` saknades/var null i request-body. `OrderEndpoints.CreateOrder`
  normaliserar nu null till en tom lista (`request.Items ?? []`), vilket
  återanvänder domänens befintliga validering ("An order must contain at
  least one line" → 400 via `DomainExceptionHandler`) istället för att
  introducera en ny felväg. Regressionstest tillagt
  (`CreateOrder_WithMissingItems_Returns400WithProblemDetails`).

**Steg 3, del 1 — `inventory-service` klart och verifierat. Del 2 (orders → inventory) återstår.**

- `inventory-service` skapad i `src/InventoryService/` med samma `Api` / `Domain` /
  `Infrastructure`-mönster som orders-service, egen databas (`inventory`, inte delad
  med orders).
- Domänmodell: `InventoryItem` (aggregate root), nyckel `ProductId`. Håller
  `AvailableQuantity` och `ReservedQuantity` separat (inte bara ett saldo) — ger
  orders-service en naturlig plats att reservera saldo vid orderskapande och släppa
  det vid avbokning. `Reserve`/`Release` validerar i domänen och kastar
  `DomainException` vid otillräckligt saldo eller ogiltig kvantitet.
- Endpoints: `POST /inventory` (skapa/seeda artikel — 400 om produkten redan finns),
  `GET /inventory`, `GET /inventory/{productId}`, `POST /inventory/{productId}/reserve`,
  `POST /inventory/{productId}/release`. Samma `DomainExceptionHandler`-mönster som
  orders (400 + ProblemDetails), 404 vid okänd produkt.
- Health checks (`/health/live`, `/health/ready`), `/version`, migrationsflagga
  (`Database:RunMigrationsOnStartup`) — samma mönster som orders-service från start,
  ingen uppdelning i steg den här gången eftersom det redan är löst.
- Dockerfile (samma multi-stage-mönster) + eget Postgres-block i
  `docker-compose.yml` (`inventory-postgres`, port 5433 mot host för att inte
  krocka med orders port 5432).
- Tester: 11 domän-tester (xUnit + FluentAssertions) + 10 API-tester
  (`WebApplicationFactory`, inkl. create/reserve/release, redan-existerar-fel,
  otillräckligt-saldo-fel, 404 för okänd produkt). Alla 40 tester i lösningen
  (orders + inventory) gröna.
- Verifierat mot riktig Postgres via `docker compose up --build`: migration körs,
  `/health/live`, `/health/ready`, `/version` svarar korrekt, full
  create→reserve→release-flöde mot riktig databas, och samma
  liveness/readiness-isolering som orders (stoppar man `inventory-postgres` går
  bara inventory-service `/health/ready` till 503 — orders-service påverkas inte).

Nästa steg: steg 3, del 2 — orders anropar inventory synkront via HTTP vid
orderskapande (reservera saldo för varje rad innan ordern accepteras). Hantera
fel när inventory är nere (timeout, retry, fallback) — kräver ett beslut om
fallback-beteende (avvisa ordern vid osäkert lagersaldo, kontra acceptera
optimistiskt) innan implementation.
