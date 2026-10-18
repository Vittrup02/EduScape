# EduScape# eduscape

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
| Frontend | React + TypeScript (Vite) | Cloudflare Pages |
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
dotnet user-secrets set "ConnectionStrings:Default" "<din-connection-string>" --project Eduscape.Api
dotnet ef database update --project Eduscape.Infrastructure --startup-project Eduscape.Api
dotnet run --project Eduscape.Api

# Frontend (ny terminal)
cd frontend
npm install
npm run dev
```

## Tests

```bash
cd backend && dotnet test     # Backend (xUnit)
cd frontend && npm run test   # Frontend (Vitest)
cd frontend && npm run test:e2e   # End-to-end (Playwright)
```

GitHub Actions bygger projektet og kører alle tests på hver pull request.