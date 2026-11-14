# M1 · One case, one trace: tracing across a message bus and systems you don't own

**Status:** proposal.

| | |
| --- | --- |
| **Level** | Mid |
| **Area** | Traces |
| **Length** | 45 minutes |
| **Audience** | Developers with tracing in place who see broken or orphan traces |
| **Takeaway** | Context propagation is the real work, and you can repair it where it breaks. |

## Outline

1. **W3C Trace Context.** `traceparent` over HTTP and gRPC.
2. **Through the bus.** Rebus copies it into message headers, including the deferred timeouts stored in PostgreSQL, so one case is one trace although nobody waits for the result.
3. **Where it breaks.** The external systems call back later without our context: each callback would start an orphan trace.
4. **The repair.** Put `traceparent` in the callback URL and sign it, so nobody can attach a callback to someone else's trace.
5. **Parent vs link.** The callback span takes the original span as its parent and links to ASP.NET Core's own request span: "part of this operation" vs "related to that one".
6. **Naming the other side.** `peer.service` on outgoing calls, so Tempo's service graph can tell four systems on `localhost` apart.
7. **Cheap enrichment.** Tagging `Activity.Current` (`chaos.injected`).

## Demo

A full case trace with the visible wait for each callback. Then switch `external-imageprocess` to `lost-callbacks` and show a trace that ends with the timeout instead of the callback.

## Files

- [CallbackTracing.cs](../../src/Common/Acheve.Common.Shared/CallbackTracing.cs)
- [CallbackSigner.cs](../../src/Common/Acheve.Common.Shared/CallbackSigner.cs)
- [PeerService.cs](../../src/Aspire/Acheve.ServiceDefaults/PeerService.cs)
- [messages-flow.md](../messages-flow.md)

## Related

Expands section 7 of [I1](I1-do-you-know-what-your-system-is-doing.md). Leads into [E1](E1-sampling-traces-that-last-11-minutes.md).
