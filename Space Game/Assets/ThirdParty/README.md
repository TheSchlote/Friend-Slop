# Assets/ThirdParty — vendor quarantine

Every Asset Store / third-party pack lives here, one folder per pack, **not** in
`Assets/<PackName>/`.

Rules (full rationale: [`docs/architecture.md`](../../../docs/architecture.md) **D-012 / D-013**,
agent summary: [`Space Game/CLAUDE.md`](../../CLAUDE.md) hard rule 10):

1. **Import-once.** A pack is added in a single dedicated PR (`vendor: add <Pack> vX`)
   that does nothing else. Feature branches never import, re-import, or re-export a
   pack — they only reference one that already landed on `main`.
2. **One folder, own asmdef.** `Assets/ThirdParty/<Pack>/` with a
   `ThirdParty.<Pack>.asmdef` (set `autoReferenced` only when our runtime calls into
   it). This stops our code coupling to the pack's churn.
3. **Strip the fat.** Delete the pack's demo / example / sample-scene folders on
   import.
4. **Binaries via LFS.** If the pack adds a new binary extension, extend
   `.gitattributes` LFS coverage in the same import PR.

Migration of the packs that were mislocated on `main` is **complete** (BACKLOG §17a–c,
landed 2026-05-18). Per the §17b inventory (GUID-reference analysis):

- **Dropped** (zero references anywhere in shipped content): `LowPolyInterior`,
  `LowPolyInterior2`, `_Recovery`.
- **Relocated here** (genuinely referenced, kept + quarantined, each with its own
  `ThirdParty.<Pack>` asmdef): `HIVEMIND` (blood VFX),
  `Plugins/Microdetail` (tier-4 terrain — plus nested `ThirdParty.Microdetail.Editor`
  and `ThirdParty.Microdetail.SetupWizard` editor asmdefs),
  `YughuesFreeRockMaterials` (rock material).

Only the optional destructive `.git`/LFS history purge (§17d, separately gated)
remains outstanding; no `FriendSlop.*` assembly references any vendor pack
(vendor is GUID/asset-wired, not code-wired).
