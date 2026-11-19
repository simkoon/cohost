#!/usr/bin/env bash
# REPO-02 / D22 / D25: third-party license allow-list check. See docs/LICENSE-POLICY.md.
#   1. npm production dependencies of the admin web      -> distribution allow-list
#   2. npm full tree (build-only tooling included)        -> distribution allow-list + D25 build-tool extension
#   3. NuGet runtime dependencies of the server publish   -> distribution allow-list + PostgreSQL (nuspec license expressions)
# The npm checks read the license checker's --json output and judge it with the same SPDX expression evaluator
# (expression_allowed) that the NuGet check uses. The checker's built-in allow option compares substrings, so it
# is not used. A self-test runs before the real checks and fails the script if the evaluator or the checker
# wiring ever lets a GPL expression through. GPL-family licenses are on no list, so they fail every check.
#
# Needs: (cd server/web && npm ci) done first, the .NET SDK, jq. Uses only the lockfile-pinned license checker
# (npx --no-install), never downloads a tool (D26).
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

DIST_ALLOWED=" MIT ISC Apache-2.0 BSD-2-Clause BSD-3-Clause 0BSD "
BUILD_ALLOWED="${DIST_ALLOWED}MPL-2.0 Python-2.0 PSF-2.0 BlueOak-1.0.0 CC0-1.0 CC-BY-3.0 "
NUGET_ALLOWED="${DIST_ALLOWED}PostgreSQL "

fail=0
report() {
  echo "check-licenses: $1" >&2
  fail=1
}

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# expression_allowed <allow-list> <spdx expression>: the allow-list is the first argument (space-wrapped names).
# AND binds tighter than OR. Any parenthesis or WITH clause is handled conservatively (every token must be
# allowed), so the check can only be stricter than SPDX, never looser.
expression_allowed() {
  local list="$1" expr="$2" group token ok piece
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
      if [[ -z "$token" || "$list" != *" $token "* ]]; then
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

# npm_licenses_json <out-file> [checker args...]: run the lockfile-pinned checker in --json mode and verify the output.
npm_licenses_json() {
  local out="$1"
  shift
  if ! (cd server/web && npx --no-install license-checker-rseidelsohn --excludePrivatePackages --json "$@") > "$out"; then
    echo "check-licenses: license checker failed" >&2
    exit 1
  fi
  if ! jq -e 'type == "object"' "$out" > /dev/null; then
    echo "check-licenses: checker output is not a JSON object" >&2
    exit 1
  fi
}

# npm_violations <json-file> <allow-list>: print "name@version<TAB>license" for every package the allow-list rejects.
# A licenses array is joined with AND (conservative); a missing licenses value becomes an empty expression and fails.
npm_violations() {
  local json="$1" list="$2" pkg lic
  while IFS=$'\t' read -r pkg lic; do
    [ -n "$pkg" ] || continue
    if ! expression_allowed "$list" "$lic"; then
      printf '%s\t%s\n' "$pkg" "$lic"
    fi
  done < <(jq -r 'to_entries[] | "\(.key)\t\(.value.licenses // "" | if type == "array" then join(" AND ") else . end)"' "$json")
}

# --- (a) npm tree must be installed ---
if [ ! -d server/web/node_modules ]; then
  echo "check-licenses: server/web/node_modules is missing. Run (cd server/web && npm ci) first." >&2
  exit 1
fi

# --- (0) self-test ---
selftest_fail() {
  echo "check-licenses: self-test failed: $1" >&2
  exit 1
}
expect_reject() { # <list-name> <list> <expression>
  if expression_allowed "$2" "$3"; then
    selftest_fail "'$3' was allowed by the $1 list"
  fi
}
expect_allow() { # <list-name> <list> <expression>
  if ! expression_allowed "$2" "$3"; then
    selftest_fail "'$3' was rejected by the $1 list"
  fi
}

for e in "MIT AND GPL-3.0" "GPL-3.0-only" "SEE LICENSE IN LICENSE.MIT" "MIT*" "UNKNOWN" "" "(MIT AND CC-BY-3.0)" "PostgreSQL"; do
  expect_reject distribution "$DIST_ALLOWED" "$e"
done
for e in "MIT AND GPL-3.0" "(GPL-2.0 AND MIT)"; do
  expect_reject extended "$BUILD_ALLOWED" "$e"
done
expect_allow distribution "$DIST_ALLOWED" "MIT"
expect_allow distribution "$DIST_ALLOWED" "MIT OR Apache-2.0"
expect_allow extended "$BUILD_ALLOWED" "(MIT AND CC-BY-3.0)"
expect_allow nuget "$NUGET_ALLOWED" "PostgreSQL"

probe="$work/license-probe"
mkdir -p "$probe/node_modules/fake-gpl"
printf '%s\n' '{"name":"license-probe","version":"0.0.0","private":true,"dependencies":{"fake-gpl":"1.0.0"}}' > "$probe/package.json"
printf '%s\n' '{"name":"fake-gpl","version":"1.0.0","license":"MIT AND GPL-3.0"}' > "$probe/node_modules/fake-gpl/package.json"
npm_licenses_json "$work/probe-prod.json" --start "$probe" --production
npm_licenses_json "$work/probe-all.json" --start "$probe"
if ! npm_violations "$work/probe-prod.json" "$DIST_ALLOWED" | grep -q 'fake-gpl@1.0.0'; then
  selftest_fail "fake-gpl@1.0.0 was not rejected by the npm production list"
fi
if ! npm_violations "$work/probe-all.json" "$BUILD_ALLOWED" | grep -q 'fake-gpl@1.0.0'; then
  selftest_fail "fake-gpl@1.0.0 was not rejected by the npm build tree list"
fi
echo "check-licenses: self-test OK (fake-gpl@1.0.0 'MIT AND GPL-3.0' rejected by both npm lists)"

# --- (b) npm production -> distribution allow-list ---
npm_licenses_json "$work/npm-prod.json" --production
n="$(jq 'length' "$work/npm-prod.json")"
if [ "$n" -eq 0 ]; then
  report "npm production: no packages found (checker output changed?)"
fi
echo "check-licenses: npm production: $n packages checked"
while IFS=$'\t' read -r pkg lic; do
  [ -n "$pkg" ] || continue
  report "npm production: $pkg: '$lic' is not in the distribution allow-list"
done < <(npm_violations "$work/npm-prod.json" "$DIST_ALLOWED")

# --- (c) npm full tree -> distribution allow-list + build-tool extension (D25) ---
npm_licenses_json "$work/npm-all.json"
n="$(jq 'length' "$work/npm-all.json")"
if [ "$n" -eq 0 ]; then
  report "npm build tree: no packages found (checker output changed?)"
fi
echo "check-licenses: npm build tree: $n packages checked"
while IFS=$'\t' read -r pkg lic; do
  [ -n "$pkg" ] || continue
  report "npm build tree: $pkg: '$lic' is not in the D25 extended list"
done < <(npm_violations "$work/npm-all.json" "$BUILD_ALLOWED")

# --- (d) NuGet runtime dependencies of the server publish output ---
echo "check-licenses: NuGet runtime dependencies of server publish"
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
  if expression_allowed "$NUGET_ALLOWED" "$expr"; then
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
