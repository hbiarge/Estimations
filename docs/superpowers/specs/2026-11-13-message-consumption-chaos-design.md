# Message consumption chaos: design

## Why

The chaos so far breaks HTTP: the external systems and the Api's callbacks. Nothing breaks the consuming side of the queues. We want to stop a worker from taking messages and watch two things on the dashboards:

- **A: a backlog means latency.** A short pause: the queue grows, cases get slower, the backlog drains, no case fails.
- **B: a backlog can quietly turn into failed cases.** The same pause, held past the saga's wait limit: the paused service shows no errors, but the saga gives up on its cases (`download_timeout`), and after the resume the worker processes messages for cases that have already moved on.

## Goals, non-goals, anti-goals

- Goal: pause and resume consumption of any Rebus worker at run time, from the Aspire dashboard, over HTTP and from chaos monkey strategies.
- Goal: one new strategy, `backlog`, that tells A and then B.
- Non-goal: changing the saga's timeouts or making them switchable.
- Non-goal: killing processes or stopping RabbitMQ or PostgreSQL. Aspire's own stop command covers those; the docs mention it as a manual experiment.
- Anti-goal: a pause that looks like a crash. The process and its health checks stay up; it only stops consuming.
- Anti-goal: messages piling up as unacked inside the worker. They must wait **ready** in RabbitMQ so "Messages waiting per queue" shows the backlog.

## Approaches

1. **Rebus worker count set to 0 at run time** (picked). `bus.Advanced.Workers.SetNumberOfWorkers(0)` to pause, back to the previous count to resume. Fits how chaos and resilience are switched today.
2. A pipeline step that blocks while paused. Rejected: messages sit unacked in the worker, so the queue panels tell the wrong story.
3. Stopping the resource through Aspire. Rejected: it is a crash, not a stall, and the monkey can't easily drive it. Documented as a manual experiment.

## Design

**Consumption switch** (a new small library, `Acheve.Common.Consumption`, so the chaos library used by the external systems doesn't take a Rebus dependency; my call):

- `ConsumptionState` remembers the configured worker count and whether consumption is paused; pause sets the workers to 0, resume restores the count.
- Endpoints, only in Development, like `/resilience`: `GET /consumption`, `POST /consumption/pause`, `POST /consumption/resume`.
- Telemetry, in the same style as chaos and resilience: a gauge `consumption_paused` (1 or 0) and a warning log line when the state really changes (the Chaos run dashboard turns these into annotations).
- Used by every Rebus worker: `imagedownloads`, `imageprocessor`, `estimationprocessor`, `callbacknotifier`, `processmanager` and `stateholder`. The Api is a one-way client and has nothing to pause.

**AppHost:** dashboard commands **Consumption: pause** and **Consumption: resume** on those six resources.

**Chaos monkey:**

- A step gets a third setting, `Consumption`: worker → `paused` or `running`. Workers a step doesn't name go back to `running`, as with chaos and resilience; so does everything when a strategy ends, is stopped or replaced, or the monkey stops.
- Switches are counted in `chaos_monkey_switches_total` with `chaos_kind` `consumption`.
- The monkey refuses to start when a step names an unknown worker or state.
- `random` stays as it is (my call: an endless random pause would mostly produce B-style failures and drown the other scenarios).

**Strategy `backlog`** (about 8 minutes, `imagedownloads` in both phases so only the duration differs):

| Step | Duration | Consumption |
| --- | --- | --- |
| Healthy baseline | 30 s | all running |
| A: short pause, only latency | 60 s | `imagedownloads` paused |
| Drain | 90 s | all running |
| B: long pause, past the saga's 2 min download limit | 3 min | `imagedownloads` paused |
| Recovery | 2 min | all running |

**Chaos run dashboard:** a consumption state per worker next to the chaos scenario and resilience profile, and a "Messages waiting per queue" panel so the backlog is on the same page as the client latency and saga outcomes.

**Docs:** `docs/chaos.md` gets the consumption switch, the `backlog` strategy row (what to look at: the `imagedownloads` queue, **Client case duration**, then `download_timeout` in **Saga outcomes**), and the manual "stop the resource in Aspire" experiment.

## Left to the builder

- Check, with the app running, whether RabbitMQ still lists a consumer on the queue while Rebus has 0 workers. Either way is fine; the docs must say whether "Queues without consumers" lights up during a pause.
- Check that pausing while a message is being handled lets that message finish (no unacked message left behind).
- Exact panel layout on the Chaos run dashboard, following the existing rows. Don't edit the Grafana provisioning files with `sed -i` (it breaks the LGTM single-file bind mount).
