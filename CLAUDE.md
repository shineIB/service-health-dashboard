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
