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
- Elever har ingen konti. Der gemmes kun et valgfrit kaldenavn og et tilfældigt enheds-token pr. session (i databasen kun tokenets hash).
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
  /src/assets               Indbygget asset-bibliotek (låse, breve, døre, baggrunde)
/backend
  /Eduscape.Api             Endpoints, SignalR-hubs
  /Eduscape.Domain          Entiteter og domænelogik
  /Eduscape.Infrastructure  EF Core, migrations, R2-klient
  /Eduscape.Tests           xUnit-tests
```

Kilde og licens for alle indbyggede grafikker står i [frontend/src/assets/ASSETS.md](frontend/src/assets/ASSETS.md).

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

Lærere opretter en konto med e-mail og adgangskode (mindst 10 tegn) på `/register` og logger ind på `/login`. Lærer-forsiden `/teacher` viser lærerens egne rum, de igangværende sessioner og de fælles skabeloner. Elever har aldrig konti.

- **Bearer tokens, ikke cookies.** Frontend (`*.workers.dev`) og API (`*.onrender.com`) ligger på forskellige sites. En auth-cookie ville derfor være en tredjeparts-cookie, som bl.a. Safari blokerer. Backenden bruger ASP.NET Core Identitys bearer tokens: access tokens gælder 30 minutter, refresh tokens 7 dage. Frontenden gemmer dem i `localStorage` og fornyer dem automatisk.
- **Log ud** tilbagekalder alle lærerens refresh tokens på alle enheder (godt på delte skole-PC'er).
- **Beskyttelse:** login og registrering er rate-limited pr. IP (20 pr. minut, `RateLimiting__Auth__PermitLimit`), og en konto låses i 5 minutter efter 5 forkerte adgangskoder. Alt under `/api/teacher` kræver login. En test fejler, hvis et nyt lærer-endpoint mangler beskyttelsen.
- **Data Protection-nøgler** (som beskytter tokens) gemmes i databasen (`DataProtectionKeys`), så lærere ikke logges ud, når Render genstarter efter dvale. Der skal ikke sættes nye environment variables.
- Endnu ikke: e-mailbekræftelse og nulstilling af adgangskode, da det kræver en mailtjeneste.

## Sessioner og PIN-koder

Læreren starter en session fra et af sine egne rum eller en skabelon, skriver klassen (fx "9.A"; findes klassen allerede, genbruges den), vælger **på hold** eller **hver for sig** og, kun på hold, antallet af hold (2–8). Sessionssiden viser PIN-koden med stor skrift og holdene med deres elever. Læreren kan låse sessionen for nye elever og lukke den.

Eleverne går ind på `/join`, skriver PIN-koden, vælger hold (springes over i individuel tilstand) og evt. et kaldenavn. Serveren udsteder et tilfældigt enheds-token, som browseren gemmer. Ved genindlæsning eller afbrudt forbindelse sender `/play` det til `POST /api/play/reconnect` og får hele holdets tilstand i ét kald.

- **PIN-koder** trækkes med `RandomNumberGenerator` og er unikke blandt sessioner, der ikke er lukket (unikt indeks `IX_Sessions_Pin_Active`). En PIN-kode virker, indtil læreren lukker sessionen, og højst 12 timer (`Sessions__Lifetime`, fx `04:00:00`). Udløbne sessioner lukkes, og deres PIN-koder frigives.
- **Rate limiting:** opslag af PIN og join er begrænset til 100 forsøg pr. IP pr. minut (`RateLimiting__Join__PermitLimit`). Grænsen er høj, fordi en hel skole deler én offentlig IP, og hver elev bruger to kald. Gætteri begrænses yderligere af, at læreren kan låse sessionen, og at PIN-koder udløber. Reconnect er ikke omfattet, da et 256-bit token ikke kan gættes.
- **Enheds-token:** kun dets SHA-256-hash gemmes i databasen. PIN og token sendes altid i request body, aldrig i URL'en, så de ikke havner i logs.
- **Låst session:** nye elever afvises, men elever, der allerede er med, kan spille videre. **Lukket session:** PIN-koden virker ikke længere, og elevernes enheder får besked om, at sessionen er lukket.

## Spilvisning og realtid

`/play` viser scenens baggrund med objekterne fra asset-biblioteket, placeret i procent af billedet. Objekter, der venter på en anden lås eller dør (`unlockedBy`), vises først, når den er løst. Lock åbner en pop-up med spørgsmålet, Letter viser lærerens tekst, Door åbner den næste scene for hele holdet, og slutdøren afslutter rummet, når alle låse er løst. Eleven kan frit gå tilbage til scener, holdet har låst op.

- **Svar valideres kun på serveren.** Klienten får spørgsmål og svarmuligheder, men aldrig det rigtige svar eller tolerancen. Et hint sendes først, når holdet har svaret forkert 3 gange.
- **Handlinger går via REST** med enheds-tokenet i body: `POST /api/play/answer`, `/api/play/doors/open` og `/api/play/exit`. Hvert kald låser holdets række i databasen (`SELECT … FOR UPDATE`), så samtidige svar på samme lås håndteres ét ad gangen. Låsen bliver løst én gang, og den anden elev får `AlreadySolved`. Der kommer ingen fejl og ingen dobbelt registrering.
- **Push via SignalR** (`/hubs/play`): hvert hold er en gruppe. Efter en gemt ændring får gruppen `LockSolved(lockId)`, `HintRevealed(lockId, hint)`, `DoorOpened(doorId)` eller `TeamEscaped()`. Hubben har kun metoden `Subscribe(deviceToken)`, og tokenet sendes som besked, ikke i URL'en. Render kører én instans, så der er ingen backplane.
- **Reconnect:** klienten forbinder til hubben, kalder `Subscribe` og henter *derefter* hele holdets tilstand med `POST /api/play/reconnect`. Så går ingen events tabt imellem. SignalR genforbinder automatisk, og imens viser rummet "Forbindelsen blev afbrudt. Forbinder igen...". Efter hver genforbindelse gentages `Subscribe` og tilstandskaldet.

## Editoren

Læreren bygger rum på `/teacher/rooms/:id/edit`. Et nyt rum kræver kun titel, fag og klassetrin (`/teacher/rooms/new`). En skabelon redigeres aldrig direkte: **Brug skabelonen** laver en kopi (`POST /api/teacher/templates/{id}/copy`), som åbnes i samme editor.

- **Træk og slip:** klistermærker fra asset-biblioteket trækkes ind på scenen, eller klikkes for at lande midt på. Objekter flyttes med musen, fingeren eller piletasterne (Shift giver større skridt). Positioner gemmes i procent af billedet.
- **Panelet** viser kun det, der er relevant for objektet: spørgsmål, svartype og svar for en lås (tolerance kun ved talsvar, svarmuligheder kun ved multiple choice, hint kun når læreren beder om det), tekst for et brev og målscene for en dør. `unlockedBy` er én dropdown: "Synlig fra start" eller "Vises når … er løst". Den tilbyder kun låse og døre, der ikke allerede venter på objektet, så der ikke kan opstå ringe.
- **Fortryd/gentag** (også Ctrl+Z / Ctrl+Y) går gennem hele versioner af rummet. Et træk eller én omgang skrivning i et felt er ét skridt.
- **Autosave:** editoren sender hele rummet med `PUT /api/teacher/rooms/{id}/editor` kort efter hver ændring. Scener og objekter får id'er i klienten (UUID v7), så fortryd kan genskabe dem præcis. Hver gemning bærer rummets `revision`; en ældre revision afvises med `ROOM_CHANGED`, så to faner ikke overskriver hinanden.
- **Kladder er tilladt:** en lås uden spørgsmål eller en dør uden mål kan gemmes. Serveren returnerer problemerne (`EscapeRoom.Validate()`), og editoren viser dem med et link til objektet. Et rum med problemer kan ikke startes eller prøves (`ROOM_INVALID`).
- **Fortællertekst:** hver scene kan have en tekst (scenepanelet), som vises stort oven på billedet, når eleven kommer ind i scenen. Eleven trykker "Videre" og kan læse den igen. Den bruges til at fortælle historien og sætte stemningen, fx i en indledningsscene.
- **Kortet** viser scenerne og dørene imellem dem fra startscenen og frem. Scener, ingen dør fører til, er stiplede.
- **Prøv rummet** (`POST /api/teacher/rooms/{id}/preview`) starter en skjult preview-session i Individual-mode uden klasse, melder lærerens enhed ind og låser den straks. Elevvisningen åbner i en ny fane og spilles på serveren som enhver anden session. Preview-sessioner vises ikke på dashboardet eller i historikken og udløber efter 2 timer (`Sessions__PreviewLifetime`).

### Egne baggrundsbilleder (R2)

Bucketten er privat. Browseren uploader direkte til et `incoming/<lærer>/<upload>`-område via en presigned PUT-URL (`POST /api/teacher/uploads`). Når upload er færdig (`POST /api/teacher/uploads/{id}/complete`), henter serveren filen, tjekker MIME-type mod filsignaturen, afviser filer over 10 MB eller 40 megapixel, vender billedet efter EXIF-orienteringen, skalerer ned til højst 2400 px og genindkoder som WebP. Genindkodningen fjerner al metadata (EXIF, GPS, kamera). Resultatet gemmes som `uploads/<tilfældigt navn>.webp`, og originalen slettes. Brugerens filnavn bruges aldrig. Billeder vises via presigned GET-URL'er, der udløber efter 12 timer.

Uden R2-konfiguration er upload slået fra, og editoren viser kun de indbyggede baggrunde.

Opsætning:
1. Opret en R2-bucket med **jurisdiction EU**.
2. Opret et R2 API-token med *Object Read & Write* på bucketten.
3. Tilføj en CORS-regel på bucketten, der tillader `PUT` og `GET` fra frontendens origin med headeren `Content-Type`.
4. Tilføj en lifecycle-regel, der sletter objekter med præfikset `incoming/` efter 1 dag (uploads, der aldrig blev færdige).
5. Sæt environment variables på Render (lokalt med `dotnet user-secrets set "R2:AccountId" …`):

| Variabel | Værdi |
|---|---|
| `R2__AccountId` | Cloudflare-kontoens id |
| `R2__AccessKeyId` / `R2__SecretAccessKey` | API-tokenets nøgler |
| `R2__Bucket` | Bucketens navn |

6. Sæt build-variablen `VITE_IMAGE_STORAGE_URL` = `https://<AccountId>.eu.r2.cloudflarestorage.com` på frontenden, så CSP'en tillader upload til og visning fra bucketten.

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

#### Sikkerhedsheadere

Buildet skriver en `dist/_headers`-fil med Content-Security-Policy, `X-Content-Type-Options`, `Referrer-Policy` m.fl., som Cloudflare sender med på alle svar. Filen genereres af [frontend/security/securityHeaders.ts](frontend/security/securityHeaders.ts), fordi `connect-src` skal indeholde API-adressen fra `VITE_API_URL` (både `https://` og `wss://` til SignalR). Ret headerne dér, ikke i `dist`. R2-bucketten kommer med i `img-src` og `connect-src` via build-variablen `VITE_IMAGE_STORAGE_URL` (se [Egne baggrundsbilleder](#egne-baggrundsbilleder-r2)).

Tjek lokalt med headerne slået til (backend skal køre; `http://localhost:8787` er tilladt i CORS i Development):

```bash
cd frontend
npm run build
npx wrangler dev   # http://localhost:8787
```

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