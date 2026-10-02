# Feature reference

How ResourceFlow's booking features behave, and the API fields, error codes and settings behind
them. [`CHANGELOG.md`](../CHANGELOG.md) says when each one arrived; this page says how it
works. For calling the API see [`http-api.md`](http-api.md); for the native app, reminders
and Wallet passes see [`native-app.md`](native-app.md).

Error codes below come back in the `code` field of a rejected request, alongside a message in
the caller's language.

## Opening hours and schedules

- **Per-day hours.** Each day of the week can have its own open and close times. A day's
  hours that run past midnight belong to the day they opened, so Saturday's 18:00–02:00 slots are
  Saturday's.
- **Slot interval.** Bookings start on a 15, 30 or 60 minute grid (default 30), separate from
  how long a booking lasts.
- **Schedule conflicts.** Narrowing hours, dropping an open day or changing the timezone never
  touches existing bookings. The Locations page lists upcoming bookings that no longer fit, and
  the dashboard shows the total. A walk-in-only day does not count as a conflict, because the
  location is still open. `resourceflow locations conflicts` lists them from the CLI.
- **Timezone changes.** Bookings are stored in UTC, so the guest is still expected at the same
  real moment, and the local time in their confirmation email is now wrong. Nothing is
  re-sent; the admin shows a warning when the location has upcoming bookings.

## Booking length (duration rules)

A location has a default booking length. Duration rules give larger parties longer: each rule
is a party size and a length, and a party gets the rule with the largest party size at or
below its own. A party smaller than every rule gets the default.

- API: `durationRules` on the venue, e.g. `[{ "minPartySize": 3, "minutes": 90 }]`. On update,
  `null` leaves the rules alone and `[]` clears them.
- Availability, holds, auto-assign, admin bookings, waitlist assignment and wait estimates all use
  the party's own length. Because of that, a hold request includes the party size.
- An existing booking keeps the length it was booked with.

## Booking status

Status is recorded alongside cancellation, not instead of it. A booking moves forward
through **Booked → Arrived → InUse → Finished**, or **Booked → No-show**.

- No-show is only offered once the slot has started.
- The last change can be undone for 5 minutes.
- Finishing early or marking a no-show ends the slot at that moment, so the resource is free
  for availability, holds and the waitlist. Undoing restores the original end time.
- Parties assigned from the waitlist start as InUse.
- In the bookings list, an unmarked booking reads **Due** once its slot starts and
  **Unmarked** once it ends.
- A guest's no-show count is worked out from their bookings under the same email, across every
  location. It is never stored, so the GDPR purge removes it with the bookings.
- Guests never see status, and nothing is charged or blocked for a no-show.

API: `POST /api/admin/bookings/{id}/status` with `{ "status": "InUse" }` (`bookings:write`,
audited as `booking.status`). Admin booking reads carry `status`, `nextStatuses`,
`undoStatus` and `previousNoShows`; the last is withheld from keys without `guests:read`.
`status=noshow` filters the list.

## Walk-ins

- **Walk-in-only location or days.** The location stays listed, but online booking is
  replaced by a walk-in notice. Holds and bookings are refused and availability is empty.
  Staff can still record bookings from the admin.
- **Walk-in-only resource.** One resource is kept for walk-ins every day. It is never offered
  online, auto-assign skips it, and a booking or hold that names it is refused with
  `resource.walk_in_only` (409). A combinable group containing it is held back too. Staff and
  the waitlist can still assign it. API: `walkInOnly` on resources.

## Walk-in waitlist

Available online while a location is walk-in-only and open. Staff can add parties at the front desk
on any day.

- The guest picks a party size, sees the estimated wait, and leaves a name and optionally an
  email. Their ticket shows their place in the queue and the estimated wait, and has its own
  page at `/waitlist/{ref}`.
- **Call** tells the guest their resource is ready: on the ticket, by push if they opted in, and by
  email if they left an address and SMTP is configured. **Assign** turns the entry into a normal
  booking on the smallest free resource that fits.
- Estimates replay the queue against the live state of the location: each resource frees when its current slot ends,
  and each party takes the first suitable resource to free up, for a slot of its own length.
- A party that could be assigned now but is quoted a wait, because the replay gave the free resource to
  a party ahead, shows "Free now, skips #N" on the board. API: `skipsNumber` on board entries.
- An entry still waiting 6 hours after joining expires. Every entry is deleted after 7 days,
  since it holds a name and email.
- Admin endpoints are under the `bookings` scope, with names and emails withheld from keys
  without `guests:read`. There is no SMS.

## Guest pacing

`maxGuestsPerSlot` caps how many guests can **start** in one booking slot (`null` is no cap).
Parties already in session from an earlier slot don't count.

- A slot the party would overfill is not offered. A booking that loses a race for the last places
  is refused with `booking.pacing_full` (409), including the cap and how many places remain.
- A booking at an off-grid time counts in the slot it falls in (19:10 counts in 19:00).
- Staff bookings and waitlist assignments aren't limited, but still count toward the total.
- The cap is per slot, so a location on a 15-minute grid gets a tighter limit than one on 30.
- The admin overview carries `todayPacing` for the dashboard's "Arrivals per Slot Today" card.

## Pausing bookings

A pause closes slots that **start** inside the pause window. It means "stop accepting new
arrivals for the next hour", not "stop taking bookings". Bookings for later today or next
week are unaffected, and the refusal names the local time the pause ends.

## Resources and combinable groups

- **Groups** mark resources that can be combined. A group holds more than its largest
  resource and no more than the total of its resources. Each resource in a group can still be booked on
  its own; auto-assign fills single resources first, then grouped resources, then groups.
- Booking a group blocks each of its resources, and booking one of its resources blocks the group.
- Deleting or resizing a resource in a group shrinks the group to fit, or removes it if fewer
  than two resources are left.
- **Oversize cap.** `MaxSpareCapacity` limits how much bigger than the party an
  auto-assigned resource can be.
- Deleting a resource or section keeps its bookings and clears their resource or section. The delete
  confirmation shows how many upcoming bookings are affected.
- Resource capacity and party size are 1–50.

## Booking references

- The **word** format (default) ends in four random digits: `swift-cedar-river-0482`. The
  **numeric** format is 8 digits with no leading zero. It is set per location.
- References are generated with a cryptographically secure random number generator. Every
  reference ever issued still resolves, whatever the location's current format.
- Guest endpoints that take a reference (lookup, cancel, reminders, waitlist tickets) are
  limited to 10 requests per minute per IP. Deployments where many guests share one IP
  should raise this in front of the app.

## Locations: archive and delete

Deleting a location removes its sections, resources and bookings, so it must be archived first.
The server refuses to delete an active location. Restoring an archived location is one press.
Deleting is Owner-only, and `GET /api/admin/venues/{id}/delete-preview` returns the
counts shown in the confirmation.

## Contact details

A location's phone and email override the brand-wide defaults **per field**, so a location
that lists only a phone still shows the brand email. Social links are brand-wide only.

## Admin accounts and roles

- **Owner** can also manage users, API keys and the activity log, and delete locations.
  **Manager** can do everything else.
- There must always be one active Owner. You can't deactivate yourself or change your own
  role.
- The first account is created from `ADMIN_EMAIL`/`ADMIN_PASSWORD` on first start.

## Activity log

Every change made through the admin or an API key is recorded at **Activity** (Owner only),
including refused attempts and failed sign-ins. Entries name the API key when one was used.

- Entries never hold passwords, secrets or guest names, emails or phone numbers. Bookings are
  referred to by reference.
- There is no way to edit or delete entries. They are removed after `Audit:RetentionDays`
  (default 365).

## Autosave

Brand and location settings save 800 ms after you stop typing, with a 10-second undo.
Values the server would reject aren't sent, and the form says why. SMTP settings, password
changes, booking edits and deletes still need an explicit press.

## Language

English, French, Spanish and German. The language used is the visitor's own choice, then the
instance default (`RESOURCEFLOW_DEFAULT_LOCALE` or `Locale:Default`), then English. The native
app also follows the phone's language.

## Configuration reference

| Setting                                                                           | Purpose                                                      |
| --------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| `RESOURCEFLOW_DEFAULT_LOCALE` / `Locale:Default`                                     | Default language (`en`, `fr`, `es`, `de`)                    |
| `Audit:RetentionDays`                                                             | Days to keep activity log entries (default 365)              |
| `GuestPush__ReminderLeadHours`                                                    | Reminder times before a booking, in hours (default `24,2`)   |
| `Wallet__Apple__*`, `Wallet__Google__*`                                           | Wallet pass issuers, see [`native-app.md`](native-app.md)    |
| `Vapid__*`                                                                        | Web Push keys for admin notifications and browser reminders  |
| `RESOURCEFLOW_CLI_PACKAGE_URL`, `RESOURCEFLOW_API_DOCS_URL`, `RESOURCEFLOW_REPOSITORY_URL` | Where the API Keys screen links, for forks                   |
| `WEBSITE_URL`                                                                     | Public address for email links, if not set in brand settings |
