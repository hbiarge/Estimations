# Estimations — Messages flow

How a case moves through the system: the messages, the case states, the happy path and every failure the design handles on its own. [system.md](system.md) has the services, the timeouts and the business decisions behind them.

Every message carries the `CaseNumber` (the ticket), which ties it to its saga. Commands and the workers' replies go to one queue. The case's own events (`EstimationStateChanged`, `CaseCompleted`, `CaseFailed`) are published to every queue that subscribed to them.

## Messages

Rows follow the order of the flow.

| Stage | Message | From → to | Meaning |
| --- | --- | --- | --- |
| Start | `EstimationRequested` | Api → ProcessManager | Starts a new saga with the client id, callback URL and image URLs. |
| Any | `EstimationStateChanged` | ProcessManager → subscribers (StateHolder) | The case moved to a new state. Carries the result, failure reason, estimation ticket and skipped images known so far. |
| 1 Download | `DownloadImage` | ProcessManager → ImageDownloads | Command: download image *n*. One per image. |
| 1 Download | `ImageDownloaded` | ImageDownloads → ProcessManager | Image stored; carries its data bus ticket. |
| 1 Download | `UnableToDownloadImage` | ImageDownloads → ProcessManager | Download failed after the HTTP retries, was too big or too slow. |
| 1 Download | `ImageDownloadTimeout` | ProcessManager → itself (deferred 2 min) | The download never answered. |
| 2 Analyse | `AnalyseImage` | ProcessManager → ExternalImageProcessor | Command: send image *n* to the external image service. |
| 2 Analyse | `ImageAnalysisAccepted` | ExternalImageProcessor → ProcessManager | The external service accepted the image; it gets a fresh wait. |
| 2 Analyse | `UnableToAnalyseImage` | ExternalImageProcessor → ProcessManager | The external service rejected the image or could not be reached. |
| 2 Analyse | `ImageAnalysisTimeout` | ProcessManager → itself (deferred 20 s) | Timeout tick while waiting for the analysis. |
| 2 Analyse | `ImageAnalysed` | Api (callback) → ProcessManager | The analysis arrived; carries the metadata ticket. |
| 3 Estimate | `EstimateCase` | ProcessManager → EstimationProcessor | Command: ask for the estimation with the analysed images' metadata tickets. |
| 3 Estimate | `EstimationRequestAccepted` | EstimationProcessor → ProcessManager | The external service accepted the request; it gets a fresh wait. |
| 3 Estimate | `UnableToEstimate` | EstimationProcessor → ProcessManager | The external service rejected the request or could not be reached. |
| 3 Estimate | `EstimationTimeout` | ProcessManager → itself (deferred 20 s) | Timeout tick while waiting for the estimation. |
| 3 Estimate | `EstimationCompleted` | Api (callback) → ProcessManager | The estimation arrived; carries the estimation ticket. |
| 4 Notify | `NotifyEstimation` | ProcessManager → CallbackNotifier | Command: post the estimation to the client. Carries the `Attempt` and the skipped images. |
| 4 Notify | `NotifyEstimationFailure` | ProcessManager → CallbackNotifier | Command: post the failure reason to the client. |
| 4 Notify | `NotificationCompleted` | CallbackNotifier → ProcessManager | The client accepted the notification (2xx). |
| 4 Notify | `UnableToNotify` | CallbackNotifier → ProcessManager | The client callback failed after the HTTP retries; carries the `Attempt` and the error. |
| 4 Notify | `NotificationTimeout` | ProcessManager → itself (deferred) | The notifier never answered this attempt. |
| End | `CaseCompleted` | ProcessManager → subscribers | Published once when a case with an estimation ends. Carries the estimation ticket, the skipped images, whether the client was notified, and when the case started and finished. |
| End | `CaseFailed` | ProcessManager → subscribers | Published once when a case without an estimation ends. Carries the failure reason (`no_valid_images`, `estimation_rejected`, `estimation_timeout`), the error text, the skipped images, whether the client was notified, and when the case started and finished. |

The saga's own `…Timeout` messages live in the ProcessManager project; they are not part of the shared contract.

## Case states

A case moves through seven states (`EstimationStates`), always forward; the number is the enum value, and StateHolder keeps the highest one it has seen. `GET /estimations/{ticket}` returns the state name and, once the case has a result, `result` (`Success` or `Failure`); a ticket the client doesn't own answers `404`.

```mermaid
stateDiagram-v2
    direction TB
    [*] --> WaitingForImagesToBeDownloaded: EstimationRequested
    WaitingForImagesToBeDownloaded --> WaitingForImagesToBeAnalysed: every image downloaded, failed or timed out (10 to 20)
    WaitingForImagesToBeDownloaded --> EstimationError: request without images
    WaitingForImagesToBeAnalysed --> WaitingForEstimation: every image analysed, failed or timed out, at least one with metadata (20 to 30)
    WaitingForImagesToBeAnalysed --> EstimationError: no image with metadata
    WaitingForEstimation --> EstimationReady: EstimationCompleted (31)
    WaitingForEstimation --> EstimationError: UnableToEstimate or wait exceeded (32)
    EstimationReady --> ClientNotified: NotificationCompleted (40)
    EstimationReady --> ClientNotificationError: last attempt failed (41)
    EstimationError --> ClientNotified: NotificationCompleted
    EstimationError --> ClientNotificationError: last attempt failed
    ClientNotified --> [*]: saga completed, images and analyses deleted
    ClientNotificationError --> [*]: saga completed, images and analyses deleted
```

`ClientNotified` after `EstimationError` means the client was told about the failure, not that the case succeeded: read `result`. The estimation itself stays on the data bus (24 hours by default) so the client can still read it from the `GET` after a notification failed.

## Happy path

With every external call succeeding, a case with two images takes about 25 s: the external image and estimation services each answer after 10 s, and the client callback takes 2 s. Every wait and limit is listed in [system.md](system.md#timeouts-and-limits). The diagram shows one image; the download and analysis steps run once per image, in parallel.

```mermaid
sequenceDiagram
    autonumber
    actor Client
    participant Api
    participant PM as ProcessManager (saga)
    participant SH as StateHolder
    participant DL as ImageDownloads
    participant IP as ExternalImageProcessor
    participant XI as External ImageProcess
    participant EP as EstimationProcessor
    participant XE as External Estimations
    participant CN as CallbackNotifier

    Client->>Api: POST /estimations (imageUrls, callBackUri, Idempotency-Key)
    Api-)PM: EstimationRequested
    Api-->>Client: 202 Accepted (ticket)
    PM-)SH: EstimationStateChanged (WaitingForImagesToBeDownloaded)

    rect rgba(120,120,120,0.08)
    Note over PM,DL: Stage 1 - one DownloadImage per image
    PM-)DL: DownloadImage
    PM->>PM: DeferLocal ImageDownloadTimeout (2 min)
    DL->>DL: GET image (size and time limited), store on data bus
    DL-)PM: ImageDownloaded (imageTicket)
    PM-)IP: AnalyseImage
    PM->>PM: DeferLocal ImageAnalysisTimeout (20 s)
    PM-)SH: EstimationStateChanged (WaitingForImagesToBeAnalysed) once all are downloaded
    end

    rect rgba(120,120,120,0.08)
    Note over PM,XI: Stage 2 - one AnalyseImage per image
    IP->>XI: POST /ImageProcess (image + signed callback URL, Idempotency-Key)
    XI-->>IP: 204 No Content
    IP-)PM: ImageAnalysisAccepted
    PM->>PM: the image's wait counter starts again
    XI->>Api: POST /callbacks/image-analysis/{case}/images/{id}?traceparent=...&expires=...&signature=... (after 10 s)
    Api->>Api: check signature and expiry, resume the case trace, store metadata on data bus
    Api-)PM: ImageAnalysed (metadataTicket)
    end

    rect rgba(120,120,120,0.08)
    Note over PM,XE: Stage 3 - once all images are analysed
    PM-)SH: EstimationStateChanged (WaitingForEstimation)
    PM-)EP: EstimateCase (metadata tickets of analysed images)
    PM->>PM: DeferLocal EstimationTimeout (20 s)
    EP->>EP: read the analyses from the data bus
    EP->>XE: POST /Estimation (analyses + signed callback URL, Idempotency-Key)
    XE-->>EP: 204 No Content
    EP-)PM: EstimationRequestAccepted
    XE->>Api: POST /callbacks/estimation/{case}?traceparent=...&expires=...&signature=... (after 10 s)
    Api-)PM: EstimationCompleted (estimationTicket)
    PM-)SH: EstimationStateChanged (EstimationReady, result, estimation ticket)
    end

    rect rgba(120,120,120,0.08)
    Note over PM,CN: Stage 4
    PM-)CN: NotifyEstimation (attempt 1)
    PM->>PM: DeferLocal NotificationTimeout (45 s)
    CN->>Client: POST callBackUri (Result = Success, Estimation, signed, Idempotency-Key)
    Client-->>CN: 2xx
    CN-)PM: NotificationCompleted
    PM-)SH: EstimationStateChanged (ClientNotified)
    PM->>PM: MarkAsComplete + delete image and analysis attachments
    end

    Client->>Api: GET /estimations/{ticket}
    Api->>SH: StateQuery (gRPC)
    SH-->>Api: ClientNotified, Success, estimation ticket
    Api-->>Client: 200 (state, result, estimation)
```

Arrows with an open head are asynchronous Rebus messages; arrows with a filled head are HTTP or gRPC calls; dashed arrows are responses. The pending `…Timeout` messages still fire later, see that the result is already there, and do nothing.

## Failure scenarios

A failure on one image never stops the case: the image is marked as failed, listed in `skippedImages`, and the case continues with the rest. The case fails only when no image can be analysed, or when the estimation or its wait fails. The client is notified either way.

These are failures of the business flow. To cause them on purpose, see [runtime-faults.md](runtime-faults.md) and the guided cases in [chaos-scenarios.md](chaos-scenarios.md).

### 1. An image can't be downloaded

The HTTP client retries first. A download also stops when the image is larger than `Downloads:MaxBytes` (20 MB) or takes longer than `Downloads:Timeout` (60 s), and a URL that isn't absolute http or https is rejected. If it still fails, the image counts as both downloaded and analysed (with an error) and is never sent for analysis. If even the worker's answer never arrives, `ImageDownloadTimeout` (2 min) does the same.

```mermaid
sequenceDiagram
    autonumber
    participant PM as ProcessManager (saga)
    participant DL as ImageDownloads
    participant IMG as External Images
    participant SH as StateHolder

    PM-)DL: DownloadImage (image 1)
    PM->>PM: DeferLocal ImageDownloadTimeout (2 min)
    DL->>IMG: GET image (standard resilience retries, size and time limit)
    IMG--xDL: error / timeout / too big
    DL-)PM: UnableToDownloadImage (error)
    PM->>PM: image 1 DownloadError = error
    alt other images still downloading
        PM->>PM: wait
    else all images have a download result
        PM-)SH: EstimationStateChanged (WaitingForImagesToBeAnalysed)
        PM->>PM: VerifyIfAllImagesAnalyzed
        Note right of PM: continues as in scenario 4 if this was the last pending image
    end
    Note over PM: A late ImageDownloaded is ignored and its attachment deleted
```

### 2. The external image service rejects an image

A non-2xx response, a connection error or a missing attachment ends the image's analysis with an error straight away.

```mermaid
sequenceDiagram
    autonumber
    participant PM as ProcessManager (saga)
    participant IP as ExternalImageProcessor
    participant XI as External ImageProcess

    PM-)IP: AnalyseImage (image 1)
    IP->>XI: POST /ImageProcess
    XI--xIP: 5xx / unreachable
    IP-)PM: UnableToAnalyseImage (error)
    PM->>PM: image 1 AnalysisError = error
    PM->>PM: VerifyIfAllImagesAnalyzed (scenario 4)
```

### 3. The image analysis never arrives (timeout)

The wait starts when the saga sends `AnalyseImage`, so it also covers a worker that never answers. When the external service accepts the image, `ImageAnalysisAccepted` starts the counter again, so the service always gets the full wait. The saga checks every 20 s; after the first wait plus 5 more (about 120 s) it gives up on that image.

```mermaid
sequenceDiagram
    autonumber
    participant PM as ProcessManager (saga)
    participant T as Timeouts (PostgreSQL)
    participant IP as ExternalImageProcessor

    PM-)IP: AnalyseImage (image 1)
    PM-)T: defer ImageAnalysisTimeout (20 s)
    IP-)PM: ImageAnalysisAccepted (image 1)
    PM->>PM: CurrentAnalysisWaits = 0 (once)
    loop CurrentAnalysisWaits < 5
        T-)PM: ImageAnalysisTimeout
        PM->>PM: not analysed yet, CurrentAnalysisWaits += 1
        PM-)T: defer again (20 s)
    end
    T-)PM: ImageAnalysisTimeout (6th, about 120 s)
    PM->>PM: image 1 AnalysisError = "Wait time exceeded"
    PM->>PM: VerifyIfAllImagesAnalyzed (scenario 4)
    Note over PM: An ImageAnalysed arriving after this is ignored and its attachment deleted
```

### 4. Deciding whether the case can be estimated

Once every image has a result (analysed, download error, analysis error or timeout), the saga either asks for the estimation with the images that have metadata, or fails the case.

```mermaid
sequenceDiagram
    autonumber
    participant PM as ProcessManager (saga)
    participant SH as StateHolder
    participant EP as EstimationProcessor
    participant CN as CallbackNotifier

    PM->>PM: VerifyIfAllImagesAnalyzed
    alt state already WaitingForEstimation or later
        PM->>PM: do nothing (estimation asked only once)
    else some images still pending
        PM->>PM: wait
    else at least one image has metadata
        PM-)SH: EstimationStateChanged (WaitingForEstimation)
        PM-)EP: EstimateCase (only images with metadata)
        PM->>PM: DeferLocal EstimationTimeout (20 s)
    else no image has metadata
        PM-)SH: EstimationStateChanged (EstimationError)
        PM-)CN: NotifyEstimationFailure ("no images with valid metadata")
        Note right of PM: continues in Stage 4 with Result = Failure
    end
```

### 5. The estimation fails or never arrives

The estimation has the same wait as an image analysis (started with `EstimateCase`, restarted by `EstimationRequestAccepted`), but any failure fails the whole case. A missing analysis attachment is reported as `UnableToEstimate` too.

```mermaid
sequenceDiagram
    autonumber
    participant PM as ProcessManager (saga)
    participant T as Timeouts (PostgreSQL)
    participant EP as EstimationProcessor
    participant XE as External Estimations
    participant SH as StateHolder
    participant CN as CallbackNotifier

    PM-)EP: EstimateCase
    PM-)T: defer EstimationTimeout (20 s)
    EP->>XE: POST /Estimation
    alt rejected or unreachable
        XE--xEP: 5xx / error
        EP-)PM: UnableToEstimate (error)
    else accepted, but no callback
        XE-->>EP: 204
        EP-)PM: EstimationRequestAccepted
        PM->>PM: CurrentExternalEstimationWaits = 0 (once)
        loop CurrentExternalEstimationWaits < 5
            T-)PM: EstimationTimeout
            PM-)T: defer again (20 s)
        end
        T-)PM: EstimationTimeout (6th, about 120 s)
    end
    PM->>PM: FailEstimation(error or "Wait time exceeded")
    PM-)SH: EstimationStateChanged (EstimationError)
    PM-)CN: NotifyEstimationFailure (reason)
    Note over PM: A late EstimationCompleted is ignored and its attachment deleted
```

### 6. Notifying the client

The notifier posts `Result = Success` with the estimation, or `Result = Failure` with the reason, plus the `skippedImages`. Each try has an `Attempt` number. The notifier's HTTP client retries first; if the call still fails (or the notifier doesn't answer within 45 s) the saga sends the command again after 15 s, then 30 s, then 60 s, up to 4 attempts. Only then the case ends as `ClientNotificationError`. Either way the saga ends and deletes the case's images and analyses; the estimation stays for the retention time.

```mermaid
sequenceDiagram
    autonumber
    participant PM as ProcessManager (saga)
    participant CN as CallbackNotifier
    actor Client as Client callback URL
    participant SH as StateHolder

    PM-)CN: NotifyEstimation or NotifyEstimationFailure (attempt 1)
    PM->>PM: DeferLocal NotificationTimeout (45 s, attempt 1)
    CN->>Client: POST callBackUri (standard resilience retries)
    alt 2xx
        Client-->>CN: 2xx
        CN-)PM: NotificationCompleted
        PM-)SH: EstimationStateChanged (ClientNotified)
    else non-2xx or unreachable
        Client--xCN: error
        CN-)PM: UnableToNotify (attempt 1, error)
        alt attempts left
            PM->>PM: attempt = 2
            PM-)CN: defer the command (15 s, 30 s, 60 s)
            PM->>PM: DeferLocal NotificationTimeout for the new attempt
        else last attempt
            PM-)SH: EstimationStateChanged (ClientNotificationError)
        end
    end
    PM->>PM: MarkAsComplete (saga data deleted)
    PM->>PM: delete image and analysis attachments
```

The client can receive the same notification more than once (an HTTP retry, a saga retry after a slow answer). Every request carries the same `Idempotency-Key` (the case number), and a signature the client can check.

### 7. Duplicate, late and out-of-order messages

RabbitMQ delivers at least once and the external services may call back twice or after the saga stopped waiting. Each case is handled without repeating a step, and an attachment stored for a message the saga ignores is deleted.

```mermaid
sequenceDiagram
    autonumber
    participant X as Sender
    participant PM as ProcessManager (saga)
    participant SH as StateHolder

    X-)PM: same message again (same message id)
    PM->>PM: idempotent saga: already handled, resend its outgoing messages, skip the handler

    X-)PM: EstimationRequested for an existing case
    PM->>PM: not IsNew: ignore

    X-)PM: second ImageAnalysed / UnableToAnalyseImage for the same image
    PM->>PM: image already Analyzed: log, ignore, delete the extra attachment

    X-)PM: ImageAnalysed before ImageAnalysisAccepted
    PM->>PM: result stored, the late Accepted sees Analyzed and changes nothing

    X-)PM: EstimationCompleted when the case is not WaitingForEstimation
    PM->>PM: log, ignore, delete the extra attachment

    X-)PM: answer of an earlier notification attempt
    PM->>PM: attempt number differs: ignore

    PM-)SH: EstimationStateChanged arriving out of order
    SH->>SH: keep the higher state
```

Messages for a saga that already finished find no saga data and are dropped by Rebus. Their attachments, and any other attachment older than `DataBus:Retention` (24 hours), are deleted by the ProcessManager's data bus sweeper.

### 8. A callback with a bad signature or an old URL

The callback URLs given to the external services carry an HMAC signature tied to the case (and image), to the expiry time (`expires`, 30 minutes after the URL was built) and to the trace context in the URL. A call with a missing or wrong signature, one reused for another case or image, one whose `traceparent` or `expires` was changed, or one used after it expired, is rejected before any message is sent or attachment stored.

```mermaid
sequenceDiagram
    autonumber
    participant Caller
    participant Api
    participant PM as ProcessManager (saga)

    Caller->>Api: POST /callbacks/image-analysis/{case}/images/{id}?signature=bad
    Api->>Api: CallbackSigner.IsValidImageAnalysis = false
    Api-->>Caller: 401 Unauthorized
    Note over Api,PM: no ImageAnalysed is sent, the saga keeps waiting and may time out (scenario 3)
```

### 9. A request that is sent twice

`POST /estimations` accepts an `Idempotency-Key` header. The case number is derived from the client and the key, so a repeated request names the same case: the Api answers `202` with the same ticket, and sends no second `EstimationRequested` when the StateHolder already knows the case (a second copy racing past that check is ignored by the saga, see scenario 7). The body of the repeated request is not compared with the first one. Without the header every request starts a new case.
