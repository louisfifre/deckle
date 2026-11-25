# MCP request rate limit bug notes

## Agents sharing one client were refused at once instead of waiting

- Trigger: several agents of the same client (one bearer, so one budget) call the gateway together, about ten in practice, and the client spends more than 60 requests within a minute.
- Symptom: every request over the budget answered HTTP 429 at once, with no `Retry-After` and no body; an agent only saw « Error POSTing to endpoint » and the tool call failed.
- Cause: the per-client limiter in `McpHttpHost.BuildApplication` was a fixed window of 60 permits per minute with `QueueLimit = 0`, so nothing could wait for a permit.
- Violated invariant: the budget limits the rate of a client, not whether a burst of legitimate calls succeeds; within a bounded wait an over-budget request waits for its turn, and only a request past that bound is refused, saying when to retry. The trap and its numbers are commented on `McpRequestRateLimit`.
- Fix: a token bucket per client (burst of 60, one permit back every second, so still 60 per minute sustained) with a queue of 30, oldest first, so the longest wait is about 30 s. A request that finds the queue full gets 429 with `Retry-After` and a plain-text body naming the budget.
- Recurrence cue: « Error POSTing to endpoint » or HTTP 429 from the Anytype gateway while several agents run, or a limiter option that sets `QueueLimit = 0` again.
- Guard: `McpHttpHostTests.ABurstAboveTheBudgetWaitsForItsTurnInsteadOfBeingRefused` and `ARequestThatFindsTheQueueFullIsRefusedWithARetryDelay`.
- Not measured: how long the MCP clients (Claude Code, Codex) wait on one HTTP request before giving up; 30 s was chosen to stay under the usual 60 s client timeouts, without checking each client.
