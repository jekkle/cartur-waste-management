# Cartur's Waste Management

A trash can and a sort button, in the inventory, where the mess is.

![Cartur's Waste Management](https://raw.githubusercontent.com/jekkle/cartur-waste-management/master/media/nexus-header.png)

![The can and the sort buttons in the inventory](https://raw.githubusercontent.com/jekkle/cartur-waste-management/master/media/nexus-gallery.png)

## The can

Pick an item up and click the can. It comes apart into what it was made of and the
parts land in your bag.

The refund is the item's own recipe — the same one the bench used to build it —
and you get **half of it back, rounded up**. Scrapping should cost you something,
and rounding up means a single unit of an ingredient still returns one rather than
nothing. A leftover that doesn't make a full craft returns nothing.

**Every upgrade you paid for is counted**, not just the last one: a level 3 sword
was charged for the craft and then again for each step up, so the refund adds all
of that together and a level 3 always gives back more than a level 1.

Anything with no recipe — wood, stone, ore, a handful of raspberries — has nothing
to give back, so it is simply thrown away.

**EpicLoot is paid too, when it's installed.** An enchanted item was charged
materials at the enchanting table, so half of that bill comes back on top of the
recipe. Anything else EpicLoot would sacrifice — a trophy, a boss drop, an
unidentified item — never cost anything to make, so those pay EpicLoot's own
sacrifice table instead, unhalved: the same numbers its enchanting UI already
shows you. Reached by reflection, so nothing happens when EpicLoot is absent.

**The can destroys things, and there is no undo.** That is what it is for.

## The sort buttons

One under the can for your own bag. Two under Place Stacks for the chests: **Sort**
for the open one, **Sort All** for every chest in range. The pair takes up the same
space the single button used to, so it doesn't grow into whatever is below it.

Sorting groups by item type, then by name, then puts the best quality first. It
also **pours split stacks back together** — half a stack of wood here and half
there come out as one pile.

Your hotbar row is never touched, and neither is anything in an equipment or
quick-slot row added by another mod.

## Pulling from nearby chests

Sort doesn't only tidy what is already in the inventory. It also **reaches out to
the chests around you** and brings in what matches, so a chest of ore and an ore
chest next door end up as one chest of ore, and a nearly empty bag and a full ore
chest beside it are both dealt with on one press. How far it looks is
`GatherRange` in the settings, and 0 turns the reaching off.

A chest only collects the kinds of item it already holds — a chest of ores stays a
chest of ores, and gains the ore from around it, rather than becoming a bin for
everything in range. Your bag is the other way round: it fills with what it already
has room for.

Chests you have no permission for, chests inside someone else's ward, chests other
players have open, carts being pulled and containers carried by a player are all left
alone.

**Sort All** does not empty a whole base into the chest you are standing at. Each
chest pulls for itself, so it settles the base rather than collapsing it into one
pile.

## Settings

`BepInEx/config/com.jekkle.valheim.carturwastemanagement.cfg`.

| Setting | Default | What it does |
| --- | --- | --- |
| `ShowTrashCan` | true | Show the can in the player panel. |
| `ShowBagSort` | true | Show the Sort button for your own bag. |
| `ShowChestSort` | true | Show Sort for the open chest. |
| `ShowChestSortAll` | true | Show Sort All under it. |
| `GatherRange` | 20 | How far, in metres, sorting pulls matching items in from nearby chests, and how far Sort All reaches. 0 turns both off. |
| `TrashCanOffset` | 0, 0 | Nudge the can, in UI pixels. X right, Y up. |
| `BagSortOffset` | 0, 0 | Nudge the bag Sort button. |
| `ChestSortOffset` | 0, 0 | Nudge both chest buttons, as a pair. |

All of them are read each time the panel opens, so a change takes effect the next time you
open your inventory. No restart.

The chest buttons sit directly under Place Stacks, which is a column other mods
build into as well. `ChestSortOffset` moves the pair out of the way, and
`ShowChestSort` / `ShowChestSortAll` remove them. That is an offset you set rather
than a collision test on purpose: finding another mod's button means guessing at its
rect and re-checking whenever it moves, and a number typed once cannot be wrong.

## Install

Needs [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).

Client-side. No server install needed, and it doesn't care whether the people you
play with have it.

---

*Free, and always will be. If it improved your game you can [tip me on Patreon](https://www.patreon.com/c/cartur).*

**[Discord](https://discord.gg/nd5RqpwNkz)** — bug reports, install help, and mod requests.
Bug reports get their own thread so nothing is lost in a chat scroll, and requests are voted on.

**More from Cartur:**
[HD Blood](https://thunderstore.io/c/valheim/p/Cartur/Carturs_HD_Blood/) ·
[Map Pins](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Map_Pins/) ·
[Compass and Clock](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Compass_and_Clock/) ·
[Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/) ·
[Follow Command](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Follow_Command/) ·
[Flooring](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Flooring/) ·
[UI HUD](https://thunderstore.io/c/valheim/p/Cartur/Carturs_UI_HUD/) ·
[Feeding Trough](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Feeding_Trough/)
