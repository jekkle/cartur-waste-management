# Cartur's Waste Management

BepInEx/Harmony mod for Valheim. Adds a trash can and two Sort buttons to the
inventory screen.

Thunderstore package `Carturs_Waste_Management`, plugin GUID
`com.jekkle.valheim.carturwastemanagement`.

## How it works

One Harmony patch: a postfix on `InventoryGui.Show`, which builds the buttons
once and reuses them. Everything else is ordinary UI and inventory work.

- **The can** is a plain `Image` + `Button` hung off the weight readout's own
  parent, so it inherits whatever scale and anchoring the player panel has —
  including under a UI overhaul that moved it. Clicking it reads the cursor's
  `m_dragItem` / `m_dragInventory` / `m_dragAmount` (all private) and recycles
  that many.
- **The refund is half.** `ObjectDB.GetRecipe(item)` is the same recipe the
  bench used. `Requirement.GetAmount(q)` is the price of one *step* rather than
  a running total — `InventoryGui.DoCrafting` targets quality 1 for a craft and
  `m_quality + 1` for an upgrade, and `Player.ConsumeResources` charges
  `GetAmount(target)` once — so a quality 3 item was billed
  `GetAmount(1) + GetAmount(2) + GetAmount(3)` over three presses. The refund
  adds those steps up, skipping requirements the game itself marks
  `m_recover = false`. Half of that bill comes back, rounded up, so a single
  unit of an ingredient still returns one. Partial crafts pay nothing. Items
  with no recipe are removed.
- **EpicLoot's materials** are a second payout on top of the recipe, when that
  mod is present, and which bill an item pays depends on whether it was ever
  enchanted. An enchanted item pays half of
  `EnchantCostsHelper.GetEnchantCost(item, rarity)` — the list the enchanting
  table charged. Everything else EpicLoot will sacrifice never cost anything
  to make, so half of nothing is nothing: trophies, boss drops and
  unidentified items pay `GetSacrificeProducts` instead, unhalved, which is
  EpicLoot's own table and the one the player already knows from its
  enchanting UI. Reached by reflection; there is no build reference and
  nothing happens when EpicLoot is absent.
- **The sort buttons** are clones of the container's Take All button, so
  they carry the game's skin, hover tint and click sound with no art of ours.
  The player one sits under the can. The container pair - Sort for the open
  chest, Sort All for every chest in range - stacks upward from the chest's
  weight box, with the weight box's anchors and width. Until 1.2.3 it sat under
  Place Stacks, which is inside the grid: it covered the first row of cells.
- **Sorting** groups by `m_itemType`, then name, then best quality first, and
  merges split stacks first. The merge rule is copied from
  `Inventory.FindFreeStackItem` — name, quality, world level, room under
  `m_maxStackSize` — and emptied items go out through `Inventory.RemoveItem`,
  which is what vanilla's `StackAll` calls.

### Reaching into nearby chests

A chest's Sort does not only rearrange what is already in it: `GatherLikeItems`
walks the containers within `GatherRange` and pulls in whatever matches. A chest
collects only the item types it already holds, so a chest of ores stays a chest
of ores and gains the ore lying around it. The bag's Sort sorts the bag only and
never takes from chests (until 1.2.3 it did). Item drawers cannot turn up in the query — no type
in the game derives from `Container`, and kg's drawers are `ZNetView`s from
`API.ClientSideV2.AllDrawers` — so they are excluded structurally, by a test
that is not there.

The move names a real slot, and this is where earlier versions went wrong.
`0, 0` is **not** "anywhere". The 5-argument `AddItem` takes
`skipValidPositionCheck` and `MoveItemToThis` hardcodes it `false`, so the guard
`x >= 0 && y >= 0 && x < m_width && (y < m_height || skip)` runs for real and
`0, 0` is slot zero: empty, it clones straight into it and never looks for a
free slot; occupied by a different type, `IsSameType` fails and the move returns
`false` having done nothing. Every gather therefore wrote into slot zero and
silently failed against any target whose first slot held something else.

`MoveOne` picks the slot itself, in the game's own order from
`FindFreeStackItem`: fill a matching partial pile first, then take an empty
slot. It **loops**, because `AddItem` fills the one slot it is handed and no
other — 50 wood offered to a chest with a 20/50 pile and ten empty slots moved
30, left 20 behind and returned `true`. Progress is measured as a fall in
`item.m_stack` rather than read off the return value, since `AddItem` returns
`moved == amount` and so reports a short move as failure; a pass that moves
nothing ends the loop, which is therefore bounded by the stack size.

The amount is clamped to the chosen slot's own room on purpose: `AddItem` can
return `false` having moved items, and the callers use that return to decide
whether to claim and save the donor, so a short move reading as failure would
skip the write and the donor would restore itself from its ZDO.

Every write is preceded by `ZNetView.ClaimOwnership`. `Container.Save` puts
`s_items` straight on the ZDO with no owner check, so a non-owner's write is
local only and the owner's copy lands on the next sync with the items back in
the chest. `ClaimOwnership` returns immediately when already owner, so it costs
nothing in single player. Reachability reads in-use off the ZDO
(`ZDOVars.s_inUse`), not `Container.IsInUse()`: that returns the local
`m_inUse`, which `SetInUse` only sets on the owning client, so it is never true
on the client the guard protects. `InventoryGui.UpdateContainer` calls
`SetInUse(true)` every frame the panel is up, so the chest the player has open
is always flagged — Sort All adds it to the list outright rather than testing it,
or it would skip the one chest the player was looking at.

Boats and carts are refused outright. `Container.CheckAccess` is a three-way
switch on `m_privacy` and one case returns `true` unconditionally, which is the
case a ship's hold takes, so a moored longship passed every other test — and
since the winner is whoever holds the most of a thing, a stocked hold could pull
the base's stores onto the water. The test is `m_wagon` (a public `Vagon` field
on `Container`, which `Catapult.Start` also sets) plus `GetComponentInParent` for
`Vagon` and `Ship`, and the per-press log line counts what it refused.

`Inventory.Changed` takes `(bool, bool)` in this build, and Unity swallows
exceptions thrown inside a `UnityEvent` callback: invoked with no arguments it
throws `TargetParameterCountException`, the items move, and the UI silently
does not refresh. It is invoked with `(false, false)`.

### The hidden-row boundary

Cartur's UI - HUD grows the player's inventory by rows the grid never draws and
keeps the equipment slots, shield slot, quiver and quick-slot foods down there.
A sort that walked the whole grid emptied all of that into the bag. The
boundary is read from `CarturUIHud.Slots.VisibleRows` by reflection — the type
is internal — and falls back to `Inventory.GetHeight()` when that mod is not
installed, which is the right answer then because nothing is hidden.

## Art

`tools/art/` draws the can from geometry (`gen_hd.py`) and lifts the recycling
mark out of a flat two-colour rendering of Gary Anderson's public-domain 1970
design (`cutout.py`). Nothing is sampled from another mod's sprite.
`make_icon.py --install` renders and copies into `src/Assets/`.

## Build

```
cd src
dotnet build
```

Override the Steam path or the r2modman profile with `-p:VALHEIM_INSTALL=...`
or `-p:R2MODMAN_PROFILE=...`; do not edit the csproj.

## Release

```
powershell -ExecutionPolicy Bypass -File tools\pack.ps1
powershell -ExecutionPolicy Bypass -File tools\releasecheck.ps1
powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -WhatIf
powershell -ExecutionPolicy Bypass -File tools\publish.ps1
powershell -ExecutionPolicy Bypass -File tools\publish-nexus.ps1
```

Nexus has no API to create a mod page, so 1.0.0 was uploaded by hand on the
site. It is mod 3888, and that id is now the default in `publish-nexus.ps1`, so
every version after goes up with the rest.

---

**[Discord](https://discord.gg/nd5RqpwNkz)** — bug reports, install help, and mod requests.
Bug reports get their own thread so nothing is lost in a chat scroll, and requests are voted on.

