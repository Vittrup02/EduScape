# eduscape

Læringsplatform hvor undervisere forvandler deres pensum til interaktive point-and-click escape rooms på under 5 minutter.

Læreren placerer kodelåse, breve med ledetråde og døre oven på et billede. Eleverne samarbejder om at løse faglige gåder for at "bryde ud". Eleverne logger aldrig ind – de joiner med en sekscifret PIN-kode.

> **Status:** Under udvikling (MVP).

## Sådan virker det

**For læreren**
- Byg et rum i en træk-og-slip-editor: vælg et baggrundsbillede og placer objekter på det.
- **Lås** – åbner et spørgsmål; når det besvares korrekt, låses den op.
- **Brev** – viser en ledetråd, fx "Læs side 42 i bogen".
- **Dør** – fører videre til en ny scene med eget billede og egne gåder.
- **Slutdør** – åbner når alt er løst, og eleverne er sluppet ud.
- Objekter kan afhænge af hinanden ("vises når lås 1 er løst"), så rummet får et forløb.
- Start ud fra en template eller byg fra bunden.
- Følg alle hold live på et dashboard.

**For eleverne**
- Tast PIN-koden, vælg hold og evt. et kaldenavn.
- Løs gåderne sammen – holdets fremskridt deles i realtid, men hver elev kan bevæge sig frit mellem de scener, holdet har låst op.
- Efter 3 forkerte forsøg på en lås vises et hint.
- Læreren kan også vælge individuel tilstand, hvor hver elev løser rummet alene.

## Privatliv

eduscape er designet til dataminimering:
- Elever har ingen konti. Der gemmes kun et kaldenavn og et tilfældigt enheds-token pr. session.
- Elevdata slettes automatisk, når sessionen er afsluttet.
- Al hosting og lagring ligger i EU.

## Tech stack

| Del | Teknologi | Hosting |
|---|---|---|
| Frontend | React + TypeScript (Vite) | Cloudflare Workers (Static Assets) |
| Backend | C# .NET 10 Web API + SignalR | Render (Frankfurt) |
| Database | PostgreSQL via EF Core | Neon (eu-central-1) |
| Billeder | Cloudflare R2 | R2 (EU jurisdiction) |

## Projektstruktur

```
/frontend                   React/TS-app
  /src/editor               Lærerens rum-editor
  /src/play                 Elevernes spilvisning
  /src/api                  API-klient + SignalR-forbindelse
/backend
  /Eduscape.Api             Endpoints, SignalR-hubs
  /Eduscape.Domain          Entiteter og domænelogik
  /Eduscape.Infrastructure  EF Core, migrations, R2-klient
  /Eduscape.Tests           xUnit-tests
```

## Kom i gang

**Krav:** Node.js (LTS), .NET 10 SDK og en PostgreSQL-database (lokal eller Neon).

```bash
# Backend
cd backend
dotnet tool restore   # installerer dotnet-ef fra dotnet-tools.json
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=eduscape;Username=postgres;Password=<kode>" --project Eduscape.Api
dotnet ef database update --project Eduscape.Infrastructure --startup-project Eduscape.Api
dotnet run --project Eduscape.Api

# Frontend (ny terminal)
cd frontend
npm install
npm run dev
```

Connection strings hører hjemme i user-secrets (lokalt) eller environment variables (`ConnectionStrings__Default`) – aldrig i repoet.

I Development seeder `dotnet ef database update` et demo-escape room ("Demo: Flugten fra matematiklokalet") med to scener, tre låse, et brev, en dør og en slutdør, så spillet kan testes uden editor.

Kør `dotnet ef database update` igen, når du har hentet ændringer med nye migrationer.

## Lærerkonti

Lærere opretter en konto med e-mail og adgangskode (mindst 10 tegn) på `/register` og logger ind på `/login`. Lærer-forsiden `/teacher` viser kun lærerens egne rum. Elever har aldrig konti.

- **Bearer tokens, ikke cookies.** Frontend (`*.workers.dev`) og API (`*.onrender.com`) ligger på forskellige sites. En auth-cookie ville derfor være en tredjeparts-cookie, som bl.a. Safari blokerer. Backenden bruger ASP.NET Core Identitys bearer tokens: access tokens gælder 30 minutter, refresh tokens 7 dage. Frontenden gemmer dem i `localStorage` og fornyer dem automatisk.
- **Log ud** tilbagekalder alle lærerens refresh tokens på alle enheder (godt på delte skole-PC'er).
- **Beskyttelse:** login og registrering er rate-limited pr. IP (20 pr. minut, `RateLimiting__Auth__PermitLimit`), og en konto låses i 5 minutter efter 5 forkerte adgangskoder. Alt under `/api/teacher` kræver login. En test fejler, hvis et nyt lærer-endpoint mangler beskyttelsen.
- **Data Protection-nøgler** (som beskytter tokens) gemmes i databasen (`DataProtectionKeys`), så lærere ikke logges ud, når Render genstarter efter dvale. Der skal ikke sættes nye environment variables.
- Endnu ikke: e-mailbekræftelse og nulstilling af adgangskode, da det kræver en mailtjeneste.

## Tests

```bash
cd backend && dotnet test     # Backend (xUnit)
cd frontend && npm run test   # Frontend (Vitest)
cd frontend && npm run test:e2e   # End-to-end (Playwright)
```

Backendens database-tests opretter og sletter deres egen midlertidige database på den server, `ConnectionStrings:Default` peger på. Uden connection string springes de over lokalt; i CI kører de altid mod en PostgreSQL-service.

GitHub Actions bygger projektet og kører alle tests på hver pull request. CI bygger også backendens Docker-image og tjekker, at containeren svarer på `/health`, så en ødelagt Dockerfile fanges før deploy.

## Deploy (Render + Neon + Cloudflare)

Backenden kører som Docker-container på Render. Imaget bygges fra [backend/Dockerfile](backend/Dockerfile).

**Render (Web Service)**
- Runtime: Docker · Region: Frankfurt · Root Directory: `backend` · Dockerfile Path: `./Dockerfile`
- Health Check Path: `/health`

**Environment variables på Render**

| Variabel | Værdi |
|---|---|
| `ConnectionStrings__Default` | Neons **pooled** endpoint (værtsnavn med `-pooler`) i Npgsql-format: `Host=<host>;Database=<db>;Username=<bruger>;Password=<kode>;SSL Mode=Require`. Neons `postgresql://`-URL virker ikke direkte i Npgsql. |
| `Cors__AllowedOrigins` | Frontendens origin(s), kommasepareret, fx `https://eduscape.<konto>.workers.dev`. Uden afsluttende `/`. |
| `PORT` | Sættes af Render selv. Appen lytter på `0.0.0.0:$PORT` (standard i imaget: 8080). |

`ASPNETCORE_ENVIRONMENT` skal ikke sættes. Containeren kører som Production, så demo-rummet ikke seedes og user-secrets ikke læses.

### Frontend (Cloudflare Workers)

Frontenden serveres som statiske filer fra en Cloudflare Worker (Workers Static Assets) med SPA-fallback. Konfigurationen ligger i [frontend/wrangler.jsonc](frontend/wrangler.jsonc).

Opret i Cloudflare-dashboardet: **Workers & Pages → Create → Import a repository** → `EduScape`.

| Felt | Værdi |
|---|---|
| Project name | `eduscape` (skal matche `name` i `wrangler.jsonc`, ellers fejler buildet) |
| Build command | `npm run build` |
| Deploy command | `npx wrangler deploy` (standard) |
| Path / Root directory | `frontend` |
| Git branch | `development` (indtil videre) |
| Build variable | `VITE_API_URL` = backendens URL, fx `https://eduscape-api-dev.onrender.com` (uden afsluttende `/`) |

`VITE_API_URL` skal være en **build-variabel** (Settings → Build → Variables and secrets), fordi Vite bager den ind i bundlen under build. Runtime-variabler (Settings → Variables & Secrets) når aldrig frem til frontenden. Ændres den, skal der bygges igen.

Sæt derefter Workerens URL i `Cors__AllowedOrigins` på Render.

### Migrationer mod Neon

Migrationer køres **aldrig automatisk** ved opstart. Kør dem manuelt fra din egen maskine, når en release indeholder nye migrationer. Kør dem **før** deploy, hvis migrationen er bagudkompatibel med den kørende version.

Brug Neons **direkte** endpoint (uden `-pooler`) til migrationer. Sæt connection string som environment variable i stedet for `--connection`, så koden ikke havner i shell-historikken (PowerShell, fra `backend/`):

```powershell
# Vigtigt: dotnet ef kører som Development som standard og ville så seede demo-rummet i produktion
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ConnectionStrings__Default = "Host=<direkte host>;Database=<db>;Username=<bruger>;Password=<kode>;SSL Mode=Require"

dotnet tool restore
dotnet ef migrations list --project Eduscape.Infrastructure --startup-project Eduscape.Api   # se hvad der mangler
dotnet ef database update --project Eduscape.Infrastructure --startup-project Eduscape.Api

Remove-Item Env:ConnectionStrings__Default, Env:ASPNETCORE_ENVIRONMENT
```

Vil du gennemse SQL'en først, kan du generere et idempotent script og køre det i Neons SQL Editor:

```bash
dotnet ef migrations script --idempotent --output migrate.sql --project Eduscape.Infrastructure --startup-project Eduscape.Api
```