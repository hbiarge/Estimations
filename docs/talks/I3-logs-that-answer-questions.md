# I3 · Logs that answer questions

**Status:** proposal.

| | |
| --- | --- |
| **Level** | Introductory |
| **Area** | Logs |
| **Length** | 30 minutes |
| **Audience** | Developers who log with string interpolation and search with grep |
| **Takeaway** | A log is a queryable event, not a sentence. |

## Outline

1. **Message templates vs string interpolation.** `"Case number {caseNumber}"` stores `caseNumber` as a field you can filter on.
2. **`[LoggerMessage]` source generation.** Faster, no boxing, the template is checked at compile time.
3. **Levels with a meaning.** Warning for a rejected callback or an injected fault (unexpected but handled); Error for something a human must fix.
4. **Correlation for free.** Every log written inside an `Activity` carries its trace and span IDs.
5. **A business ID everywhere.** Searching by case number beats any grep.
6. **The cost of noise.** Noisy logs hide the important ones and cost money.

## Demo

Filter structured logs by case number in the Aspire dashboard, jump from a log to its trace, then find the same case in Loki from the **Case number** filter on the **Case flow** dashboard.

## Files

- [ChaosState.cs](../../src/Common/Acheve.Common.Chaos/ChaosState.cs)
- [ImageAnalysisCallbackEndpoints.cs](../../src/Application/Acheve.Application.Api/Features/ImageAnalysisCallback/ImageAnalysisCallbackEndpoints.cs)
- [observability.md](../observability.md#following-one-case)

## Related

Expands section 6 of [I1](I1-do-you-know-what-your-system-is-doing.md).
