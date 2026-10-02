#!/bin/sh

# Fail on any error
set -e

# 1. Extract nameservers for Nginx resolver
RESOLVERS=$(awk '/^nameserver/ {if ($2 ~ /:/) print "["$2"]"; else print $2}' /etc/resolv.conf | xargs echo)
export RESOLVER="$RESOLVERS 127.0.0.11"

echo "Using resolver: $RESOLVER"

# 2. Set defaults for required variables
export DOMAIN_NAME="${DOMAIN_NAME:-localhost}"
export BACKEND_HOST="${BACKEND_HOST:-backend}"
export BACKEND_PORT="${BACKEND_PORT:-8080}"
export FRONTEND_HOST="${FRONTEND_HOST:-frontend}"
export FRONTEND_PORT="${FRONTEND_PORT:-8081}"
export SSL_CERT_PATH="${SSL_CERT_PATH:-/etc/nginx/ssl/server.crt}"
export SSL_KEY_PATH="${SSL_KEY_PATH:-/etc/nginx/ssl/server.key}"

echo "Configuring Nginx for $DOMAIN_NAME..."
echo "  Proxying /api/* -> http://$BACKEND_HOST:$BACKEND_PORT"
echo "  Proxying /*      -> http://$FRONTEND_HOST:$FRONTEND_PORT"
echo "  SSL Certificate: $SSL_CERT_PATH"
echo "  SSL Key:         $SSL_KEY_PATH"

# 3. Substitute env vars in template
envsubst '${DOMAIN_NAME} ${BACKEND_HOST} ${BACKEND_PORT} ${FRONTEND_HOST} ${FRONTEND_PORT} ${RESOLVER} ${SSL_CERT_PATH} ${SSL_KEY_PATH}' \
  < /tmp/default.conf.template \
  > /etc/nginx/conf.d/default.conf

# 4. Restore the client address when another proxy sits in front of this one.
# TRUSTED_PROXIES is a comma-separated list of addresses or CIDR ranges (for example the
# Docker network of a Caddy, Traefik or Cloudflare Tunnel container). Requests arriving
# from those addresses have their X-Forwarded-For header honoured, so the API's per-client
# rate limits see each visitor rather than the proxy. Left empty, the header is ignored
# and the connecting address is the client, which is right when nginx faces the internet.
REAL_IP_CONF=/etc/nginx/conf.d/real-ip.conf
: > "$REAL_IP_CONF"
if [ -n "${TRUSTED_PROXIES:-}" ]; then
  for proxy in $(echo "$TRUSTED_PROXIES" | tr ',' ' '); do
    echo "set_real_ip_from $proxy;" >> "$REAL_IP_CONF"
  done
  echo "real_ip_header X-Forwarded-For;" >> "$REAL_IP_CONF"
  echo "real_ip_recursive on;" >> "$REAL_IP_CONF"
  echo "  Trusting X-Forwarded-For from: $TRUSTED_PROXIES"
fi

# 5. Validate Nginx configuration
echo "Validating Nginx configuration..."
nginx -t

# 6. Start Nginx
echo "Starting Nginx..."
exec nginx -g 'daemon off;'
