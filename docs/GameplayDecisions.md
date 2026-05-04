# Gameplay Decisions

Open design questions for Friend Slop Retrieval. This is a working document for you and your friend to talk through and check off as decisions land.

Each section includes: the question, why it matters downstream, the concrete options on the table, and my read on the right call. None of these are technical blockers — they're design calls that will shape the next set of features.

When a decision is made, replace the **Decided?** line with the choice and a short rationale, then update the relevant section of [BACKLOG.md](../BACKLOG.md) and (if it changes architecture) [docs/architecture.md](architecture.md).

---

## 1. Tier 2 planet identity

**Question.** Are Cobalt Trench, Volt Foundry, Wraith Halo, Deep Haul, and Ghost Shift unique planets, or mission variants on the same scene?

**Why it matters.** Today they all fall back to Rusty Moon's environment. That's fine as a stub but every additional planet asset compounds the visual sameness. The decision drives whether the scene-split work (BACKLOG #1) needs five new scenes per tier or just five new `PlanetDefinition` assets pointing at one shared scene with different objectives.

**Options:**
- **A. Mission variants on the same scene** — Each tier 2 planet uses the same scene (Rusty Moon) but has a distinct `PlanetDefinition.asset` with a unique `RoundObjective`, loot pool, and HUD copy. Cheap, ships fast, leans into the "you've been here before but the job is different" framing. Risk: visually monotone.
- **B. Unique planets, unique scenes** — Each tier 2 gets its own scene with its own geometry, lighting, hazards. Aligns with the "one planet per scene" architecture target. Cost: ~5 new scenes worth of authoring + ~1 PR per planet per Phase-2 of the planet split.
- **C. Hybrid** — Two or three "real" tier 2 planets with their own scenes (the marquee ones), the rest as mission variants on Rusty Moon framed as "return contracts."

**My read.** Start with **C**. Pick two unique tier 2 planets that read as visually distinct (Cobalt Trench = water/cave, Volt Foundry = industrial). The other three become "Rusty Moon: [variant name]" contracts with clearly different objectives. This gets visual variety where it counts and keeps scene authoring manageable.

**Decided?** Pending.

---

## 2. Progression endpoint and run loop

**Question.** What happens when a crew clears the final tier? `PlanetCatalog.MaxTier` is 10; authored content only reaches 3.

**Why it matters.** Right now there's no defined "win the game" state. Every UI screen, the round-end transition, replay decisions, and even saved-progress design depend on this answer.

**Options:**
- **A. Run-based (rogue-like).** Final tier success → credits → return to lobby with a session score. Each session starts from tier 1. Optional: persistent unlocks (cosmetics, ship modules) carry between runs.
- **B. Endless cycle.** Final tier success loops back to tier 1 with a difficulty multiplier (more hazards, higher quotas). The "ending" is whenever the crew dies out. Score-attack framing.
- **C. Authored campaign with epilogue.** Tier 10 has unique narrative content; success ends the expedition; failure → game over. Fixed-length experience.
- **D. Open-ended sandbox.** Pick which planet to fly to from a star map; no forced tier progression; "win" by hitting a money goal you set.

**My read.** **A (run-based)** fits a co-op salvage game's repeat-play loop best, and lets you ship with tier 3 content while the loop still feels complete. The "return to lobby" beat is something `RoundManager` already handles structurally. **B** is a strong follow-up once the loop is fun.

**Failure handling.** If a crew All-Dead's on a mid-tier planet, do they restart that tier or lose the run? Recommendation: lose the run on All-Dead; keep tier on partial-fail (Quota miss with surviving players).

**Decided?** Pending.

---

## 3. Ship stations

**Question.** What does each ship station actually do?

**Why it matters.** `ShipStation` exists as a generic "claim/release" component. Until each station has a real flow, the ship interior is decoration. Stations are the most natural place to gate co-op coordination (one player drives, another scans, another loads).

**Decisions needed per station type:**
- **Pilot station** — Does it control travel? Does it require a player to occupy it for travel to start? Or is travel a vote?
- **Holographic board / planet preview** — How are planet choices presented? Roll-of-three (per BACKLOG #1's `ServerRollNextPlanetChoices`)? Star map with travel cost? Random with re-roll consumable?
- **Module slot / customization bench** — Cosmetic only, or gameplay-affecting (ship upgrades: bigger inventory, faster travel, ammo refills)?
- **Vending / restock** — Where do players buy ammo, healing, ship parts they're missing? Tied to deposited money?

**My read.** Treat the ship as a hub, not a UI replacement. Recommendation:
- Pilot station: **required-occupant model**. Travel starts only when one player presses "Engage" while occupying it. Adds tactility, gives the host station a clear role.
- Holographic board: **roll-of-three with one re-roll**. Already half-built; just needs UI.
- Customization: **cosmetic only at first**. Gameplay upgrades open a balancing rabbit hole; defer until the core loop is fun.
- Restock: **single vending kiosk**, money-driven. Sell back unwanted loot. Don't duplicate the deposit flow; let the same money stat drive both.

**Decided?** Pending.

---

## 4. Teleporter flow

**Question.** Should teleport be automatic (current), interact-to-use, station-controlled, or phase-gated?

**Why it matters.** Auto-teleport feels surprising during launchpad/extraction phases. It also makes carrying loot or bodies awkward (you might teleport mid-haul). Phase-gating it constrains the round shape.

**Options:**
- **A. Keep it automatic.** Trigger pad teleports on contact. Simple; works today.
- **B. Press-to-teleport.** Stand on the pad, hold E, get teleported. Removes accidents.
- **C. Station-controlled.** Only the ship-side pad activates when a crewmate at the right station hits a button. Forces coordination.
- **D. Phase-gated automatic.** Auto-teleport, but only during certain phases (e.g. Active and Success on planet → ship; Lobby and Transitioning blocked).

**Carrying interactions.** Should you be able to teleport while carrying loot? A body? A dead crewmate?

**My read.** **B (press-to-teleport)** for the planet-side pad with a 1-second hold to confirm; auto for the ship-side pad once the round resolves. Allow teleport with carried loot (it's the whole point); allow carried bodies but emit a chat log so the rest of the crew sees who came back with whom.

**Decided?** Pending.

---

## 5. Carried/deposited loot across travel

**Question.** What happens to carried and deposited loot when the crew travels to a new planet?

**Why it matters.** This was a grooming question on BACKLOG item 1. It affects the entire economy: do players need to commit to depositing before extraction, or can they hoard?

**Options:**
- **A. Deposited loot persists; carried loot is lost.** Strong incentive to deposit before flying. Punishing for last-second extraction.
- **B. Both persist.** Travel teleports loot with the player. Easiest from a dev standpoint; risks letting players carry oversized inventories between planets.
- **C. Deposited persists; carried converts to cash on travel.** Loot turns into money in the team pool when you fly. Removes the "carry forever" hoard while not punishing the player who didn't make it to the deposit zone in time.
- **D. Both persist, but the ship has a finite cargo bay.** Hard cap on what comes home. Leans into the "salvage" framing.

**My read.** **C** for the first iteration (simple, fair, supports the deposit zone's purpose), with **D** as a stretch goal if you add ship customization (cargo bay upgrade as a meaningful gameplay choice).

**Decided?** Pending.

---

## 6. Objective UX

**Question.** What does the HUD actually say at each phase, and how prominent should objective state be?

**Why it matters.** Today the success/fail copy still assumes rocket assembly. New objective types (`QuotaObjective`, `SurvivalObjective`) reuse this copy and read wrong.

**Decisions needed:**
- **HUD start of round.** "Find 3 ship parts" / "Reach $500 in deposits" / "Survive 5 minutes" — should it scroll across the screen, or live in a corner?
- **Mid-objective progress.** Per-part icons? Money counter? Survival timer?
- **Extraction-ready signal.** When is the crew clear to leave? Does the launchpad change visually? Audio cue?
- **Success / failure copy.** Per-objective, not "the rocket is assembled."
- **Compact line vs. dedicated panel.** The current single HUD line is dense; should each objective type get its own widget?

**My read.** Per-objective `BuildHudStatus()` already exists on `RoundObjective` — that's the right seam. Decisions are mostly text and a per-objective icon. Recommend:
- One persistent HUD widget for primary objective (top-left, replaces current `timerText` content).
- One toast/banner for extraction-ready ("Launchpad active! Board to extract").
- Success/failure copy moves into the `RoundObjective` asset itself as a `successText`/`failureText` field.

**Decided?** Pending.

---

## 7. Loot economy and budgets

**Question.** How is loot value distributed per mission?

**Why it matters.** Hand-tuned quotas per planet don't scale. As content grows, you'll want a budget rule that says "this mission guarantees $X minimum value with rarity variance."

**Decisions needed:**
- **Minimum guaranteed value per objective.** Should the planet always spawn enough loot to clear the quota with margin, or should rarity rolls sometimes leave the crew short?
- **Rare items: required or optional upside?** If a mission can roll without enough rares, is it a bad luck loss or a "scour the map harder" challenge?
- **Scaling axis.** Tier? Crew size? Objective type? Or a simple hand-set per-planet number?

**My read.** Use a **budget per-mission model** with a **120% guaranteed minimum and 200% potential ceiling**:
- Each `PlanetDefinition` declares `quotaTarget` (already exists) and `lootValueBudget` (new).
- Spawner rolls until total spawned value ≥ 1.2 × `quotaTarget` and ≤ 2.0 × `quotaTarget`.
- Within the budget, item selection comes from a `LootPool.asset` rolled by rarity weights.
- Don't scale by crew size for v1 — too easy to game by ghost-joining.

**Decided?** Pending.

---

## 8. Combat and weapons

**Question.** Friendly fire? Weapon role? Ammo persistence?

**Why it matters.** Laser gun and boxing gloves work mechanically but their place in the game isn't decided. Friendly fire in particular changes group dynamics dramatically and constrains future combat content.

**Decisions needed:**
- **Friendly fire.** On (full PvP), off (no damage between crewmates), or partial (knockback yes, damage no)?
- **Weapon distribution.** Loot-only (find on planets), shop-only (buy at the ship), mission-tool (provided per round), or rare chaos items (one per run, high impact)?
- **Ammo and cooldowns.** Reset between rounds? Persist with the weapon? Recharge over time?
- **Death from a teammate.** If FF is on, does the killer get the body? Does anyone get accountability beyond a chat log?

**My read.** **Partial FF (knockback yes, damage no)** is the sweet spot for a co-op salvage game — it allows funny moments and tactical pushes without "friend just killed me at the launchpad." Weapons as **loot-only with rarity tiers**, ammo persists with the weapon, refilled at a ship vending kiosk (#3) for cash.

**Decided?** Pending.

---

## 9. Hazard pacing and per-planet identity

**Question.** Which hazards belong to which planet, and how do they pace?

**Why it matters.** Today monsters and anomaly orbs spawn generically. Per-planet hazard sets are the cheapest way to make planets feel different (BACKLOG #2 plays into this).

**Decisions needed:**
- **Per-planet hazard composition.** Should `PlanetDefinition` declare which hazard types are eligible? Should hazards have their own catalog asset?
- **Time-based escalation.** Do hazards ramp during the round (more spawns, faster movement)? Or stay static?
- **Counterplay signaling.** Audio cue before a monster aggros? Visual telegraph for anomaly orbs? How much "see it coming" do players get?
- **Hazard difficulty by tier.** Tier 1 = one slow monster, tier 5 = anomaly fields, etc.?

**My read.** Add a **`PlanetHazardSet.asset`** ScriptableObject referenced from `PlanetDefinition`. It declares spawn rates, eligible hazards, ramp curve. This keeps hazard tuning data-driven (matches the SO-first rule). Per-planet flavor unlocks naturally: "Wraith Halo has anomaly orbs but no monsters; Cobalt Trench has slow monsters but flooding."

**Decided?** Pending.

---

## 10. Dead-player loop

**Question.** Can dead players be revived? What does carrying a body to the ship/launchpad do?

**Why it matters.** Body carrying is built but the loop around it isn't defined. Without rules, dying just means the player spectates and waits — boring.

**Options:**
- **A. Permadeath per round.** Bodies carried back are cosmetic / for chat banter. Dead players spectate until the next round.
- **B. Revive at the ship.** Carry the body to a specific ship station (medbay?). Costs money or a consumable. Player respawns.
- **C. Revive on extraction.** All carried bodies revive automatically when the crew extracts successfully. Bodies left on the planet stay dead.
- **D. Revive via launchpad.** Bodies dropped on the launchpad before takeoff get the player back; otherwise dead.

**Spectator mechanics.** While dead, the player follows a teammate's camera. Should they be able to ping objects? Voice chat with the living? Whisper-only mode?

**My read.** **C (revive on extraction)** plus **partial spectator interaction** (can ping locations, can voice chat with the living). Reasoning: it makes the carry mechanic load-bearing without adding a station, rewards crews that actually retrieve fallen friends, and gives the dead player a meaningful goal (help the living finish so I revive).

**Decided?** Pending.

---

## 11. UI generation strategy

**Question.** Should `FriendSlopUI` stay procedural, move to prefabs, or use UI Toolkit (UXML)?

**Why it matters.** Five partial files of procedural UI is workable for a prototype, but the next layer of UI work (planet selection screen, station UIs, inventory expansion, settings menu) will balloon those files past readable.

**Options:**
- **A. Stay procedural.** Lowest churn. Each new screen adds a partial.
- **B. Move large screens to prefabs.** Menu, planet selection, station dialogs become prefabs assigned via `SerializeField`. HUD stays procedural.
- **C. UI Toolkit (UXML/USS).** Modern Unity UI; designer-friendly with hot reload; steeper authoring learning curve; mostly orthogonal to existing UGUI code.
- **D. Hybrid: prefabs + drive from `OnValueChanged` events.** Replace the per-frame `RefreshUi` polling with subscriptions; move composite screens to prefabs.

**My read.** **D**. The polling-architecture cost (every frame rebuilds layout) is the real pain point, not the procedural authoring per se. Move large composite screens to prefabs as you add them; subscribe to `NetworkVariable.OnValueChanged` for per-widget updates; keep the simple HUD widgets procedural for now.

**Decided?** Pending.

---

## 12. Lobby and matchmaking UX

**Question.** How do players find each other?

**Why it matters.** Relay + LAN exist but the join flow is "type a code." For public play, that's friction. For friend play, it's fine.

**Decisions needed:**
- **Discovery model.** Code-only (today)? Public lobby browser? Friend list from a platform (Steam, etc.)?
- **Host configuration.** Should the host pick crew size, seed, difficulty, privacy at lobby creation, or are those all defaults?
- **Ready-up.** Does everyone press Ready before round start, or does the host start whenever they want?
- **Mid-session join.** Allowed today; should there be a player count cap, a phase restriction (no joining mid-Active?), or a host invite-only toggle?

**My read.** Stay code-based until the game is fun enough to invite strangers. When that day comes, **public lobby browser** > friend list (no platform dependency). Host config at lobby creation: privacy, max crew size. Skip ready-up; host starts the round. Mid-join allowed in Lobby and Transitioning phases only.

**Decided?** Pending.

---

## 13. Chat scope

**Question.** What does chat actually need before public play?

**Why it matters.** Functional but minimal. With strangers, chat needs moderation hooks; with friends, it doesn't.

**Decisions needed:**
- **Persistence.** Across rounds? Across sessions?
- **System messages.** Should kills, deposits, deaths, phase transitions appear in chat?
- **Moderation.** Mute, block, rate-limit, profanity filter?
- **Voice chat.** In scope, or out?

**My read.** Defer chat polish until lobby/matchmaking decisions land — those will dictate whether chat needs heavy moderation tooling. For friend-only play (current state), keep chat simple: persist within session, add system messages for big events (deposits, deaths, extraction), skip moderation entirely. Voice chat: defer (Steam/Discord will cover it for friend play).

**Decided?** Pending.

---

## Decision dependencies

Some decisions block or enable others. Roughly:

```
[2. Progression endpoint] ─┬─> [3. Ship stations: pilot]
                            └─> [11. UI: planet selection screen]

[4. Teleporter flow] ───────> [5. Carried loot across travel]

[7. Loot economy] ──────────> [8. Combat: ammo refills via vending]

[9. Hazard pacing] ─────────> [1. Tier 2 identity]  (hazard set per planet)

[12. Lobby UX] ─────────────> [13. Chat moderation needs]
```

Suggested decision order if you want to unblock the most downstream work: **2 → 4 → 5 → 11 → 9 → 1**, then the rest.

---

## When to update this doc

- A decision is made → strike through "Pending" and write the choice + rationale on the **Decided?** line.
- A new gameplay question surfaces in playtest → add a section.
- Architecture impact → cross-reference [docs/architecture.md](architecture.md).
- A decision changes a tracked BACKLOG item's scope → update [BACKLOG.md](../BACKLOG.md) at the same time.
