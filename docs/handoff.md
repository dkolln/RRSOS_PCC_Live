# Handoff: where things stand and what comes next

Updated at the end of the 2026-09-22/23 session so a new session can pick up cleanly. The 2026-09-21 write-up below
this point described Module 7/8 (Base, Extractors, Drones) as "built but not yet run in the game" — that has since
happened and been iterated on heavily; treat this top section as current and the rest as history/background.

## Where everything is

Unchanged from before — see the table further down. One addition: decompiled game source (used this session to
settle a real bug, see below) currently lives in two old scratchpad folders on this machine, not in the repo —
these are the exact paths found and used on 2026-09-23, both still present at that point:

```
C:\Users\dkoll\AppData\Local\Temp\claude\C--Users-david-source-repos-Planet-Crafter-RRSOS-PCC-Live\c2dd33cb-7cbc-46d7-adbe-caf297b71495\scratchpad\decomp\SpaceCraft
C:\Users\dkoll\AppData\Local\Temp\claude\C--Users-david-source-repos-Planet-Crafter-RRSOS-PCC\bd791197-e25f-4eb1-8f89-dda0e4b8fb25\scratchpad\decomp\SpaceCraft
```

The second one (the plain `RRSOS-PCC` one) is the more complete decompile of the two — it's the one that actually
had `WorldObjectsIdHandler.cs`, `WorldObjectFromScene.cs` and `WorldObject.cs`; the `-Live` one was missing several
classes the other had. Scratchpad folders get cleaned up eventually (they're tied to a specific past session, not
guaranteed to persist); if neither path exists anymore, regenerate with the `ilspycmd` command at the bottom of
this file — worth doing a search first (`find`/`Get-ChildItem` for `WorldObjectsIdHandler.cs` under
`AppData\Local\Temp\claude\`) in case a newer scratchpad has it before re-running the ~30s decompile.

## Where the dashboard actually is now

Well past "Base and Extractors, not yet tested." Current shape:

- **One page, three columns** (unchanged layout philosophy). Player card's top row is Heading + Elevation; directly
  under that, all **four mini-maps sit in a single row** (Bases, Drones, Vehicle, Extractors) — moved there
  deliberately so they read as one cluster instead of being scattered across the row with the instruments.
- **Every mini-map** (`BaseMap`, `DroneMap`, `RelativeMap`, `ExtractorMap`) now has:
  - a decorative rotating radar sweep (2 RPM, 40° wide wedge) using `mix-blend-mode: plus-lighter` so a pip
    visibly flares as the sweep's light crosses it — this is real-time pixel compositing, not a pre-scheduled CSS
    animation, specifically because a scheduled version (tried first, via `animation-delay` computed from each
    pip's angle) **drifted out of sync** as the player moved and pip bearings kept changing under a fixed
    schedule. The blend-mode version can't drift; there's nothing to keep in sync.
  - manual `+`/`−`/`×` zoom controls, because the auto-fit range could squash several close-together pips (e.g.
    extractors right next to the player marker) into an unclickable cluster. Range steps now go down to 10m
    (previously bottomed out at 100m/50m) so tight clusters can actually be separated.
  - Drones and Vehicles and Extractors all show the same 3-line caption pattern under the map: label / count /
    nearest-with-distance-and-direction (Extractors and Vehicles were changed this session to match Drones, which
    already had it).
- **Cheats page** (`/cheats`, route only reachable via a button next to LAUNCH PC, and only while the game is not
  in a world): a generalized, configurable Resupply — pick a save file, add any number of (container label →
  product) configs, Resupply clears each labelled container down to just that product. This is a from-scratch,
  safer redesign of RRSOS-PCC's old hardcoded DaveFood/DaveWater/DaveOxygen resupply (empties by rewriting each
  existing item's own `gId` in place rather than deleting/recreating records — see `SaveResupplyEngine.cs`'s doc
  comment for the full reasoning). The live plugin itself is still strictly read-only; Cheats only ever edits the
  save file on disk, same constraint the old RRSOS-PCC always had (main menu or closed only).
- **Player card**: Backpack and Gear now share one scrollable box with a fixed 8px gap between them, instead of
  Backpack's wrapper flex-growing to fill the whole card and shoving Gear down to the bottom with a huge gap.

## This session's real bug: Boneyard undercounting a known ore pile

Worth reading in full if boneyard counts ever look wrong again — this was chased down properly, not guessed at,
and the final fix is confirmed correct against real game data.

**Symptom:** the owner hand-counted 23 Titanium in one pile near a base; the dashboard's Boneyard showed 9.

**Root cause:** `VisitLoose` in `src/Live/WorldScan.cs` excluded anything where
`WorldObjectsIdHandler.IsWorldObjectFromScene(id)` was true. Pulled the actual decompiled source for that method:
it is literally `id < 200,000,000`, and `WorldObjectsIdHandler.GetNewWorldObjectIdForDb()` proves anything created
during play always gets `id >= 201,000,000`. So that check only ever answers "has this object existed since the
world was generated" — never "is this an embedded, unminable resource" versus "a loose chunk sitting on the
ground." World generation apparently scatters some ore directly as already-loose ground chunks (or an item
started life as loot inside a scene-placed container), so a lot of genuinely-loose, pickupable material carries a
"from scene" id and was being silently dropped from `live-world.json` before ever reaching the dashboard.

**Fix:** removed that check from `VisitLoose`. It now relies on `GetIsPlaced()` + `GetGroup() is GroupItem` to
identify real loose items; the dashboard's own `BaseDirectory.IsBoneyardMaterial` (ore/alloy/quartz/rod only)
still keeps wreck loot and decorative containers out of the Boneyard, and that filter was never dependent on id
range in the first place.

**Verification, not just theory:** cross-checked against the raw save file (`Custom-1.json`) by parsing every
individual Titanium/Cobalt/etc. object record directly and summing by distance from the base pod — matched the
dashboard's live numbers exactly, material by material, after the fix. Also confirmed every loose entry the
plugin reports carries real non-null position data (no phantom entries).

**Note for future self:** this got reverted once mid-session (a live in-game test — removing an "unharvested"
Cobalt piece — looked at first like it proved the fix was wrongly counting unmined ore deposits). It took actually
reading the decompiled `WorldObjectsIdHandler` source to realize "from scene" only means "existed since world
load," not "still embedded" — the two are unrelated. If this ever looks wrong again, don't re-add the id check
without re-deriving this from the actual game source, not from the old comment's assumption.

**Also explored this session (read-only, nothing built):**
- Whether the game tracks item/container ownership at all — it does not. No owner/placer concept anywhere on
  `WorldObject`; base "ownership" of anything is 100% a dashboard-side computation (nearest pod within 100m),
  recomputed fresh every time, never stored.
- Whether "items sitting on the Launch Platform" is queryable — checked by hand against the save (the platform is
  an 8x4m `LaunchPlatform` piece, `Type` 6530, not currently reported in `live-world.json` at all since it has no
  linked inventory and doesn't match any of `VisitConstructed`'s categories). Confirmed 0 items currently sit
  within its real footprint. If this becomes a real feature request, it needs new plugin-side geometry work
  (Renderer/Collider world bounds, read live) — nothing built, just scoped.

## Not committed

Nothing from this session (or several sessions before it — the owner's rule is commits only when asked) has been
committed yet. The working tree currently has, on top of the 2026-09-21 base: the `WorldScan.cs` loose-item fix
above, all four mini-map components (radar sweep + zoom on each), the Cheats page and its supporting services
(`SaveResupplyEngine`, `SaveResupplyService`, `ResupplyConfigStore`, `SaveFolderResolver`, `ToastService`), and
the Backpack/Gear spacing fix. Ask before committing or pushing, per the working rules below.

## Next

1. Keep an eye on Boneyard counts for a regression now that the id filter is gone — if actual landscape decoration
   (wreck loot, a decorative crate) ever shows up as "loose material," the fix over-corrected and needs a
   *narrower* distinguishing signal than id range (which is now confirmed unreliable for this).
2. If "what's on this platform/foundation" becomes a real ask, it needs new plugin-side bounds-reading — scoped
   above, not started.
3. Commit the accumulated changes once the owner is ready.
4. Continue down the original test-plan checklist below for anything not explicitly re-verified this session.

## What works (original, still true)

**Verified in the game (2026-09-21, plugin 0.2.0):** player position, yaw and vitals (with maximums), backpack, gear, six
planet stats with live per-second rates, power (produced, used, generators by kind), rocket counts and multipliers, and
the vehicle (position, trunk, gear). Rocket multipliers matched the wiki maths exactly, the vehicle position matched a
save, and the owner reviewed the dashboard live and was delighted. Later the same day the owner confirmed again that
player, vehicle, power and planet data "all look good".

**Base, Extractors and Drones (plugin 0.3.0):** now fully run and iterated on in the game across several sessions
(see above for what changed most recently). Original build notes:

- **Plugin:** `live-world.json`, written about every 5 seconds by `WorldPoller` (a coroutine), using `WorldScan`, which
  walks the game's placed objects in three passes and spreads the work over frames (about 2 ms each). It reports **raw
  facts**: pods with their panels, signs, containers (with their items) within 120 m of a pod, loose items near a pod, and
  ore, gas, water and algae extractors (product, size, count, contents). It also reports its own cost (`scan`). See `contract.md`, "The world file".
- **Dashboard:** `WorldFileService` reads that file and works out the bases with `BaseDirectory` (a base is a pod with a door;
  door plus connection is a "Base", else an "Outpost"; things belong to the nearest base within 100 m; names from signs, then
  a saved name, then a pool: `BaseNames`).
- **Ported from RRSOS-PCC (copied, not referenced):** `BaseMap`, `BaseContents` (built on the existing `ItemList`), `BaseSelector`,
  the extractor grouping, the item-type table (`Assets/worldobjectdata.json` and `ItemType`/`ItemCatalog`) and the two name pools.

**Drones:** flying drones are read every second into `live.json` (`DroneReader`: `FindObjectsByType<Drone>` re-listed every 5 s, then positions, task and cargo);
drone stations are in the world file (`droneStations`). See `contract.md` ("Drones") and `test-plan.md` section 10.

Not yet confirmed by the owner from the original list: the Energy Levels and Terraformation screen comparisons, the
compass in all eight directions, whether a stowed truck's trunk and gear still show, and the long main-menu wait.
See `test-plan.md` (tick items off as they are done).

## Working rules that emerged (from the owner)

- Read-only toward the running game, awareness not shortcuts. Never change live game state — the plugin only
  observes. Cheats/Resupply is a deliberate, separate exception scoped to editing the save file on disk while the
  game is not in a world; it does not touch the running game.
- RRSOS-PCC stays save-file based; do not make the two depend on each other.
- Ask before committing or pushing. Commits only when explicitly asked, even after a long stretch of changes.
- Changing the game folder (installing or updating BepInEx or the plugin) needs the game closed for the build to
  copy the DLL in, then the game needs a **full restart** (not just a world reload) to actually load new plugin
  code — BepInEx does not hot-swap a running plugin from a file overwrite.
- Test the dashboard against fake data (`tools\sample-live.ps1`) or a real save (`tools\save-to-world.ps1`) first;
  for anything touching the plugin's read of live game state, verify with real data before trusting it (this
  session's Boneyard bug is the case study for why — id-range assumptions and even a live in-game test can both
  mislead if you don't cross-check against the actual game source).
- The owner's saves: `Custom-1.json` (currently the active one) and `Custom-2.json` (older, larger, flagged
  `modded`). Never edit them directly outside of Cheats/Resupply, which the owner explicitly drives.

## Facts worth remembering

- Compass: north is world +X, east is world -Z (measured in the game). `Compass.ToEastNorth` in the dashboard, `PCMath.ToEastNorth` in RRSOS-PCC.
- The game marks saves `modded: true` while `BepInEx\plugins` is non-empty. Cosmetic.
- Fandom wiki pages cannot be fetched by tools (HTTP 402) but open fine in a browser; the wiki's tables match the game.
- The vehicle is the world object with group id `VehicleTruck`; a stowed vehicle has no position. The live data
  model only tracks one vehicle today (`LiveData.Vehicle` is singular) — the Vehicles mini-map caption is
  written to already read correctly as a count/nearest-of-many if that ever becomes a list, but nothing forces
  that yet; the owner plans to test multi-vehicle behavior once they can build a second one in-game.
- **Object ids** (corrected/expanded this session): `WorldObjectsIdHandler.IsWorldObjectFromScene(id)` is
  literally `id < 200,000,000`, and `WorldObjectsIdHandler.GetNewWorldObjectIdForDb()` proves anything created
  during play always gets `id >= 201,000,000`. This is purely an "object age" signal (existed since world load,
  or created later) — **it is not a reliable signal for "is this a real, loose, pickupable item"** versus
  landscape decoration. World generation can place genuinely-loose ore chunks directly, and they keep a
  "from scene" id forever. Panel value 4 is a door, 2 a corridor connection (unrelated, still true).
- A shell quirk on this machine: a long `bash` heredoc with several files in one command can fail to parse; write files one at a time.
- To look at the dashboard while developing: the launch config in `.claude/launch.json` (ignored by git) starts it on fake or converted data; regenerate the decompile
  with `ilspycmd -p -o <a scratch folder> "<game>\Planet Crafter_Data\Managed\Assembly-CSharp.dll"` (about 30 seconds, kept out of the repo).
