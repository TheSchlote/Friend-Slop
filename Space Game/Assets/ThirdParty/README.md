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

Migration of the packs currently mislocated on `main` (`HIVEMIND`,
`LowPolyInterior`, `LowPolyInterior2`, `Plugins/Microdetail`,
`YughuesFreeRockMaterials`, `_Recovery`) is staged in **BACKLOG §17**.
