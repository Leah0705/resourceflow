#!/bin/sh
set -e

# Named volumes initialise with root ownership at runtime, overriding the
# build-time chown. Fix both volume mount points on every start so the app
# user can always write to them, then drop privileges.
mkdir -p /data /app/wwwroot/media
chown app:app /data /app/wwwroot/media
chmod 775 /data /app/wwwroot/media

exec runuser -u app -- "$@"
