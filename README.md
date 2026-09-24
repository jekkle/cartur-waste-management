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
- **The two Sort buttons** are clones of the container's Take All button, so
  they carry the game's skin, hover tint and click sound with no art of ours.
  The player one sits under the can; the container one takes Place Stacks'
  anchors, pivot and size and sits directly beneath it.
- **Sorting** groups by `m_itemType`, then name, then best quality first, and
  merges split stacks first. The merge rule is copied from
  `Inventory.FindFreeStackItem` — name, quality, world level, room under
  `m_maxStackSize` — and emptied items go out through `Inventory.RemoveItem`,
  which is what vanilla's `StackAll` calls.

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
