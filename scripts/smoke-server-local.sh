#!/usr/bin/env bash
# Docker-free smoke test for the server bootstrap (plan 01-03).
# Starts the server against an unreachable database and verifies TLS/key bootstrap,
# fingerprint reporting, health endpoints, plaintext rejection and restart stability.
set -euo pipefail

cd "$(dirname "$0")/.."

PORT="${SMOKE_LOCAL_PORT:-18444}"
DLL="server/CoHost.Server/bin/Debug/net10.0/CoHost.Server.dll"
BASE="https://127.0.0.1:${PORT}"

command -v jq >/dev/null || { echo "smoke-server-local: jq is required" >&2; exit 1; }
command -v openssl >/dev/null || { echo "smoke-server-local: openssl is required" >&2; exit 1; }
command -v curl >/dev/null || { echo "smoke-server-local: curl is required" >&2; exit 1; }

fail() {
  echo "smoke-server-local: FAIL - $*" >&2
  exit 1
}

DATA_DIR="$(mktemp -d)"
LOG1="${DATA_DIR}.run1.log"
LOG2="${DATA_DIR}.run2.log"
SERVER_PID=""

stop_server() {
  if [ -n "${SERVER_PID}" ] && kill -0 "${SERVER_PID}" 2>/dev/null; then
    kill "${SERVER_PID}" 2>/dev/null || true
    wait "${SERVER_PID}" 2>/dev/null || true
  fi
  SERVER_PID=""
}

cleanup() {
  stop_server
  rm -rf "${DATA_DIR}" "${LOG1}" "${LOG2}"
}
trap cleanup EXIT

start_server() {
  local log="$1"
  COHOST_DATA_DIR="${DATA_DIR}" \
  COHOST_PORT="${PORT}" \
  COHOST_HOSTNAMES="cohost.local" \
  COHOST_DB="Host=127.0.0.1;Port=1;Database=cohost;Username=cohost;Password=cohost-change-me;Timeout=2" \
    dotnet "${DLL}" >"${log}" 2>&1 &
  SERVER_PID=$!

  local i
  for i in $(seq 1 60); do
    if curl -sk --max-time 2 -o /dev/null "${BASE}/healthz/live"; then
      return 0
    fi
    kill -0 "${SERVER_PID}" 2>/dev/null || { cat "${log}" >&2; fail "server exited during startup"; }
    sleep 1
  done
  cat "${log}" >&2
  fail "server did not answer /healthz/live within 60s"
}

code_of() {
  curl -sk --max-time 5 -o /dev/null -w '%{http_code}' "$1" || true
}

echo "smoke-server-local: building server"
dotnet build server/CoHost.Server/CoHost.Server.csproj --nologo -v quiet >/dev/null

echo "smoke-server-local: first start (fresh data dir)"
start_server "${LOG1}"

# (1) liveness, (2) readiness without DB
[ "$(code_of "${BASE}/healthz/live")" = "200" ] || fail "/healthz/live is not 200"
[ "$(code_of "${BASE}/healthz")" = "503" ] || fail "/healthz is not 503 while DB is unreachable"

# (3) server info
INFO="$(curl -sk --max-time 5 "${BASE}/api/server-info")"
FP="$(echo "${INFO}" | jq -r '.certSha256')"
KID1_INFO="$(echo "${INFO}" | jq -r '.appName')"
[ "${KID1_INFO}" = "CoHost" ] || fail "appName is not CoHost"
[ "$(echo "${INFO}" | jq -r '.protocolVersion')" = "0.1" ] || fail "protocolVersion is not 0.1"
echo "${FP}" | grep -Eq '^[0-9A-F]{64}$' || fail "certSha256 is not 64 uppercase hex chars"
[ "$(echo "${INFO}" | jq -r '.database')" = "unavailable" ] || fail "database is not unavailable"
[ "$(echo "${INFO}" | jq -r '.defaultDbPassword')" = "true" ] || fail "defaultDbPassword is not true"

# (4) JWKS
KEYS="$(curl -sk --max-time 5 "${BASE}/api/keys")"
echo "${KEYS}" | jq -e '(.keys | length) == 1 and .keys[0].kty == "EC" and .keys[0].crv == "P-256" and .keys[0].alg == "ES256" and .keys[0].use == "sig" and (.keys[0].kid | length) > 0' >/dev/null \
  || fail "/api/keys JWKS has an unexpected shape"
KID="$(echo "${KEYS}" | jq -r '.keys[0].kid')"

# (5) startup log lines
grep -q "fingerprint: ${FP}" "${LOG1}" || fail "log is missing the certificate fingerprint"
grep -q "(generated)" "${LOG1}" || fail "log is missing the (generated) marker"
grep -q "default database password" "${LOG1}" || fail "log is missing the default database password warning"

# (6) generated files and permissions
[ -f "${DATA_DIR}/certs/cert.pem" ] || fail "certs/cert.pem missing"
[ -f "${DATA_DIR}/certs/key.pem" ] || fail "certs/key.pem missing"
[ -f "${DATA_DIR}/keys/es256-private.pem" ] || fail "keys/es256-private.pem missing"
perm_of() {
  if stat -f %Lp "$1" >/dev/null 2>&1; then stat -f %Lp "$1"; else stat -c %a "$1"; fi
}
[ "$(perm_of "${DATA_DIR}/certs/key.pem")" = "600" ] || fail "key.pem permissions are not 600"
[ "$(perm_of "${DATA_DIR}/keys/es256-private.pem")" = "600" ] || fail "es256-private.pem permissions are not 600"

# (7) fingerprint matches openssl (handles GNU/LibreSSL "SHA256 Fingerprint=AA:BB:..." output)
OSSL_FP="$(openssl x509 -in "${DATA_DIR}/certs/cert.pem" -noout -fingerprint -sha256 | sed 's/^[^=]*=//' | tr -d ':' | tr '[:lower:]' '[:upper:]')"
[ "${OSSL_FP}" = "${FP}" ] || fail "openssl fingerprint (${OSSL_FP}) differs from server-info (${FP})"

# (8) plaintext HTTP must not be served
PLAIN="$(curl -s --max-time 3 -o /dev/null -w '%{http_code}' "http://127.0.0.1:${PORT}/" || true)"
[ "${PLAIN}" != "200" ] || fail "plaintext HTTP request returned 200"

# (9) --healthcheck mode
COHOST_PORT="${PORT}" dotnet "${DLL}" --healthcheck || fail "--healthcheck did not exit 0"

echo "smoke-server-local: restart with the same data dir"
stop_server
start_server "${LOG2}"

# (10) same fingerprint and kid, loaded not generated
INFO2="$(curl -sk --max-time 5 "${BASE}/api/server-info")"
[ "$(echo "${INFO2}" | jq -r '.certSha256')" = "${FP}" ] || fail "fingerprint changed after restart"
[ "$(curl -sk --max-time 5 "${BASE}/api/keys" | jq -r '.keys[0].kid')" = "${KID}" ] || fail "kid changed after restart"
grep -q "(loaded)" "${LOG2}" || fail "second run log is missing the (loaded) marker"

echo "smoke-server-local: OK"
