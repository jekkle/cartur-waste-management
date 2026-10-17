# Changelog

## 1.2.6

- In-game screenshots in the README. No code changes.

## 1.2.5

- New icon, and the README is rewritten: a fuller description of what the mod does, every setting and default checked against the code, and image captions that match the pictures. No code changes. Thunderstore also gets 1.2.4's fixes with this version, since 1.2.4 went out while the Thunderstore listing was down.

## 1.2.4

- The bag's Sort sorts your bag only. It no longer pulls matching items in from the chests
  around you (bug report on Nexus); a chest's Sort still gathers from nearby chests.
- The chest's Sort and Sort All moved above the chest's weight. Under Place Stacks they
  covered the first row of slots.
- No more yellow "Could not find type" warnings in the log when Epic Loot or Cartur's UI
  is not installed (bug report on Nexus). They were harmless, and are gone.

## 1.2.3

- Sort skips graves and world chests; only chests you built are sorted.
- Sort All while standing on a boat sorts only that boat's storage.
- Refunds drop in full stacks.
- Equipped arrows and hidden inventory rows are left alone.
- A guard against the item on your cursor being lost.

## 1.2.2

- **Sort no longer destroys items.** Merging split piles walked each stack as a target after
  it had already been poured out and emptied, so it refilled itself from the piles after it
  and was then removed anyway - with a full stack inside. Four quarter-stacks of wood, the
  exact mess the button exists for, went in as 100 and came out as 50, and `Container.Save`
  wrote that to the chest. This hit every sort, bag and chest alike.
- **Scrapping no longer loses or duplicates the refund.** `Inventory.AddItem(prefab, amount)`
  clamps the stack it creates to one max stack and silently drops the rest, so scrapping 20
  bread owed 100 barley flour and handed back 50. It is now given out one stack at a time.
  The fallback that drops the refund at your feet when the bag is full was worse: the game's
  add can place some of the units and still report failure, and the old code believed the
  failure and dropped the whole amount on top of what it had already put in your bag. What
  the bag actually took is now counted, and only the shortfall hits the ground.
- **Sort All now includes the chest you have open.** It was the one chest it skipped. The
  game flags an open container as in use on its ZDO every frame the panel is up, and Sort All
  used that flag to avoid chests somebody else is in - so the chest you were looking at was
  never tidied, never a donor and never a winner.
- **A stack now moves in full.** The game's add fills the one slot it is handed and no other,
  so a move could only ever take that slot's remaining room: 50 wood offered to a chest with a
  20/50 pile and ten empty slots moved 30, left 20 behind, and reported success. It now keeps
  going until the stack has gone or the target is genuinely full.
- **Boats and carts are left alone.** A container's access check returns yes unconditionally
  for anything that is not a player-built piece, which is what a ship's hold is - so a
  longship moored by the base passed every test, and because the winner is whoever holds the
  most of a thing, a well-stocked hold could pull the base's stores out onto the water. Holds
  on ships and carts are now refused outright, for Sort as well as Sort All, and the log line
  says how many were in range and skipped.
- **Sorting no longer hides things in rows you cannot see.** With Cartur's UI - HUD installed
  the bag has rows the grid never draws, holding the equipment, shield and quick slots. The
  layout already stopped at the visible rows; the gather did not, so an item pulled in from a
  chest could land in one and look as though the sort had eaten it. Wearing a bronze sword
  also counted as "the bag stocks bronze swords", and pulled every one within range out of its
  chest - worn gear and hidden slots are no longer read as stock.

- **Sort no longer duplicates items.** Pulling a stack in from a nearby chest asked the
  target whether it could take the whole thing before the move, and that check ignores item
  quality - so a chest holding quality 1 copper reported room for a quality 2 stack of the
  same name. The move then topped up a quality 1 pile one unit at a time without decrementing
  the source, which was removed at full size. The two halves have to agree now, and room is
  counted the way the game's own stacking rule counts it, so quality and world level both
  matter.
- **A stack too big for the room left is now brought across in part**, rather than being
  skipped whole, and what it could not take is left behind as a valid stack instead of
  vanishing.
- **Sorting reaches into the chests around you.** Sort pulls matching items in from the
  chests within `GatherRange`, so a chest of ore and an ore chest next door settle as one,
  and a nearly empty bag and a full ore chest beside it are both dealt with on one press.
  A chest only collects the kinds of item it already holds; your bag fills with what it
  already has room for. Item drawers are never touched - kg's drawers are not containers and
  this cannot see them.
- **The bag sort reaches too**, centred on the player, with the same rules.
- **Sort All actually consolidates.** The first version tidied each chest in range and moved
  nothing between them, so two chests of rubies each kept their own - there was no reason for
  either to give any up. There is now: one winner per item, the chest holding the most of it
  (ties go to the nearer), and every other copy is pulled into it. A chest is only ever a
  winner for a kind of thing it already holds, so a chest of ores still cannot swallow a chest
  of wood.
- **What will not fit stays where it is.** A winner is filled to its own capacity and the
  remainder is left in the chest it came from, so nothing is lost and a consolidation that
  could not finish finishes on the next press rather than needing a different chest.
- **Sort All says how much it moved.** The log line used to report the number of chests tidied
  whether it moved eight items or eight hundred, so "nothing happened" and "it worked" printed
  the same thing. It now reports the stack count too.
- **The two chest buttons sit side by side.** Full Place Stacks height, half its width each,
  on one row - so the pair covers exactly the footprint of one vanilla button and the column
  below is untouched. `ShowChestSortAll` and `ChestSortOffset` control them; `ChestSortOffset`
  moves the pair as a unit.
- **The buttons were invisible in the first build of this.** They were laid out correctly and
  drew nothing, because the half-height was written as a negative `sizeDelta` on the assumption
  that Place Stacks has stretched anchors. It does not - the anchors are the same point - so
  the negative size simply flipped the rect. The build log already said `size (133.00, -20.00)`;
  it now also prints the anchors.
- **Multiplayer: the chests you pull from stay pulled from.** `Container.Save` writes
  straight to the ZDO with no owner check, so a write by a player who does not own the chest
  is local only and the owner's copy lands on the next sync with the items back inside. The
  ZDO is now claimed before the write, the way the game does when a player opens a chest.
  This is why 1.2.0 could hand back an item that a chest still had.
- **A chest somebody else has open is left alone.** The in-use flag has to be read off the
  ZDO: `IsInUse()` returns the local field, and the game only sets that on the client that
  owns the chest, so it was never true on the client this guard protects.
- **Sorting during a teleport no longer moves items.** The game guards Take All and Place
  Stacks the same way.
- **An oversized layout warns instead of writing past the end of the grid.** Another mod
  resizing a container used to put items at positions nothing draws.

## 1.2.1

- **No more losing an item when the bag is full.** The fallback that drops the item on the ground
  used the raw prefab template, whose drop reference is never populated, so it threw - and because
  the refund runs before the item is removed, the item stayed in your bag and on the cursor with
  the materials already paid. Clicking again repeated it.
- **An upgraded item no longer refunds less than a plain one.** The refund read the cost of the
  last upgrade step rather than the whole bill. A quality 2 item could hand back less than quality
  1 despite costing more to make. The steps are summed now.
- **Unidentified EpicLoot items pay the sacrifice table again.** They were taking the halved
  enchanting branch, because unidentified items are also magic items, which is what that test
  asked. Both the readme and the code comment promised otherwise.
## 1.2.0

- **The buttons can be turned off and moved.** Asked for on Nexus: the chest Sort button is
  put directly under Place Stacks, which is a column other mods build into as well, and there
  was no way to shift it or remove it. Six settings in a new `Buttons` section - `ShowTrashCan`,
  `ShowBagSort`, `ShowChestSort`, and an offset in UI pixels for each of the three.
- All six are read every time the inventory opens, so a change takes effect the next time you
  open it. No restart.
- An offset you set rather than a collision test on purpose: finding another mod's button means
  guessing at its rect and re-checking it whenever that mod moves, while a number typed once
  cannot be wrong.

## 1.1.0

- **The refund is half now, rounded up.** 1.0.0 returned a whole craft's materials,
  which made the can a free undo button. Half of the recipe comes back instead, and
  rounding up means a single-unit ingredient still pays one rather than nothing.
- **EpicLoot items pay a second time, on top of the recipe.** An enchanted item was
  charged materials at the enchanting table, so half of `GetEnchantCost` comes back.
  Anything else EpicLoot will sacrifice - a trophy, a boss drop, an unidentified item -
  never cost anything to make, so half of nothing is nothing; those pay
  `GetSacrificeProducts` unhalved instead, which is EpicLoot's own table and the number
  the player already sees in its enchanting UI. All of it by reflection: EpicLoot is a
  soft dependency with no build reference, every lookup is null when it is absent, and
  the whole path is skipped.

## 1.0.0

First release.

- A trash can in the player panel. Click it with an item on the cursor and the
  item's own recipe is paid back — whole crafts only, quality included,
  honouring the game's `m_recover` flag. No recipe means no refund, just gone.
- A Sort button under the can for the player's bag, and one under Place Stacks
  for the open chest. Groups by type, then name, then best quality first.
- Sorting also merges split stacks of the same item, by the game's own stacking
  rule: same name, same quality, same world level.
- The hotbar row is left where the player put it, and rows belonging to another
  mod's equipment or quick slots are left alone.
