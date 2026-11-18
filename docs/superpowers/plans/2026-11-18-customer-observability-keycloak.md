# Customer Observability + Keycloak Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the "client" domain concept to "Customer", authenticate customers against a local Keycloak (three customers), and carry the customer through logs, traces, metrics, dashboards and alerts without letting an unbounded customer count blow up metric cardinality.

**Architecture:** The customer id comes from a `customer_id` claim in the Keycloak access token and travels as an explicit `CustomerId` field on every message that a worker or the saga handles. One helper (`CustomerTelemetry.BeginScope`) puts it on the current span and in a log scope, so it is a high-cardinality-safe attribute on logs (Loki structured metadata) and spans. Metrics get a *bounded* `estimation.customer_id` label: values on a configured allow-list, everything else collapsed to `other` (`CustomerLabels`). Alerts become per-customer instances routed by that label to internal on-call contact points.

**Tech Stack:** .NET 11 / Aspire 13.6.5, Rebus, OpenTelemetry, `Aspire.Hosting.Keycloak` 13.6.5-preview.1.26559.7, Grafana (LGTM 0.39.0) provisioning, xUnit + Shouldly.

**Spec:** none separate; decisions agreed in the session of 2026-11-18 and restated in Global Constraints.

## Global Constraints

- Domain term is **Customer** everywhere in our own code, docs, dashboards and alerts. **Do not rename:** every `HttpClient`/`IHttpClientFactory`, the generated `StateHolderServiceClient`, the OAuth vocabulary `client_id`/`ClientId`/`ClientSecret` at the identity boundary (`TokenOptions`-style settings and Keycloak client ids), Tempo service-graph labels `client`/`server`, `peer.service`.
- Customers: `acme`, `globex`, `initech` (Keycloak client id == customer id). **No customer tier.** Duende demo server is removed entirely.
- Token claim: `customer_id` (hard-coded per client in the realm). Audience: `api`. Realm: `estimations`. Keycloak host port: `8180` (fixed, so the token issuer is stable).
- Attribute/label name everywhere: `estimation.customer_id` (Prometheus label `estimation_customer_id`, Loki structured metadata `estimation_customer_id`). Allow-list setting: `Customers:Known` (array). Overflow value: `other`.
- Logs and spans carry the full customer id (unbounded OK). Metrics carry only the bounded value, and only on `estimations.cases.started`, `estimations.cases.finished`, `estimations.case.duration`. No customer label on image/state-duration metrics or StateHolder gauges.
- `grafana/provisioning/alerting.yaml` and `dashboards.yaml` are single-file bind mounts: edit with the Edit/Write tool, **never `sed -i`**; reload with `POST /api/admin/provisioning/alerting/reload` (restart the `lgtm` resource if it reports "no such file"). `grafana/dashboards/` is a directory mount and is safe.
- `OncallGrafana.cs` rewrites `alerting.yaml`/dashboards for the on-call Grafana and fails AppHost start if text mentions chaos/tools it cannot rewrite: new alert text must not mention chaos, the traffic generator or the monkey.
- Which customer's notifications fail in the demo is **out of scope** (decided later); all three customers keep using the one fake notifications endpoint.
- Tests live in `test/` with xUnit + Shouldly; run all with `dotnet test Estimations.slnx`. Shared-library tests (`Acheve.Common.Shared`) live in `test/Application/Acheve.Application.ProcessManager.Tests/Handlers/`.

## Review Focus

1. A token without a usable `customer_id` claim is refused (403), never labelled `"N/A"` — pinned in Task 3 (`CustomerClaimsTests`).
2. A flood of distinct/unknown/odd-cased customer ids creates no new metric series; they all become `other` — pinned in Task 3 (`CustomerLabelsTests`) and Task 5 (`EstimationMetricsTests`).
3. The first case of a customer after a restart is counted by `rate()`/`increase()`: counters are pre-seeded at 0 for every known customer plus `other` — pinned in Task 5.
4. A null/empty customer id in a message never throws in a handler and opens no scope — pinned in Task 3 (`CustomerTelemetryTests`).
5. A customer with 1 failed case out of 2 does not page, and a customer with no traffic produces no alert (min-volume guard, `noDataState: OK`) — verified in Task 8 via the Grafana rules API.

## File Structure

| File | Responsibility |
|---|---|
| `src/Common/Acheve.Common.Shared/CustomerClaims.cs` (new) | Reads the customer id from a `ClaimsPrincipal` |
| `src/Common/Acheve.Common.Shared/CustomerTelemetry.cs` (new) | Span tag + log scope for a customer id |
| `src/Common/Acheve.Common.Shared/CustomerLabels.cs` (new) | Allow-list → bounded metric label |
| `src/Aspire/Acheve.AppHost/keycloak/estimations-realm.json` (new) | Realm with three service-account clients |
| `src/Aspire/Acheve.AppHost/AppHost.cs` | Keycloak resource, `Customers:Known` to ProcessManager, Api/traffic wiring |
| `src/Application/Acheve.Application.Api/Program.cs` | Authority/audience from config, customer policy |
| `src/Tools/Acheve.Tools.TrafficGenerator/*` | Per-customer token, callback URL, weight, metric tag |
| `src/Aspire/Acheve.AppHost/grafana/dashboards/{overview,case-flow}.json` | `$customer` variable and filters |
| `src/Aspire/Acheve.AppHost/grafana/provisioning/alerting.yaml` | Per-customer rules, contact points, policy tree |
| `docs/*`, `.claude/skills/investigating-incidents/references/*`, `requests.http`, `README.md` | Rename + new signals + runbook |

---

### Task 1: Rename Client → Customer (mechanical)

**Files:** Modify every file listed by the grep in Step 1 (src, test, docs, dashboards, alerting, skill references). No behavior change.

**Interfaces:**
- Produces: `CustomerId` (messages `EstimationRequested`, `EstimationStateChanged`, `CaseCompleted`, `CaseFailed`; saga `EstimationState.CustomerId`; StateHolder `CaseSnapshot.CustomerId`; proto `StateRequest.customerId`), `CustomerNotifier`, `EstimationStates.CustomerNotified = 40` / `CustomerNotificationError = 41`, `CaseCompleted.CustomerNotified`/`CaseFailed.CustomerNotified` (bool), `Constants.CustomerCallbacks.SigningKeyKey = "CustomerCallbacks:SigningKey"`, DB column `customer_id`.

- [ ] **Step 1: List the symbols to change**

Run: `grep -rInE 'ClientNotif|ClientNotified|ClientNotificationError|ClientId|clientId|client_id|ClientCallbacks|NotifyClient' src test docs .claude/skills README.md requests.http | grep -v HttpClient`
Expected: the ~107 hits in application code, tests, docs, dashboards and alerting. Leave the OAuth hits (`TokenOptions.ClientId/ClientSecret`, `grant_type`/`client_id` in `TokenProvider.cs` and `requests.http`) and `HttpClient` alone.

- [ ] **Step 2: Apply the rename table**

| Old | New |
|---|---|
| `ClientId` / `clientId` (messages, saga data, snapshot, endpoints, log placeholders) | `CustomerId` / `customerId` |
| proto `string clientId = 2` | `string customerId = 2` (field number unchanged) |
| `ClientNotifier` | `CustomerNotifier` |
| `EstimationStates.ClientNotified` / `ClientNotificationError` | `CustomerNotified` / `CustomerNotificationError` |
| `CaseCompleted.ClientNotified`, `CaseFailed.ClientNotified` | `CustomerNotified` |
| `Constants.ClientCallbacks`, config key `ClientCallbacks:SigningKey` | `Constants.CustomerCallbacks`, `CustomerCallbacks:SigningKey` |
| prose "client" meaning the tenant (logs, docs, "Client success rate", "What clients saw", skill references) | "customer" |
| PromQL/doc literals `ClientNotified`, `ClientNotificationError` (`overview.json`, `case-flow.json`, `alerting.yaml`, docs) | `CustomerNotified`, `CustomerNotificationError` |

Use `git mv` for renamed files (`ClientNotifier.cs` → `CustomerNotifier.cs`).

- [ ] **Step 3: Rename the Postgres column idempotently**

In `src/Application/Acheve.Application.StateHolder/CaseStateStore.cs`, the schema creation changes `client_id` to `customer_id` (also in the PK, INSERT, SELECT) and is preceded by a statement that renames an existing `client_id` column (guard with `information_schema.columns` so a fresh database and a migrated one both work).

- [ ] **Step 4: Check the signing-key secret**

Run: `dotnet user-secrets list --project src/Aspire/Acheve.AppHost && dotnet user-secrets list --project src/Aspire/Acheve.External.AppHost`
Expected: if either lists `ClientCallbacks:SigningKey`, re-set it as `CustomerCallbacks:SigningKey` (otherwise notifications silently go unsigned).

- [ ] **Step 5: Build and test**

Run: `dotnet build Estimations.slnx && dotnet test Estimations.slnx`
Expected: build succeeds with no `Client*` references left (`grep` from Step 1 shows only OAuth/HttpClient lines); all tests PASS.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "Rename the client concept to customer"
```

---

### Task 2: Keycloak container and Api authentication; remove Duende

**Files:**
- Create: `src/Aspire/Acheve.AppHost/keycloak/estimations-realm.json`
- Modify: `Directory.Packages.props`, `src/Aspire/Acheve.AppHost/Acheve.AppHost.csproj`, `src/Aspire/Acheve.AppHost/AppHost.cs`, `src/Common/Acheve.Common.Shared/Constants.cs`, `src/Application/Acheve.Application.Api/Program.cs`, `src/Application/Acheve.Application.Api/appsettings.json`, `requests.http`

**Interfaces:**
- Produces: `Constants.Resources.Keycloak = "keycloak"`, `Constants.Identity.Realm = "estimations"`, `Constants.Identity.Port = 8180`; Api reads `Authentication:Authority` and `Authentication:Audience`; token endpoint `http://localhost:8180/realms/estimations/protocol/openid-connect/token`; token claim `customer_id`. (Task 3 adds the policy that requires the claim.)

- [ ] **Step 1: Add the package**

`Directory.Packages.props`: `<PackageVersion Include="Aspire.Hosting.Keycloak" Version="13.6.5-preview.1.26559.7" />`; reference it (no version) in `Acheve.AppHost.csproj`.

- [ ] **Step 2: Write the realm file**

`estimations-realm.json`: realm `estimations`, `enabled: true`, `accessTokenLifespan: 300`, `sslRequired: "none"`. Three clients with `clientId` and `name` in `acme`, `globex`, `initech`; each `publicClient: false`, `serviceAccountsEnabled: true`, `standardFlowEnabled: false`, `directAccessGrantsEnabled: false`, `clientAuthenticatorType: "client-secret"`, `secret: "<clientId>-dev-secret"`, and two `protocolMappers`: `oidc-audience-mapper` (`included.custom.audience: "api"`, `access.token.claim: "true"`) and `oidc-hardcoded-claim-mapper` (`claim.name: "customer_id"`, `claim.value: "<clientId>"`, `jsonType.label: "String"`, `access.token.claim: "true"`). Secrets are dev-only and committed on purpose.

- [ ] **Step 3: Add the resource**

In `AppHost.cs`, before the Api: `builder.AddKeycloak(Constants.Resources.Keycloak, port: Constants.Identity.Port).WithDataVolume().WithRealmImport("./keycloak")`. The Api gets `.WithEnvironment("Authentication__Authority", $"http://localhost:{Constants.Identity.Port}/realms/{Constants.Identity.Realm}")`, `.WithEnvironment("Authentication__Audience", "api")` and `.WaitFor(keycloak)`. Keep the volume only if realm re-import on changes works without wiping it; otherwise drop `WithDataVolume()` (realm is the source of truth, no state to keep).

- [ ] **Step 4: Point the Api at it**

`Program.cs`: `options.Authority = builder.Configuration["Authentication:Authority"]`, `options.Audience = builder.Configuration["Authentication:Audience"]`, `options.RequireHttpsMetadata = !builder.Environment.IsDevelopment()`, `NameClaimType = "customer_id"`. No Duende URL may remain: `grep -rn duende src docs README.md requests.http` must return nothing after this task and Task 9.

- [ ] **Step 5: Update `requests.http`**

The token request posts to the Keycloak endpoint with `client_id=acme&client_secret=acme-dev-secret` (no `scope`), still saving `{{token}}`.

- [ ] **Step 6: Verify against a running AppHost**

Run: `dotnet run --project src/Aspire/Acheve.AppHost` (wait for `keycloak` and `api` healthy), then in another terminal:

```bash
TOKEN=$(curl -s -d grant_type=client_credentials -d client_id=globex -d client_secret=globex-dev-secret \
  http://localhost:8180/realms/estimations/protocol/openid-connect/token | jq -r .access_token)
echo "$TOKEN" | jq -R 'split(".")[1] | @base64d | fromjson | {iss, aud, customer_id, azp}'
```
Expected: `iss` ends `/realms/estimations`, `aud` contains `"api"`, `customer_id` is `"globex"`. Then `curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $TOKEN" <api-url>/estimations/00000000-0000-0000-0000-000000000000` prints `404` (not 401); the same call without the header prints `401`.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "Authenticate with a local Keycloak and drop the Duende demo server"
```

---

### Task 3: Customer primitives (claims, telemetry, labels)

**Files:**
- Create: `src/Common/Acheve.Common.Shared/CustomerClaims.cs`, `CustomerTelemetry.cs`, `CustomerLabels.cs`
- Test: `test/Application/Acheve.Application.ProcessManager.Tests/Handlers/CustomerClaimsTests.cs`, `CustomerTelemetryTests.cs`, `CustomerLabelsTests.cs`
- Modify: `src/Application/Acheve.Application.Api/Program.cs` (policy), `Acheve.Common.Shared.csproj` (add `Microsoft.Extensions.Logging.Abstractions` only if not already referenced)

**Interfaces:**
- Produces:
  - `public static class CustomerClaims { public const string ClaimType = "customer_id"; public static string? CustomerIdOf(ClaimsPrincipal user); }` — null for missing, empty or whitespace claim.
  - `public static class CustomerTelemetry { public const string Tag = "estimation.customer_id"; public static IDisposable? BeginScope(ILogger logger, string? customerId); }` — sets `Activity.Current?.SetTag(Tag, id)` and returns `logger.BeginScope` with one `KeyValuePair<string, object?>(Tag, id)`; returns null and does nothing for null/empty/whitespace.
  - `public sealed class CustomerLabels { public const string Other = "other"; public CustomerLabels(IEnumerable<string> known); public IReadOnlyCollection<string> Known { get; } public IReadOnlyCollection<string> All { get; } /* Known + Other */ public string For(string? customerId); public static CustomerLabels FromConfiguration(IConfiguration configuration); }` — `For` is ordinal-case-sensitive, returns `Other` for unknown/null/empty; `FromConfiguration` reads `Customers:Known`.

- [ ] **Step 1: Write the failing tests**

- `CustomerClaimsTests`: `Returns_the_customer_id_claim`, and `Returns_null_when_the_claim_is_missing_empty_or_whitespace` (Theory over `null`, `""`, `"  "`).
- `CustomerLabelsTests`: `Known_customer_keeps_its_name` (`For("acme") == "acme"`); `Unknown_null_empty_and_differently_cased_ids_become_other` (`"zzz"`, `null`, `""`, `"ACME"` → `"other"`); `All_is_known_plus_other`; `A_thousand_distinct_ids_map_to_at_most_known_plus_one_values` (distinct `For(...)` results ≤ `Known.Count + 1`); `FromConfiguration_reads_Customers_Known` (in-memory configuration).
- `CustomerTelemetryTests`: `Tags_the_current_activity_and_opens_a_log_scope` (an `ActivityListener` + `Activity`, a capturing `ILogger` whose `BeginScope` records the state: tag `estimation.customer_id == "acme"`, scope state contains `("estimation.customer_id","acme")`); `Null_or_empty_id_opens_no_scope_and_does_not_throw` (returns null, activity untagged).

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test test/Application/Acheve.Application.ProcessManager.Tests --filter "FullyQualifiedName~Customer"`
Expected: build FAIL (types not defined).

- [ ] **Step 3: Implement the three types** in `Acheve.Common.Shared` with the signatures above.

- [ ] **Step 4: Run to verify they pass**

Run: same command. Expected: PASS.

- [ ] **Step 5: Require the claim in the Api**

In `Program.cs`, the authorization policy of `/estimations` adds `.RequireAssertion(ctx => CustomerClaims.CustomerIdOf(ctx.User) is not null)` (in `EstimationsEndpoints.MapEstimations`), and both endpoints read `var customerId = CustomerClaims.CustomerIdOf(user)!;` instead of `user.Identity?.Name ?? "N/A"`.

- [ ] **Step 6: Build and commit**

Run: `dotnet build Estimations.slnx && dotnet test Estimations.slnx` → PASS.

```bash
git add -A && git commit -m "Add customer claim, telemetry and metric-label primitives"
```

---

### Task 4: Carry the customer through messages, logs and spans

**Files:**
- Modify: `src/Common/Acheve.Common.Messages/{DownloadImage,AnalyseImage,EstimateCase,NotifyEstimation,NotifyEstimationFailure}.cs`
- Modify: `EstimationSaga.cs`, `EstimationSaga.Stage01–04.cs` (saga), `ImageDownloads/Handlers/DownloadImageHandler.cs`, `ExternalImageProcessor/Handlers/AnalyseImageHandler.cs`, `EstimationProcessor/Handlers/EstimateCaseHandler.cs`, `CallbackNotifier/Handlers/{NotifyEstimationHandler,NotifyEstimationFailureHandler}.cs`, `StateHolder/Handlers/EstimationStateChangedHandler.cs`, `Api/Features/Estimations/EstimationsEndpoints.cs`
- Test: `test/Application/Acheve.Application.ProcessManager.Tests/Handlers/EstimationSagaFlowTests.cs`, `Factories/Sagas.cs`, new `Factories/CapturingLogger.cs`

**Interfaces:**
- Consumes: `CustomerTelemetry.BeginScope(ILogger, string?)` (Task 3), `Data.CustomerId` (Task 1).
- Produces: `public required string CustomerId { get; init; }` on the five command messages; saga helper `private IDisposable? BeginCustomerScope() => CustomerTelemetry.BeginScope(logger, Data.CustomerId);` called as the first statement of every saga `Handle` (using declaration; for `EstimationRequested` call `CustomerTelemetry.BeginScope(logger, message.CustomerId)` because `Data` is not set yet); test helper `CapturingLogger<T> : ILogger<T>` exposing `IReadOnlyList<object?> Scopes`; `Sagas.Create(..., ILogger<EstimationSaga>? logger = null)`.

- [ ] **Step 1: Write the failing tests** in `EstimationSagaFlowTests`: `Commands_sent_by_the_saga_carry_the_customer_id` (drive `EstimationRequested` with `CustomerId = "acme"` through download → analysis → estimation → notification; assert every `DownloadImage`, `AnalyseImage`, `EstimateCase`, `NotifyEstimation` in `_bus.SentMessages` has `CustomerId == "acme"`), and `Saga_logs_inside_a_customer_scope` (with `CapturingLogger`, deliver `ImageDownloaded`; some scope contains `("estimation.customer_id","acme")`). Update `Factories/Messages.cs` for `CustomerId = "acme"`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test test/Application/Acheve.Application.ProcessManager.Tests --filter "FullyQualifiedName~EstimationSagaFlowTests"`
Expected: FAIL (property/scope missing).

- [ ] **Step 3: Add `CustomerId` to the five commands; the saga fills it from `Data.CustomerId` wherever it builds them** (`Stage01–04`, including the `NotificationCommand()` builders).

- [ ] **Step 4: Open the scope** at the top of each saga handler (15 handlers), each worker handler (use `message.CustomerId`; the handlers already hold an `ILogger`), `EstimationStateChangedHandler` (`message.CustomerId`) and both Api endpoints (`customerId` from Task 3; the Api tags the request span through the same call).

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test Estimations.slnx` → PASS.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "Carry the customer on commands and tag logs and spans with it"
```

---

### Task 5: Bounded customer label on the case metrics

**Files:**
- Modify: `src/Application/Acheve.Application.ProcessManager/Handlers/EstimationMetrics.cs`, `.../Program.cs`, `.../Handlers/EstimationSaga.cs` + `EstimationSaga.Stage04.cs` (call sites), `src/Aspire/Acheve.AppHost/AppHost.cs`, `src/Aspire/Acheve.AppHost/appsettings.json`
- Test: `test/Application/Acheve.Application.ProcessManager.Tests/Handlers/EstimationMetricsTests.cs`, `Factories/Sagas.cs`

**Interfaces:**
- Consumes: `CustomerLabels` (Task 3).
- Produces: `new EstimationMetrics(IMeterFactory meterFactory, CustomerLabels customers)`; `CaseStarted(string customerId)`; `CaseFinished(string customerId, CaseResult result, EstimationStates finalState, string failureReason, TimeSpan? duration)`; tag `estimation.customer_id` (value `customers.For(customerId)`) on `estimations.cases.started` and `estimations.cases.finished`; `estimations.case.duration` tags become `estimation.result` + `estimation.customer_id` only; every counter series is seeded at 0 for each of `customers.All`. Config: `Customers:Known` = `["acme","globex","initech"]` in the AppHost `appsettings.json`, passed to ProcessManager as env `Customers__Known__0..2`.

- [ ] **Step 1: Write the failing tests** in `EstimationMetricsTests` (the test factory builds `CustomerLabels(["acme","globex"])`): `Finished_case_is_tagged_with_its_known_customer` (`estimation.customer_id == "acme"` on started and finished); `Unknown_customer_is_recorded_as_other`; `A_thousand_distinct_customers_add_no_series_beyond_known_plus_other` (start 1000 cases with distinct ids; distinct `estimation.customer_id` values across `_started` ≤ 3); `Every_known_customer_and_other_start_at_zero` (collector created before the metrics sees a 0 measurement per `All` value on `cases.started` and per customer×result×state×reason on `cases.finished`); `Case_duration_has_only_result_and_customer_tags`. Existing assertions in the file gain the customer tag where they inspect `Tags`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test test/Application/Acheve.Application.ProcessManager.Tests --filter "FullyQualifiedName~EstimationMetricsTests"` → FAIL (constructor/overload).

- [ ] **Step 3: Implement** the signatures above. `StartCountersAtZero` loops `customers.All` around the existing seeding. `Program.cs`: `builder.Services.AddSingleton(CustomerLabels.FromConfiguration(builder.Configuration));`. Update the saga call sites to pass `Data.CustomerId`. AppHost: add the env vars from the `Customers:Known` setting.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test Estimations.slnx` → PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Tag the case metrics with a bounded customer label"
```

---

### Task 6: Traffic generator plays the three customers

**Files:**
- Modify: `src/Tools/Acheve.Tools.TrafficGenerator/{TrafficOptions,TokenProvider,EstimationsClient,CaseFactory,CaseRunner,TrafficWorker,TrafficMetrics,Program}.cs`, `appsettings.json`, `src/Aspire/Acheve.AppHost/AppHost.cs`
- Create: `src/Tools/Acheve.Tools.TrafficGenerator/CustomerPicker.cs`
- Test: `test/Tools/Acheve.Tools.TrafficGenerator.Tests/{CaseFactoryTests,CaseRunnerTests}.cs`, new `CustomerPickerTests.cs`

**Interfaces:**
- Produces: `TrafficOptions.Customers: CustomerOptions[]` (replaces `TrafficOptions.CallbackUrl` and the single `Token`'s credentials), `TrafficOptions.TokenEndpoint: string` (required); `public sealed class CustomerOptions { [Required] string Name; [Required] string ClientId; [Required] string ClientSecret; [Required] string CallbackUrl; [Range(0, 1000)] double Weight = 1; }`; `CustomerPicker(TrafficOptions options)` with `CustomerOptions Pick(Random random)` (weighted); `TokenProvider.GetAsync(CustomerOptions customer, CancellationToken)` / `Invalidate(string customerName)` (one cached token per customer); `IEstimationsClient.StartAsync(CustomerOptions customer, NewEstimationRequest request, CancellationToken)` and `GetStateAsync(CustomerOptions customer, string ticket, CancellationToken)`; `CaseFactory.Create(CustomerOptions customer)` (callback URL from the customer); `CaseRunner.RunAsync(CustomerOptions customer, NewEstimationRequest request, CancellationToken)`; `TrafficMetrics.Started(string customer)` and `Finished(string customer, CaseOutcome outcome, TimeSpan? duration)` with tag `traffic.customer`.

- [ ] **Step 1: Write the failing tests**: `CustomerPickerTests.Picks_in_proportion_to_weight` (seeded `Random`, weights 1/3/0 → third never picked, second ≈3× first within tolerance) and `Rejects_when_every_weight_is_zero`; `CaseFactoryTests.Request_uses_the_customers_callback_url`; `CaseRunnerTests.Case_is_run_and_measured_for_the_given_customer` (fake client records the customer passed to `StartAsync`/`GetStateAsync`).

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test test/Tools/Acheve.Tools.TrafficGenerator.Tests` → FAIL (build).

- [ ] **Step 3: Implement** the signatures above. `TokenProvider` posts to `options.TokenEndpoint` with `grant_type=client_credentials`, the customer's `ClientId`/`ClientSecret` and no scope. `TrafficWorker.Launch` picks a customer with `CustomerPicker` per case. `appsettings.json`: `TokenEndpoint` empty (AppHost sets it), `Customers` = acme/globex/initech with `ClientId == Name`, `ClientSecret == "<name>-dev-secret"`, `Weight` 1, and no `CallbackUrl`.

- [ ] **Step 4: Wire the AppHost**: replace `Traffic__CallbackUrl` with `Traffic__Customers__{0,1,2}__CallbackUrl` (all three: the fake notifications `Notification` URL) and set `Traffic__TokenEndpoint` to `http://localhost:{Constants.Identity.Port}/realms/{Constants.Identity.Realm}/protocol/openid-connect/token`; `.WaitFor(keycloak)`.

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test Estimations.slnx` → PASS.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "Traffic generator plays three customers with their own tokens"
```

---

### Task 7: Dashboards filter by customer

**Files:** Modify `src/Aspire/Acheve.AppHost/grafana/dashboards/overview.json`, `case-flow.json`.

**Interfaces:** Consumes the label `estimation_customer_id` on `estimations_cases_started_total`, `estimations_cases_finished_total`, `estimations_case_duration_seconds_*`, and the Loki structured metadata `estimation_customer_id` / span attribute `estimation.customer_id`.

- [ ] **Step 1: Add the variable** to both dashboards' `templating.list`: `name: "customer"`, `type: "query"`, Prometheus datasource, `query: "label_values(estimations_cases_started_total, estimation_customer_id)"`, `includeAll: true`, `multi: true`, `allValue: ".+"`, `label: "Customer"`.

- [ ] **Step 2: Filter the case queries**: add `estimation_customer_id=~"$customer"` to every `estimations_cases_started_total`, `estimations_cases_finished_total` and `estimations_case_duration_seconds_bucket` selector in `overview.json` (lines using them: 64, 122, 188, 439, 448, 507, 516, 650) and `case-flow.json` (76, 134, 458). Panels on image results, state duration and active cases stay unfiltered; their descriptions say "all customers". Do not touch `chaos-run.json` or `edges.json`.

- [ ] **Step 3: Case flow drilldowns**: change the trace panel's TraceQL to `{ resource.service.name = "api" && span.http.route = "/estimations/" && span.http.request.method = "POST" && span.estimation.customer_id =~ "$customer" }`; add a logs panel "Warnings and errors for customer $customer" with `{service_name=~".+"} | estimation_customer_id=~"$customer" | severity_text=~"Warning|Error|Critical"` below the existing logs panel.

- [ ] **Step 4: Verify** (AppHost running with the traffic generator started, a few minutes of cases): `python3 -c "import json,glob;[json.load(open(f)) for f in glob.glob('src/Aspire/Acheve.AppHost/grafana/dashboards/*.json')]"` exits 0; then in Grafana (`lgtm` link, admin/admin) the Customer dropdown lists acme, globex, initech, selecting one changes the Overview stat panels, and the new logs panel shows only that customer's lines.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Dashboards: filter case panels by customer"
```

---

### Task 8: Per-customer alerts and routing to internal on-call

**Files:** Modify `src/Aspire/Acheve.AppHost/grafana/provisioning/alerting.yaml` (Edit tool only).

**Interfaces:** Consumes `estimation_customer_id` on the case counters/histogram. Produces: rules grouped per customer and contact points `oncall`, `oncall-acme`, `oncall-globex`, `oncall-initech`.

- [ ] **Step 1: Make the case rules per customer**
  - `estimations-case-failure-rate`: both sums become `sum by (estimation_customer_id)`; the min-volume guard becomes `and on (estimation_customer_id) (sum by (estimation_customer_id) (increase(estimations_cases_finished_total[15m])) >= 5)`; summary adds `for {{ $labels.estimation_customer_id }}`.
  - `estimations-slow-cases`: `histogram_quantile(0.95, sum by (le, estimation_customer_id) (rate(estimations_case_duration_seconds_bucket[15m])))`; summary names the customer.
  - Leave `stuck-case`, `wait-timeouts` and the Messaging/Services/Edges rules global (their metrics have no customer).

- [ ] **Step 2: Add the notification rule** in the Cases group: uid `estimations-customer-notification-failing`, title `Customer notification failing`, expr `sum by (estimation_customer_id) (increase(estimations_cases_finished_total{estimation_final_state="CustomerNotificationError"}[15m]))`, threshold `gt 2`, `for: 1m`, `noDataState: OK`, severity `warning`, summary `Cases of customer {{ $labels.estimation_customer_id }} can't be notified`, description explaining that the estimation succeeded but the customer's callback URL kept failing after all retries, `__dashboardUid__: estimations-case-flow`, `__panelId__: "3"`. (The existing failure-rate rule filters on `estimation_result="failure"` and never sees these cases.)

- [ ] **Step 3: Add contact points and policy tree** (top-level keys in the same file): contact points `oncall` and `oncall-<customer>` (type `email`, addresses `oncall@estimations.local` / `oncall-<customer>@estimations.local`; they can't deliver without SMTP, like today, but the routing is visible in Grafana); policy `receiver: oncall`, `group_by: [alertname, estimation_customer_id]`, one child route per known customer with `object_matchers: [["estimation_customer_id", "=", "<customer>"]]` → `oncall-<customer>`. Customers outside the allow-list arrive as `other` and fall to the root `oncall`. Update the file's header comment accordingly.

- [ ] **Step 4: Reload and verify**

```bash
curl -s -u admin:admin -X POST http://localhost:3000/api/admin/provisioning/alerting/reload
curl -s -u admin:admin http://localhost:3000/api/v1/provisioning/policies | jq '.routes[].object_matchers'
curl -s -u admin:admin http://localhost:3000/api/prometheus/grafana/api/v1/rules | jq -r '.data.groups[].rules[] | select(.name|test("notification|failure rate")) | "\(.name) \(.health) \(.state)"'
```
(adjust the port to the `lgtm` resource's Grafana port). Expected: reload returns 200, three child routes, the new rule and the failure-rate rule with `health: ok`. With traffic running and no failures every rule is `inactive` (a customer with no failures produces no instance; one failure out of two cases does not fire the failure-rate rule).

- [ ] **Step 5: Verify the on-call copy still builds**: restart the AppHost; expected: it starts (the `OncallGrafana` rewrite does not throw) and `lgtm-oncall` shows the same rules and policies.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "Alerts per customer, routed to internal on-call contact points"
```

---

### Task 9: Docs, skill references and the "add a customer" runbook

**Files:** Modify `docs/observability.md`, `docs/system.md`, `docs/messages-flow.md`, `docs/chaos-scenarios.md`, `docs/runtime-faults.md`, `docs/traffic.md`, `README.md`, `.claude/skills/investigating-incidents/references/{signals,system,grafana-api}.md`; `docs/talks/*` stay as written (historical), except they must not be contradicted by new facts: add one line to `docs/talks/README.md`: "Talk outlines predate the Customer rename."

- [ ] **Step 1: `observability.md`**: document the three signals' customer attribute `estimation.customer_id` (logs: structured metadata, spans: attribute, metrics: bounded), the `Customers:Known` allow-list rule and why (cardinality), the new `customer`/`estimation_customer_id` rows in the metrics table, the new alert and dashboard variable.
- [ ] **Step 2: Runbook "Add a customer"** in `docs/observability.md`: (1) add a client with the two mappers to `keycloak/estimations-realm.json`, (2) add the name to `Customers:Known` in the AppHost `appsettings.json` (otherwise its metrics show as `other`), (3) add it to `Traffic:Customers`, (4) optionally add a contact point and policy route in `alerting.yaml`. Logs and traces need nothing.
- [ ] **Step 3: Rename and Keycloak in the rest**: `system.md` (authentication is Keycloak at `http://localhost:8180`, realm `estimations`), `requests.http` (done in Task 2), `README.md` (Keycloak admin credentials location: Aspire dashboard parameter), the incident-skill references (`signals.md`: new label and attribute, `ClientNotified` → `CustomerNotified`; `grafana-api.md`: example LogQL `| estimation_customer_id="acme"`).
- [ ] **Step 4: Verify**: `grep -rniE 'duende|ClientNotif|ClientId|clientId' docs README.md .claude/skills requests.http | grep -v -E 'talks/|OAuth|client_id|ClientSecret'` returns nothing.
- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Docs: customers, Keycloak and the metric label rule"
```

---

### Task 10: End-to-end verification

**Files:** none (evidence in the PR description).

- [ ] **Step 1: Reset volumes once** (renamed message fields and saga data are not compatible with old in-flight state): stop the AppHost, remove the `postgres`, RabbitMQ and `lgtm` data volumes (`docker volume ls | grep -E 'postgres|rabbit|lgtm'`), start it, press Start on the traffic generator, wait 5 minutes.
- [ ] **Step 2: Metrics cardinality**: in Grafana Explore (Prometheus): `count by (estimation_customer_id) (estimations_cases_finished_total)` shows exactly `acme`, `globex`, `initech` (no `other` traffic, but its seeded series exists), and `count({__name__=~"estimations_.*"})` is stable between two readings a few minutes apart.
- [ ] **Step 3: Logs**: Loki query `{service_name=~"processmanager|callbacknotifier|imagedownloads"} | estimation_customer_id="globex"` returns lines from all three services, and the customer is **not** a stream label (`curl -s -G localhost:<loki>/loki/api/v1/labels` does not list `estimation_customer_id`; if it does, stop and add an exclusion in the collector before continuing).
- [ ] **Step 4: Traces**: Tempo TraceQL `{ span.estimation.customer_id = "acme" }` returns traces of that customer only.
- [ ] **Step 5: Alert smoke test**: set `external-notifications` to `Chaos: down` (Aspire dashboard of the External AppHost) for ~10 minutes: the `Customer notification failing` rule fires once per customer that had cases (all three, since they share the endpoint), each routed to its `oncall-<customer>` contact point; set it back to healthy and confirm they resolve. This is the hook for the later per-customer failure demo.
- [ ] **Step 6: Final checks**

Run: `dotnet build Estimations.slnx && dotnet test Estimations.slnx` → PASS. `git status` is clean.
