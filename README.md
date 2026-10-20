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
- **Data Protection-nøgler** (som beskytter tokens) gemmes i databasen (`DataProtectionKeys`), så lærere ikke logges ud, når Render genstarter efter dvale. Uden for Development er nøglerne krypteret med et certifikat, der kun findes på Render (se [Kryptering af Data Protection-nøgler](#kryptering-af-data-protection-nøgler)).
- Endnu ikke: e-mailbekræftelse og nulstilling af adgangskode, da det kræver en mailtjeneste.

## Sessioner og PIN-koder

Læreren starter en session fra et af sine egne rum eller en skabelon, skriver klassen (fx "9.A"; findes klassen allerede, genbruges den), vælger **på hold** eller **hver for sig** og, kun på hold, antallet af hold (2–8). Læreren lander på sessionens live-dashboard (se nedenfor) og kan låse sessionen for nye elever og lukke den.

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
- **Scene:** elevens enhed melder sin scene med `POST /api/play/scene` ved hvert sceneskift, hver genforbindelse og hvert holdskift. Kun scener, holdet har låst op, accepteres (`SCENE_LOCKED`). Det bruges kun til lærerens dashboard.

## Lærerens live-dashboard

`/teacher/sessions/:id` viser PIN-koden stort (knappen **Vis PIN-koden på tavlen** viser den i fuld skærm til projektoren) og ét kort pr. hold (pr. elev i individuel tilstand): løste låse, hvilken lås holdet arbejder på, og hvor hver elev er. Har holdet ikke gjort fremskridt på sin aktuelle lås i 5 minutter, står der "Har siddet fast på …" med antal forkerte svar. Tid og fremskridt vises kun for læreren, aldrig for eleverne. Siden sender `GET /health` som wake-up-kald, så snart den åbnes.

- **Aktuel lås** er den uløste lås, holdet senest har svaret forkert på. "Siddet fast siden" er holdets første forsøg på låsen eller holdets seneste fremskridt (løst lås, åbnet dør), hvis det kom senere. Tidspunkterne gemmes på `TeamLockProgress` (`FirstAttemptAt`, `LastAttemptAt`).
- **Realtid:** dashboardet forbinder til `/hubs/teacher` med lærerens access token (i query-strengen for WebSockets, kun accepteret på denne hub) og kalder `Watch(sessionId)`, som kun virker for lærerens egne sessioner. Ved hver ændring (join, svar, dør, scene, flytning, lås/luk) sendes `DashboardChanged()`, og dashboardet henter hele tilstanden med `GET /api/teacher/sessions/{id}/dashboard`. Svar og hints sendes aldrig. Mens forbindelsen genoprettes, vises "Forbinder til live-overblikket...".
- **Flyt elev** (kun på hold): `POST /api/teacher/sessions/{id}/participants/{participantId}/move`. Elevens enheder får `TeamChanged()` på deres egen SignalR-gruppe, starter en ny forbindelse og henter det nye holds tilstand. Har det nye hold ikke låst elevens scene op, går eleven til startscenen.
- **Sletning af elevdata:** en baggrundsjob (hvert 10. minut, `Sessions__CleanupInterval`) lukker udløbne sessioner og sletter elevdata (kaldenavne, enheds-tokens, scener) 1 time efter, at en session er lukket (`Sessions__StudentDataRetention`). Holdenes resultater (løste låse, åbnede døre, tidspunkt for udbrud) og antallet af elever (`Teams.ParticipantCount`) bevares uden navne til lærerens historik. På Render free tier kører jobbet, når instansen er vågen.

## Editoren

Læreren bygger rum på `/teacher/rooms/:id/edit`. Et nyt rum kræver kun titel, fag og klassetrin (`/teacher/rooms/new`). En skabelon redigeres aldrig direkte: **Brug skabelonen** laver en kopi (`POST /api/teacher/templates/{id}/copy`), som åbnes i samme editor.

- **Træk og slip:** klistermærker fra asset-biblioteket trækkes ind på scenen, eller klikkes for at lande midt på. Objekter flyttes med musen, fingeren eller piletasterne (Shift giver større skridt). Positioner gemmes i procent af billedet.
- **Panelet** viser kun det, der er relevant for objektet: spørgsmål, svartype og svar for en lås (tolerance kun ved talsvar, svarmuligheder kun ved multiple choice, hint kun når læreren beder om det), tekst for et brev og målscene for en dør. `unlockedBy` er én dropdown: "Synlig fra start" eller "Vises når … er løst". Den tilbyder kun låse og døre, der ikke allerede venter på objektet, så der ikke kan opstå ringe.
- **Fortryd/gentag** (også Ctrl+Z / Ctrl+Y) går gennem hele versioner af rummet. Et træk eller én omgang skrivning i et felt er ét skridt.
- **Autosave:** editoren sender hele rummet med `PUT /api/teacher/rooms/{id}/editor` kort efter hver ændring. Scener og objekter får id'er i klienten (UUID v7), så fortryd kan genskabe dem præcis. Hver gemning bærer rummets `revision`; en ældre revision afvises med `ROOM_CHANGED`, så to faner ikke overskriver hinanden.
- **Kladder er tilladt:** en lås uden spørgsmål eller en dør uden mål kan gemmes. Serveren returnerer problemerne (`EscapeRoom.Validate()`), og editoren viser dem med et link til objektet. Et rum med problemer kan ikke startes eller prøves (`ROOM_INVALID`).
- **Tekstfelter** trækkes ind som de andre objekter og viser lærerens tekst direkte på billedet, fx "Vagten er gået til frokost". De klikkes ikke, men kan vente på en lås via `unlockedBy`.
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

GitHub Actions bygger projektet og kører alle tests på hver pull request. CI bygger også backendens Docker-image og tjekker, at containeren svarer på `/health`, så en ødelagt Dockerfile fanges før deploy. Ved push til `development` og `main` kører CI derefter migrationerne mod Neon (se [Migrationer mod Neon](#migrationer-mod-neon)).

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
| `DataProtection__CertificatePath` | `/etc/secrets/dataprotection.pem` (en secret file, se [Kryptering af Data Protection-nøgler](#kryptering-af-data-protection-nøgler)). Uden den starter appen ikke. |

`ASPNETCORE_ENVIRONMENT` skal ikke sættes. Containeren kører som Production, så demo-rummet ikke seedes og user-secrets ikke læses.

### Kryptering af Data Protection-nøgler

Nøglerne, der beskytter lærernes tokens, ligger i tabellen `DataProtectionKeys`. Uden for Development krypteres de med et RSA-certifikat, der kun findes som secret file på Render. En kopi af databasen (backup, Neon-branch, lækket connection string) er så ikke nok til at forfalske en lærers login.

- **Lærere forbliver logget ind ved genstart.** Nøglerne ligger stadig i databasen. Ved opstart læser appen dem og dekrypterer dem med certifikatet fra secret filen.
- **Mangler certifikatet, starter appen ikke.** Render-loggen viser `OptionsValidationException: The Data Protection keys in the database must be encrypted outside Development, …`. Er filen der, men forkert (ingen privat nøgle, ikke RSA, ikke læsbar), siger fejlen hvad der er galt.
- **Lokalt kræves intet certifikat.** I Development gemmes nøglerne ukrypteret i den lokale database, og Data Protection logger en advarsel om det. Vil du prøve med certifikat lokalt: `dotnet user-secrets set "DataProtection:CertificatePath" "<sti>\dataprotection.pem" --project Eduscape.Api`.
- **Ældre, ukrypterede nøgler** (fra før certifikatet blev sat op) krypteres automatisk i baggrunden ved første opstart med certifikatet. Det er de samme nøgler, så ingen logges ud. Loggen skriver `Encrypted N Data Protection keys that were stored unencrypted`.

**1. Lav certifikatet** (Git Bash på Windows, macOS eller Linux):

```bash
MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
  -subj "/CN=eduscape-dataprotection" -keyout key.pem -out certificate.pem
cat certificate.pem key.pem > dataprotection.pem
```

- `MSYS_NO_PATHCONV=1` forhindrer Git Bash i at lave `/CN=…` om til en Windows-sti. Andre steder gør den ingenting.
- Certifikatet skal være RSA, og den private nøgle må ikke have adgangskode (`-nodes`). Det er secret filen på Render, der beskytter den.
- Data Protection tjekker ikke certifikatets udløbsdato. 10 år er fint, og du roterer, når du selv vil.
- Lav ét certifikat pr. miljø (Render-servicen for `development` og for `main`). Del dem ikke.
- Gem `dataprotection.pem` i en password manager, og slet de tre filer fra disken bagefter. `*.pem` er i `.gitignore`.

**2. Installer det på Render**

1. Servicen → **Environment → Secret Files → Add Secret File**. Filename: `dataprotection.pem`. Contents: hele indholdet af filen (både `BEGIN CERTIFICATE` og `BEGIN PRIVATE KEY`).
2. Samme side, **Environment Variables**: `DataProtection__CertificatePath` = `/etc/secrets/dataprotection.pem`.
3. **Save Changes**. Render deployer igen. Tjek i loggen, at appen starter uden fejl.

Render kopierer også secret files ind i Docker-buildets rodmappe. De kommer ikke med i imaget: Dockerfilen kopierer kun projektmapperne, og `.dockerignore` udelader `*.pem`. Når containeren kører, kan filen i `/etc/secrets` kun læses af gruppe 1000. Dockerfilen tilføjer derfor appens bruger til gruppe 1000. CI tjekker begge dele: at imaget nægter at starte uden certifikat, og at det starter med en fil monteret som på Render.

**3. Rotér certifikatet**

Data Protection krypterer aldrig en gemt nøgle om. En nøgle forbliver krypteret med det certifikat, der var aktivt, da den blev lavet. Det gamle certifikat skal derfor blive, indtil dets nøgler ikke bruges længere.

1. Lav et nyt certifikat (trin 1).
2. **Secret Files:** tilføj `dataprotection-previous.pem` med det *nuværende* indhold af `dataprotection.pem`. Erstat derefter indholdet af `dataprotection.pem` med det nye certifikat.
3. **Environment Variables:** `DataProtection__PreviousCertificatePaths` = `/etc/secrets/dataprotection-previous.pem` (flere filer kommasepareret).
4. **Save Changes.** Nye nøgler krypteres med det nye certifikat, de gamle kan stadig læses, og ingen logges ud.
5. Efter **97 dage** (en nøgle er aktiv i højst 90 dage, og refresh tokens gælder 7 dage): slet `dataprotection-previous.pem` og `DataProtection__PreviousCertificatePaths`. Slet evt. de udløbne nøgler i Neons SQL Editor:

   ```sql
   DELETE FROM "DataProtectionKeys"
   WHERE (xpath('/key/expirationDate/text()', "Xml"::xml))[1]::text::timestamptz < now() - interval '7 days';
   ```

**Hvis certifikatet er tabt eller lækket**

- **Tabt:** lav et nyt, og installer det uden `DataProtection__PreviousCertificatePaths`. Appen kan ikke læse de gamle nøgler og laver en ny. Alle lærere skal logge ind igen én gang. Ingen data går tabt, for nøglerne beskytter kun login-tokens.
- **Lækket:** slet først alle nøgler (`DELETE FROM "DataProtectionKeys";` i Neons SQL Editor). Installer derefter et nyt certifikat uden det gamle som previous. Deployet genstarter appen, som laver en ny nøgle. Alle lærere skal logge ind igen.

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

Migrationer køres **aldrig** ved opstart af appen. GitHub Actions kører dem automatisk i jobbet `migrate` i [.github/workflows/ci.yml](.github/workflows/ci.yml):

- **Kun ved push** til `development` og `main`, aldrig på pull requests, og først når build, Docker-tjek, unit tests og E2E-tests er grønne.
- Jobbet bygger et migration bundle (`dotnet ef migrations bundle`) og kører det mod databasen. Bundlet anvender kun de migrationer, der mangler. Er databasen opdateret, sker der ingenting.
- Push til `development` bruger GitHub-environmentet **`development`** (Neons dev-database), push til `main` bruger **`production`**.
- Bundlet kører som `Production`, så demo-rummet aldrig seedes i Neon.
- Fejler en migration, eller mangler secret'en, fejler jobbet med en rød fejlmeddelelse øverst i workflow-kørslen. Hver migration kører i sin egen transaktion, så en fejlet migration rulles tilbage, men de foregående i samme kørsel bliver stående. Loggen viser, hvilke der nåede at blive anvendt.
- Pushes til samme branch afbryder ikke hinanden, de stilles i kø, så en migration aldrig stoppes halvvejs.

**Opsætning af secrets (én gang)**

1. GitHub → repoet → **Settings → Environments → New environment**. Opret `development` og `production` (navnene skal staves præcis sådan).
2. Under hvert environment: **Deployment branches and tags → Selected branches and tags**. Tillad kun `development` i `development` og kun `main` i `production`, så ingen anden branch kan læse secret'en.
3. Under hvert environment: **Environment secrets → Add secret** med navnet `NEON_CONNECTION_STRING`. Værdien er den pågældende Neon-databases **direkte** endpoint (værtsnavn *uden* `-pooler`; migrationer og Neons connection pooler passer ikke sammen) i Npgsql-format:
   `Host=<direkte host>;Database=<db>;Username=<bruger>;Password=<kode>;SSL Mode=Require`
   Neons `postgresql://`-URL virker ikke direkte.

Connection string'en logges aldrig. Den findes kun i det trin, der kører bundlet, og gives videre som environment variable (`ConnectionStrings__Default`), ikke som kommandolinje-argument. GitHub maskerer desuden secret'en, hvis den skulle dukke op i loggen.

**Rækkefølge med Render.** Sæt Render-servicens **Auto-Deploy** til **"After CI Checks Pass"** (Settings → Build & Deploy). Så deployer Render først den nye version, når CI inklusive `migrate` er grøn. Rækkefølgen bliver: migration → ny version.

**Migrationer skal være bagudkompatible.** Lige efter migrationen kører Render stadig den gamle version et øjeblik, indtil den nye er bygget og startet. Fejler deployet, bliver den gamle version kørende på det nye skema. Den gamle kode skal derfor kunne køre mod det nye skema:

- Nye kolonner skal være nullable eller have en default-værdi.
- Nye tabeller og indekser er fine.
- Omdøb eller slet aldrig en kolonne eller tabel i samme release som den kode, der holder op med at bruge den. Gør det i to trin (expand/contract): 1) tilføj det nye, og deploy kode, der bruger det nye. 2) Fjern det gamle i en senere release, når ingen kørende version bruger det.
- Gør ikke en eksisterende kolonne `NOT NULL`, før al kode skriver en værdi i den.

#### Manuelt (kun i nødstilfælde)

Skal en migration køres uden om CI, så kør den fra din egen maskine mod Neons **direkte** endpoint (uden `-pooler`). Sæt connection string som environment variable i stedet for `--connection`, så koden ikke havner i shell-historikken (PowerShell, fra `backend/`):

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