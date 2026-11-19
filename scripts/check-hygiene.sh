#!/usr/bin/env bash
# REPO-02 / D19: public-repo hygiene. Fails when a file that would be committed carries a key or certificate,
# a private-key PEM, a private IPv4 literal, a tracked .env, or a term from the local denylist.
#
# Scope: "files to be committed" = tracked files plus untracked files that are not gitignored
# (git ls-files --cached --others --exclude-standard), deleted files excluded. Ignored files are never scanned.
#
# Local denylist: put one forbidden term per line in .hygiene-denylist (gitignored; '#' lines and blank lines are
# ignored). This lets a developer block company or site identifiers without writing them into the repo.
#
# Portable: only grep -E / -F (no -P); runs on macOS (BSD), Linux and Git Bash.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

fail=0
report() {
  echo "check-hygiene: $1" >&2
  fail=1
}

self="scripts/check-hygiene.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# --- file lists (NUL separated so odd names are safe) ---
git ls-files -z --cached --others --exclude-standard | sort -z > "$work/all.raw"

: > "$work/all.nul"
: > "$work/scan.nul"
all_names=()
while IFS= read -r -d '' f; do
  [ -f "$f" ] || continue # deleted in the working tree
  all_names+=("$f")
  printf '%s\0' "$f" >> "$work/all.nul"
  [ "$f" = "$self" ] && continue
  printf '%s\0' "$f" >> "$work/scan.nul"
done < "$work/all.raw"

# (a) required repo-level files
if [ -f LICENSE ]; then
  grep -q "Apache License" LICENSE || report "LICENSE does not look like Apache License text"
  grep -q "Version 2.0, January 2004" LICENSE || report "LICENSE is not Apache License Version 2.0"
else
  report "LICENSE is missing"
fi
for required in NOTICE SECURITY.md docs/LICENSE-POLICY.md; do
  [ -f "$required" ] || report "$required is missing"
done

# (b) key / certificate files and (c) env files
for f in "${all_names[@]}"; do
  base="${f##*/}"
  lower="$(printf '%s' "$base" | tr '[:upper:]' '[:lower:]')"
  case "$lower" in
    *.pem | *.key | *.pfx | *.p12 | *.crt | *.cer | *.der | *.jks | *.keystore)
      report "key or certificate file would be committed: $f"
      ;;
  esac
  case "$base" in
    .env.example) ;;
    .env | .env.*)
      report "env file would be committed: $f (only .env.example is allowed)"
      ;;
  esac
done

scan() { # scan <grep args...>; prints matches, never fails the script
  if [ -s "$work/scan.nul" ]; then
    xargs -0 grep -I "$@" < "$work/scan.nul" 2> /dev/null || true
  fi
}

# (d) PEM private key headers
pem_hits="$(scan -nE -e '-----BEGIN ([A-Z]+ )*PRIVATE KEY-----')"
if [ -n "$pem_hits" ]; then
  report "PEM private key header found:"
  echo "$pem_hits" | cut -d: -f1,2 >&2
fi

# (e) private IPv4 literals: all four octets, bounded so version strings do not match
octet='[0-9]{1,3}'
lead='(^|[^0-9.])'
trail='($|[^0-9.]|\.($|[^0-9]))'
ip_re="${lead}(10(\.${octet}){3}|192\.168(\.${octet}){2}|172\.(1[6-9]|2[0-9]|3[01])(\.${octet}){2})${trail}"
ip_hits="$(scan -nE -e "$ip_re")"
if [ -n "$ip_hits" ]; then
  report "private IPv4 literal found (use RFC 5737 documentation addresses such as 192.0.2.10 in examples):"
  echo "$ip_hits" | cut -d: -f1,2 >&2
fi

# (f) local denylist (matches are reported as file:line only so the terms never reach CI logs)
if [ -f .hygiene-denylist ]; then
  tr -d '\r' < .hygiene-denylist | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' | grep -v -e '^$' -e '^#' > "$work/deny.txt" || true
  if [ -s "$work/deny.txt" ]; then
    deny_hits="$(scan -niF -f "$work/deny.txt")"
    if [ -n "$deny_hits" ]; then
      report "term from .hygiene-denylist found:"
      echo "$deny_hits" | cut -d: -f1,2 >&2
    fi
  else
    echo "check-hygiene: .hygiene-denylist has no terms, skipped"
  fi
else
  echo "check-hygiene: denylist not present, skipped"
fi

# (g) .gitignore protection
for path in .env x/key.pem x/cert.pfx .data/certs/cert.pem server/web/src/generated/X.d.ts \
  server/web/generated/schema/X.json server/web/dist/index.html .hygiene-denylist; do
  git check-ignore -q "$path" || report "$path is not gitignored"
done
if git check-ignore -q .env.example; then
  report ".env.example must not be gitignored"
fi

if [ "$fail" -ne 0 ]; then
  exit 1
fi
echo "check-hygiene: OK"
