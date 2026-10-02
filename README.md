# ResourceFlow

ResourceFlow is a booking system for an operator who runs several locations. Guests pick a date, a party size and a start time, then book a meeting room, a studio or any other space with limited capacity. Staff configure resources, manage bookings and the walk-in waitlist, and review an audit trail of admin actions.

## Tech stack

| Layer | Implementation |
| --- | --- |
| Guest and admin apps | TypeScript, React Native and Expo Router, built for web and native |
| API | C# on ASP.NET Core / .NET 10, split into Core, Infrastructure and API projects |
| Data | EF Core, SQLite and versioned migrations |
| Access control | Admin authentication, roles and scoped API keys |
| Business rules | Time zones, opening hours, capacity, combinable resources, duration rules, holds, waitlist |
| Operations and quality | Docker, Nginx, health checks, audit log, xUnit, Jest, Playwright |

## How it works

An admin sets each location's time zone, opening hours, default booking length and start-time interval, then groups resources into sections and gives each resource a capacity. A guest chooses a date and a party size and sees every available start time for that day, which can be narrowed to AM or PM in the location's local time. Choosing a time places a five-minute hold on a resource, and the guest confirms the booking with their contact details while the hold lasts. Staff handle bookings, cancellations, the waitlist and booking status from the admin app.

An empty database is seeded with two locations, Central Workspace and Harbour Studio, which contain meeting rooms, studios and workspaces. The optional demo data generator in `scripts/demo_data.py` adds richer scenarios: locations in several time zones, opening hours that run past midnight, guest caps per slot and combinable resources.

## Run locally

Requires .NET 10 and Node.js 24.

```sh
npm ci
npm ci --prefix resourceflow-frontend
npm run dev
```

The development API listens on http://localhost:5062. The frontend reads the API address from `EXPO_PUBLIC_API_URL` in `resourceflow-frontend/.env`, and `resourceflow-frontend/.env.template` is the starting point for that file. The development admin account is configured in `ResourceFlowApi/appsettings.Development.json`.

## Run with Docker

Create `.env` from `.env.example` and set the JWT key, the admin account and the allowed frontend origins. Then run:

```sh
docker compose -f docker-compose.release.yml up -d --build
```

The command builds the backend, frontend and proxy images from the current source. The app is served on port 80 by default, and `HOST_PORT` changes the port. The database, uploaded media and keys are kept in separate volumes.

## Checks

```sh
dotnet test ResourceFlowApi.Tests/ResourceFlowApi.Tests.csproj
TZ=UTC npm test --prefix resourceflow-frontend -- --runInBand
cd resourceflow-frontend
npx tsc --noEmit
npx expo export --platform web
```

The command-line client lives in `resourceflow-cli`. Running `npm ci` and `npm test` in that folder builds and tests it. It provides the `resourceflow` command and is not published to npm.

## Design scope

ResourceFlow targets one operator running a single application instance. Guest bookings, admin bookings and waitlist assignments share an in-process write lock, so the availability check and the insert of a new booking run one at a time. Holds are kept in process memory as well. Editing, restoring and extending a booking are not serialized by that lock, so running several instances would require shared holds and conflict control at the database level.

The data model has four main entities. A venue is a location, a section groups the resources of a venue, a resource is a bookable unit with a capacity, and a resource group combines resources for larger parties. A booking records its party size, and its length comes from the venue's default duration and the duration rules defined by party size. Each venue can publish one guide PDF. A booking moves through Booked, Arrived, InUse and Finished, or ends as NoShow.

Technical documentation is in `docs/` and release notes are in `CHANGELOG.md`. The admin app shows links to the CLI package, the API documentation and the repository only when `RESOURCEFLOW_CLI_PACKAGE_URL`, `RESOURCEFLOW_API_DOCS_URL` and `RESOURCEFLOW_REPOSITORY_URL` are set.

## License

Released under the MIT License. See [LICENSE](LICENSE).
