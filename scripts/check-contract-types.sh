#!/usr/bin/env bash
# PROTO-02: the admin must not carry hand-written contract types. Message and payload types come only from
# server/web/src/generated (gitignored, produced from CoHost.Contracts at build time).
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

fail=0
report() {
  echo "check-contract-types: $1" >&2
  fail=1
}

# (1) generated directories must be gitignored
for path in server/web/src/generated/probe.d.ts server/web/generated/schema/probe.json; do
  if ! git check-ignore -q "$path"; then
    report "$path is not gitignored"
  fi
done

# (2) nothing under the generated directories may be tracked or stageable
leaked=$(git ls-files --cached --others --exclude-standard server/web/src/generated server/web/generated)
if [ -n "$leaked" ]; then
  report "generated files are tracked or not ignored:"
  echo "$leaked" >&2
fi

# hand-written sources: every ts/tsx under server/web/src except generated/
sources=()
while IFS= read -r file; do
  sources+=("$file")
done < <(find server/web/src -type f \( -name '*.ts' -o -name '*.tsx' \) -not -path '*/generated/*' | sort)

if [ "${#sources[@]}" -gt 0 ]; then
  # (3) contract discriminator string literals
  discriminators='hello|hello\.ack|heartbeat|state\.snapshot|local\.started|local\.extended|local\.ended|local\.forceEnd|remote\.started|remote\.ended|bypass\.detected|update\.available|device\.updated|subscribe'
  if grep -nE "(['\"\`])(${discriminators})\1" "${sources[@]}"; then
    report "hand-written contract discriminator literal found (see lines above)"
  fi

  # (4) hand-declared contract types
  if grep -nE '^[[:space:]]*(export[[:space:]]+)?(declare[[:space:]]+)?(interface|type)[[:space:]]+(ServerInfo|AgentMessage|ClientMessage|AdminMessage|[A-Za-z0-9_]*Payload)\b' "${sources[@]}"; then
    report "hand-written contract type declaration found (see lines above)"
  fi
fi

# (5) App.tsx must consume the generated ServerInfo
if ! grep -qE "from '\./generated/ServerInfo'" server/web/src/App.tsx; then
  report "server/web/src/App.tsx does not import from './generated/ServerInfo'"
fi

if [ "$fail" -ne 0 ]; then
  exit 1
fi
echo "check-contract-types: OK"
