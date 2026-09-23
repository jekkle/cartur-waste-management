# Changelog

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
