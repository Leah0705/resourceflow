# ResourceFlow Changelog

## [Unreleased]

- Classify available start times as AM / PM in each venue's local time.
- Serialize booking creation (guest, staff and waitlist) within a single instance.
- Show the admin documentation and repository links only when they are configured.
- Re-check an admin's account status and role on every request, so deactivating or demoting a user takes effect at once.
- Rate-limit by the real client address when the stack runs behind an extra reverse proxy.
- Cap the number of concurrent holds one client can place.

## [2.3.0]

- Initial public release.
