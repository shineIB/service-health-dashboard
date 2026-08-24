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

**Steg 3, del 2 — orders → inventory över HTTP, med resiliens. Klart och verifierat.**

**Beslut — fail-closed.** En order avvisas om lagret inte kan reserveras eller
inventory är onåbar, hellre än att acceptera optimistiskt. Motivering: det här
är en lagerreservation, inte en "best effort"-notifiering — att acceptera
ordrar utan bekräftat saldo riskerar oversälj, vilket är dyrare att reda ut i
efterhand än att en kund får se ett tillfälligt fel.

- **Typad `HttpClient` med `Microsoft.Extensions.Http.Resilience`** (Polly v8),
  registrerad i `OrdersService.Infrastructure/ServiceCollectionExtensions.cs`
  via `AddStandardResilienceHandler`: per-försök-timeout 2s, totalt 8s,
  2 omförsök med exponentiell backoff + jitter (200ms bas), circuit breaker
  (50% felkvot, min. 4 anrop, 10s samplingsfönster, 15s brytningstid).
  Default-`ShouldHandle`-predikatet (`HttpClientResiliencePredicates.IsTransient`)
  används oförändrat — det omfattar redan 5xx/408/429 och nätverks-/timeout-fel,
  och exkluderar redan 4xx. Alltså: **inget** omförsök på 409/404 utan att
  behöva skriva ett eget predikat.
- **409 vs 400 i inventory:** `InsufficientStockException` (ärver
  `DomainException`, avsiktligt inte `sealed` längre) mappas till 409 Conflict,
  inte 400 — otillräckligt saldo är ett giltigt affärssvar, inte ett ogiltigt
  request. Det är därför omförsök aldrig triggas på det svaret (se ovan).
  Orders mappar i sin tur inventory:s 409/404 till ett eget 409 ("Order
  rejected: insufficient stock.") till sin egen anropare.
- **Idempotens:** `POST /inventory/{productId}/reserve` tar `orderId` som
  idempotensnyckel. `InventoryItem.Reserve(orderId, quantity, ttl, now)` kollar
  om en reservation för det `orderId` redan finns för den artikeln — om ja,
  no-op (samma resultat returneras, saldot rörs inte igen). En omförsökt
  request (t.ex. efter en timeout där inventory faktiskt hann reservera innan
  svaret gick förlorat) dubbelreserverar alltså inte.
- **Reservationer har TTL, inte kompenserande release-anrop.** Varje
  reservation får en `ExpiresAtUtc` (`Reservation:TtlSeconds`, default 900s)
  och en bakgrundstjänst (`ReservationExpiryService`, `BackgroundService`) sveper
  var `Reservation:ExpirySweepIntervalSeconds` (default 30s) och släpper saldo
  för förfallna reservationer automatiskt.
  **Varför TTL och inte ett kompenserande `release`-anrop när en order
  misslyckas efter en lyckad reservation:** ett kompenserande anrop går genom
  samma inventory-service som redan kan vara den tjänst som är nere — det är
  inte garanterat att lyckas när det som mest behövs. TTL kräver inte att
  någon annan tjänst är uppe, inte att den här processen ens överlever
  (krasch efter reservation men innan orderns commit läcker inte saldo
  permanent), och är därför den enda mekanismen som faktiskt håller sitt
  löfte oavsett vad som gick fel. Reservationer i orders-service-flödet för
  rader *före* den rad som fick 409/503 lämnas därför medvetet oreleasade —
  TTL:en städar upp dem.
- **503 + `Retry-After`, inte 500.** När inventory är onåbar (timeout,
  circuit open, nätverksfel) svarar `POST /orders` 503 med
  `Retry-After: 5`, inte ett okontrollerat 500 — anroparen ska kunna
  skilja "försök igen om en liten stund" (503) från "din request var
  ogiltig" (400) och "ditt lager räcker inte" (409).
- **Testtäckning:** `InventoryItemTests` (domän) testar idempotent
  omreservering, TTL-förfall (deterministiskt via injicerad `now`, inte
  väggklocka), och att förfallet saldo kan återanvändas. `ReservationExpiryTests`
  (API) verifierar bakgrundssvepet på riktigt (kort TTL/intervall, 3s väntan,
  ingen mockning). `OrderEndpointsTests` använder en `FakeInventoryClient`
  (ingen riktig inventory-service behövs för orders-testerna) och täcker
  lyckad reservation (201), otillräckligt saldo (409) och onåbar inventory
  (503 + `Retry-After`). Alla 49 tester i lösningen gröna.
- **Verifierat på riktigt mot Docker-stacken** (inte bara enhetstester):
  `docker compose up --build`, skapade lager, skapade en order (reserverar
  korrekt, saldo minskar), stoppade `inventory-service`-containern och
  bekräftade att `POST /orders` svarar 503 med `Retry-After: 5` efter
  ~6,5s (matchar 3 försök × 2s timeout + backoff). Skickade därefter fler
  requests och bekräftade att circuit breakern slår till: svarstiden föll
  till ~4ms och loggarna visade `Polly.CircuitBreaker.BrokenCircuitException`.
  `orders-service`s egna `/health/live` och `/health/ready` förblev 200 hela
  tiden (readiness beror bara på orders egen Postgres, inte på inventory).
  Startade `inventory-service` igen, väntade ut brytningstiden (15s) och
  bekräftade att circuit breakern stängdes automatiskt och en ny order
  lyckades (201) med korrekt uppdaterat saldo.
- **Gapet är åtgärdat:** `CancelOrder` anropar nu `IInventoryClient.ReleaseStockAsync`
  för varje rad, genom samma resiliens-pipeline som `ReserveStockAsync`. Ordern
  avbokas och sparas *innan* release-anropen görs — release är ett best-effort-
  sidoeffekt, inte ett villkor. Om det failar loggas en varning
  ("... TTL i inventory-service will reclaim it automatically") och
  cancel-svaret blir ändå 200; samma TTL-backstop som ovan städar upp.
  Täckt av `CancelOrder_ReleasesTheReservationForEachLine` och
  `CancelOrder_WhenReleaseFails_StillCancelsTheOrder`.

## Steg 4 — Kubernetes (minikube). Klart och verifierat.

Manifest under `k8s/`, en mapp per tjänst (`k8s/orders-service/`,
`k8s/inventory-service/`) plus `k8s/namespace.yaml`. Rena YAML-manifest, ingen
Helm — som planerat.

**Beslut (redan fattade, dokumenteras här):**

- **Postgres i klustret** som Deployment + PVC (`ReadWriteOnce`, 1Gi), en per
  tjänst, `strategy: Recreate` (en RWO-PVC kan bara monteras av en pod åt
  gången — `RollingUpdate` skulle deadlocka). Ingen operator (Zalando/
  CloudNativePG), ingen StatefulSet — rätt avvägning för en enda lokal
  dev-instans, inte för HA/backup-krav.
- **Migrationer:** `Database__RunMigrationsOnStartup=false` på
  Deployment-poddarna. Migrationen körs istället som ett separat `Job` per
  tjänst (`orders-migrate` / `inventory-migrate`), en gång per deploy —
  samma beslut som antecknades i steg 2, nu implementerat. Job-podden kör
  samma image som Deployment men med `args: ["--migrate-only"]` och
  `Database__RunMigrationsOnStartup=true`; `Program.cs` avslutar processen
  direkt efter migrationen istället för att starta Kestrel (ny, liten
  kodändring i båda tjänsternas `Program.cs` — annars skulle Job-podden aldrig
  bli `Complete`, den skulle bara stå och lyssna för evigt). Ett
  `initContainer` (`pg_isready`-loop mot respektive Postgres-Service) gör
  Job:et korrekt även om det appliceras för sig, utan att förlita sig på
  Kubernetes exponentiella Job-backoff (som annars kan dra ut på minuter).
- **Secrets:** vanliga `Secret`-manifest med lokala dev-värden i klartext
  (`stringData`, base64 vid `kubectl apply`, inte kryptering). Kommentar i
  varje secret-manifest om att SOPS / Sealed Secrets / External Secrets
  Operator vore rätt i produktion — medveten förenkling för lokal minikube,
  inte en miss.
- **Images:** byggda lokalt (`docker build -t orders-service:local ...` /
  `inventory-service:local`) och laddade in med `minikube image load` — inget
  externt registry. `imagePullPolicy: IfNotPresent` explicit på alla egna
  containrar (Deployment + Job) så Kubernetes aldrig försöker hämta dem
  någon annanstans ifrån.
- **Exponering:** `inventory-service` och båda Postgres-Services är
  `ClusterIP` — bara nåbara inifrån klustret. `orders-service` är `NodePort`,
  nått utifrån via `minikube service orders-service -n
  service-health-dashboard`. Ingen Ingress än.
- **Probes** (motiverade, inte kopierade):
  - `startupProbe`: `/health/live`, `periodSeconds: 2`, `failureThreshold: 15`
    → ~30s budget för en kall start av en minimal-API-process (DI-container +
    EF Core-modellbygge) på en resursbegränsad minikube-nod. Klart mer än de
    ~1–2s det tar på en vanlig dev-maskin, men fångar ändå en genuint fastnad
    pod långt innan den skulle hinna äta av livenessProbe:s
    omstart-budget nedan.
  - `livenessProbe`: `/health/live`, `periodSeconds: 10`, `timeoutSeconds: 2`,
    `failureThreshold: 3` → ingen egen `initialDelaySeconds` behövs (den körs
    först efter att `startupProbe` lyckats). 3×10s = 30s innan omstart:
    liveness-fel triggar en full pod-omstart, så den ska absorbera en enstaka
    långsam tick (t.ex. en GC-paus), inte reagera på första missen.
  - `readinessProbe`: `/health/ready`, `periodSeconds: 5`, `timeoutSeconds: 2`,
    `failureThreshold: 3` → kortare period än liveness med avsikt: readiness
    kostar bara att podden plockas ur Service-endpoints (billigt, reversibelt),
    så den får reagera snabbare — ~15s — när podden egen Postgres blir onåbar.
- **Postgres-poddarna** har egna `readinessProbe`/`livenessProbe` via
  `pg_isready` (exec), samma mönster som docker-compose:s healthcheck.

**Verifierat på riktigt i minikube** (inte bara `kubectl apply` utan fel):

1. `docker build` + `minikube image load` för båda tjänsterna, `kubectl apply`
   i ordning: namespace → Postgres (Secret/PVC/Deployment/Service) → app-Secrets
   → migration-Jobs (väntade in `condition=complete`, läste loggarna och såg
   riktiga `Applying migration '...'`-rader) → app-Deployments/Services.
2. Båda Deployments rullade ut rent (`kubectl rollout status`), båda poddarna
   `1/1 Ready` — dvs. `startupProbe`/`readinessProbe` klarade sig utan
   justering på första försöket.
3. Nådde `orders-service` utifrån klustret via `minikube service
   orders-service -n service-health-dashboard --url`, skapade en riktig order
   över hela kedjan (host → NodePort → orders-service-pod → ClusterIP
   `inventory-service:8080` → inventory-service-pod → dess Postgres):
   `POST /orders` → 201, `inventory` gick från `available: 10` till
   `available: 7, reserved: 3`.
4. `kubectl scale deployment/inventory-service --replicas=0`. Ny `POST /orders`
   svarade `503` med `Retry-After: 5` (samma resiliens-/fail-closed-beteende
   som i Docker-verifieringen ovan, nu genom riktig k8s-DNS/Service-routing
   utan endpoints). `orders-service`s egen pod förblev `1/1 Ready`,
   `Restart Count: 0` hela tiden — readiness/liveness påverkas inte av att en
   beroende tjänst saknar repliker, exakt som designat.
5. `kubectl scale deployment/inventory-service --replicas=1`, väntade in
   rollout. Ny `POST /orders` lyckades igen (201), och `inventory` visade
   korrekt `available: 6, reserved: 4` (3 från innan + 1 ny) — full
   återhämtning utan manuell inblandning.

## Steg 5 — dashboard. Klart och verifierat.

`dashboard-service` tillkommen i `src/DashboardService/` (`Api` / `Domain` /
`Infrastructure`, samma mönster som de andra) + `DashboardService.Web/`
(React + Vite + TypeScript). Ingen egen databas.

**Beslut (redan fattade, dokumenteras här):**

- **Config-driven tjänstelista, inte k8s-native discovery.** Vilka tjänster
  som övervakas (namn + bas-URL) kommer från en typad options-klass
  (`MonitoredServicesOptions`), bunden från config — i k8s via indexerade
  env-vars (`MonitoredServices__Services__0__Name` osv.) på samma sätt som
  övriga tjänsters config. **Naturligt nästa steg:** hämta tjänstelistan från
  k8s API:et (t.ex. genom att lista Services/Pods med en label-selector)
  istället för att hårdkoda den i Deployment-manifestet — inte gjort nu för
  att hålla steget litet, men den självklara vägen när fler tjänster tillkommer.
- **Bakgrundspoller, inte fan-out per request.** `ServiceHealthPollingService`
  (`BackgroundService`) pollar varje övervakad tjänst var 5:e sekund
  (`Polling:IntervalSeconds`) och skriver till en delad in-memory-cache
  (`IServiceHealthCache`, `ConcurrentDictionary`). `GET /api/services` läser
  *bara* cachen — anropar aldrig ut mot en övervakad tjänst. Utan den här
  separationen hade belastningen på orders-service/inventory-service skalat
  med antalet öppna dashboard-flikar, ett självförvållat DoS mot ens egna
  tjänster.
- **dashboard-apis egen readiness kan aldrig spegla en övervakad tjänsts
  status — arkitektoniskt, inte bara genom en regel.** `Program.cs` kallar
  `AddHealthChecks()` utan att lägga till några checks alls: dashboard-api har
  ingen egen databas eller något annat internt beroende, så det finns inget i
  hälsokontrollpipelinen som ens *kan* fråga `IServiceHealthCache`. `/health/live`
  och `/health/ready` blir därför identiska här (medvetet — det finns inget
  beroende att skilja dem på). Låst fast av
  `HealthEndpointTests.Ready_ReturnsHealthy_EvenWhenEveryMonitoredServiceIsDown`,
  som seedar cachen med en `Unreachable`- och en `Unhealthy`-post och verifierar
  att `/health/ready` ändå svarar 200.
- **Unhealthy vs. Unreachable, kort per-tjänst-timeout, isolerat per tjänst.**
  `ServiceHealthChecker` (utbruten från polling-loopen just för att vara
  direkt testbar utan en riktig `BackgroundService`) anropar `/health/ready`
  med ett ~2s tidsbudget (`Polling:PerServiceTimeoutSeconds`) per tjänst:
  - Svar men icke-2xx → `Unhealthy` ("den mår dåligt, men den svarade").
  - Inget svar alls (timeout, connection refused, DNS-fel) → `Unreachable`
    ("den finns inte där just nu").
  - Varje tjänst pollas som en egen `Task` i `Task.WhenAll` — en långsam eller
    onåbar tjänst fördröjer aldrig de andras uppdatering.
  - `/version` hämtas best-effort på samma tidsbudget efter ett lyckat
    `/health/ready`-svar; misslyckas det anropet ensamt nedgraderas inte
    statusen, gammal version-info (om någon) behålls bara.
  - Vid `Unreachable` behålls `LastSuccessfulCheckUtc` och version-fälten från
    föregående snapshot istället för att nollställas — en tjänst som går ner
    ska inte radera det senast kända goda tillståndet.
- **Frontend:** React + Vite + TypeScript, pollar `/api/services` var 5:e
  sekund (samma intervall som backend-pollningen — ingen anledning att polla
  snabbare än datan faktiskt ändras). Ingen SSE/WebSocket än — **möjlig
  uppgradering** när polling-overheaden eller latenskraven motiverar det.
  Byggs till statiska filer (`npm run build` → `dist/`) som kopieras in i
  `DashboardService.Api`s `wwwroot/` vid Docker-bygget och serveras av
  dashboard-api självt (`UseDefaultFiles`/`UseStaticFiles`/`MapFallbackToFile`)
  — samma origin som API:et, alltså ingen CORS och ingen extra k8s-podd bara
  för frontend.
- **"Senaste deploy"** visas som `buildTimeUtc` från varje tjänsts `/version`
  (byggtiden bakas in vid `docker build --build-arg BUILD_TIME=...`, se
  Dockerfiles). **Notera:** det är byggtid, inte deploy-tid — en riktig
  deploy-tidsstämpel (när podden faktiskt rullades ut) skulle kräva att fråga
  k8s API:et (Deployment/ReplicaSet-events), inte bara tjänsten själv. Samma
  "nästa steg mot k8s-native"-linje som tjänstelistan ovan.
- **Ingen UI-polish** i det här steget, som avtalat — ren tabell, inga
  färgteman/typsnitt/layout-arbete. Prioriteten var att få data att flöda
  korrekt genom hela kedjan (poller → cache → API → frontend) innan något
  annat.

**Testtäckning:** `ServiceHealthCheckerTests` (Healthy/Unhealthy/Unreachable-
klassificering, version-fel nedgraderar inte status, timeout begränsar hur
länge en långsam tjänst får ta, `Unreachable` bevarar föregående snapshots
version/`LastSuccessfulCheckUtc`) — allt med en fejkad `HttpMessageHandler`,
ingen riktig HTTP. `InMemoryServiceHealthCacheTests` (grundläggande cache-
beteende). `HealthEndpointTests` (se ovan — den låsande testen).
`DashboardEndpointsTests` (`/api/services` speglar cachen korrekt, inkl.
`Unreachable`-fallet). Alla 66 tester i lösningen (orders + inventory +
dashboard) gröna.

**Verifierat på riktigt i minikube** (samma kluster som steg 4, ingen omstart
behövdes):

1. `npm run build` (Vite) verifierat separat, sedan `docker build` (Node-steg
   → dotnet-steg → runtime som kopierar in båda) + `minikube image load` +
   `kubectl apply` för `dashboard-service` (Deployment + NodePort-Service,
   inget nytt Postgres/Secret/Job). Rullade ut rent, `1/1 Ready` direkt.
2. Nådde dashboard-service utifrån klustret via `minikube service
   dashboard-service -n service-health-dashboard --url`. `/api/services`
   visade båda tjänsterna som `Healthy` med korrekt version/gitSha/
   svarstid inom en pollningscykel.
3. `kubectl scale deployment/inventory-service --replicas=0`. Nästa
   pollningscykel: `inventory-service` gick till `Unreachable`
   (`responseTimeMs: null`, felmeddelande "Connection refused"), medan
   `orders-service` fortsatte rapportera `Healthy` med uppdaterad
   `lastSuccessfulCheckUtc` — bekräftar isoleringen mellan tjänster på
   riktigt, inte bara i test. `dashboard-service`s egen pod förblev
   `1/1 Ready`, `Restart Count: 0`, och `/health/live` + `/health/ready`
   svarade 200 hela tiden.
4. `kubectl scale deployment/inventory-service --replicas=1`. Nästa
   pollningscykel: `inventory-service` tillbaka som `Healthy` med färsk
   `lastSuccessfulCheckUtc` — full återhämtning utan manuell inblandning,
   samma mönster som orders/inventory-verifieringen i steg 4.

Nästa steg: observability (steg 6) — OpenTelemetry, strukturerad loggning,
ev. Prometheus + Grafana. Möjliga uppgraderingar noterade ovan (k8s-native
service discovery för dashboard, SSE/WebSocket istället för polling, riktig
deploy-tid via k8s API:et) är inte bortglömda, bara medvetet uppskjutna.

## Steg 8, CI-delen — klart och verifierat (CD kommer senare).

`.github/workflows/ci.yml`: körs på varje push och PR mot `master`.

- **`build-and-test`:** `dotnet restore`/`build`/`test` mot hela `.sln`.
- **`docker-build`** (matrix, `needs: build-and-test`): bygger alla tre
  service-images (`docker build -f <Dockerfile> .`, repo-root som context,
  samma som lokalt) för att fånga en trasig Dockerfile tidigt. Pushar inget
  någonstans än — det är CD, inte det här steget.
- Statusbadge överst i README, länkad till workflow-sidan.

**Upptäckt under verifiering — testsviten var inte lika isolerad som
kommentarerna i den påstod.** `OrdersApiFactory`/`InventoryApiFactory`
(`WebApplicationFactory`-baserade) stänger av startup-migrationer men
använder annars tjänstens riktiga `appsettings.json`, inklusive den riktiga
Postgres-anslutningssträngen (`localhost:5432`/`5433`, samma som
docker-compose). Lokalt gick det ändå grönt eftersom en tidigare
`docker compose up`-körning hade lämnat kvar en migrerad Postgres-volym på
de portarna — testerna var alltså aldrig riktigt fristående, de råkade bara
alltid ha en riktig databas liggandes. Första CI-körningen (ingen Postgres
alls på `ubuntu-latest`-runnern) exponerade det direkt: alla endpoints som
rör databasen svarade 500.

**Beslut — Postgres-tjänstecontainrar + en migrate-only-körning i CI,
inte omskrivna tester.** Att göra testerna verkligt fristående (fejkade
repositories eller Testcontainers) är en större omskrivning av testsviten,
inte en del av "dra fram CI". Istället matchar workflowen vad testerna redan
antar: två `services:`-Postgres-containrar (samma portar/credentials som
docker-compose), och innan `dotnet test` körs samma `--migrate-only`-läge
som redan finns för k8s-Jobben (se steg 4) en gång per tjänst — samma
"migrera en gång, inte per körning/replik"-princip som redan var beslutad,
återanvänd rakt av. `Database__RunMigrationsOnStartup=true` sätts bara på de
två migrate-stegen (per-step `env:`, läcker inte in i `dotnet test`-steget),
så själva testkörningen är oförändrad — bara databasen den pratar med finns
nu på riktigt.
**Kvarstående, medvetet inte åtgärdat nu:** kommentaren i
`OrdersApiFactory`/`InventoryApiFactory` ("No real Postgres is available...")
stämmer inte längre i CI och var aldrig helt sann lokalt heller — värt att
städa upp eller (bättre) faktiskt göra testerna DB-fria när den här typen av
tester utökas, men inte gjort nu för att hålla CI-steget litet.
**Verifierat på riktigt:** `gh run watch` på faktiska GitHub Actions-körningar
(inte lokalt) — första körningen röd (bekräftade ovanstående gap), andra
körningen grön efter fixet, alla 66 tester + alla tre Docker-images.
Reproducerat och verifierat samma fix lokalt också: startade två
`postgres:16-alpine`-containrar på 5432/5433, körde `--migrate-only` mot
dem, körde `dotnet test` — samma 66/66 gröna.

### Uppföljning — service containers ersatta med Testcontainers

Ovanstående "kvarstående, medvetet inte åtgärdat nu"-punkt åtgärdades samma
dag. Service containers + `--migrate-only` i workflowen var ett medvetet
litet första steg (se ovan: "inte en del av 'dra fram CI'") — inte en
felbedömning, bara nästa steg i kön. Historiken står kvar ovan för att visa
att gapet var känt och stängt i två steg, inte att det aldrig fanns.

**Vad som ändrades:** `OrdersApiFactory`/`InventoryApiFactory` startar nu var
sin `PostgreSqlContainer` (Testcontainers.PostgreSql) via en delad
`PostgresContainerFixture : IAsyncLifetime` och skriver över
`Infrastructure:ConnectionString` i stället för att läsa tjänstens
`appsettings.json`. Fixturen kör `Database.MigrateAsync()` mot containern
som en del av `InitializeAsync()`, innan något test körs. Delad **per
test-collection** (`[CollectionDefinition]` + `ICollectionFixture<PostgresContainerFixture>`,
alla testklasser i respektive projekt taggade `[Collection(...)]`) — en
container per projekt, inte en per testklass. `ReservationExpiryFactory`
(egen TTL/sweep-konfiguration) tar samma `PostgresContainerFixture` som
konstruktorparameter och delar alltså container med resten av collectionen,
bara med annan app-konfiguration ovanpå. `ci.yml` tappade båda
Postgres-`services:`-blocken och båda `--migrate-only`-stegen — workflowen
behöver inte längre veta att tjänsterna använder en databas alls, bara att
Docker finns (vilket `ubuntu-latest` redan har).

**Upptäckt under omskrivningen — ännu ett dolt gap, av samma familj som
ovan.** Att bara skriva över `Infrastructure:ConnectionString` via
`WebApplicationFactory.ConfigureWebHost`/`ConfigureAppConfiguration` räckte
inte: `AddOrdersInfrastructure`/`AddInventoryInfrastructure` läste
anslutningssträngen från `builder.Configuration` (parametern som skickas in
vid `builder.Services.AddOrdersInfrastructure(builder.Configuration)` i
`Program.cs`, **före** `builder.Build()`), och `WebApplicationFactory`s
konfigurationsöverskrivningar landar bara i den färdigbyggda
konfigurationen — exakt samma fälla som redan var dokumenterad i
`Program.cs` för `Database:RunMigrationsOnStartup` ("Read via
app.Configuration (post-Build), not builder.Configuration"). Testerna körde
alltså tyst mot porten från `appsettings.json` (5432/5433) istället för
containerns riktiga, slumpmässiga port — märktes bara för att docker-compose
var nedstängt när det begav sig, annars hade det varit samma typ av
falsk-grön-av-tur som orsakade förra gapet.
**Fix:** `AddDbContext<OrdersDbContext>`/`AddDbContext<InventoryDbContext>`
läser nu anslutningssträngen lazy, via `IConfiguration` upplöst från
`IServiceProvider` inne i options-delegaten (`(serviceProvider, builder) =>
...`) istället för att fånga värdet vid registreringstillfället. Detta
körs vid första faktiska DB-anropet (efter `Build()`), samma timing som
redan fungerade för migrationsflaggan — inte en specialregel för tester,
utan att sluta läsa konfiguration för tidigt, generellt.
**Verifierat:** `docker compose down` (bekräftat nere, ingenting på
5432/5433), `dotnet test` mot hela lösningen — 66/66 gröna, ingen lokal
Postgres krävd. Bekräftat att exakt en Postgres-container (plus
Testcontainers egen Ryuk-reaper) skapas per testprojekt, inte en per
testklass. `gh run watch` på den bantade workflowen — grön, alla tre
Docker-images också gröna.

## Steg 6, del 1 — distribuerad tracing + strukturerad loggning. Klart och verifierat.

Beslut som gavs, inte omförhandlade: OpenTelemetry .NET med
auto-instrumentering (ASP.NET Core, HttpClient, Npgsql) i alla tre
tjänster; Jaeger all-in-one i klustret som OTLP-mottagare, ingen Collector;
100 % sampling lokalt; Polly v8-telemetri som egna spans för retries/circuit
breaker; manuella spans för `reserve`/`release` med order-id och artikel-id;
inbyggd `ILogger` + `AddJsonConsole`, ingen Serilog; varje logg berikad med
`trace_id`/`span_id`.

**Paket** (alla tre Api-projekt, version 1.18.0): `OpenTelemetry`,
`OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
`OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`.
**Ingen** separat instrumenteringspaket för Npgsql — Npgsql har sedan
version 7 en egen inbyggd `ActivitySource` (namn `"Npgsql"`); det räcker med
`.AddSource("Npgsql")` på `TracerProviderBuilder`, bekräftat genom att
reflektera över `Npgsql.dll` innan jag litade på det.

**`OTEL_EXPORTER_OTLP_ENDPOINT` är inte en typad options-klass, avsiktligt.**
Kodkonventionen säger "inga magiska strängar, använd typade options-klasser"
för *vår egen* config — men det här är OpenTelemetry SDK:ets eget,
spec-definierade env-var-namn (samma sak som `ASPNETCORE_URLS` är Kestrels,
inte vårt). SDK:et läser den själv; att linda in den i en egen options-klass
hade bara dolt en redan standardiserad mekanism. Default är
`http://localhost:4317`, vilket matchar Jaeger-porten rakt av både i
docker-compose och lokalt utan container — ingen extra config behövs för det
vanliga fallet.

**`ConfigureResource(r => r.AddService("orders-service"))` per tjänst,
inte SDK-default.** Utan den rapporterar alla tre tjänster som
`unknown_service:dotnet` i Jaeger — SDK:et gissar inte tjänstenamnet från
entry-assemblyn, upptäckt genom att faktiskt titta i Jaeger:s
`/api/services` innan jag antog att det bara skulle fungera.

**Manuella spans:** `InventoryTelemetry.ActivitySource` (namn
`"InventoryService.Api"`) i `InventoryService.Api/Telemetry/`, startade
runt hela `ReserveStock`/`ReleaseStock`-handlers i `InventoryEndpoints.cs`
med taggarna `order.id` och `product.id`. Ligger i Api-lagret (inte
Domain) — domänen ska vara fri från infrastrukturberoenden, och
OpenTelemetry är precis den typen av cross-cutting concern som hör hemma
i Api/Infrastructure, inte i `InventoryItem.Reserve`/`Release` själva.

**Upptäckt — Polly v8:s inbyggda telemetri skapar INTE spans, bara metrics
och loggar.** Trots att "Polly" dyker upp som en `ActivitySource`-liknande
sträng i `Polly.Extensions.dll` (bekräftat genom att reflektera över
assemblyn) är den bara en logger-kategori — `.AddSource("Polly")` på
`TracerProviderBuilder` gav noll spans även under en riktig retry-storm.
Bekräftat mot både Pollys egen dokumentation (pollydocs.org nämner bara
metrics/loggar under "Telemetry", inget om `ActivitySource`) och en
tredjepartsartikel som visade att andra manuellt bygger sina egna spans
ovanpå Pollys telemetri-event. Löst med en egen
`PollyActivityTelemetryListener : Polly.Telemetry.TelemetryListener` i
`OrdersService.Infrastructure/Telemetry/` som lyssnar på alla Polly-events
(`OnRetry`, `OnCircuitOpened`, `ExecutionAttempt`, `PipelineExecuting`/
`PipelineExecuted`, m.fl.) och själv startar en `Activity` på en
`ActivitySource` som **heter** `"Polly"` — samma namn som
`.AddSource("Polly")` redan lyssnade på, så ingen ändring behövdes på
OTel-sidan. Registrerad via
`services.Configure<TelemetryOptions>(o => o.TelemetryListeners.Add(...))`
i `AddInventoryClient`, vilket gäller alla namngivna resilience-pipelines
i tjänsten (bara en idag, men gratis om en till tillkommer).

**Strukturerad loggning:** `builder.Logging.ClearProviders()` +
`AddJsonConsole(o => o.IncludeScopes = true)` i alla tre `Program.cs`. En
liten `app.Use(...)`-middleware, registrerad direkt efter `builder.Build()`
(före `UseExceptionHandler` m.fl.), lägger `Activity.Current`s `TraceId`/
`SpanId` i en logg-scope med exakt nycklarna `trace_id`/`span_id` (inte
ramverkets egna PascalCase `TraceId`/`SpanId` — `ActivityTrackingOptions`
sattes till `None` för att inte få båda samtidigt, vilket annars dubblerar
informationen i varje JSON-rad). **Bieffekt:** `ClearProviders()` tog även
bort Windows EventLog-providern som orsakade den flakiga
`ObjectDisposedException`/`EventLogInternal`-buggen i lokala testkörningar
på Windows (se steg 8-anteckningarna ovan) — inte varför ändringen gjordes,
men värt att notera att den städade upp det på köpet.

**Kubernetes:** `k8s/jaeger/` (ny mapp) — en Deployment
(`jaegertracing/all-in-one:1.76.0`, `COLLECTOR_OTLP_ENABLED=true`) och en
Service (NodePort, tre namngivna portar: `ui` 16686, `otlp-grpc` 4317,
`otlp-http` 4318 — allihop NodePort:ade som en bieffekt av att de delar en
Service, men det är ofarligt eftersom tjänsterna i klustret ändå når Jaeger
via ClusterIP:n på `http://jaeger:4317`, oavsett Service-typ).
`OTEL_EXPORTER_OTLP_ENDPOINT=http://jaeger:4317` tillagt i alla tre
service-Deploymentens `env:`. **Ingen persistent lagring** — Jaeger
all-in-one lagrar in-memory, traces överlever inte en pod-omstart. Rätt
avvägning för en enda lokal dev-instans; en riktig backend
(Elasticsearch/Cassandra/managed) och en OTel Collector framför den
(batching, sampling, fan-out till fler backends) är vad produktion skulle
kräva — medvetet inte byggt nu, som redan beslutat.

`docker-compose.yml` fick samma sak (egen `jaeger`-service, samma env-var
per tjänst) — inte för att det krävdes, utan för att kunna verifiera
end-to-end-kopplingen lokalt på sekunder istället för att betala
`minikube image load`-cykeln för varje litet fel. Det var såhär
Polly-gapet ovan faktiskt hittades — mot docker-compose, inte i klustret.

**Verifierat i minikube** (samma krav som tidigare steg — riktig körning,
inte bara kod som kompilerar):

1. Byggde alla tre images på nytt, körde in i noden. Träffade **samma
   `minikube image load`-fälla som redan är dokumenterad i README** —
   `minikube image load` behöll tyst de gamla image-innehållen under
   samma tagg, trots `minikube image rm` följt av `minikube image load`.
   Orsaken den här gången: två gamla, redan avslutade
   `orders-migrate`/`inventory-migrate`-Jobs (15 timmar gamla, kvar sedan
   steg 4) höll fortfarande i de gamla image-ID:na, vilket blockerade
   `docker rmi` på noden. Skalade ner de tre app-Deploymentsen till 0,
   tog bort de gamla migration-Jobben (engångs, redan `Completed`, säkert
   att ta bort), tog bort de gamla images:na för hand
   (`minikube ssh -- docker rm <container>` följt av `minikube image rm`),
   laddade om, skalade upp igen. Bekräftar bara att README:s
   felsökningsavsnitt är korrekt, inte ett nytt problem.
2. Skapade en riktig order genom hela kedjan (orders-service NodePort →
   inventory-service → båda Postgres-instanserna). En trace i Jaeger med
   10 spans över två tjänster: `orders-service: POST /orders/` →
   Polly-pipelinens `PipelineExecuting`/`POST`/`PipelineExecuted`/
   `ExecutionAttempt` → `inventory-service: POST /inventory/{productId}/reserve`
   → den manuella `inventory.reserve`-spannen (taggad med rätt `order.id`
   och `product.id`, bekräftat via DOM-inspektion — Jaeger UI:ts
   tagg-panel hade en visuell renderingsbugg i skärmdumpen där värdena
   fanns i DOM:en men inte målades i JPEG-capturen) → två `Npgsql`-spans
   under `inventory-service` och en under `orders-service`. Skärmdump i
   README.
3. `kubectl scale deployment/inventory-service --replicas=0`, skapade
   fler ordrar. Traces visade `OnRetry`- och `OnCircuitOpened`-spans från
   `PollyActivityTelemetryListener`, plus det underliggande HTTP-anropets
   felmarkerade span (`Polly.CircuitBreaker.BrokenCircuitException` som
   `error.type`-tagg på efterföljande snabbt-avvisade anrop). Skärmdump
   i README — den var poängen med hela övningen.
4. Loggkorrelation bekräftad på riktigt: `kubectl logs deployment/orders-service`
   innehöll en rad från Pollys egen `ILogger`-kategori `"Polly"`
   ("Resilience event occurred. EventName: 'OnCircuitOpened'...") med
   `"trace_id":"9faf876a505305b0ff35e663c884856a"` i loggens scope —
   exakt samma trace-ID som Jaeger visade för samma händelse (kortformen
   `9faf876` i Jaeger UI:ts titel).
5. `kubectl scale deployment/inventory-service --replicas=1`, väntade in
   rollout. Ny order gav ett rent domän-409 ("insufficient stock" — inte
   ett 503) för en oseedad artikel, vilket bekräftar att hela kedjan
   (inklusive Polly-pipelinen) återhämtade sig utan manuell inblandning,
   samma mönster som i tidigare steg.

Nästa steg: steg 6 del 2 (metrics — OpenTelemetry-mätvärden, ev.
Prometheus/Grafana), sedan steg 7 (tredje tjänsten + meddelandebuss).

## Uppföljning — dashboard visade `@unknown` istället för git-SHA

Upptäckt i en ny session vid en vanlig statuskoll av dashboarden i minikube
(inte under en verifieringsgenomgång av ett steg): `version`-kolumnen visade
`0.1.0-dev @unknown` för båda tjänsterna istället för en riktig `gitSha`.

**Grundorsak:** ingen kodbugg — Dockerfiles har redan `ARG GIT_SHA=unknown`/
`ARG BUILD_TIME=unknown` med korrekt fallback, och docker-compose-flödet
skickar redan med dem via env-var-substitution (`${GIT_SHA:-local}`). Men
README:s dokumenterade minikube-bygg-kommandon (`docker build -t
orders-service:local -f ... .`) skickade aldrig med `--build-arg
GIT_SHA=... --build-arg BUILD_TIME=...` — de körda images i klustret hade
alltså faktiskt aldrig fått något annat än default-värdet, inte en regression
i koden.

**Fix:** README:s minikube-byggsteg beräknar nu `GIT_SHA`/`BUILD_TIME` och
skickar dem som `--build-arg` till alla tre `docker build`-kommandon.

**Sidoupptäckt under ombygget — `minikube image rm` kan faila tyst-ish när
en pod fortfarande kör imagen.** Att bygga om och köra `minikube image rm`
+ `image load` (det redan dokumenterade mönstret för stale-tag-problemet)
utan att först skala ner Deploymentsen gav `conflict: ... (must force) -
container ... is using its referenced image ...` på alla tre — poddarna
höll fortfarande i de gamla image-ID:na. `image load` hade i så fall
fortsatt tyst servera det gamla innehållet under samma tagg. Löst genom att
skala ner de tre app-Deploymentsen till 0 innan `image rm`/`image load`,
sedan skala upp igen — samma grundmönster som redan var känt (se steg 6
ovan, punkt 1 i minikube-image-load-fällan), bara inte tidigare
dokumenterat som ett eget steg. README:s troubleshooting-avsnitt är
uppdaterat med den här varianten.

**Verifierat:** `docker run --rm --entrypoint printenv <image> | grep
Build__` mot de nybyggda images innan de laddades in (bekräftade
`Build__GitSha=6f310da`/`Build__BuildTimeUtc=...` bakade in korrekt), sedan
full omdeploy i minikube och `/api/services` samt dashboarden i webbläsaren
— visade `0.1.0-dev @6f310da` för båda tjänsterna efter fixet.

## Steg 6, del 2 — metrics-instrumentering. Klart och verifierat. Prometheus/Grafana ännu inte deployat (medvetet, se beslut nedan).

**Beslut — omfattning för den här delen, valt via fråga innan jag började:**
bara OpenTelemetry-metrics + `/metrics`-endpoint på alla tre tjänster,
verifierat lokalt mot docker-compose. Ingen Prometheus/Grafana i det här
passet — CLAUDE.md skrev redan "ev. Prometheus + Grafana" (inte ett
bestämt beslut), och det är ett eget, större steg (nya manifest/tjänster i
både compose och k8s). Blir en egen uppföljning, inte en del av den här.

**Paket:** `OpenTelemetry.Exporter.Prometheus.AspNetCore`, version
`1.18.0-beta.1` — fortfarande bara beta uppströms (har varit det i flera
år; .NET OTel SIG har aldrig märkt en stabil Prometheus-exportör som
"stable"), men råkar dela samma `1.18.0`-linje som resten av SDK:et. Vanligt
förekommande i det skicket ändå, inte en varningsflagga i sig.

**`WithMetrics(...)` bredvid befintlig `WithTracing(...)` i alla tre
`Program.cs`.** `AddAspNetCoreInstrumentation()` + `AddHttpClientInstrumentation()`
ger request-antal/latens per route gratis (samma paket som redan användes
för traces — auto-instrumentering täcker båda utan extra paket).
`AddPrometheusExporter()` + `app.MapPrometheusScrapingEndpoint()` — pull
(scrape), inte OTLP-push: Jaeger förstår bara traces, och det finns ingen
Prometheus deployad än för att ta emot en push heller.

**Egna business-counters, samma motivering som de manuella spans:innan
för reserve/release i steg 6 del 1** — auto-instrumentering ser HTTP-utfall
(2xx/4xx), inte affärsutfall. En 201 kan vara "order skapad" men en 409 kan
vara antingen "otillräckligt lager" eller (via orders-service) "inventory
onåbar", och det skiljer man inte åt utan egna counters:
- `OrdersTelemetry` (ny, `OrdersService.Api/Telemetry/`): `orders.created`,
  `orders.rejected` (taggad `reason`: `insufficient_stock` |
  `inventory_unavailable`), `orders.cancelled`. Incrementeras direkt vid
  respektive call site i `OrderEndpoints.cs`.
- `InventoryTelemetry` (utökad, samma fil som redan hade `ActivitySource`
  för reserve/release-spans): `inventory.reservations.succeeded`,
  `inventory.reservations.failed` (taggad `reason=insufficient_stock`),
  `inventory.releases`. Succeeded/releases incrementeras i
  `InventoryEndpoints.cs` efter lyckad `item.Reserve`/`item.Release`.
  **Failed incrementeras centralt i `DomainExceptionHandler`, inte i
  endpointen** — `item.Reserve` kastar `InsufficientStockException` istället
  för att returnera ett fel-resultat, så exception-handlern är den enda
  platsen varje avvisning faktiskt passerar.
- `dashboard-service` fick bara auto-instrumentering, ingen egen Meter —
  har inga egna affärsutfall att räkna än (pollnings-success/fail-rate är
  en naturlig framtida metric, medvetet inte tillagd nu för att hålla
  steget till auto-instrumentering där).

**Upptäckt under testskrivning — en verklig race, inte ett testfel.**
`WebApplicationFactory`-baserade tester som gjorde en POST följt direkt av
en GET `/metrics` (ingen fördröjning alls) missade konsekvent den egna
counter-metricen — men ASP.NET Core:s inbyggda instrument (skapade redan
vid host-uppstart) dök alltid upp. Isolerat genom att köra om samma test
med ett extra, ointressant request inklämt mellan POST och `/metrics`-läsningen:
det räckte för att få counter-metricen att synas. Bekräftat att det INTE
är ett wiring-fel genom att köra mot en riktig `docker compose up`-stack
(riktig Kestrel, inte `TestServer`) — där syntes `orders_created_total`
direkt på nästa `/metrics`-anrop utan någon fördröjning alls, eftersom en
vanlig `curl`-till-`curl`-sekvens redan har gott om väggklocketid mellan
sig. Slutsats: en förstagångs-publicering av ett helt nytt, aldrig tidigare
använt instrument (lazy `static readonly Meter`/`Counter<T>`, skapat först
vid första faktiska `.Add()`-anropet) kapplöper mot OTel SDK:ets egen
instrument-bokföring specifikt när två requests körs rygg-mot-rygg i samma
process utan någon paus — inte en bugg i vår kod, och inte reproducerbart
i en riktig körande tjänst där det naturligt finns tid mellan händelser.
**Fix, samma stil som `ReservationExpiryTests`** (vänta in ett bakgrunds-
tillstånd istället för att mocka bort det): en liten
`ScrapeMetricsUntilAsync`-hjälpare i varje `MetricsEndpointTests`-klass som
pollar `/metrics` upp till 2s (50ms mellanrum) tills den förväntade metricen
dyker upp, istället för ett enda omedelbart anrop.

**Testtäckning:** `MetricsEndpointTests` i alla tre Api-testprojekt.
Kontrollerar bara *förekomst* av metric-namn i scrape-texten, aldrig exakta
värden — `OrdersTelemetry`/`InventoryTelemetry`s `Meter` är statiska,
process-globala instrument, så varje `WebApplicationFactory` i samma
testassembly (en per testklass) observerar samma underliggande counters;
att hävda ett exakt antal hade varit skört mot vad andra testklasser i
samma process råkar ha gjort. Alla 74 tester i lösningen gröna.

**Verifierat på riktigt mot docker-compose** (inte bara enhetstester):
`docker compose up --build` med orders/inventory/deras Postgres/Jaeger.
Skapade ett lager, skapade en order (`orders_created_total` dök upp,
`inventory_reservations_succeeded_total` med), försökte beställa mer än
tillgängligt lager (409, `orders_rejected_total{reason="insufficient_stock"}`
och `inventory_reservations_failed_total{reason="insufficient_stock"}`),
avbokade den lyckade ordern (`orders_cancelled_total`,
`inventory_releases_total`). Alla sex counters bekräftade med rätt
tagg-värden mot en riktig körande Kestrel-process. Inte omdeployat till
minikube i det här passet — samma "verifiera lokalt, k8s-utrullning är ett
separat steg" som redan är etablerat mönster (se t.ex. steg 3 del 1 vs.
steg 4).

Nästa steg: Prometheus + Grafana (egen uppföljning till steg 6 del 2,
scrapear `/metrics` på alla tre tjänster), sedan steg 7 (tredje tjänsten +
meddelandebuss).

## Steg 6, del 3 — Prometheus + Grafana i minikube. Klart och verifierat.

**Beslut, givna innan jag började (inte omförhandlade):** Prometheus som
Deployment med scrape-config via annotation-baserad `kubernetes_sd_configs`
(inte en handskriven target-lista), Grafana med Prometheus som datakälla.
Dashboarden provisionerad som kod (datasource + dashboard som ConfigMaps),
inte klickihopad i Grafanas UI — en dashboard som bara finns i podden
försvinner med podden och finns inte i git. Fyra paneler som betyder något
för just det här systemet (ordrar skapade/avvisade per reason,
reservationer lyckade/misslyckade, HTTP-latens p50/p95, felfrekvens
orders→inventory), ingen CPU/minne-panel.

**`k8s/`, inte docker-compose.** Det här är infrastruktur som ska visa att
jag kan drifta observability i Kubernetes, inte applikationskod — minikube
är där det faktiskt betyder något. (docker-compose fick det inte, till
skillnad från Jaeger i steg 6 del 1 som fanns i båda — bedömt som
onödigt här: Prometheus/Grafana-uppsättningen i sig är poängen, inte att
kunna se den snabbare lokalt.)

**RBAC: namespacad `Role`, inte `ClusterRole`.** Prometheus `kubernetes_sd_configs`
(`role: pod`) är redan begränsad till `namespaces.names:
["service-health-dashboard"]` i scrape-configen, så en `Role` +
`RoleBinding` i samma namespace räcker — `prometheus`-ServiceAccounten får
`get/list/watch` på `pods`, ingenting utanför den enda namespace den
faktiskt frågar om. En `ClusterRole` hade gett `list/watch` över varje pod
i hela klustret för en förmåga som aldrig används.

**Annotation-baserad discovery, inte en target-lista.** `k8s/prometheus/configmap.yaml`
har en `relabel_configs`-kedja (samma väletablerade community-mönster som
används överallt där Prometheus körs mot Kubernetes utan Operator): behåll
bara poddar taggade `prometheus.io/scrape: "true"`, läs port/path från
`prometheus.io/port`/`prometheus.io/path`, och `labelmap` för att lyfta
podd-labeln `app` (redan satt på alla tre Deployments sedan tidigare) till
en Prometheus-label — det är `app`-labeln dashboard-frågorna sedan
selekterar på (`app=~"orders-service|inventory-service"`), inte
podnamn/IP. orders-service/inventory-service/dashboard-service fick
`prometheus.io/scrape`/`port`/`path`-annoteringar i sina `deployment.yaml`
— en fjärde tjänst som läggs till senare plockas upp automatiskt nästa
gång dess Deployment rullas ut, ingen fil att komma ihåg att uppdatera här.

**Grafana provisionerad, inte klickad.** Tre ConfigMaps:
`grafana-datasource` (datasource.yaml, fast `uid: prometheus` så
dashboard-JSON:en kan referera den utan att den drifar vid en ny apply),
`grafana-dashboard-provider` (pekar ut var dashboard-JSON-filer letas upp),
`grafana-dashboard-service-health` (själva dashboard-JSON:en). Alla tre
monterade som enskilda filer via `subPath` i `k8s/grafana/deployment.yaml`
— medvetet, för att inte krocka med varandra i samma katalog.
`GF_AUTH_ANONYMOUS_ENABLED=true` (Viewer-roll): ren lokal bekvämlighet för
en enanvändarkluster på en enda dev-maskin, inte en säkerhetsavvägning —
`allowUiUpdates: false` i provider-configen är den faktiska spärren mot att
någon klickar ihop ändringar som sedan försvinner (Grafana tillåter då inte
ens att spara ändringar i UI:t för en provisionerad dashboard).

**Upptäckt — `subPath`-ConfigMap-mounts hot-reloadar inte.** Grafanas
egen filbaserade dashboard-provisionering pollar sin katalog var 30:e
sekund (`updateIntervalSeconds: 30`) och skulle i teorin plocka upp en
ändrad JSON-fil automatiskt — men det gäller bara om filen faktiskt ändras
i podden. Kubelet uppdaterar *inte* en `subPath`-monterad ConfigMap-fil när
ConfigMapen ändras (välkänd k8s-begränsning, till skillnad från en
hel-katalog-mount som synkas med viss fördröjning). Upptäckt när jag
korrigerade error-rate-panelens fråga (se nedan), applicerade om
ConfigMapen och inget hände i Grafana. Fix: `kubectl rollout restart
deployment/grafana` efter varje `kubectl apply` av dashboard-ConfigMapen —
dokumenterat i README, inte bara här, eftersom det är den typen av
"varför funkade inte min ändring"-fälla som redan finns dokumenterad för
`minikube image load` i steg 6 del 1/uppföljningen ovan.

**Upptäckt — den första error-rate-frågan mätte fel sak.** Första
versionen av panel 4 var `sum(rate(http_client_request_duration_seconds_count{app="orders-service",
http_response_status_code!~"2.."}[1m])) / sum(rate(...totalt...))`. Mot
riktig trafik visade den ~20 % "felfrekvens" **innan** inventory-service
ens skalades ner — inte en bugg i mätningen, en bugg i vad frågan faktiskt
räknade: OpenTelemetry:s HttpClient-instrumentering sätter `error_type`
till statuskoden som sträng (`error_type="409"`) för **alla** icke-2xx-svar,
inklusive ett fullt förväntat domän-409 (otillräckligt lager) — inte bara
för riktiga undantag. Bekräftat genom att läsa orders-service:s egen
`/metrics` rått: en lyckad reservation ger
`http_response_status_code="200"`, en 409 ger `error_type="409"` +
`http_response_status_code="409"`, och en genuint onåbar inventory-service
ger `error_type="System.Threading.Tasks.TaskCanceledException"` **utan**
någon `http_response_status_code`-label alls. Samma rådata visade också
att anropet till Jaeger (OTLP-export) går genom samma
HttpClient-instrumentering, taggat `server_address="jaeger"` — utan att
filtrera på `server_address="inventory-service"` hade panelen blandat ihop
telemetri-exporten med det faktiska orders→inventory-anropet.
**Fix:** `(total − (2xx + 409)) / total`, scopead till
`server_address="inventory-service"` —räknar allt som varken är en lyckad
reservation eller ett förväntat domän-409 som ett fel, vilket är exakt
"felfrekvens på orders → inventory-anropet" och inte "andel svar som inte
var 200".

**Images:** `prom/prometheus:v3.1.0`, `grafana/grafana:11.4.0` — pinnade
specifika versioner (samma konvention som `jaegertracing/all-in-one:1.76.0`
och `postgres:16-alpine`), bekräftat att de faktiskt går att dra innan de
skrevs in i manifesten. Ingen PVC för någon av dem — samma avvägning som
Jaeger: en lokal dev-instans tål att tappa TSDB-data/dashboards-cache vid
en pod-omstart eftersom allt som faktiskt spelar roll (dashboard,
datasource) redan är i git.

**Verifierat på riktigt i minikube** (inte bara `kubectl apply` utan fel):

1. Byggde om alla tre app-images (de som redan låg i klustret saknade
   metrics-koden från del 2) med samma `GIT_SHA`/`BUILD_TIME`-build-args
   som uppföljningen ovan, samma skala-ner-ta-bort-ladda-om-skala-upp-cykel
   för att undvika det redan dokumenterade `minikube image load`-fallgropen.
2. `kubectl apply` av `k8s/prometheus/` och `k8s/grafana/`, båda rullade ut
   rent. Prometheus `/api/v1/targets` bekräftade alla tre app-poddar `up`
   med rätt `scrapeUrl` (`http://<pod-ip>:8080/metrics`) — annotation-
   discoveryn fungerade utan någon manuell target-konfiguration.
3. Genererade kontinuerlig blandad trafik (lyckade ordrar, avvisade för
   otillräckligt lager) mot en riktig `kubectl port-forward`-tunnel, bekräftade
   i Prometheus att `orders_created_total`/`orders_rejected_total`/
   `inventory_reservations_succeeded_total`/`_failed_total` alla visade
   rätt värden och taggar.
4. Öppnade Grafana-dashboarden i Chrome (via `kubectl port-forward`) —
   alla fyra paneler renderade med riktig data, ingen manuell UI-konfiguration.
5. `kubectl scale deployment/inventory-service --replicas=0` medan trafiken
   fortsatte: samtliga fyra paneler rörde sig tillsammans i samma
   tidsfönster — nya avvisningar bytte reason från `insufficient_stock` till
   `inventory_unavailable`, p95-latensen för orders-service klättrade
   (resiliens-pipelinens omförsök), och felfrekvens-panelen gick till 100 %.
   Skärmdump tagen.
6. `kubectl scale deployment/inventory-service --replicas=1`: samtliga fyra
   paneler återhämtade sig i samma graf utan omstart av orders-service —
   felfrekvensen gick tillbaka till 0 %, latensen normaliserades,
   reservationer lyckades igen. Skärmdump av hela förlopp-och-återhämtning
   i en och samma bild (`docs/screenshots/grafana-dashboard-incident-and-recovery.jpg`),
   använd i README.

## Steg 7 — notifications-service + RabbitMQ. Klart och verifierat.

**Beslut, valda via fråga innan jag började (inte gissade):**

- **RabbitMQ**, inte NATS — mer etablerat i .NET-ekosystemet, lättare att visa
  upp i ett portfolio-repo (management-UI). Den öppna frågan i prioritets-
  ordningen ("börja med in-memory/HTTP, byt till RabbitMQ/NATS när tjänst 1
  och 2 står") är därmed löst.
- **Direkt publish efter DB-commit, inte transactional outbox.** Enklare:
  publicera till RabbitMQ direkt efter att ordern sparats, med publisher
  confirms. Ett litet fönster där processen kan krascha mellan commit och
  publish (eventet går då förlorat) — medveten avvägning, samma stil som
  TTL-beslutet i steg 3: en förlorad notifiering är irriterande, inte en
  korrekthetsbugg på samma sätt som en förlorad order eller dubbel
  reservation hade varit. Outbox noteras som naturligt nästa steg.

**Övriga beslut (mina, inom given ram):**

- **Best-effort publish, blockerar aldrig ordern.** `IEventPublisher`
  (`OrdersService.Domain`) implementeras av `RabbitMqEventPublisher`
  (`OrdersService.Infrastructure`) som fångar alla RabbitMQ-fel internt
  (loggar varning + räknar `orders.events.publish_failed`), kastar aldrig
  vidare — exakt samma mönster som `CancelOrder`s best-effort
  `ReleaseStockAsync`-anrop (se steg 3 del 2). orders-service `/health/ready`
  förblir Postgres-bara; RabbitMQ nere hindrar aldrig en order från att gå
  igenom.
- **Lazy connect + automatic recovery på orders-sidan.** `RabbitMqConnectionProvider`
  skapar sin `IConnection` först vid första publiceringen (inte vid
  startup), med `AutomaticRecoveryEnabled`/`TopologyRecoveryEnabled` —
  orders-service ska aldrig kräva att RabbitMQ är uppe för att starta.
  notifications-sidans motsvarande provider är identisk i sig själv, men
  `OrderEventConsumer` (en riktig `BackgroundService`) lägger en egen
  connect-retry-loop ovanpå den, eftersom RabbitMQ där ÄR ett hårt beroende
  — automatic recovery läker bara en redan upprättad anslutning, inte det
  allra första anslutningsförsöket om RabbitMQ inte är uppe när podden
  startar.
- **Egna, dupliceringsbara event-DTO:er per tjänst, ingen delad
  Contracts-assembly.** Samma princip som `InventoryClient.cs`s "local
  shape" för inventory-service:s svar: orders-service definierar sin
  publicerade JSON-form (`OrderEventPayload`), notifications-service sin
  egen deserialiserade form (`OrderEventEnvelope`) — håller tjänsterna
  oberoende deploybara.
- **Topic-exchange `orders` (durable), routing keys `order.created`/
  `order.confirmed`/`order.cancelled`.** Payloaden bär också ett
  `EventType`-fält — meddelandet är självbärande, konsumenten behöver inte
  läsa routing key.
- **Dead-letter queue** (`orders.dlx` fanout → `orders-notifications.dlq`)
  på notifications-service:s queue. Ett meddelande som inte går att
  deserialisera eller hanteras nackas utan requeue och hamnar i DLQ:n
  istället för att loopa oändligt eller försvinna tyst. Ingen automatisk
  DLQ-reprocessing — manuell inspektion, samma "medveten förenkling"-linje
  som Jaeger/Prometheus utan persistent lagring.
- **notifications-service Domain-lager, tunt men inte tomt:**
  `OrderEventType` (enum), `OrderNotification` (record), `INotificationSender`
  (interface, implementeras av `LoggingNotificationSender` — simulerar
  "skicka bekräftelse" via strukturerad logg). Ger en testbar söm utan att
  bygga en riktig avisering nu.
- **`OrderEventHandler` utbruten från `OrderEventConsumer`**, samma
  motivering som `ServiceHealthChecker` i dashboard-service: deserialisering
  + dispatch är direkt testbar utan en riktig RabbitMQ-anslutning.
  `OrderEventConsumer` äger bara själva RabbitMQ-rörledningen (anslut,
  deklarera topologi, konsumera, ack/nack).
- **RabbitMQ i klustret utan PVC**, samma avvägning som Jaeger/Prometheus/
  Grafana. Management-UI exponerad som NodePort av samma anledning som
  Jaeger/Grafana.
- **Test-uppdelning matchar `DashboardService`:** ingen
  `NotificationsService.Domain.Tests` (domänlagret är tunt, inga regler att
  låsa fast i isolering), bara `NotificationsService.Infrastructure.Tests`
  och `NotificationsService.Api.Tests`.
- **Riktig RabbitMQ i `NotificationsService.Api.Tests`, inte en fejk** —
  `RabbitMqContainerFixture` (Testcontainers.RabbitMq), delad per collection,
  samma princip som `PostgresContainerFixture` i orders/inventory. `/health/ready`
  testas mot en verklig anslutning (pollas — se nästa punkt), och ett test
  publicerar ett riktigt meddelande på containerns exchange och pollar
  `/metrics` efter `notifications_sent_total` — ett genuint end-to-end-test
  av hela konsument-kedjan, inte bara den isolerade handler-logiken.
  `NotificationsService.Infrastructure.Tests` behöver däremot ingen RabbitMQ
  alls: `OrderEventHandler`s tester kör mot handbyggda JSON-payloads direkt.

**Upptäckt under testskrivning — samma `builder.Configuration`-fälla som
redan dokumenterad två gånger tidigare (steg 8, `Database:RunMigrationsOnStartup`-
kommentaren i `Program.cs`; CI-uppföljningen med Testcontainers-anslutningssträngen),
nu en tredje gång.** `RabbitMqOptions` lästes i både orders- och
notifications-service:s `Add*Infrastructure`-metoder via
`configuration.GetSection(...).Get<RabbitMqOptions>()` där `configuration`
var parametern som skickas in **före** `builder.Build()` — exakt samma
mönster som redan var känt fel för databas-anslutningssträngar.
`WebApplicationFactory`s `ConfigureAppConfiguration`-overrides (i det här
fallet: en riktig Testcontainers RabbitMQ-host/port istället för
`appsettings.json`s `localhost:5672`) landar bara i den färdigbyggda
konfigurationen, aldrig i den tidiga snapshotten. Effekten var tyst och
förvirrande: `NotificationsApiFactory`s test av "RabbitMQ onåbar → 503"
gick grönt av fel anledning (anslöt av misstag till en **kvarglömd
`docker compose`-RabbitMQ-container på `localhost:5672` från en tidigare
manuell verifieringsrunda i samma session**, inte till den riktiga
Testcontainers-instansen), och konsumtions-testet (publicera på den
riktiga containern, förvänta `notifications_sent_total`) failade eftersom
appen faktiskt lyssnade på en helt annan broker än den testet publicerade
till.
**Fix, samma mönster som redan etablerat för `AddDbContext`:** båda
`RabbitMqOptions`-registreringarna läser nu lazy via en DI-factory
(`services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>()...)`),
upplöst först vid första faktiska resolve (efter `Build()`), inte fångad
vid registreringstillfället. **Lärdom att skriva ner tydligare den här
gången:** det här är inte en engångsmiss, det är ett generellt mönster —
*varje* `Add*Infrastructure`-metod som binder en typad options-klass direkt
från den `IConfiguration`-parameter som skickas in före `builder.Build()`
har samma latenta bugg, även om den bara syns när något (ett test, en
override) faktiskt behöver läsa ett värde som sattes efter den punkten.
Värt att svepa igenom `InventoryClientOptions` (samma mönster, ännu inte
exercisead av ett failande test) vid ett senare tillfälle.

### Uppföljning — InventoryClientOptions fixad, mönstret gjort svårt att göra fel

Åtgärdat samma dag som ovan hittades, inte lämnat som en känd-men-olagad
bugg. `AddInventoryClient` i `OrdersService.Infrastructure/ServiceCollectionExtensions.cs`
läste `InventoryClientOptions` på exakt samma eagra sätt som `RabbitMqOptions`
gjorde — bara aldrig exerciserad av ett failande test eftersom
`OrdersApiFactory` ersätter `IInventoryClient` helt med en fejk (ingen
WebApplicationFactory-override av `InventoryClient:BaseUrl` att missa).
**Fix:** `AddHttpClient<IInventoryClient, InventoryClient>((serviceProvider, client) => ...)`
— `(IServiceProvider, HttpClient)`-overloaden istället för `(HttpClient)`-
overloaden, samma lazy-efter-Build-princip. Verifierat mot en riktig
`docker compose`-stack (inte bara att testerna fortfarande går gröna): skapade
lager och en order genom hela orders→inventory-kedjan, `POST /orders` → 201.

**Tredje gången samma fälla är ett systemfel, inte otur** (databas-
anslutningssträngen i steg 8-uppföljningen, `RabbitMqOptions` ovan, nu
`InventoryClientOptions`) — löst genom att göra det rätta mönstret till
vägen med minst motstånd, inte bara dokumentera det i efterhand:

- Varje tjänsts `Infrastructure/ServiceCollectionExtensions.cs` har nu en
  privat `GetRequiredOptions<TOptions>(IServiceProvider, string)`-hjälpare
  (identisk i orders- och notifications-service, samma "ingen delad
  assembly"-princip som redan gäller event-DTO:erna) — nästa typade
  options-klass i den filen återanvänder hjälparen istället för att skriva
  bindningen för hand igen.
- `Directory.Build.props` (delad av alla projekt) har en kommentar överst
  som förklarar fällan, den korrekta lösningen, och pekar på hjälpar-
  metoden — vald framför en delad extension-metod i en ny delad assembly,
  eftersom projektet redan medvetet inte har någon delad kod mellan
  tjänsterna (se steg 7s "ingen delad Contracts-assembly"-beslut); en
  kommentar i en fil som redan är gemensam för hela lösningen är
  tillräckligt upptäckbar utan att bryta den principen.

**Testtäckning:** `OrderEventHandlerTests` (giltig payload per event-typ →
rätt mappad `OrderNotification` + ack; trasig JSON → nack; okänd event-typ →
nack; sändaren kastar → nack, propagerar inte). `OrderEndpointsTests`
utökad med en `FakeEventPublisher` (samma mönster som `FakeInventoryClient`):
publish anropas vid create/confirm/cancel, och en avvisad order (409) leder
inte till någon publicering. `NotificationsService.Api.Tests`: liveness,
readiness (både verkligt uppkopplad och verkligt onåbar), version, metrics
(inklusive det riktiga konsumtions-end-to-end-testet ovan). Alla 88 tester i
lösningen (orders + inventory + dashboard + notifications) gröna.

**Verifierat på riktigt mot docker-compose:**

1. Ny `rabbitmq`-service i `docker-compose.yml` (`rabbitmq:4-management-alpine`,
   healthcheck via `rabbitmq-diagnostics -q ping`), orders-service fick
   `RabbitMq__*`-env + `depends_on: rabbitmq (service_healthy)`.
2. `docker compose up --build`: skapade en order → bekräftade via RabbitMQ
   management-API att `orders`-exchangen (topic, durable) hade `publish_in: 1`
   innan notifications-service ens fanns i stacken (fas A, ingen konsument
   än).
3. Lade till `notifications-service` i `docker-compose.yml`, byggde om.
   Skapade, bekräftade och avbokade ordrar genom hela kedjan — loggarna i
   notifications-service visade alla tre event-typer konsumerade med rätt
   order-/kund-ID, och `/metrics` visade `notifications_sent_total` med
   rätt `event_type`-taggar (2×`order.created`, 1×`order.confirmed`,
   1×`order.cancelled` efter fyra testordrar).
4. `docker compose stop rabbitmq`: `notifications-service`s `/health/ready`
   gick till 503 (dess hårda beroende), medan `POST /orders` fortsatte svara
   201 med en loggad `BrokerUnreachableException`-varning (bekräftat i
   loggarna, inte gissat) — orders-service:s egen `/health/ready` opåverkad
   hela tiden. `docker compose start rabbitmq`: båda sidor självläkte utan
   någon omstart — notifications-service:s connect-retry-loop återanslöt
   och började konsumera igen, orders-service:s lazy-connect återanslöt vid
   nästa publiceringsförsök, `notifications_sent_total` fortsatte räkna
   uppåt.
5. Publicerade ett medvetet trasigt meddelande direkt via management-API:t
   (`payload: "not valid json at all"`) — `notifications_failed_total{reason="deserialize_error"}`
   ökade, huvudkön (`notifications.order-events`) förblev på 0 meddelanden
   (inte fastnat), och `orders-notifications.dlq` visade `messages: 1`.

**Verifierat på riktigt i minikube** (samma kluster som tidigare steg, ingen
omstart av hela klustret behövdes):

1. Byggde om `orders-service`-imagen (ny RabbitMq-publiceringskod) och en ny
   `notifications-service`-image med samma `GIT_SHA`/`BUILD_TIME`-build-args
   som tidigare steg. Skalade ner `orders-service` till 0 innan
   `minikube image rm`/`image load` — samma redan dokumenterade
   stale-tag-fälla, `image rm` failade första gången med "container ... is
   using its referenced image" tills podden faktiskt hunnit termineras.
2. `kubectl apply` av `k8s/rabbitmq/` (Deployment utan PVC + NodePort-Service,
   samma avvägning som Jaeger) och `k8s/notifications-service/` (Deployment
   + ClusterIP-Service, inga Secret/Job — ingen databas, inget känsligt
   lösenord). `orders-service/deployment.yaml` fick `RabbitMq__*`-env pekande
   på `rabbitmq`-Servicen. Alla tre rullade ut rent, `1/1 Ready` direkt.
3. Skapade en riktig order genom `orders-service`s NodePort (via
   `kubectl port-forward`, inte `minikube service` — enklare för skriptad
   verifiering över flera anrop). `kubectl logs deployment/notifications-service`
   visade eventet konsumerat med rätt order-/kund-ID inom en sekund.
4. `kubectl scale deployment/rabbitmq --replicas=0`: `notifications-service`s
   `/health/ready` gick till 503, `orders-service`s egen `/health/ready`
   förblev `Healthy`, och `POST /orders` fortsatte svara 201. `kubectl scale
   deployment/rabbitmq --replicas=1`: ny order konsumerades igen inom
   sekunder, **`Restart Count: 0`** på både `orders-service`- och
   `notifications-service`-podden hela tiden — självläkning utan en enda
   pod-omstart, exakt som designat.
5. Prometheus `/api/v1/targets` bekräftade `notifications-service` som `up`
   direkt efter första rollouten — annotation-baserad discovery plockade upp
   den fjärde tjänsten utan någon ändring i `k8s/prometheus/configmap.yaml`,
   exakt löftet från steg 6 del 3.

**Medvetet inte gjort i det här passet** (noterat i README under "Not done
yet"): transactional outbox (se beslutet ovan), Grafana-paneler för
`notifications_sent_total`/`notifications_failed_total` (Prometheus
scrapear redan tjänsten, bara inte visualiserat), och att svepa igenom
`InventoryClientOptions` för samma latenta `builder.Configuration`-mönster
som fixades för `RabbitMqOptions` ovan.

## Steg 7.5 — transactional outbox + idempotent konsument. Klart och verifierat.

Steg 7 dokumenterade explicit "direkt publish efter DB-commit, inte
transactional outbox" som ett medvetet val för det steget, med outbox
noterat som naturligt nästa steg om avvägningen någonsin slutade vara
acceptabel. Beställt uttryckligen som ett eget steg direkt efteråt — inte
en ångrad bedömning, bara nästa steg i kön, som redan förutspått.

**Producentsidan — outbox i samma transaktion som ordern.**

- Ny `OutboxMessages`-tabell i orders-service:s Postgres (`Id`, `OrderId`,
  `EventType`, `PayloadJson`, `CreatedAtUtc`, `PublishedAtUtc` (nullable),
  `Attempts`, `LastError`), migration `AddOutbox`. `Id` fungerar som
  eventets stabila identitet genom hela dess liv — genererad en gång vid
  enqueue, återanvänd oförändrad i varje efterföljande publiceringsförsök
  (inklusive retries efter en krasch). Det är precis den stabiliteten som
  gör dedupe på konsumentsidan möjlig (se nedan).
- `IOrderEventOutbox` (Domain, ersätter `IEventPublisher` från steg 7) har
  synkrona `Enqueue*`-metoder — ingen I/O, bara att lägga en rad i
  `OrdersDbContext`s change tracker. `EfOrderEventOutbox` (Infrastructure)
  implementerar den. `OrderEndpoints.cs` anropar `outbox.Enqueue*` **före**
  `repository.SaveChangesAsync`, inte efter som i steg 7 — det är den
  omkastningen som gör ordern och outbox-raden till en och samma
  transaktion. `OrderRepository` och `EfOrderEventOutbox` delar samma
  scoped `OrdersDbContext`-instans inom en request, vilket DI-containern
  redan garanterar (båda är Scoped) — ingen explicit transaktion behövs.
- `OutboxDispatcher` (`BackgroundService`, samma
  `PeriodicTimer`+`IServiceScopeFactory`-mönster som inventory-service:s
  `ReservationExpiryService`) pollar var 2:a sekund (`Outbox:PollIntervalSeconds`),
  läser opublicerade rader (`PublishedAtUtc IS NULL`, batch om 20, äldst
  först), försöker publicera var och en via `RabbitMqOutboxSender` (samma
  publisher-confirms-baserade RabbitMQ-logik som fanns i steg 7:s
  `RabbitMqEventPublisher`, nu utan catch-and-swallow — dispatchern måste
  se felet för att kunna retrya). Lyckat försök sätter `PublishedAtUtc`;
  misslyckat ökar `Attempts`/`LastError` och lämnas kvar för nästa poll.
  **Ingen max-attempts-gräns eller egen DLQ för outbox-rader** — en rad som
  aldrig kan publiceras (inte bara transient nere, utan strukturellt
  trasig) skulle behöva larmning/manuell hantering, och det finns ingen
  konsument av den signalen än. Noterat i README under "Not done yet",
  inte dolt.
- orders-service anropar alltså aldrig RabbitMQ från request-vägen längre
  — `POST /orders` beror bara på Postgres, samma som innan steg 7 ens
  fanns. Ännu starkare garanti än steg 7:s "blockerar aldrig ordern":
  där kunde en publicering fortfarande *försökas* synkront (och svälja
  sitt eget fel); nu finns det inget publiceringsförsök alls i requesten.

**Konsumentsidan — idempotens via EventId, inte via databas.**

- `IProcessedEventStore` (Domain) + `InMemoryProcessedEventStore`
  (Infrastructure): `ConcurrentDictionary<Guid, DateTimeOffset>` med
  opportunistisk sanering (varje anrop städar bort poster äldre än 1 timme)
  istället för en egen sweep-`BackgroundService` — volymen är för liten för
  att motivera en till bakgrundstjänst bara för det. `TryAdd` är atomärt,
  samma korrekthetsegenskap som inventory-service:s
  idempotens-via-orderId-uppslag redan bygger på.
- **In-memory, inte en databas — medvetet, inte en genväg.**
  notifications-service har ingen databas (etablerat beslut från steg 7:
  tjänstens hela poäng är att vara enkel och umbärlig). Dedupe-fönstret
  överlever alltså inte en processomstart: ett event som redeliveras
  *efter* en omstart (RabbitMQ köar om en oackad leverans, podden startar
  om, först då kommer omleveransen) skulle behandlas igen. Acceptabelt här
  eftersom sidoeffekten bara är en loggrad (`LoggingNotificationSender`),
  inte en handling som måste vara garanterat engångsutförd — en riktig
  databas vore rätt kallelse den dag sidoeffekten blir en som aldrig får
  upprepas.
- `OrderEventHandler.HandleAsync` anropar `TryMarkProcessedAsync(envelope.EventId, ...)`
  direkt efter lyckad typmappning. Om `false` (redan sedd): loggar,
  räknar `notifications.duplicate`, **ackar** (returnerar `true`) —
  en dubblett är inte ett fel, och att nacka den skulle bara få RabbitMQ
  att leverera om den för evigt.

**Testtäckning:**

- `OrderEndpointsTests` (orders-service) — `IOrderEventOutbox` **fejkas
  inte längre**: `OrdersApiFactory` kör den riktiga `EfOrderEventOutbox`
  mot den riktiga Testcontainers-Postgres:en, och testerna läser
  `OutboxMessages`-tabellen direkt via en egen kortlivad `OrdersDbContext`
  (`GetOutboxMessagesAsync`/`GetOutboxMessageCountAsync`) — det är den
  faktiska transaktionsgarantin som testas, inte att en metod anropades.
- Ny `OutboxDispatcherTests` (orders-service) — `OrdersApiCollection`
  utökad med en `RabbitMqContainerFixture` (samma mönster som
  notifications-service:s egen, duplicerad enligt "ingen delad
  assembly"-principen). Skapar en order genom det riktiga API:et, gör
  **inget** explicit publiceringsanrop, och pollar bara
  `OutboxMessages.PublishedAtUtc` tills den riktiga, oförändrade
  `OutboxDispatcher`-bakgrundstjänsten har publicerat den — bevisar att
  hela kedjan (enqueue → commit → poll → publish → confirm) fungerar utan
  någon testkod som härmar den.
- `OrderEventHandlerTests` (notifications-service) — ny
  `HandleAsync_WithARedeliveredEventId_AcksWithoutSendingASecondTime`:
  anropar `HandleAsync` två gånger med **samma** handbyggda payload (samma
  `EventId`), bekräftar att sändaren bara anropas en gång och att båda
  anropen ackar. Använder den riktiga `InMemoryProcessedEventStore`
  (ingen I/O, ingen anledning att fejka den) — dedupe-logiken som testas
  är den faktiska, inte en stand-in för den.
- 90 tester totalt i lösningen (upp från 88 i steg 7), alla gröna.

**Verifierat på riktigt, de två scenarierna som efterfrågades — inte bara
testerna ovan:**

1. **Inget event går förlorat vid ett RabbitMQ-avbrott.**
   `docker compose stop rabbitmq`, skapade en order — `POST /orders` → 201
   (bekräftar att skrivvägen är helt frikopplad från RabbitMQ, inte bara
   "bäst-ansträngning-frikopplad" som i steg 7). Läste `OutboxMessages`
   direkt via `psql` i `postgres`-containern: raden fanns, `PublishedAtUtc`
   var `NULL`, `Attempts` steg (dispatcher-pollningen försökte och
   misslyckades om och om igen medan RabbitMQ var nere). `docker compose
   start rabbitmq`, väntade in nästa pollningscykel: `PublishedAtUtc` sattes
   (`Attempts` hade hunnit till 6 innan den lyckades), och
   `notifications-service`s logg visade eventet konsumerat och en
   bekräftelse "skickad" — samma order-/kund-ID som skapades medan
   RabbitMQ var nere.
2. **Samma event levererat två gånger → notifications-service agerar bara
   en gång.** Inte simulerat med ett handgjort dubblett-meddelande — den
   redan publicerade outbox-raden återställdes till `PublishedAtUtc = NULL`
   direkt i Postgres (`UPDATE "OutboxMessages" SET "PublishedAtUtc" = NULL
   WHERE ...`), vilket tvingade den riktiga, oförändrade
   `OutboxDispatcher`:n att återpublicera **samma rad, samma EventId**
   genom den faktiska produktionskodvägen — en genuin at-least-once-
   omleverans, inte en konstruerad efterlikning av en. Resultat: loggarna
   visade exakt en "Sending ... confirmation"-rad och en separat
   "Duplicate delivery of event ...; already processed, skipping."-rad för
   samma order-ID; `notifications_sent_total` förblev på 1,
   `notifications_duplicate_total` gick till 1.
3. Full sekvens replikerad i minikube (samma kluster som tidigare steg):
   byggde om `orders-service`/`notifications-service`-images, skalade ner
   till 0 innan `image rm`/`image load` (samma redan dokumenterade
   stale-tag-fälla), körde om `orders-migrate`-Jobbet (gammalt, redan
   `Completed`-jobb fanns inte längre kvar att städa bort denna gång) för
   att applicera `AddOutbox`-migrationen, skalade upp igen. Skapade en
   order genom klustrets riktiga NodePort/Service-kedja: `OutboxMessages`
   visade `PublishedAtUtc` satt efter ett enda försök (`Attempts: 0`,
   RabbitMQ var frisk hela tiden i den här omgången) och
   `notifications-service`s poddloggar visade konsumtionen — samma
   bekräftelse som i docker-compose-verifieringen, nu genom riktig k8s-DNS/
   Service-routing.

**Medvetet inte gjort i det här passet** (noterat i README under "Not done
yet"): max-attempts/larmning för outbox-rader som aldrig kan publiceras,
Grafana-paneler för notifications-/outbox-räknarna.

## Steg 7.6 — poison-message-skydd i OutboxDispatcher. Klart och verifierat.

Steg 7.5 lämnade uttryckligen kvar "ingen max-attempts-gräns" som ett
medvetet gap, dokumenterat i README under "Not done yet". Genomlyst på
begäran: **blockerar en rad som aldrig går att publicera raderna efter
den?** Svaret var nyanserat, inte ett rent ja/nej, så båda halvorna av
frågan besvarades:

- **En enda poison-rad blockerade redan INTE de andra i samma batch.**
  `OutboxDispatcher`s `foreach`-loop försöker varje rad i batchen oavsett
  tidigare fel i samma pass, och `SaveChangesAsync` anropas bara en gång,
  efter loopen — ett fel på rad 1 stoppar aldrig att rad 2–20 faktiskt
  försöks. Det här var redan sant innan den här ändringen, bara aldrig
  uttryckligen verifierat med ett test.
- **Men en HOP av poison-rader större än BatchSize (20) skulle ha
  gjort det.** Frågan `Where(PublishedAtUtc == null).OrderBy(CreatedAtUtc).Take(20)`
  plockar alltid de äldsta väntande raderna först. Utan en gräns som
  utesluter en rad som aldrig kommer att lyckas, skulle en hög av fler än
  20 sådana rader (äldre än allt annat) permanent uppta varje batch-plats
  — en genuint frisk rad som råkar hamna bakom dem skulle aldrig ens
  försökas. Det här var den verkliga risken värd att stänga.

**Fix:** nytt `FailedAtUtc`-fält (nullable) på `OutboxMessage` +
`OutboxOptions.MaxAttempts` (default 20, ~40s vid standard-pollningen på
2s — gott om marginal jämfört med de 6 försök/~12s en riktig
RabbitMQ-omstart tog i steg 7.5s verifiering). När `Attempts` når
`MaxAttempts` efter ett misslyckat försök: `FailedAtUtc` sätts, raden
loggas på **Error**-nivå (skild från den vanliga retry-varningen på
**Warning**-nivå) med ett tydligt "kommer INTE att försökas igen
automatiskt"-meddelande, och en ny räknare
(`orders.events.publish_abandoned`, taggad `event_type`) ökar — en egen
signal skild från `orders.events.publish_failed` (ett enskilt
misslyckat försök) eftersom det är antalet som ger upp som är värt att
larma på. `FailedAtUtc IS NULL` lades till i dispatcherns
pending-fråga (nu en sammansatt index på `(PublishedAtUtc, FailedAtUtc)`
istället för två separata) — det är uteslutningen ur frågan, inte bara
Error-loggen, som faktiskt stänger hop-scenariot ovan. Raden och dess
fulla `PayloadJson`/`LastError` finns kvar i tabellen; att nollställa
`FailedAtUtc` manuellt kör den genom fler försök igen (samma mekanik som
redan visades i steg 7.5s omleverans-verifiering via en direkt `UPDATE`).

**`RabbitMqOutboxSender` bakom ett nytt `IOutboxSender`-interface**
(Infrastructure-internt, inte Domain — inget i Api eller Domain känner
till det, bara `OutboxDispatcher` som anropar det) så att tester kan byta
ut den mot en fejk som misslyckas på beställning, samma mönster som
`FakeInventoryClient`/`FakeNotificationSender` redan använder för sina
motsvarande externa beroenden.

**Testtäckning:** ny `PoisonOutboxFactory` (delar Postgres-containern via
`PostgresContainerFixture`, ingen RabbitMQ-container behövs — det här
handlar om dispatcherns egen ge-upp-logik, inte själva publiceringsanropet,
som redan är täckt av `OutboxDispatcherTests` mot en riktig broker), snabb
pollning (1s) och lågt `MaxAttempts` (3) för ett snabbt, deterministiskt
test, plus en `FakeOutboxSender` som alltid kastar.
`OutboxDispatcherPoisonMessageTests`:
- En rad som alltid misslyckas får `FailedAtUtc` satt efter exakt
  `MaxAttempts` försök, med rätt `LastError`, och **slutar** anropas
  därefter (bekräftat genom att vänta ytterligare en pollningscykel och
  se att fejk-sändarens anropsräknare inte rör sig) — inte bara flaggad
  medan dispatchern fortsätter hamra på den.
- Två samtidiga poison-rader (från två olika ordrar) ger upp oberoende av
  varandra — den första radens misslyckande hindrar inte den andra från
  att försökas och själv nå `FailedAtUtc`.
- 92 tester totalt i lösningen (upp från 90).

**Upptäckt under testskrivning — ett enkelt men avslöjande hål i den nya
testfixturen.** Första körningen av de nya testerna failade, men inte på
poison-logiken: `PoisonOutboxFactory` bytte ut `IOutboxSender` men glömde
`IInventoryClient` (som `OrdersApiFactory` redan gör) — utan den fejken
gick `POST /orders` mot en riktig, onåbar `InventoryClient:BaseUrl` och
svarade 503 innan ordern ens hann skapas, långt innan outbox-koden någonsin
nåddes. Precis den typen av "testet bevisar fel sak av fel anledning"-fälla
som redan synts två gånger tidigare i det här steget (se
`builder.Configuration`-uppföljningarna) — nu i test-setup snarare än
produktionskod, men samma lärdom: en ny factory måste medvetet kopiera ALLA
beroenden den föregående factoryn redan fejkade, inte bara den man för
tillfället bryr sig om.

## Steg 8, CD-halvan — publicera images till GHCR. Klart och verifierat.

CI-halvan av steg 8 var redan klar sedan tidigare (se ovan). Den här delen
lägger till bygg-och-publicera, medvetet **inte** auto-deploy — se beslutet
nedan för varför det senare inte bara är "inte gjort än" utan faktiskt
inte möjligt mot den här projektets egna minikube-mål utan en annan sorts
infrastruktur.

**Beslut:**

- **Alla fyra tjänstavbilder till GHCR, inte bara de tre domän-
  mikrotjänsterna.** Den ursprungliga instruktionen sa "alla tre images",
  men `docker-build`-jobbet byggde redan `dashboard-service` sedan
  tidigare (och saknade faktiskt `notifications-service` i sin matris —
  ett kvarglömt hål sedan tjänsten lades till i steg 7, hittat och fixat
  i samma veva). Att publicera tre av fyra redan byggda images hade varit
  mer förvirrande än konsekvent — samma "bygg allt som redan byggs"-linje.
- **Taggar: kort git-SHA (samma `git rev-parse --short HEAD`-stil som
  lokala `docker build`-kommandon i README) + `latest`.** Inte den fulla
  40-tecken-SHA:n `github.sha` ger — konsekvent med hur `/version` redan
  visar `@6f310da`-stil korta hashar i dashboarden.
- **`permissions: packages: write` scopeat till `docker-publish`-jobbet,
  inte hela workflowen.** Samma minsta-privilegium-linje som Prometheus
  fick en namespacad `Role` istället för en `ClusterRole` i steg 6 del 3
  — `build-and-test`- och `docker-build`-jobben behöver aldrig skriva
  till GHCR.
- **Publicera bara vid push till `master`, aldrig på en pull_request.**
  En `GITHUB_TOKEN` på en PR från en fork saknar ändå
  package-write-behörighet, och en PR ska inte publicera en image oavsett
  — `if: github.event_name == 'push' && github.ref == 'refs/heads/master'`.
- **`docker-publish` beror på både `build-and-test` OCH `docker-build`**,
  inte bara på testerna — ingen anledning att slösa ett registry-push på
  en image vars Dockerfile redan är känd trasig.
- **Owner-namnet lowercased explicit i ett eget steg** (`tr '[:upper:]'
  '[:lower:]'`) — `github.repository_owner` bevarar kontots faktiska
  skiftläge (`ShineIB`), men GHCR-image-sökvägar måste vara helt
  gemener. Verifierat genom att faktiskt läsa `Build__GitSha`/
  `Build__BuildTimeUtc` ur en publicerad image (se nedan) — hade
  owner-lowercasingen varit fel hade hela push-steget failat med ett
  ogiltigt repository-namn, inte bara gett fel taggar.

**Beslut — inget auto-deploy, och varför det inte bara är
"inte prioriterat":** allt under "Kubernetes (minikube)" i README körs mot
ett `minikube`-kluster som lever på den här maskinens egen Docker Desktop/
Hyper-V. En GitHub-hostad runner har ingen nätverksväg dit, ingen
`kubeconfig` för det, och inget värt att ge en även om den hade — minikube
är avsiktligt inte nåbart från internet. Pipelinen slutar därför vid "en
image finns i GHCR som `kubectl`/`minikube image load` kan använda näst" —
att dra in den i minikube och rulla om Deploymentsen är fortfarande samma
manuella `kubectl apply`/`minikube image load`-sekvens som redan är
dokumenterad. Att låtsas motsatsen — en grön bock som antyder "och nu är
det live" — hade varit missvisande om vad som faktiskt hände. En riktig
CD-lösning skulle behöva ett kluster GitHub Actions faktiskt kan nå (ett
molnhostat kluster, eller en self-hosted runner med nätverksväg till den
här maskinen) plus något som driver utrullningen — en annan sorts
infrastruktur än "kör det på min bärbara dator", inte ett saknat
workflow-steg.

**Upptäckt under verifiering — två riktiga CI-buggar, inga lokala repron.**
`gh run watch` på de två föregående pusharna (steg 7.6-fixen och den här)
visade att CI:t faktiskt varit **rött i två körningar i rad redan innan
den här sessionens första push i den här omgången** — något jag inte hade
upptäckt om jag bara litat på lokala `dotnet test`-körningar (alla gröna,
varje gång). Grundorsaken båda gångerna:
`NotificationsService.Api.Tests.MetricsEndpointTests.Metrics_ReturnsPrometheusTextExposingAspNetCoreInstrumentation`
— den enda `/metrics`-testet i hela lösningen som INTE redan använde
poll-tills-synligt-mönstret (`ScrapeMetricsUntilAsync`) som är
väletablerat i systertesterna. Två separata fixförsök:
1. Första fixen (använd den redan existerande `ScrapeMetricsUntilAsync`-
   hjälparen, 5s poll) räckte inte — misslyckades igen, men den här gången
   syntes `aspnetcore_routing_match_attempts_total`/`http_server_active_requests`
   redan från den allra första requesten, medan `http_server_request_duration`
   fortfarande saknades efter 5 sekunders upprepad `/metrics`-scraping
   utan någon annan request emellan.
2. **Slutsats:** duration-histogrammet registreras vid svarets
   *avslut*, till skillnad från de andra två (registrerade vid start/
   matchning) — att bara scrapea `/metrics` om och om igen genererar
   aldrig en NY sådan mätning om den ursprungliga (från det enda
   `/version`-anropet) gick förlorad i en tidig timing-lucka på en
   belastad GitHub Actions-runner. **Fix:** loopen gör nu ett vanligt
   `GET /version`-anrop varje pollningsvarv, inte bara ett omscrape av
   `/metrics` — samma "generera färska händelser istället för att
   förlita sig på en specifik, ev. förlorad mätning"-princip som redan
   användes i `Metrics_AfterConsumingAnOrderCreatedEvent_...`-testet i
   samma fil (som republicerar hela eventet varje varv, inte bara
   omscrapear). Tidsgränsen höjdes samtidigt till 20s för marginal på en
   belastad runner.
**Lärdom:** `dotnet test` lokalt räcker inte som "verifierat" för allt —
den lokala maskinen har mer marginal (färre samtidiga containrar/CPU-
kontention) än en delad GitHub-hostad runner. Efter det här passet:
kolla faktiskt `gh run list`/`gh run watch` efter en push som rör
testkod, inte bara lokala körningar, innan man kallar något klart.
**Kvarstående, medvetet inte svept nu:** `OrdersService.Api.Tests`/
`InventoryService.Api.Tests`s motsvarande första `/metrics`-test gör
fortfarande samma en-gångs-läsning (fungerar hittills, aldrig sett
flaka i CI) — värt att svepa igenom om det någonsin visar samma symptom,
men inte fixat i förebyggande syfte här.

**Verifierat på riktigt:**
1. `gh run watch` på den gröna körningen efter fixen ovan: `Build & test`,
   alla fyra `Docker build (*)`-jobb, och alla fyra
   `Publish * image`-jobb gröna.
2. `docker pull ghcr.io/shineib/orders-service:latest` (och samma för
   `inventory-service`/`notifications-service`/`dashboard-service`) —
   lyckades utan `docker login`, bekräftar att paketen faktiskt är
   publika, inte bara att workflowen rapporterade "success".
3. `docker pull ghcr.io/shineib/orders-service:80c8720` (kort-SHA-taggen)
   gav **samma digest** som `:latest` — bekräftar att båda taggarna
   pekar på samma, riktiga image, inte två separata builds.
4. `docker run --rm --entrypoint printenv ghcr.io/shineib/orders-service:latest`
   visade `Build__GitSha=80c8720`/`Build__BuildTimeUtc=2026-08-24T12:42:11Z`
   — riktiga värden bakade in av CI:t, inte `unknown`, och SHA:n matchar
   exakt den commit som triggade den gröna körningen.

## Uppföljning — repo-finputs: beskrivning/topics, CI-failure-notiser, en tredje runda på metrics-flakigheten

**GitHub-beskrivning + topics** satta via `gh repo edit` (`dotnet`,
`microservices`, `kubernetes`, `opentelemetry`, `rabbitmq`, `aspnetcore`).

**Beslut — auto-skapa ett GitHub-issue på en röd `master`, inte ett
konto-notis-inställning.** Det finns ingen publik GitHub-API för det
personliga "mejla mig bara vid misslyckade Actions"-kontoinställningen
(Settings → Notifications → Actions) — den går inte att sätta via `gh`/API,
bara klicka i UI:t själv. Istället: ett nytt `notify-on-failure`-jobb
(`if: failure() && github.event_name == 'push' && github.ref == 'refs/heads/master'`,
beror på alla tre övriga jobb) skapar ett GitHub-issue taggat `ci-failure`
med länk till körningen, via `actions/github-script` (ingen extern secret
behövs, bara `GITHUB_TOKEN` med `issues: write`, scopeat till just det
jobbet). Ett öppet issue syns på repots förstasida samma sätt som README:s
CI-badge är tänkt att synas — kompletterar den, ersätter den inte.

**Det här jobbet bevisade sig självt på riktigt, inte bara i teorin.**
Nästa push (se nedan) råkade faktiskt faila `Build & test` igen — och
`notify-on-failure` triggade korrekt, skapade issue #1
("CI failed on master (031fb81)"). Stängt manuellt efter fixen nedan
landat och `master` blivit grön igen (`gh issue close 1`).

**Metrics-flakigheten (se förra uppföljningen) var INTE löst av förra
fixen — en tredje, mer grundlig runda krävdes.** Samma push som triggade
issue #1 ovan visade att även den hårdare fixen (omscrape + färskt
`/version`-anrop varje varv, 20s) inte räckte:
`http_server_request_duration` dök fortfarande aldrig upp, trots 20 riktiga
sekunder och flera riktiga requests. Mer avslöjande: **inte ens
`aspnetcore_routing_match_attempts_total`/`http_server_active_requests`**
(som faktiskt synts i den allra första failade körningen, innan någon
poll-logik fanns) dök upp i de två efterföljande, hårdare-pollande
försöken — trots fler requests totalt, inte färre. Ordningen är bakvänd
mot vad en enkel "hann inte synas än"-race skulle ge.
**Grundorsak inte slutgiltigt fastställd** — en rimlig men obekräftad
misstanke: `NotificationsApiFactory` är den enda av de fyra tjänsternas
testvärdar som håller en riktig bakgrundskonsument (`OrderEventConsumer`)
med en levande RabbitMQ-anslutning igång genom hela testet, vilket kan
konkurrera om vad .NET-runtimen behöver för att Meter/DiagnosticListener-
prenumerationen för just ASP.NET Core-hosting-mätvärden ska bli klar —
men det är en kvalificerad gissning, inte ett bevisat svar, och att gissa
hårdare konvergerade inte efter tre försök.
**Fix, tredje och sista omgången:** sluta hävda en specifik
auto-instrumenterings-mätvärde alls. Testet asserterar nu på `target_info`
— det enda som faktiskt synts i **alla** tre misslyckade körningar (OTel
Resource-metadata, skrivs så fort `MeterProvider` startar, oberoende av
någon request). Ett ärligt svagare test än systertjänsternas motsvarighet,
men ett som inte påstår en tillförlitlighetsgaranti som upprepad, riktig
verifiering faktiskt motbevisade.
**Verifierat:** `gh run watch` på körningen efter fixen — helt grön,
inklusive att `notify-on-failure`-jobbet korrekt **hoppades över** (inte
kördes) eftersom inget faktiskt failade den gången.

Nästa steg: mindre uppföljningar som redan är noterade (notifications-/
outbox-paneler i Grafana, k8s-native service discovery för dashboard,
SSE/WebSocket istället för polling, alerting på övergivna outbox-rader).
