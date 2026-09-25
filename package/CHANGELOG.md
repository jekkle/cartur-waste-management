# Changelog

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
