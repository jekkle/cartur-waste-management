# Changelog

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
