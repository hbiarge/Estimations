#!/usr/bin/env bash
# Checks that the on-call Grafana (http://localhost:3300) holds none of the answers: no chaos metrics, logs or span
# tags, no tools or external systems, no Chaos run dashboard. Every check also runs against the main Grafana, which
# must return data: run it while chaos is active, or an empty result proves nothing.
#
# Usage: check-blindfold.sh <main Grafana URL>
# Auth on 3300: GRAFANA_TOKEN (the agent's token), or ONCALL_ADMIN_PASSWORD for the admin user.
# The main Grafana has anonymous access. Needs curl and jq.
set -euo pipefail

main="${1:?Usage: check-blindfold.sh <main Grafana URL>}"
main="${main%/}"
oncall="http://localhost:3300"

if [[ -n "${GRAFANA_TOKEN:-}" ]]; then
  oncall_auth=(-H "Authorization: Bearer ${GRAFANA_TOKEN}")
elif [[ -n "${ONCALL_ADMIN_PASSWORD:-}" ]]; then
  oncall_auth=(-u "admin:${ONCALL_ADMIN_PASSWORD}")
else
  echo "Set GRAFANA_TOKEN or ONCALL_ADMIN_PASSWORD." >&2
  exit 2
fi

now=$(date +%s)
from=$((now - 1800))

# get <grafana> <path>: GET with the right auth, body on stdout.
get() {
  if [[ "$1" == "$oncall" ]]; then
    curl -sfG "${oncall_auth[@]}" "$1$2" "${@:3}"
  else
    curl -sfG "$1$2" "${@:3}"
  fi
}

# Each check prints how many forbidden things it found.
metric_names() {
  get "$1" /api/datasources/proxy/uid/prometheus/api/v1/label/__name__/values \
    | jq '[.data[] | select(test("^(chaos_|consumption_paused|traffic_)"))] | length'
}

service_names() {
  get "$1" /api/datasources/proxy/uid/prometheus/api/v1/label/service_name/values \
    | jq '[.data[] | select(test("^(trafficgenerator|chaosmonkey|external-)"))] | length'
}

chaos_logs() {
  get "$1" /api/datasources/proxy/uid/loki/loki/api/v1/query_range \
    --data-urlencode 'query={service_name=~".+"} |= "Chaos "' \
    --data-urlencode "start=${from}000000000" --data-urlencode "end=${now}000000000" --data-urlencode 'limit=100' \
    | jq '[.data.result[].values[]] | length'
}

chaos_spans() {
  get "$1" /api/datasources/proxy/uid/tempo/api/search \
    --data-urlencode 'q={ span.chaos.injected != nil }' \
    --data-urlencode "start=${from}" --data-urlencode "end=${now}" --data-urlencode 'limit=20' \
    | jq '.traces | length'
}

# The requests that switch chaos, consumption and resilience (the chaos monkey calls every service at every step,
# so they mark each step).
switch_routes() {
  get "$1" /api/datasources/proxy/uid/prometheus/api/v1/label/http_route/values \
    | jq '[.data[] | select(test("^/(chaos|consumption|resilience)"))] | length'
}

switch_logs() {
  get "$1" /api/datasources/proxy/uid/loki/loki/api/v1/query_range \
    --data-urlencode 'query={service_name=~".+"} |~ "/(chaos|consumption|resilience)/|Message consumption"' \
    --data-urlencode "start=${from}000000000" --data-urlencode "end=${now}000000000" --data-urlencode 'limit=100' \
    | jq '[.data.result[].values[]] | length'
}

switch_spans() {
  get "$1" /api/datasources/proxy/uid/tempo/api/search \
    --data-urlencode 'q={ span.url.path =~ "/(chaos|consumption|resilience)/.*" }' \
    --data-urlencode "start=${from}" --data-urlencode "end=${now}" --data-urlencode 'limit=20' \
    | jq '.traces | length'
}

chaos_dashboard() {
  local code
  if [[ "$1" == "$oncall" ]]; then
    code=$(curl -s -o /dev/null -w '%{http_code}' "${oncall_auth[@]}" "$1/api/dashboards/uid/estimations-chaos-run")
  else
    code=$(curl -s -o /dev/null -w '%{http_code}' "$1/api/dashboards/uid/estimations-chaos-run")
  fi
  [[ "$code" == 200 ]] && echo 1 || echo 0
}

failed=0
printf '%-34s %6s %8s  %s\n' check main on-call result
for check in metric_names service_names chaos_logs chaos_spans switch_routes switch_logs switch_spans chaos_dashboard; do
  m=$($check "$main" || echo error)
  o=$($check "$oncall" || echo error)
  result=OK
  if [[ "$o" != 0 ]]; then
    result="FAIL (on-call has it)"
    failed=1
  elif [[ "$m" == 0 || "$m" == error ]]; then
    result="FAIL (main is empty: is chaos running?)"
    failed=1
  fi
  printf '%-34s %6s %8s  %s\n' "$check" "$m" "$o" "$result"
done

exit $failed
