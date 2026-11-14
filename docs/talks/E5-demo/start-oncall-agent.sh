#!/usr/bin/env bash
# Hands the on-call agent its access, like an incident commander would: a Viewer service account token for the
# on-call Grafana (http://localhost:3300), valid for 8 hours, and nothing else.
#
# It creates (or reuses) the oncall-agent service account, mints a new token, and prepares a folder outside this
# repository with the investigating-incidents skill and a .claude/settings.json that hands over the token, allows
# curl to the on-call Grafana and jq, and denies reading this repository. Then run claude in that folder.
#
# Usage: start-oncall-agent.sh [folder]   (default: a new temporary folder)
# Needs: the application's AppHost running, dotnet, curl, jq.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
grafana="http://localhost:3300"
folder="${1:-$(mktemp -d)/oncall}"

password=$(dotnet user-secrets list --project "$repo/src/Aspire/Acheve.AppHost" \
  | sed -n 's/^Parameters:oncall-grafana-admin-password = //p')
if [[ -z "$password" ]]; then
  echo "No oncall-grafana-admin-password in the AppHost user secrets: start the AppHost once first." >&2
  exit 1
fi

admin() { curl -sSf -u "admin:${password}" -H 'Content-Type: application/json' "$@"; }

id=$(admin "$grafana/api/serviceaccounts/search?query=oncall-agent" \
  | jq -r '.serviceAccounts[] | select(.name == "oncall-agent") | .id' | head -n 1)
if [[ -z "$id" ]]; then
  id=$(admin -X POST "$grafana/api/serviceaccounts" -d '{"name":"oncall-agent","role":"Viewer"}' | jq -r .id)
  echo "Created the oncall-agent service account (Viewer)."
else
  echo "Reusing the oncall-agent service account."
fi

token=$(admin -X POST "$grafana/api/serviceaccounts/$id/tokens" \
  -d "{\"name\":\"oncall-$(date +%Y%m%d-%H%M%S)-$RANDOM\",\"secondsToLive\":28800}" | jq -r .key)

mkdir -p "$folder/.claude/skills"
rm -rf "$folder/.claude/skills/investigating-incidents"
cp -R "$repo/.claude/skills/investigating-incidents" "$folder/.claude/skills/"
# Deny the whole repository, the main checkout too when this runs from a worktree.
# Absolute paths in permission rules start with //.
root="$(dirname "$(git -C "$repo" rev-parse --path-format=absolute --git-common-dir)")"
jq -n --arg token "$token" --arg repo "/$root/**" '{
  env: { GRAFANA_TOKEN: $token },
  permissions: {
    allow: ["Bash(curl * http://localhost:3300/*)", "Bash(curl *http://localhost:3300/*)", "Bash(jq *)"],
    deny: [("Read(" + $repo + ")"), ("Grep(" + $repo + ")"), ("Glob(" + $repo + ")")]
  }
}' > "$folder/.claude/settings.json"

echo "Token minted, valid for 8 hours. The agent's folder is ready:"
echo
echo "  cd $folder && claude"
