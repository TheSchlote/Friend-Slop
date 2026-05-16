# Interior System

Buildings on planets open into their own additively loaded interior scene. The content is data-driven and the layout is deterministic per seed. There are two generation paths: **procedural** (rolled from a `BuildingDefinition` + seed) and **authored** (driven by a `BlueprintAsset`). Both produce the same `InteriorLayout` shape, so the bootstrapper, door spawner, and furniture pipeline don't fork.

This document is the long form of architecture decisions [D-009](architecture.md) (data-driven interiors) and [D-010](architecture.md) (blueprints). Use it before adding a new building, room, furniture piece, or blueprint.

## Pipeline overview

```
[planet scene]                                    [interior scene]
                                                              
  InteriorEntrance ──writes──▶ InteriorSessionData ──read──▶ InteriorSceneBootstrapper
   (NetworkBehaviour)             (static carrier)             (NetworkBehaviour)
                                                                       
                                                                       ▼
                                                  ┌────────── procedural ──────────┐
                                                  │  InteriorLayoutGenerator       │
                                                  │  .Generate(definition, seed)   │
                                                  └────────────────────────────────┘
                                                                  OR
                                                  ┌────────── authored ────────────┐
                                                  │  BlueprintLayoutBuilder        │
                                                  │  .Build(blueprint, definition) │
                                                  └────────────────────────────────┘
                                                                       │
                                                                       ▼
                                                              InteriorLayout
                                                              (rooms + connections)
                                                                       │
                                                                       ▼
                                                  spawn rooms, doors, furniture
```

1. **Player enters the trigger** on `InteriorEntrance` (or `BlueprintEntrance`) attached to the building exterior.
2. **Server captures session data**: seed (random for procedural; ignored for blueprints), `BuildingDefinition`, return pose, requesting client ID, interior scene path, optional `BlueprintAsset`. This is written to the static `InteriorSessionData`.
3. **Server loads the interior scene** additively through `NetworkSceneTransitionService`.
4. **`InteriorSceneBootstrapper.OnNetworkSpawn`** reads `InteriorSessionData` and replicates seed/origin to clients via `NetworkVariable`s.
5. **All clients** run the layout pipeline locally. Procedural goes through `InteriorLayoutGenerator`; blueprints go through `BlueprintLayoutBuilder`. Both return an `InteriorLayout` (placed rooms + connections).
6. **The bootstrapper spawns rooms** by instantiating per-room prefabs. For blueprints, `BlueprintLayoutBuilder` resolves each authored slot to a family of variants via `RoomVariants.FindVariants` (room defs whose asset name shares a family — `Room_Residential_Bathroom_2x2.A` and `.B` belong to family `Room_Residential_Bathroom_2x2`) and picks one at spawn time, giving spawn-to-spawn variety without re-rolling the layout.
7. **Server spawns doors** as `NetworkObject`s so open/close state syncs.
8. **Each client picks furniture** from the seed locally. Pieces are deterministic — same seed yields same furniture on every client — but they are *not* `NetworkObject`s.
9. **Players are teleported** into the interior; the exit door (`InteriorExitDoor`) carries the return pose back to the planet.

## Data definitions (ScriptableObjects)

| Asset | Purpose | Location |
|---|---|---|
| `BuildingDefinition` | Min/max rooms, floor count, required-room list, optional room pool, special-room targets. | `Assets/Interiors/Buildings/*.asset` |
| `RoomDefinition` | Cell footprint, floor restrictions, furniture rules, room kind/category, socket directions. | `Assets/Interiors/Rooms/*.asset` |
| `FurnitureDefinition` | Anchor placement (`Wall`/`Corner`/`Center`/`Tabletop`/`AroundTable`/`WallHanging`), tags, footprint, weight, interactable flag, prefab. | `Assets/Interiors/Furniture/*.asset` |
| `BlueprintAsset` | Authored layout: explicit room placements + per-edge wall/open/door overrides + per-slot variant overrides. | `Assets/Interiors/Blueprints/*.asset` |
| `InteriorCatalog` | Registry of all `BuildingDefinition`s and `FurnitureDefinition`s; the runtime lookup surface. | `Assets/Interiors/InteriorCatalog.asset` |

Adding content is a `.asset` file, not a code branch. See the "New Building Type", "New Furniture Definition", and "New Blueprint" entries in [FeatureIntegrationContracts.md](FeatureIntegrationContracts.md).

## What is networked vs. local

| Thing | Where | Why |
|---|---|---|
| Seed + building identifier + origin | `NetworkVariable` on `InteriorSceneBootstrapper` | Clients need them to reproduce the layout. |
| Layout (room placements, connections) | Regenerated locally on each client from the seed | Determinism contract; cheaper than syncing the graph. |
| Doors (`InteriorDoor`, `InteriorExitDoor`) | Spawned as `NetworkObject`s on the server | Open/close state must sync across clients. |
| Furniture | Local GameObjects on each client | Deterministic-from-seed; no `NetworkObject` per chair (bandwidth/spawn-count savings). |
| Blueprint asset reference | Carried by `InteriorSessionData`; clients load the same `BlueprintAsset` by reference | Asset is content, not state. |

**The determinism contract is load-bearing.** Any source of nondeterminism in generation — `Time.time`, an `unseeded Random`, an order-dependent `FindObjectsByType` — is a correctness bug, not a style nit. Clients will see different rooms and the bug will not surface until two players are in the same interior trying to walk through walls.

## Procedural vs. blueprint

| Concern | Procedural | Blueprint |
|---|---|---|
| Entry component | `InteriorEntrance` | `BlueprintEntrance` |
| Seeded? | Yes — server picks, clients regenerate | No — layout is explicit |
| Door policy pass (`ApplyDoorPolicy`) | Runs | **Bypassed** — edge state is user-authored |
| Room variants | Generator picks from `BuildingDefinition.RoomPool` per slot | `BlueprintLayoutBuilder` resolves family + grid size via `RoomVariants.FindVariants` and rolls per spawn |
| When to use | Generic dungeons, warehouses, randomised buildings | Hand-authored signature buildings (residential, homebase) |
| Authoring tool | None — content is the `BuildingDefinition` + room pool | `BlueprintEditorController` / `BlueprintEditorUI` (F1 toggle in test scenes) |

Don't mix the two in a single building. Pick one entry component.

## Where the code lives

- `Space Game/Assets/Scripts/Interiors/` — runtime data, entrance, exit, bootstrapper, generator, doors, minimap, events.
- `Space Game/Assets/Scripts/Interiors/Blueprints/` — blueprint asset, builder, entrance variant, editor controller/UI, room variant helpers.
- `Space Game/Assets/Scripts/Editor/Builders/FriendSlopSceneBuilder.Interiors*.cs` — editor-time prefab/scene generation for interior content.
- `Space Game/Assets/Tests/EditMode/Editor/InteriorLayoutGeneratorTests.cs` — current EditMode coverage. More tests queued in [BACKLOG.md](../BACKLOG.md) section 16c (`BlueprintLayoutBuilderTests`, `BuildingDefinitionRoomPoolTests`, `FurnitureSelectionTests`, `InteriorCatalogTests`, plus a PlayMode smoke).

## Known oversized files

The following are baselined in `ArchitectureGuardrailTests.ExistingOversizedRuntimeFiles` and **must be split before adding to**:

- `InteriorLayoutGenerator.cs` (1727 lines) — split at: required-room quotas, frontier expansion, downward-connector mirroring, fallback generation.
- `BlueprintEditorUI.cs` (1005 lines, editor-only) — per-panel split.
- `InteriorSceneBootstrapper.cs` (832 lines) — extract wall-patching / garage-door geometry, then materials, then door spawning.
- `BlueprintEditorController.cs` (545 lines, editor-only) — smallest, safest first split.
- `InteriorSceneBootstrapper.Furniture.cs` (527 lines) — extract pickers (`PickFurnitureForAnchor`, `HasTagOverlap`) into `.Furniture.Picking.cs`.

## Related

- [Space Game/CLAUDE.md](../Space%20Game/CLAUDE.md) — agent-facing hard rules, including the Interiors subsection.
- [architecture.md](architecture.md) — decisions D-009 (data-driven content) and D-010 (blueprints).
- [NetworkObjectSceneOwnership.md](NetworkObjectSceneOwnership.md) — D-011, which governs how doors and any other interior `NetworkObject`s get spawned.
- [FeatureIntegrationContracts.md](FeatureIntegrationContracts.md) — the "New Building Type", "New Furniture Definition", and "New Blueprint" contracts.
