# The on-call Grafana API

Grafana at `http://localhost:3300`. Every request carries the token; every recipe below was run as written. Pipe through `jq` and keep only the fields you need: raw responses are large.

```bash
G=http://localhost:3300
A=(-H "Authorization: Bearer $GRAFANA_TOKEN")
now=$(date +%s)
```

Times: the Prometheus and Tempo APIs take Unix seconds, Loki takes Unix nanoseconds (`${now}000000000`). Responses are in UTC.

## Alerts

Firing and pending alerts, with when they became active and their value:

```bash
curl -s "${A[@]}" $G/api/prometheus/grafana/api/v1/alerts \
  | jq -c '[.data.alerts[] | select(.state | test("Alerting|Pending")) | {name: .labels.alertname, state, activeAt, value, summary: .annotations.summary}]'
```

The rules, with their query and state:

```bash
curl -s "${A[@]}" $G/api/prometheus/grafana/api/v1/rules \
  | jq -c '.data.groups[].rules[] | {name, state, query}'
```

Alert state history (each change is stored as an annotation, in epoch milliseconds):

```bash
curl -s "${A[@]}" "$G/api/annotations?from=$(( (now-3600)*1000 ))&limit=50" \
  | jq -c '[.[] | {alertName, newState, time: (.time/1000|strftime("%H:%M:%S"))}]'
```

## Prometheus (metrics)

Instant query: the value now.

```bash
curl -sG "${A[@]}" $G/api/datasources/proxy/uid/prometheus/api/v1/query \
  --data-urlencode 'query=sum by (estimation_failure_reason) (increase(estimations_cases_finished_total[15m]))' \
  | jq -c '.data.result[] | {labels: .metric, value: .value[1]}'
```

Range query: a value per minute, which you need to build a timeline:

```bash
curl -sG "${A[@]}" $G/api/datasources/proxy/uid/prometheus/api/v1/query_range \
  --data-urlencode 'query=sum(rate(estimations_cases_finished_total{estimation_result="failure"}[1m])) * 60' \
  --data-urlencode "start=$((now-3600))" --data-urlencode "end=$now" --data-urlencode step=60 \
  | jq -c '.data.result[] | {labels: .metric, values: [.values[] | [(.[0]|strftime("%H:%M")), .[1]]]}'
```

Discover what exists:

```bash
curl -s "${A[@]}" $G/api/datasources/proxy/uid/prometheus/api/v1/label/__name__/values | jq -c '.data'
curl -s "${A[@]}" $G/api/datasources/proxy/uid/prometheus/api/v1/label/<label>/values | jq -c '.data'
```

## Loki (logs)

Logs are indexed by `service_name` only. Filter the rest with structured metadata (`severity_text`, `scope_name`, `caseNumber`, `trace_id`) or line filters (`|=`, `|~`).

```bash
curl -sG "${A[@]}" $G/api/datasources/proxy/uid/loki/loki/api/v1/query_range \
  --data-urlencode 'query={service_name="processmanager"} | severity_text=~"Warning|Error"' \
  --data-urlencode "start=$((now-1800))000000000" --data-urlencode "end=${now}000000000" --data-urlencode limit=50 \
  | jq -r '.data.result[] | .stream.service_name as $s | .values[] | "\(.[0][0:10]|tonumber|strftime("%H:%M:%S")) \($s) \(.[1])"' | sort
```

Count per minute (a metric query over logs):

```bash
curl -sG "${A[@]}" $G/api/datasources/proxy/uid/loki/loki/api/v1/query_range \
  --data-urlencode 'query=sum by (service_name) (count_over_time({service_namespace="application"} | severity_text="Warning" [1m]))' \
  --data-urlencode "start=$((now-3600))000000000" --data-urlencode "end=${now}000000000" --data-urlencode step=60
```

To see the structured metadata of each line, add `-H 'X-Loki-Response-Encoding-Flags: categorize-labels'`.

## Tempo (traces)

Search with TraceQL. Our call to an external system is a span on the caller with `peer.service`:

```bash
curl -sG "${A[@]}" $G/api/datasources/proxy/uid/tempo/api/search \
  --data-urlencode 'q={ resource.service.name = "estimationprocessor" && span.peer.service = "external-estimations" }' \
  --data-urlencode "start=$((now-900))" --data-urlencode "end=$now" --data-urlencode limit=10 --data-urlencode spss=5 \
  | jq -c '.traces[] | {traceID, start: (.startTimeUnixNano|tonumber/1e9|strftime("%H:%M:%S")), spans: [.spanSets[0].spans[] | {ms: (.durationNanos|tonumber/1e6|floor), attrs: [.attributes[]? | {(.key): (.value.stringValue // .value.intValue)}] | add}]}'
```

Useful filters: `status = error`, `duration > 3s`, `span.http.response.status_code >= 500`, `span.estimation.case_number = "<guid>"`.

One trace, span by span:

```bash
curl -s "${A[@]}" $G/api/datasources/proxy/uid/tempo/api/v2/traces/<traceID> \
  | jq -c '.trace.resourceSpans[] | (.resource.attributes[] | select(.key=="service.name") | .value.stringValue) as $s | .scopeSpans[].spans[] | {service: $s, name, ms: (((.endTimeUnixNano|tonumber) - (.startTimeUnixNano|tonumber))/1e6|floor), status: .status.code, attrs: [.attributes[]? | {(.key): (.value.stringValue // .value.intValue)}] | add}'
```

## Dashboards

The teams' own queries are a good starting point. List the dashboards, then read a dashboard's panel titles and queries:

```bash
curl -s "${A[@]}" "$G/api/search?type=dash-db&query=Estimations" | jq -c '[.[] | {uid, title}]'
curl -s "${A[@]}" $G/api/dashboards/uid/estimations-case-flow \
  | jq -c '.dashboard.panels[] | select(.targets) | {title, queries: [.targets[] | (.expr // .query)]}'
```
