#!/usr/bin/env bash
# REPO-02 / D22 / D25: third-party license allow-list check. See docs/LICENSE-POLICY.md.
#   1. npm production dependencies of the admin web      -> distribution allow-list
#   2. npm full tree (build-only tooling included)        -> distribution allow-list + D25 build-tool extension
#   3. NuGet runtime dependencies of the server publish   -> distribution allow-list + PostgreSQL (nuspec license expressions)
# The npm checks read the license checker's --json output and judge it with the same SPDX expression evaluator
# (expression_allowed) that the NuGet check uses. The checker's built-in allow option compares substrings, so it
# is not used. A self-test runs before the real checks and fails the script if the evaluator or the checker
# wiring ever lets a GPL expression through. GPL-family licenses are on no list, so they fail every check.
# A licenses array is parenthesized element by element before the AND join, so the evaluator judges it
# conservatively. jq output is written to a file first and its exit status (and, for npm, the row count) is checked,
# so a jq failure on any single entry fails the script (fail-closed). The self-test checks both behaviors too.
#
# Needs: (cd server/web && npm ci) done first, the .NET SDK, jq. Uses only the lockfile-pinned license checker
# (npx --no-install), never downloads a tool (D26).
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

DIST_ALLOWED=" MIT ISC Apache-2.0 BSD-2-Clause BSD-3-Clause 0BSD "
BUILD_ALLOWED="${DIST_ALLOWED}MPL-2.0 Python-2.0 PSF-2.0 BlueOak-1.0.0 CC0-1.0 CC-BY-3.0 "
NUGET_ALLOWED="${DIST_ALLOWED}PostgreSQL "
# Each licenses array element is parenthesized before the AND join (WR-01); a non-string value is stringified and fails.
NPM_ROW_FILTER='to_entries[] | "\(.key)\t\(.value.licenses // "" | if type == "array" then map("(" + tostring + ")") | join(" AND ") elif type == "string" then . else tostring end)"'

fail=0
report() {
  echo "check-licenses: $1" >&2
  fail=1
}

# Unlike report, fatal stops the script; inside a subshell it ends only that subshell.
fatal() {
  echo "check-licenses: $1" >&2
  exit 1
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
# Arrays are parenthesized (conservative); a missing licenses value becomes an empty expression and fails.
# Any jq failure or row-count mismatch is fatal. Call it as a plain command with its output redirected to a file
# (not in a pipe or process substitution), so fatal ends the script.
npm_violations() {
  local json="$1" list="$2" rows n m pkg lic
  rows="$(mktemp "$work/rows.XXXXXX")"
  jq -r "$NPM_ROW_FILTER" "$json" > "$rows" || fatal "jq failed reading $json"
  n="$(jq 'length' "$json")" || fatal "jq failed counting $json"
  m="$(wc -l < "$rows" | tr -d '[:space:]')"
  [ "$m" -eq "$n" ] || fatal "license row count mismatch in $json ($m rows for $n packages)"
  while IFS=$'\t' read -r pkg lic; do
    [ -n "$pkg" ] || continue
    if ! expression_allowed "$list" "$lic"; then
      printf '%s\t%s\n' "$pkg" "$lic"
    fi
  done < "$rows"
}

# nuget_package_libs <deps-json> <out-file>: write the package library keys of deps.json to a file; a jq failure is fatal.
nuget_package_libs() {
  local deps="$1" out="$2"
  jq -r '.libraries | to_entries[] | select(.value.type == "package") | .key' "$deps" > "$out" || fatal "jq failed reading $deps"
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
npm_violations "$work/probe-prod.json" "$DIST_ALLOWED" > "$work/probe-prod.viol"
if ! grep -q 'fake-gpl@1.0.0' "$work/probe-prod.viol"; then
  selftest_fail "fake-gpl@1.0.0 was not rejected by the npm production list"
fi
npm_violations "$work/probe-all.json" "$BUILD_ALLOWED" > "$work/probe-all.viol"
if ! grep -q 'fake-gpl@1.0.0' "$work/probe-all.viol"; then
  selftest_fail "fake-gpl@1.0.0 was not rejected by the npm build tree list"
fi

# licenses array fixture (WR-01): a GPL element must not hide behind an OR element.
printf '%s\n' '{"array-gpl@1.0.0":{"licenses":["GPL-3.0","MIT OR ISC"]},"array-ok@1.0.0":{"licenses":["MIT","ISC"]}}' > "$work/array-probe.json"
npm_violations "$work/array-probe.json" "$DIST_ALLOWED" > "$work/array-probe.viol"
if ! cut -f1 "$work/array-probe.viol" | grep -qx 'array-gpl@1.0.0'; then
  selftest_fail "array-gpl@1.0.0 (licenses array with a GPL element) was not rejected by the distribution list"
fi
if cut -f1 "$work/array-probe.viol" | grep -qx 'array-ok@1.0.0'; then
  selftest_fail "array-ok@1.0.0 (licenses array MIT, ISC) was rejected by the distribution list"
fi

# jq error fixtures (WR-02): the subshell call sits in an if condition, where set -e is off, so fatal's exit is what we detect.
printf '%s\n' '{"a@1.0.0":{"licenses":"MIT"},"broken@1.0.0":"GPL-3.0","z@1.0.0":{"licenses":"GPL-3.0"}}' > "$work/jq-fail.json"
if ( npm_violations "$work/jq-fail.json" "$BUILD_ALLOWED" > "$work/jq-fail.viol" 2> "$work/jq-fail.err" ); then
  selftest_fail "npm_violations ignored a jq error (fail-open)"
fi
printf '%s\n' '{"libraries":{"Good.Package/1.0.0":{"type":"package"},"Broken.Entry/1.0.0":"not-an-object","Later.Package/1.0.0":{"type":"package"}}}' > "$work/deps-probe.json"
if ( nuget_package_libs "$work/deps-probe.json" "$work/deps-probe.txt" 2> "$work/deps-probe.err" ); then
  selftest_fail "nuget_package_libs ignored a jq error (fail-open)"
fi
echo "check-licenses: self-test OK (fake-gpl@1.0.0 'MIT AND GPL-3.0' rejected by both npm lists, licenses arrays parenthesized, jq errors fail closed)"

# --- (b) npm production -> distribution allow-list ---
npm_licenses_json "$work/npm-prod.json" --production
n="$(jq 'length' "$work/npm-prod.json")"
if [ "$n" -eq 0 ]; then
  report "npm production: no packages found (checker output changed?)"
fi
echo "check-licenses: npm production: $n packages checked"
npm_violations "$work/npm-prod.json" "$DIST_ALLOWED" > "$work/npm-prod.viol"
while IFS=$'\t' read -r pkg lic; do
  [ -n "$pkg" ] || continue
  report "npm production: $pkg: '$lic' is not in the distribution allow-list"
done < "$work/npm-prod.viol"

# --- (c) npm full tree -> distribution allow-list + build-tool extension (D25) ---
npm_licenses_json "$work/npm-all.json"
n="$(jq 'length' "$work/npm-all.json")"
if [ "$n" -eq 0 ]; then
  report "npm build tree: no packages found (checker output changed?)"
fi
echo "check-licenses: npm build tree: $n packages checked"
npm_violations "$work/npm-all.json" "$BUILD_ALLOWED" > "$work/npm-all.viol"
while IFS=$'\t' read -r pkg lic; do
  [ -n "$pkg" ] || continue
  report "npm build tree: $pkg: '$lic' is not in the D25 extended list"
done < "$work/npm-all.viol"

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

nuget_package_libs "$deps" "$work/nuget-libs.txt"

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
done < "$work/nuget-libs.txt"

if [ "$count" -eq 0 ]; then
  report "no NuGet package libraries found in deps.json (publish output changed?)"
fi

if [ "$fail" -ne 0 ]; then
  exit 1
fi
echo "check-licenses: OK"
