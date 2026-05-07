---
sidebar_position: 2
---

# NetMediate Performance Improvements Roadmap

This document lists concrete improvements we want to implement in sequence (single PR track when applicable) to recover and improve throughput.

---

## Current focus

- [ ] Resolve `Notify` throughput regression (`+62.0%` timing and `+144 B` allocation vs baseline).
- [ ] Resolve `Request` throughput regression (`+17.8%` timing vs baseline).
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
