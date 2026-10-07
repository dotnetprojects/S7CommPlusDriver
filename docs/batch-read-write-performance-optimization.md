# S7CommPlus Batch Read/Write Performance Optimization

Notes on the analysis and optimizations made to the batched read/write path of the
S7CommPlus driver, targeted at scenarios with a very large number of tags.

## Context / problem statement

The optimization was driven by a real-world case:

- **~210,000 tags** read in a single logical operation.
- `_tagsPerReadRequestMax = 100`, i.e. the PLC negotiates a maximum of **100 items per
  read request**.
- Requirement: strictly preserve the existing **hard-abort-on-error** behavior. Partial
  results are deliberately *not* surfaced to the caller (the protocol layer produces
  them, but the client layer intentionally discards them via `ThrowIfError`). Only
  performance may change, not observable behavior.

## Where the time goes (baseline analysis)

Key methods:

- `S7CommPlusClient.ReadTagValuesInBatches` (client-side outer batching loop)
- `S7CommPlusProtocolSession.ReadValues` / `WriteValues` (protocol-layer request building)
- `GetSerializedRequestLengthForBatching` (payload-size probe)
- `ExceedsSingleFramePayload` (comparison against the negotiated single-frame payload limit)

Findings:

1. **2100 sequential requests.** With 100 items per request, 210,000 tags turn into
   2100 sequential request/response round-trips. This is dominated by the PLC item limit,
   so it is mostly I/O-bound and hard to change without protocol-level pipelining (see
   backlog item 6).

2. **Two nested batching layers.** The client batches into `_tagsPerReadRequestMax`
   chunks, and the protocol layer *again* splits by the negotiated TPDU payload size.

3. **O(n^2) payload check (the main avoidable CPU cost).** For every candidate item that
   was added, the *entire* request was fully re-serialized to measure its byte length.
   Building one chunk of `n` items therefore cost `O(n)` full serializations, i.e.
   `O(n^2)` byte-level work per chunk. Across 2100 chunks this dominated the CPU time.

## Optimizations implemented

### 1. Remove the O(n^2) reserialization (committed)

- Branch `feature/perf-fix-batching-payload-check`, commit `f0bccad`.
- New helper `AppendItemsWithinPayloadLimit` in `S7CommPlusProtocolSession.cs`, used by
  both the read path (`ReadValues`) and the write path (`CreateWriteRequestBatch`).
- Technique: **galloping search followed by binary search** to find the exact number of
  items that fit within the payload limit, using **O(log n)** full-request serializations
  per chunk instead of O(n).
- Correctness basis: the serialized request length **grows monotonically** with the item
  count (each item contributes at least one byte; VLQ-encoded counters never shrink as
  items are added). This makes the search find exactly the same maximum as the original
  linear scan.
- Preserved semantics: the **first item of a chunk is always included unconditionally**
  (matching the legacy "never reject the first item" behavior), even if that item alone
  would exceed the limit.
- Regression test added: `WriteBatchSplitPointMatchesLinearScanForVariousSizes`
  (12 cases across power-of-two boundaries) proves identical chunk boundaries vs. the old
  linear scan.

### 2. Pool the probe MemoryStream (committed)

- Commit `8851fed`.
- `GetSerializedRequestLengthForBatching` previously allocated a new `MemoryStream`
  (and backing byte array) on **every** probe call. With galloping/binary search this can
  still be thousands of allocations for large tag counts.
- Now reuses a `[ThreadStatic] MemoryStream` across calls, resetting it via
  `SetLength(0)` instead of reallocating.
- Thread-safety rationale: a single batching operation runs **synchronously on one
  thread**, and the helper is never called reentrantly, so `[ThreadStatic]` is safe
  without locking. The session is never used concurrently for read and write.

### 3. Optimistic fast path in the batching check (implemented)

- Adds an optimistic path at the top of `AppendItemsWithinPayloadLimit`: add **all**
  candidate items of a chunk at once and check the payload limit **once**. If it fits,
  return immediately.
- Rationale: the PLC-negotiated item limit (`TagsPerReadRequestMax` /
  `TagsPerWriteRequestMax`) already usually fits the negotiated PDU payload, so the whole
  chunk typically fits in a single frame.
- Effect: reduces full-request serializations per chunk from **O(log n)** (item 1) to
  **1** in the common case. When the optimistic attempt overflows the limit, the code
  rolls back and falls back to the galloping/binary-search path from item 1, so behavior
  stays identical.
- Covered by the existing split-point regression tests (which exercise the fallback).

### 4. Conditionally skip the payload check

Implemented as part of item 3: because the payload check is only performed when it can
actually bind (optimistic attempt fails), the expensive search path is skipped entirely
in the common case.

## Verification

- Build clean (0 warnings / 0 errors) for **net48** and **net8.0**.
- Full test suite: **468/468 passing** after each step, including the batch-split
  regression tests in `S7CommPlusProtocolSessionReceiveTests`.
- Debug hooks used to drive the split logic from tests: e.g.
  `DebugCreateWriteRequestBatchForTests` in `S7CommPlusProtocolSession.cs`.

## Remaining backlog

5. **Typed values instead of `object`.** The read path returns `List<object>`, which
   boxes value types. Switching to typed values would cut allocations further but is an
   **API break** and was deferred.

6. **Pipelining / parallel sessions.** Issuing multiple requests concurrently could
   overlap I/O across the 2100 round-trips. Likely **not feasible** given protocol
   constraints: fragmented S7CommPlus response bodies do not carry the request sequence
   number, so concurrent fragment-producing requests make fragment ownership ambiguous
   (see `online-multiplexing-todo.md`). One foreground request stays in flight per
   physical connection.

## Behavior preserved (invariants)

- Hard-abort-on-error semantics unchanged; partial results still discarded at the client
  layer via `ThrowIfError`.
- Chunk boundaries byte-identical to the original linear scan.
- First item of a chunk always included unconditionally.
- Serialized-length monotonicity is the core assumption behind the search-based
  optimizations; if item serialization ever stopped being monotonic, the galloping/binary
  search would need to be revisited.
