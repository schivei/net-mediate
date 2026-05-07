# NetMediate Performance Improvements Roadmap

This document lists concrete improvements we want to implement in sequence (single PR track when applicable) to recover and improve throughput.

---

## Current focus

- [ ] Resolve `Notify` throughput regression (`+41.6%` timing and `+144 B` allocation vs baseline).
- [ ] Keep `Request` timing at or better than baseline (`+8.1%` currently, inside noise band but still tracked).
- [ ] Keep `Command` and `Stream` performance stable while applying fixes.

---

## Improvement backlog

1. **Identify hot allocations in notification path**
   - Inspect behavior/handler composition for avoidable intermediate allocations.
   - Re-run targeted benchmark after each change.
2. **Reduce per-request overhead**
   - Verify request pipeline composition and delegate creation/caching behavior.
   - Confirm no additional closure/lambda allocations were introduced.
3. **Strengthen benchmark reporting**
   - Keep the benchmark report synced in both docs locations.
   - Keep this roadmap updated with resolved and pending items.

---

## Validation gate

Before closing this roadmap item set:

- `Notification Notify` should return to baseline timing range (±10%) and recover previous allocation footprint.
- `Request Request` should return to baseline timing range (±10%).
- No regression should be introduced for `Command Send` or `RequestStream`.
