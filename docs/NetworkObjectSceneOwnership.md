# NetworkObject Scene Ownership

Friend Slop runs Netcode for GameObjects (NGO) with `NetworkConfig.EnableSceneManagement = true` and loads `ShipInterior` + `Planet_*` + interior scenes **additively**. That choice forces a small set of contracts on every piece of runtime code that spawns or queries `NetworkObject`s. Get these wrong and the symptoms are silent: objects end up in `DontDestroyOnLoad`, spawn counts assert before any real spawn fires, post-Spawn scene moves "succeed" and then revert. Each pitfall below has already cost real debugging time.

This document is the long form of CLAUDE.md hard rule 9 and architecture decision D-011. Read it before writing new spawn code or new tests that count network objects.

## Rule 1 — Set the active scene *before* `Instantiate` + `Spawn`

`NetworkObject.Spawn(destroyWithScene: true)` latches `SceneOriginHandle` to whatever scene the GameObject is in **at the moment of `Spawn`**. The way to put a clone in `targetScene` is to make `targetScene` active before `Instantiate`. Don't `Instantiate` first and `MoveGameObjectToScene` afterwards — with NGO scene management on, the move fights NGO and silently fails to stick.

The canonical pattern is an active-scene swap around the whole spawn batch:

```csharp
// PrototypeNetworkBootstrapper.Spawning.cs — private ref struct ActiveSceneScope.
using (new ActiveSceneScope(activeEnv.gameObject.scene))
{
    foreach (var spawnPoint in anchors)
    {
        var clone = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
        clone.NetworkObject.Spawn(destroyWithScene: true);
    }
}
```

`ActiveSceneScope` is a private nested `ref struct` in [`PrototypeNetworkBootstrapper.Spawning.cs`](../Space%20Game/Assets/Scripts/Session/PrototypeNetworkBootstrapper.Spawning.cs) (in the `FriendSlop.Gameplay` assembly, not Networking — the bootstrapper is the gameplay composition root; see D-006). The same pattern is inlined in [`PlanetLootSpawner.TrySpawnNow`](../Space%20Game/Assets/Scripts/Loot/PlanetLootSpawner.cs) as a `try`/`finally` (it would be reasonable to hoist `ActiveSceneScope` into a shared utility once a third call site appears — don't promote it for a single duplication).

Pass `destroyWithScene: true` unless the object genuinely needs to outlive its owning scene. The default of `false` sends the object to `DontDestroyOnLoad`, which is almost never the right answer for planet/interior content and leaks across planet transitions.

## Rule 2 — Filter `IsSpawned` on `FindObjectsByType<T>`

With NGO scene management on, `NetworkManager.NetworkConfig.NetworkPrefabsList` **parks every prefab template** in the Bootstrap scene as an inactive instance. They have a `NetworkObject` component but `IsSpawned == false`. Calling:

```csharp
Object.FindObjectsByType<NetworkLootItem>(FindObjectsInactive.Include, FindObjectsSortMode.None)
```

returns those templates **alongside** the live runtime clones. Unfiltered counts are non-zero before any real spawn fires, which makes spawn-count assertions race-prone and false-positive.

The fix is to filter by `NetworkObject.IsSpawned`. Reference helper for tests:

```csharp
// FriendSlopPrototypeSmokeTests.CountSpawned<T>
private static int CountSpawned<T>() where T : NetworkBehaviour
{
    var items = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    var count = 0;
    for (var i = 0; i < items.Length; i++)
    {
        if (items[i].NetworkObject != null && items[i].NetworkObject.IsSpawned)
            count++;
    }
    return count;
}
```

In runtime code, the same filter applies whenever you query the world for "live X" instead of "any X". `FindObjectsInactive.Exclude` is *not* sufficient — NGO can leave templates with active GameObjects, depending on prefab settings.

## Rule 3 — Despawn before unload, scope find queries by scene

When a planet or interior unloads, despawn its `NetworkObject`s explicitly on the server (`NetworkObject.Despawn(destroy: true)`) before tearing down the scene. Relying on `destroyWithScene` works *if* `SceneOriginHandle` was captured correctly (rule 1) — but explicit despawn is the only safe behaviour when the object should outlive scene unload (rare; e.g. a player's carried item across planets).

When a query genuinely needs to be scoped to a single scene (loot in the active planet, doors in the current interior), check `gameObject.scene` after the find. Don't infer location from scene name — see SpaceshipSceneManagement.md rule "never infer location from current scene name".

## Known stale sites

These don't follow the contract yet and are queued in [BACKLOG.md](../BACKLOG.md) section 16b:

- `MeteorShower.SpawnMeteor` ([Hazards/MeteorShower.cs:121](../Space%20Game/Assets/Scripts/Hazards/MeteorShower.cs)) — still uses `Instantiate` → `MoveGameObjectToScene` → `Spawn`. Convert to the active-scene swap.
- `AnomalySpawner.Spawn` ([Hazards/AnomalySpawner.cs:71](../Space%20Game/Assets/Scripts/Hazards/AnomalySpawner.cs)) — defaults `destroyWithScene = false`, leaking anomalies into `DontDestroyOnLoad`.

## Related

- [Space Game/CLAUDE.md](../Space%20Game/CLAUDE.md) hard rule 9 — the short version of this contract.
- [architecture.md](architecture.md) D-011 — the decision record and why this is load-bearing.
- [FeatureIntegrationContracts.md](FeatureIntegrationContracts.md) "New Networked Spawn" — the contract every new spawn site should satisfy.
- [SpaceshipSceneManagement.md](SpaceshipSceneManagement.md) — additive scene roles (`Bootstrap`, `ShipInterior`, `Planet_*`, `Travel_*`).
