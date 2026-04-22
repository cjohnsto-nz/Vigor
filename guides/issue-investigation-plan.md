# Vigor Issue Investigation Plan

This file is the working checklist for community-raised issues. Keep it updated as we investigate so we do not guess, lose context, or make multiple unvalidated changes at once.

## Rules

- Make one code change at a time.
- Validate each change with build, deployment, and playtesting before the next change.
- Prefer in-memory diagnostics and snapshots over continuous logging.
- Record findings here after each profiling or playtesting pass.
- Diagnostic collection should be easy for users to hand back as a single play-session folder.

## Current Focus

The first investigation target is long-session choppiness / suspected memory leak.

Most suspicious current paths:

- `Core/BatchedTreeAttribute.cs`
- `VigorModSystem.cs` packet handling path
- `Hud/HudVigorBar.cs` GUI composition and listener lifecycle

## Community Issue Board

### Performance / Leak Investigation

- [x] Create a diagnostics framework that writes on-demand snapshots to file.
- [x] Instrument `BatchedTreeAttribute` creation, approximate lifetime, `ForceSync`, deferred flushes, and `MarkPathDirty`.
- [x] Instrument stamina packet send/receive counts and local-player packet handling.
- [x] Add an on-demand diagnostics dump trigger.
- [x] Automate diagnostics snapshots every 10 minutes during play.
- [x] Group diagnostics snapshots by play session in a dedicated session folder.
- [x] Gate diagnostics collection behind config settings.
- [x] Make startup messaging state clearly whether diagnostics collection is enabled.
- [ ] Capture baseline diagnostics snapshot shortly after world join.
- [ ] Capture a second snapshot after 30-60 minutes of normal play.
- [ ] Compare snapshots for monotonic growth in batchers, deferred flushes, and packet caches.
- [ ] If growth is confirmed, isolate the first concrete leak fix and validate it separately.

### Logging / UX

- [x] Remove excessive API operation logging from `VigorAPI`.
- [x] Make startup/loading-screen logging concise and user-friendly.
- [x] Remove unconditional profiling notifications from normal play.

### Hydrate Or Diedrate

- [ ] Investigate hydration-based stamina regeneration bonus.
- [ ] Revisit default HUD positioning relative to HoD bars.

### Compatibility / Integrations

- [ ] Review Verticality / climbing support.
- [ ] Review Combat Overhaul compatibility constraints.
- [ ] Review attack / block / tool-use stamina API surface.

### Gameplay Requests

- [ ] Configurable nutrition threshold below 100% for bonuses.
- [ ] Sleep / resting-specific regen multipliers.
- [ ] Temperature effects on stamina regeneration.
- [ ] Animal stamina / persistence hunting concept.
- [ ] Wading stamina behavior in shallow water.
- [ ] Optional movement-speed model where full stamina feels like vanilla and penalties start below thresholds.

## Diagnostic Workflow

1. Deploy current diagnostic build.
2. Enable diagnostics snapshot settings in `vigor.json`.
3. Start a session. An initial snapshot should be captured automatically once the local player is active.
4. Play normally until the usual performance issue appears, or for 30-60 minutes.
5. Use the automatic interval snapshots and optionally press `F6` again at key moments.
5. Compare the generated JSON snapshots in `VintagestoryData/Logs`.

Important metrics to compare:

- `batchedTree.created`
- `batchedTree.activeApprox`
- `batchedTree.finalized`
- `batchedTree.flushInterval`
- `batchedTree.flushPerformed`
- `batchedTree.flushSkippedNotDue`
- `batchedTree.flushSkippedNoChanges`
- `batchedTree.forceSync`
- `batchedTree.markPathDirty`
- `network.packetReceived`
- `network.packetSent`
- `network.packetLocalPlayerCached`
- `clientStaminaState.count`

## Current Findings

- The current diagnostics build adds snapshot-based instrumentation and does not intentionally change gameplay behavior.
- The old timer-based `BatchedTreeAttribute` model has been replaced with main-thread flushing from the entity behavior tick.
- Client packet handling no longer creates temporary batchers or mirrors packet state back into `WatchedAttributes`.
- The client HUD now prefers the custom stamina packet cache for current/max/exhausted state, while still reading debug-only values from the entity tree.
- Diagnostics snapshots are now grouped into a single per-session folder under `VintagestoryData/Logs/VigorDiagnostics`.

## Next Planned Change

Run a fresh diagnostic playtest to compare batching efficacy and long-session stability with the timer-free main-thread flush model.
