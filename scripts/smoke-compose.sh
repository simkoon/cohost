#!/usr/bin/env bash
# End-to-end smoke test for the compose stack (SRV-01, Phase 1 success criterion 1).
#
# Brings up cohost-server + postgres from an empty state and verifies: healthy start, admin
# index, server-info, JWKS, certificate fingerprint agreement across API / log / TLS handshake /
# volume file, plaintext rejection, postgres not published, non-root image, restart persistence
# and an air-gapped docker save / docker load round trip.
#
# Isolated from a developer's real stack: own compose project (cohost-smoke), image tag
# (smoke), port (SMOKE_PORT, default 18443) and an empty env-file so a local .env is ignored.
#
# Environment: SMOKE_PORT, SMOKE_KEEP=1 (leave the stack running), SMOKE_SKIP_SAVE_LOAD=1.
set -euo pipefail

cd "$(dirname "$0")/.."

for tool in docker jq openssl curl; do
  command -v "$tool" >/dev/null || { echo "smoke-compose: $tool is required" >&2; exit 1; }
done

PROJECT="cohost-smoke"
PORT="${SMOKE_PORT:-18443}"
BASE="https://127.0.0.1:${PORT}"
TMP="$(mktemp -d)"
ENV_FILE="${TMP}/empty.env"
: > "${ENV_FILE}"

# Shell variables override env-file values; COHOST_DB_PASSWORD is empty on purpose so the
# default-password path is exercised.
export COHOST_VERSION="smoke"
export COHOST_PORT="${PORT}"
export COHOST_HOSTNAMES="cohost.local"
export COHOST_DB_PASSWORD=""

dc() {
  docker compose -p "${PROJECT}" --env-file "${ENV_FILE}" "$@"
}

fail() {
  echo "smoke-compose: FAIL - $*" >&2
  dc logs --no-color --tail 40 cohost-server >&2 2>/dev/null || true
  exit 1
}

cleanup() {
  local rc=$?
  if [ "${SMOKE_KEEP:-0}" != "1" ]; then
    dc down -v --remove-orphans >/dev/null 2>&1 || true
    docker image rm "cohost-server:${COHOST_VERSION}" >/dev/null 2>&1 || true
    rm -rf "${TMP}"
  else
    echo "smoke-compose: SMOKE_KEEP=1, stack left running (project ${PROJECT}); temp dir ${TMP}" >&2
  fi
  exit "${rc}"
}
trap cleanup EXIT

code_of() {
  curl -sk --max-time 5 -o /dev/null -w '%{http_code}' "$1" || true
}

# Normalises "SHA256 Fingerprint=AA:BB:.." / "sha256 Fingerprint=aa:bb:.." to AABB...
fp_of_pem() {
  openssl x509 -in "$1" -noout -fingerprint -sha256 | sed 's/^[^=]*=//' | tr -d ':' | tr '[:lower:]' '[:upper:]'
}

not_root() {
  local user
  user="$(docker image inspect "cohost-server:${COHOST_VERSION}" --format '{{.Config.User}}')"
  [ -n "${user}" ] && [ "${user}" != "root" ] && [ "${user}" != "0" ] && [ "${user}" != "0:0" ]
}

wait_for_database_ok() {
  local i
  for i in $(seq 1 90); do
    if [ "$(curl -sk --max-time 3 "${BASE}/api/server-info" | jq -r '.database' 2>/dev/null || true)" = "ok" ]; then
      return 0
    fi
    sleep 1
  done
  return 1
}

echo "smoke-compose: (1) clean slate, build and start"
dc down -v --remove-orphans >/dev/null 2>&1 || true
dc up -d --build --wait --wait-timeout 180

echo "smoke-compose: (2) /healthz becomes Healthy"
HEALTHY=""
for i in $(seq 1 90); do
  BODY="$(curl -sk --max-time 3 "${BASE}/healthz" || true)"
  if [ "${BODY}" = "Healthy" ]; then HEALTHY=1; break; fi
  sleep 1
done
[ -n "${HEALTHY}" ] || fail "/healthz did not become Healthy within 90s (last body: '${BODY:-}')"

echo "smoke-compose: (3) /api/server-info"
INFO="$(curl -sk --max-time 5 "${BASE}/api/server-info")"
[ "$(echo "${INFO}" | jq -r '.appName')" = "CoHost" ] || fail "appName is not CoHost: ${INFO}"
[ "$(echo "${INFO}" | jq -r '.protocolVersion')" = "0.1" ] || fail "protocolVersion is not 0.1: ${INFO}"
[ "$(echo "${INFO}" | jq -r '.database')" = "ok" ] || fail "database is not ok: ${INFO}"
[ "$(echo "${INFO}" | jq -r '.defaultDbPassword')" = "true" ] || fail "defaultDbPassword is not true: ${INFO}"
FP="$(echo "${INFO}" | jq -r '.certSha256')"
echo "${FP}" | grep -Eq '^[0-9A-F]{64}$' || fail "certSha256 is not 64 uppercase hex chars: ${FP}"

echo "smoke-compose: (4) /api/keys"
KEYS="$(curl -sk --max-time 5 "${BASE}/api/keys")"
echo "${KEYS}" | jq -e '(.keys | length) == 1 and .keys[0].kty == "EC" and .keys[0].crv == "P-256" and .keys[0].alg == "ES256" and .keys[0].use == "sig" and (.keys[0].kid | length) > 0' >/dev/null \
  || fail "/api/keys JWKS has an unexpected shape: ${KEYS}"
KID="$(echo "${KEYS}" | jq -r '.keys[0].kid')"

echo "smoke-compose: (5) admin index and SPA fallback"
[ "$(code_of "${BASE}/")" = "200" ] || fail "GET / is not 200"
curl -sk --max-time 5 "${BASE}/" | grep -q 'id="root"' || fail "index.html has no id=\"root\""
[ "$(code_of "${BASE}/devices/EQ-001")" = "200" ] || fail "SPA path /devices/EQ-001 is not 200"

echo "smoke-compose: (6) startup log lines"
LOGS="$(dc logs --no-color cohost-server)"
echo "${LOGS}" | grep -q "fingerprint: ${FP}" || fail "log is missing the certificate fingerprint"
echo "${LOGS}" | grep -q "(generated)" || fail "log is missing the (generated) marker"
echo "${LOGS}" | grep -q "default database password" || fail "log is missing the default database password warning"

echo "smoke-compose: (7) fingerprint from the TLS handshake"
openssl s_client -connect "127.0.0.1:${PORT}" -servername localhost </dev/null 2>/dev/null | openssl x509 > "${TMP}/served.pem"
[ "$(fp_of_pem "${TMP}/served.pem")" = "${FP}" ] || fail "TLS handshake fingerprint differs from server-info (${FP})"
openssl x509 -in "${TMP}/served.pem" -noout -text | grep -q 'DNS:cohost.local' || fail "certificate SAN is missing DNS:cohost.local"

echo "smoke-compose: (8) plaintext HTTP is not served"
PLAIN="$(curl -s --max-time 3 -o /dev/null -w '%{http_code}' "http://127.0.0.1:${PORT}/" || true)"
[ "${PLAIN}" != "200" ] || fail "plaintext HTTP request returned 200"

echo "smoke-compose: (9) postgres is not published to the host"
PG_PORT="$(dc port postgres 5432 2>&1 || true)"
if echo "${PG_PORT}" | grep -Eq ':[1-9][0-9]*$'; then
  fail "postgres 5432 is published to the host: ${PG_PORT}"
fi

echo "smoke-compose: (10) image runs as non-root"
not_root || fail "image user is empty or root"

echo "smoke-compose: (11) certificate and key files on the volume"
dc cp cohost-server:/data/certs/cert.pem "${TMP}/volume-cert.pem" >/dev/null
[ "$(fp_of_pem "${TMP}/volume-cert.pem")" = "${FP}" ] || fail "volume cert.pem fingerprint differs from server-info"
dc cp cohost-server:/data/keys/es256-private.pem "${TMP}/volume-signing.pem" >/dev/null
[ -f "${TMP}/volume-signing.pem" ] || fail "signing key file is missing on the volume"
rm -f "${TMP}/volume-signing.pem"

echo "smoke-compose: (12) restart keeps fingerprint and kid"
dc down >/dev/null
dc up -d --no-build --wait --wait-timeout 180
wait_for_database_ok || fail "database not ok after restart"
INFO2="$(curl -sk --max-time 5 "${BASE}/api/server-info")"
[ "$(echo "${INFO2}" | jq -r '.certSha256')" = "${FP}" ] || fail "fingerprint changed after restart"
[ "$(curl -sk --max-time 5 "${BASE}/api/keys" | jq -r '.keys[0].kid')" = "${KID}" ] || fail "kid changed after restart"
LOGS2="$(dc logs --no-color cohost-server)"
echo "${LOGS2}" | grep -q "(loaded)" || fail "log after restart is missing the (loaded) marker"

if [ "${SMOKE_SKIP_SAVE_LOAD:-0}" = "1" ]; then
  echo "smoke-compose: (13) skipped (SMOKE_SKIP_SAVE_LOAD=1)"
else
  echo "smoke-compose: (13) air-gapped import via docker save / docker load"
  docker save -o "${TMP}/images.tar" "cohost-server:${COHOST_VERSION}" postgres:18-alpine
  dc down -v --remove-orphans >/dev/null
  docker image rm "cohost-server:${COHOST_VERSION}" >/dev/null
  docker load -i "${TMP}/images.tar" >/dev/null
  dc up -d --no-build --pull never --wait --wait-timeout 180
  [ "$(code_of "${BASE}/healthz/live")" = "200" ] || fail "/healthz/live is not 200 after docker load"
  wait_for_database_ok || fail "database not ok after docker load"
  not_root || fail "loaded image user is empty or root"
fi

echo "smoke-compose: OK"
