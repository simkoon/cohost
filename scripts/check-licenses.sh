#!/usr/bin/env bash
# REPO-02 / D22 / D25: third-party license allow-list check. See docs/LICENSE-POLICY.md.
#   1. npm production dependencies of the admin web      -> distribution allow-list
#   2. npm full tree (build-only tooling included)        -> distribution allow-list + D25 build-tool extension
#   3. NuGet runtime dependencies of the server publish   -> distribution allow-list (nuspec license expressions)
# GPL-family licenses are on neither list, so they fail every check.
#
# Needs: (cd server/web && npm ci) done first, the .NET SDK, jq. Uses only the lockfile-pinned license checker
# (npx --no-install), never downloads a tool (D26).
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

DIST_ALLOW="MIT;ISC;Apache-2.0;BSD-2-Clause;BSD-3-Clause;0BSD"
BUILD_ALLOW="${DIST_ALLOW};MPL-2.0;Python-2.0;PSF-2.0;BlueOak-1.0.0;CC0-1.0;CC-BY-3.0"
NUGET_ALLOWED=" MIT Apache-2.0 BSD-2-Clause BSD-3-Clause ISC 0BSD PostgreSQL "

fail=0
report() {
  echo "check-licenses: $1" >&2
  fail=1
}

# --- (a) npm tree must be installed ---
if [ ! -d server/web/node_modules ]; then
  echo "check-licenses: server/web/node_modules is missing. Run (cd server/web && npm ci) first." >&2
  exit 1
fi

# --- (b) npm production -> distribution allow-list ---
echo "check-licenses: npm production dependencies"
if ! (cd server/web && npx --no-install license-checker-rseidelsohn --production --excludePrivatePackages \
  --onlyAllow "$DIST_ALLOW" --summary); then
  report "npm production dependencies contain a license outside the distribution allow-list"
fi

# --- (c) npm full tree -> distribution allow-list + build-tool extension ---
echo "check-licenses: npm build-only tree (D25 extended list)"
if ! (cd server/web && npx --no-install license-checker-rseidelsohn --excludePrivatePackages \
  --onlyAllow "$BUILD_ALLOW" --summary); then
  report "npm dependency tree contains a license outside the D25 extended list"
fi

# --- (d) NuGet runtime dependencies of the server publish output ---
# expression_allowed <spdx expression>: AND binds tighter than OR. Any parenthesis or WITH clause is handled
# conservatively (every token must be allowed), so the check can only be stricter than SPDX, never looser.
expression_allowed() {
  local expr="$1" group token ok piece
  if [[ "$expr" == *"("* || "$expr" == *")"* || "$expr" == *" WITH "* ]]; then
    expr="${expr//(/ }"
    expr="${expr//)/ }"
    expr="${expr// WITH / AND }"
    expr="${expr// OR / AND }"
  fi
  local rest="$expr"
  while :; do
    if [[ "$rest" == *" OR "* ]]; then
      group="${rest%% OR *}"
      rest="${rest#* OR }"
    else
      group="$rest"
      rest=""
    fi
    ok=1
    while :; do
      if [[ "$group" == *" AND "* ]]; then
        piece="${group%% AND *}"
        group="${group#* AND }"
      else
        piece="$group"
        group=""
      fi
      token="$(printf '%s' "$piece" | tr -d '[:space:]')"
      if [[ -z "$token" || "$NUGET_ALLOWED" != *" $token "* ]]; then
        ok=0
      fi
      [ -z "$group" ] && break
    done
    if [ "$ok" -eq 1 ]; then
      return 0
    fi
    [ -z "$rest" ] && break
  done
  return 1
}

echo "check-licenses: NuGet runtime dependencies of server publish"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

dotnet publish server/CoHost.Server/CoHost.Server.csproj -c Release -o "$work/publish" --nologo > "$work/publish.log" 2>&1 || {
  cat "$work/publish.log" >&2
  echo "check-licenses: dotnet publish failed" >&2
  exit 1
}

deps="$work/publish/CoHost.Server.deps.json"
if [ ! -f "$deps" ]; then
  echo "check-licenses: $deps not found" >&2
  exit 1
fi

packages_dir="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
count=0
while IFS= read -r lib; do
  [ -n "$lib" ] || continue
  name="$(printf '%s' "${lib%%/*}" | tr '[:upper:]' '[:lower:]')"
  version="$(printf '%s' "${lib#*/}" | tr '[:upper:]' '[:lower:]')"
  nuspec="$packages_dir/$name/$version/$name.nuspec"
  count=$((count + 1))
  if [ ! -f "$nuspec" ]; then
    report "$lib: nuspec not found at $nuspec"
    continue
  fi
  expr="$(grep -o '<license type="expression">[^<]*</license>' "$nuspec" | head -n 1 | sed -e 's/<license type="expression">//' -e 's/<\/license>//' || true)"
  if [ -z "$expr" ]; then
    report "$lib: no <license type=\"expression\"> in nuspec"
    continue
  fi
  if expression_allowed "$expr"; then
    echo "  $lib: $expr"
  else
    report "$lib: license '$expr' is not in the distribution allow-list"
  fi
done < <(jq -r '.libraries | to_entries[] | select(.value.type == "package") | .key' "$deps")

if [ "$count" -eq 0 ]; then
  report "no NuGet package libraries found in deps.json (publish output changed?)"
fi

if [ "$fail" -ne 0 ]; then
  exit 1
fi
echo "check-licenses: OK"
