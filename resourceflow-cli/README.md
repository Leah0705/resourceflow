# resourceflow-cli

A command-line client for ResourceFlow. It uses an admin API key to manage locations, resources and bookings.

## Run from source

Requires Node.js 24. In this folder, run:

```sh
npm ci
npm run build
node dist/index.js --help
npm link
```

`npm link` puts the `resourceflow` command on your `PATH`. Connect it to an instance:

```sh
resourceflow auth login
resourceflow bookings list
```

The profile is saved to `~/.config/resourceflow/config.json`. The `RESOURCEFLOW_URL` and `RESOURCEFLOW_API_KEY` environment variables override it.

## Docker

The client is not published as an image, so the image is built from the current source:

```sh
docker build -t resourceflow-cli:local .
docker run --rm -e RESOURCEFLOW_URL=https://booking.example.com -e RESOURCEFLOW_API_KEY=YOUR_KEY resourceflow-cli:local bookings list
```

## Set up an API key and log in

1. In the admin UI, go to **Settings → API Keys** (Owner role required) and create a key. Give
   it only the scopes you actually need — see [Scopes](#scopes) below — and copy the secret
   (`rflow_<id>_<secret>`); it is shown exactly once.
2. Log the CLI in:

   ```bash
   resourceflow auth login
   Server URL: https://booking.example.com
   API key: ••••••••••••••••••••••••••••••••••
   Saved profile "default" for https://booking.example.com.
   ```

   **The key is never accepted as a command-line argument** — the same reason
   [`scripts/demo_data.py`](../scripts/demo_data.py) won't take a password on argv:
   anything passed as `argv` is visible to
   every other process on the machine via `ps`. `auth login` either prompts for it with the
   terminal's input hidden, or reads it from stdin when piped, e.g. in a script:

   ```bash
   printf '%s' "$RESOURCEFLOW_KEY" | resourceflow auth login --url https://booking.example.com
   ```

3. Confirm it works:

   ```bash
   resourceflow auth whoami
   ```

`auth login` and `auth whoami` also check the server's version against the CLI's own (major.minor
only — a patch difference is expected and silent) and print a one-line `warning:` to stderr on a
mismatch, e.g. `warning: server is 1.8.0, CLI is 1.9.0 — commands may not match the server's API`.
A self-hosted server can lag behind the CLI it's paired with, so this is advisory, not fatal — it
never fails the command it's attached to, and it never appears in `--json` output since it always
goes to stderr. An older server that predates this check (no `/api/version` endpoint) gets its own
"server version is unknown" warning instead of a silent skip.

## Profiles

Multiple servers/keys can be saved as named profiles in `~/.config/resourceflow/config.json`
(written at mode `0600`):

```bash
resourceflow --profile staging auth login
resourceflow --profile staging bookings list
```

Environment variables always win over a stored profile, field by field — useful for CI or a
one-off override without touching the saved config:

| Variable            | Overrides                                  |
| ------------------- | ------------------------------------------ |
| `RESOURCEFLOW_URL`     | the profile's server URL                   |
| `RESOURCEFLOW_API_KEY` | the profile's API key                      |
| `RESOURCEFLOW_PROFILE` | which profile is active (like `--profile`) |

```bash
RESOURCEFLOW_URL=https://booking.example.com RESOURCEFLOW_API_KEY="$CI_RESOURCEFLOW_KEY" \
  resourceflow bookings list --json
```

`resourceflow auth logout` removes the saved key for the active profile (the server URL stays, so
`auth login` next time only needs to ask for a new key).

## Output

Every command prints a human-readable resource by default. Pass `--json` (before or after the
subcommand) for machine-readable output:

```bash
resourceflow bookings list --json | jq '.[] | select(.partySize > 6)'
```

## Command groups

Run `resourceflow <group> --help` or `resourceflow <group> <command> --help` for full flag lists. One
example per group:

- **status** — the admin overview: booking totals, today's guests, paused locations and the
  schedule-conflict count. Server state, where `auth whoami` answers for the key itself.

  ```bash
  resourceflow status
  ```

- **auth** — `login`, `whoami`, `logout`.

  ```bash
  resourceflow auth whoami
  ```

- **bookings** — `list`, `get`, `create`, `update`, `extend`, `email`, `cancel`, `restore`,
  `purge`.

  ```bash
  resourceflow bookings list --location 1 --status upcoming
  resourceflow bookings extend 42 --minutes 30
  ```

  `purge` permanently deletes a booking (the GDPR purge path) and asks for confirmation; pass
  `--yes` to skip the prompt, which is required when stdin isn't a terminal (scripts/CI).

  `email` sends a one-off message to the guest on a booking. The body is multi-line, so it comes
  from a file or from stdin rather than a flag:

  ```bash
  resourceflow bookings email 42 --subject "Your resource tonight" --body-file note.html
  printf 'Running 20 minutes late — see you soon.' | resourceflow bookings email 42 --subject "Update"
  ```

  On a server with no SMTP settings this fails with the code `email.not_configured` rather than a
  wrapped transport error, so a script can tell a permanent setup problem from a transient one.
  `resourceflow email status` answers the same question without sending anything.

- **availability** — `check` (public endpoint, no key needed for this one call, but the CLI
  still sends one if configured).

  ```bash
  resourceflow availability check --location 1 --date 2026-09-01 --party-size 4
  ```

- **locations** — `list` (includes archived locations), `get`, `create`, `pause`, `unpause`,
  `extend`, `conflicts`, `archive`, `restore`, `delete`.

  ```bash
  resourceflow locations pause 1 --minutes 60
  resourceflow locations extend 1 --minutes 30   # every active booking: "we're running late"
  ```

  `conflicts` lists bookings stranded by a narrowed schedule — taken under opening hours, open
  days or a walk-in policy the location no longer runs. Editing a schedule deliberately leaves
  existing bookings alone, so this is how you find who needs calling.

  `delete` requires the location to already be archived (the server enforces this — see
  `VenueManagementService` — and the CLI surfaces that error with a pointer to run
  `locations archive` first) and shows a delete-preview (section/resource/booking counts) before
  asking for confirmation.

- **resources** — `list` (a location's sections and resources together, since a resource can't be
  created/edited/deleted without naming its section), `create`, `update`, `delete`. Use
  `resources list --location <id>` or `sections list --location <id>` to find a section's id, then
  pass it as `--section` to the other resource commands.

  ```bash
  resourceflow resources list --location 1
  resourceflow resources create --location 1 --section 2 --name "T5" --capacity 4
  ```

- **sections** — `list`, `create`, `update` (rename), `delete`. Reordering sections is left to
  the admin UI (no CLI command for it yet).

  ```bash
  resourceflow sections create --location 1 --name "Annex"
  resourceflow sections delete 2 --location 1
  ```

  `delete` removes the section's resources along with it; any upcoming bookings referencing the
  section or one of its resources keep their booking and only lose that reference (the server nulls
  the FK rather than cascading). The CLI previews
  how many bookings that affects before asking for confirmation.

- **brand** — `get`, `set` (via flags, or `--from-json <file>` / `--from-json -` for stdin — an
  empty string clears a field, an omitted one leaves it unchanged, matching `PATCH /api/brand`).

  ```bash
  resourceflow brand set --app-name "My Venue" --primary-color "#0a7ea4"
  ```

- **users** — `list`, `activate`, `deactivate`. Owner-only server-side; a key without the `users`
  scope (or one whose underlying account isn't an Owner) gets a clear 403.

  ```bash
  resourceflow users list
  resourceflow users deactivate 3
  ```

  There is deliberately no way to create an account, change a role or set a password from here.
  Those three hand out or move interactive privilege, and the server refuses them to any API-key
  session: a key that could mint a login would be a way to escalate out of its own scopes into the
  full admin UI. Use the admin UI, signed in as an Owner, for those. `list` needs `users:read`;
  `activate`/`deactivate` need `users:write`.

- **audit** — `list`, with the same filters as the admin activity trail (`actorUserId`, an
  `action` prefix, `targetType`, `location`, `from`/`to`, `page`/`pageSize`). Owner-only.

  ```bash
  resourceflow audit list --action booking --from 2026-08-01
  ```

- **email** — `status`, `failures`. Read-only: the SMTP credentials are unreachable with an API
  key by design, so there is no `email set` to pair with these.

  ```bash
  resourceflow email status
  resourceflow email failures
  ```

  Booking confirmations are best-effort server-side — a send failure is recorded and the booking
  goes through regardless — so an integration creating bookings by key would otherwise never
  learn its guests are receiving nothing. `status` separates the two causes with the same visible
  effect: `isConfigured` false means no SMTP settings at all, `sendBookingConfirmations` false
  means they are configured but switched off. `failures` blanks the recipient address for a key
  without `guests:read`, the same redaction the booking endpoints apply.

## Scopes

Every admin endpoint the CLI calls is gated by a `{resource}:{access}` scope on the key
(`bookings`, `locations`, `resources`, `brand`, `users`, `audit`, `guests`, `email` × `read`/`write`;
a `write` grant also satisfies the matching `read` requirement). `audit`, `guests` and `email` are
read-only — there is no write level to mint. `email` in particular reaches only whether mail is
configured and what has failed to send: a key that could rewrite the SMTP host and credentials
would be able to redirect every outgoing mail to a relay it controls. **Mint the narrowest key that does
the job** — a read-only reporting script should get `bookings:read` and nothing else, never a
key with every resource at `write`. A 403 from the CLI names exactly which scope is missing
(`This API key is missing the 'bookings:write' scope.`), so widening a key later is a quick,
deliberate step rather than a guess made up front. `auth whoami` shows a key's own scopes at any
time.

## Development

These run inside a clone of the [ResourceFlow repository](../README.md), not against the published package.

```bash
npm run typecheck   # tsc --noEmit
npm test            # builds, then runs the node:test suite in dist/
npm run build       # compile TypeScript to dist/
```

### Regenerating the transport types

`src/generated/api.d.ts` is generated from the backend's OpenAPI document
(`resourceflow-cli/openapi/v1.json`) via [`openapi-typescript`](https://openapi-ts.dev/). It types
the shape of requests/responses for the hand-written fetch transport (`src/transport.ts`) to lean
on — **generated operation names never dictate the CLI's command structure**; the command tree
above is designed by hand and mapped onto the real endpoints.

Both files are committed, and CI's `OpenAPI Drift` job
([`.github/workflows/ci.yml`](../.github/workflows/ci.yml)) fails if
either goes stale. To regenerate them after an API change:

```bash
# from the repo root
dotnet build
(cd tools/OpenApiExport && dotnet run --no-build -- ../../resourceflow-cli/openapi/v1.json)
(cd resourceflow-cli && npm run generate:types)
```

See [`tools/OpenApiExport/Program.cs`](../tools/OpenApiExport/Program.cs) for why the document is emitted by booting the API
in-process (`WebApplicationFactory`) rather than via MSBuild's build-time document generation.
