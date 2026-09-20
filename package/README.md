# Cartur's Waste Management

*Free, and always will be — if it improved your game you can [tip me on Patreon](https://www.patreon.com/c/cartur).*

**More from Cartur:** [HD Blood](https://thunderstore.io/c/valheim/p/Cartur/Carturs_HD_Blood/) ·
[Map Pins](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Map_Pins/) ·
[Follow Command](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Follow_Command/) ·
[Compass and Clock](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Compass_and_Clock/) ·
[Flooring](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Flooring/) ·
[UI HUD](https://thunderstore.io/c/valheim/p/Cartur/Carturs_UI_HUD/) ·
[Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/)

A trash can and a sort button, in the inventory, where the mess is.

## The can

Pick an item up and click the can. It comes apart into what it was made of and
the parts land in your bag.

The refund is not a number I invented. It is the item's own recipe — the same
one the bench used to build it — and the same `m_recover` flag the game reads
when you upgrade or repair. One whole craft returns one whole craft's
materials; a leftover that does not make a full craft returns nothing, the way
the bench never sold you half a shield. Quality is paid for: a level 3 sword
cost more than a level 1, and gives back more.

Anything with no recipe — wood, stone, ore, a handful of raspberries — has
nothing to give back, so it is simply thrown away.

## The sort buttons

One under the can for your own bag, one under Place Stacks for whatever chest
is open.

Sorting groups by item type, then by name, then puts the best quality first.
**It also pours split stacks back together** — half a stack of wood here and
half there come out as one pile, by the game's own stacking rule (same item,
same quality, same world level).

Your hotbar row is never touched. Neither is anything living in an equipment
or quick-slot row added by another mod: the boundary comes from
[Cartur's UI - HUD](https://thunderstore.io/c/valheim/p/Cartur/Carturs_UI_HUD/)
when it is installed, and from the grid's own height when it is not.

## Worth knowing

The can destroys things. That is what it is for, and there is no undo — it
asks no questions, exactly like dropping an item into lava, only tidier.

No config file yet. Nothing in here needed a setting.

## Install

Needs [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).
Client-side; no server install needed, and it does not care whether the people
you play with have it.
