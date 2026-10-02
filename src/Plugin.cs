using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturWasteManagement
{
    // Drop an item on the can and half of what it cost comes back. Valheim already knows the
    // bill: ObjectDB.GetRecipe(item) is the same recipe the crafting bench used, and
    // Requirement.GetAmount(q) is what it charged for the single step up to quality q, so the
    // whole bill is those steps added up. Anything with no recipe - wood, stone, ore, a berry -
    // has nothing to give back, so it is simply removed.
    //
    // Sort sits under the can and orders everything except the hotbar row.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jekkle.valheim.carturwastemanagement";
        public const string PluginName = "Cartur's Waste Management";
        public const string PluginVersion = "1.2.2";

        internal static Sprite CanSprite;
        internal static BepInEx.Logging.ManualLogSource Log;

        internal static BepInEx.Configuration.ConfigEntry<bool> ShowTrashCan;
        internal static BepInEx.Configuration.ConfigEntry<bool> ShowBagSort;
        internal static BepInEx.Configuration.ConfigEntry<bool> ShowChestSort;
        internal static BepInEx.Configuration.ConfigEntry<bool> ShowChestSortAll;
        internal static BepInEx.Configuration.ConfigEntry<float> GatherRange;
        internal static BepInEx.Configuration.ConfigEntry<Vector2> TrashCanOffset;
        internal static BepInEx.Configuration.ConfigEntry<Vector2> BagSortOffset;
        internal static BepInEx.Configuration.ConfigEntry<Vector2> ChestSortOffset;

        private void Awake()
        {
            Log = Logger;
            CanSprite = LoadSprite("trashcan_128.png");

            // Asked for: the chest Sort button is put directly under Place Stacks, which is a
            // column other mods also build into, and there was no way to move it or turn it off.
            // Deliberately an offset you set rather than a collision test: finding another mod's
            // button means guessing at its RectTransform and re-checking whenever it moves, and
            // a number typed once cannot be wrong.
            //
            // All six are read every time the panel opens, so changing one takes effect the next
            // time you open your inventory - no restart.
            ShowTrashCan = Config.Bind("Buttons", "ShowTrashCan", true,
                "Show the trash can in the player panel.");
            ShowBagSort = Config.Bind("Buttons", "ShowBagSort", true,
                "Show the Sort button under the trash can, for your own bag.");
            ShowChestSort = Config.Bind("Buttons", "ShowChestSort", true,
                "Show the Sort button under Place Stacks, for the open chest.");
            ShowChestSortAll = Config.Bind("Buttons", "ShowChestSortAll", true,
                "Show the Sort All button under Sort, for every chest in range.");
            // The same 20 metres Craft From Containers calls nearby, so the two agree about which
            // chests are part of a base. Read at click time, so a change takes effect on the next
            // sort with no restart.
            GatherRange = Config.Bind("Buttons", "GatherRange", 20f,
                "How far, in metres, Sort pulls matching items in from nearby chests, and how far Sort All reaches. 0 disables both.");
            TrashCanOffset = Config.Bind("Buttons", "TrashCanOffset", Vector2.zero,
                "Nudge the trash can from where it normally sits, in UI pixels. X is right, Y is up.");
            BagSortOffset = Config.Bind("Buttons", "BagSortOffset", Vector2.zero,
                "Nudge the bag Sort button, in UI pixels. X is right, Y is up.");
            ChestSortOffset = Config.Bind("Buttons", "ChestSortOffset", Vector2.zero,
                "Nudge the chest Sort button, in UI pixels. X is right, Y is up. Use this when another mod already owns the space under Place Stacks.");

            try
            {
                Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
                Logger.LogInfo($"{PluginName} {PluginVersion} loaded - trash can refunds half the item's own recipe, sort button added.");
            }
            catch (Exception e)
            {
                Logger.LogWarning($"Failed to patch InventoryGui - no can and no sort button this run. {e}");
            }
        }

        // ImageConversion.LoadImage has to go through reflection: a direct call does not compile
        // against Valheim's netstandard facade. Same as the other Cartur mods.
        private Sprite LoadSprite(string fileName)
        {
            string path = Path.Combine(
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "assets"),
                fileName);

            if (!File.Exists(path))
            {
                Logger.LogWarning("asset missing: " + path);
                return null;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            MethodInfo loadImage = null;
            Type imageConversion = AccessTools.TypeByName("UnityEngine.ImageConversion");
            foreach (MethodInfo m in imageConversion?.GetMethods(BindingFlags.Public | BindingFlags.Static) ?? new MethodInfo[0])
            {
                ParameterInfo[] ps = m.GetParameters();
                if (m.Name == "LoadImage" && ps.Length == 3 && ps[1].ParameterType == typeof(byte[]))
                {
                    loadImage = m;
                    break;
                }
            }

            if (loadImage == null || !(loadImage.Invoke(null, new object[] { tex, File.ReadAllBytes(path), false }) is bool ok) || !ok)
            {
                Logger.LogWarning("could not decode " + path);
                return null;
            }

            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    public static class Patch_InventoryGui_Show
    {
        // The column hangs off the weight readout's own parent, so it inherits whatever scale
        // and anchoring the player panel is using - including under a UI overhaul that moved it.
        private const float CanSize = 54f;
        private const float ButtonWidth = 54f;
        private const float ButtonHeight = 30f;
        private const float Gap = 6f;

        private static GameObject s_can;
        private static GameObject s_sort;
        private static GameObject s_containerSort;
        private static GameObject s_containerSortAll;

        /// Where each button was put before any offset was applied. Kept so the offset is always
        /// measured from the same place: adding it to the button's current position instead would
        /// move it again every time the panel opened.
        private static Vector2 s_canHome, s_sortHome, s_containerSortHome, s_containerSortAllHome;

        private static void Postfix(InventoryGui __instance)
        {
            if (__instance == null)
                return;

            BuildContainerSort(__instance);
            BuildPlayerButtons(__instance);
            ApplySettings();
        }

        private static void BuildPlayerButtons(InventoryGui gui)
        {
            if (s_can != null || gui.m_weight == null)
                return;

            RectTransform weight = gui.m_weight.rectTransform;
            RectTransform parent = weight.parent as RectTransform;
            if (parent == null)
                return;

            // Stacked upward from the weight box: weight, then sort, then the can on top.
            float sortY = weight.anchoredPosition.y + ButtonHeight + Gap;
            float canY = sortY + ButtonHeight * 0.5f + CanSize * 0.5f + Gap;

            s_canHome = new Vector2(weight.anchoredPosition.x, canY);
            s_sortHome = new Vector2(weight.anchoredPosition.x, sortY);

            s_can = BuildCan(parent, s_canHome);
            s_sort = BuildSort(gui, parent, s_sortHome);
        }

        /// Shows, hides and positions all three buttons from the config, every time the panel
        /// opens. Doing it here rather than at build time is what makes the settings live: the
        /// buttons are built once and reused, so a value read only at build time would need a
        /// restart to take effect.
        private static void ApplySettings()
        {
            Place(s_can, s_canHome, Plugin.ShowTrashCan.Value, Plugin.TrashCanOffset.Value);
            Place(s_sort, s_sortHome, Plugin.ShowBagSort.Value, Plugin.BagSortOffset.Value);
            Place(s_containerSort, s_containerSortHome, Plugin.ShowChestSort.Value, Plugin.ChestSortOffset.Value);
            Place(s_containerSortAll, s_containerSortAllHome, Plugin.ShowChestSortAll.Value, Plugin.ChestSortOffset.Value);
        }

        private static void Place(GameObject go, Vector2 home, bool show, Vector2 offset)
        {
            if (go == null)
                return;
            go.SetActive(show);
            ((RectTransform)go.transform).anchoredPosition = home + offset;
        }

        /// <summary>
        /// Two buttons for the chest panel: Sort for the one that is open, and Sort All for every
        /// chest in range. Cloned from Take All so they carry the game's own skin and click sound,
        /// and built once and reused: the container panel is the same object for every chest, only
        /// its inventory changes.
        ///
        /// The pair sits directly under Place Stacks (InventoryGui.m_stackAllButton) - Cartur's
        /// call - and takes that button's anchors and pivot so it reads as part of the same stack
        /// however the panel has been moved or scaled.
        ///
        /// The two sit side by side on one row: full Place Stacks height, half its width each, so
        /// the pair covers exactly the footprint of one vanilla button and the column below is
        /// untouched. Asked for - two stacked half-height buttons were hard to read.
        ///
        /// The x offsets are derived from the pivot rather than assumed to be centred. A rect of
        /// width w at anchoredPosition a has its left edge at a - pivot.x * w, so a half-width
        /// button lines up with Place Stacks' left edge at -pivot.x * w/2, and the second one
        /// starts half a button further right. At the usual centre pivot that is the symmetric
        /// -/+ quarter width; at a left pivot it is 0 and +half. Neither is hardcoded.
        /// </summary>
        private static void BuildContainerSort(InventoryGui gui)
        {
            if (s_containerSort != null || gui.m_takeAllButton == null || gui.m_container == null)
                return;

            RectTransform stackAll = gui.m_stackAllButton != null
                ? gui.m_stackAllButton.GetComponent<RectTransform>() : null;
            if (stackAll == null)
                return;

            float height = stackAll.rect.height;
            float halfWidth = stackAll.rect.width * 0.5f;
            float shift = stackAll.pivot.x * halfWidth;

            // One full button height below Place Stacks, and both on that same row.
            Vector2 row = stackAll.anchoredPosition - new Vector2(0f, height + Gap);
            var size = new Vector2(halfWidth, height);

            s_containerSortHome = row + new Vector2(-shift, 0f);
            s_containerSortAllHome = row + new Vector2(halfWidth - shift, 0f);

            s_containerSort = MakeContainerButton(gui, stackAll, "CarturSortContainerButton", "Sort",
                WasteBin.SortOpenContainer, s_containerSortHome, size);

            s_containerSortAll = MakeContainerButton(gui, stackAll, "CarturSortAllContainerButton", "Sort All",
                WasteBin.SortAllContainers, s_containerSortAllHome, size);

            // The three vanilla buttons' own positions, so where the pair landed can be read
            // rather than eyeballed - if it sits on top of one of them, this line says so.
            RectTransform takeAll = gui.m_takeAllButton.GetComponent<RectTransform>();
            RectTransform drop = gui.m_dropButton != null ? gui.m_dropButton.GetComponent<RectTransform>() : null;
            Plugin.Log.LogInfo("container sort buttons at " + s_containerSortHome + " and " + s_containerSortAllHome
                + " size " + ((RectTransform)s_containerSort.transform).rect.size
                + "  (place stacks " + stackAll.anchoredPosition + " size " + stackAll.rect.size
                + " anchors " + stackAll.anchorMin + "-" + stackAll.anchorMax
                + " pivot " + stackAll.pivot
                + ", take all " + (takeAll != null ? takeAll.anchoredPosition.ToString() : "?")
                + ", drop " + (drop != null ? drop.anchoredPosition.ToString() : "none") + ")");
        }

        /// <summary>
        /// One half-height clone of Take All. The height goes on as a plain positive sizeDelta:
        /// Place Stacks' anchors are the same point, not stretched, so sizeDelta is the button's
        /// size outright. Negating it to mean "half a stretched button" only flips the rect and
        /// draws nothing - which is what 1.2.1 did, and the log line below is what said so.
        /// </summary>
        private static GameObject MakeContainerButton(InventoryGui gui, RectTransform stackAll,
            string name, string caption, UnityEngine.Events.UnityAction onClick, Vector2 home, Vector2 sizeDelta)
        {
            GameObject go = UnityEngine.Object.Instantiate(gui.m_takeAllButton.gameObject, stackAll.parent);
            go.name = name;
            go.SetActive(true);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = stackAll.anchorMin;
            rect.anchorMax = stackAll.anchorMax;
            rect.pivot = stackAll.pivot;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = home;

            foreach (TMP_Text label in go.GetComponentsInChildren<TMP_Text>(true))
                label.text = caption;

            var button = go.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);

            return go;
        }

        private static GameObject BuildCan(RectTransform parent, Vector2 pos)
        {
            var go = new GameObject("CarturTrashCan", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(CanSize, CanSize);
            rect.anchoredPosition = pos;

            var image = go.GetComponent<Image>();
            image.sprite = Plugin.CanSprite;
            image.preserveAspect = true;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(WasteBin.DropCarriedItem);
            return go;
        }

        // Cloned from the container's own Take All button rather than built from scratch, so it
        // carries the game's skin, hover tint and click sound with no art of ours.
        private static GameObject BuildSort(InventoryGui gui, RectTransform parent, Vector2 pos)
        {
            if (gui.m_takeAllButton == null)
                return null;

            GameObject go = UnityEngine.Object.Instantiate(gui.m_takeAllButton.gameObject, parent);
            go.name = "CarturSortButton";
            go.SetActive(true);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            rect.anchoredPosition = pos;

            foreach (TMP_Text label in go.GetComponentsInChildren<TMP_Text>(true))
                label.text = "Sort";

            var button = go.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(WasteBin.SortPlayerInventory);
            return go;
        }
    }

    internal static class WasteBin
    {
        // What the cursor is carrying is private on InventoryGui, and so is the call that puts
        // it down again. Looked up once here rather than per click.
        private static readonly FieldInfo DragItem = AccessTools.Field(typeof(InventoryGui), "m_dragItem");
        private static readonly FieldInfo DragInventory = AccessTools.Field(typeof(InventoryGui), "m_dragInventory");
        private static readonly FieldInfo DragAmount = AccessTools.Field(typeof(InventoryGui), "m_dragAmount");
        private static readonly MethodInfo SetupDragItem = AccessTools.Method(typeof(InventoryGui), "SetupDragItem");
        // Inventory.Changed takes (bool success, bool cheatedStateChanged) in this build. It was
        // being invoked with null - no arguments - which throws TargetParameterCountException.
        // Unity swallows exceptions thrown inside a UnityEvent callback, so the click half
        // worked: the items moved, the inventory was never told, and the UI did not refresh.
        private static readonly MethodInfo InventoryChanged =
            AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });

        private static void NotifyChanged(Inventory inv)
        {
            if (inv != null)
                InventoryChanged?.Invoke(inv, new object[] { false, false });
        }

        internal static void DropCarriedItem()
        {
            InventoryGui gui = InventoryGui.instance;
            Player player = Player.m_localPlayer;
            // Same guard the game puts on its own Take All and Place Stacks: the cursor and the
            // inventory it points at are both live, and a teleport changes what they are.
            if (gui == null || player == null || player.IsTeleporting())
                return;

            var item = DragItem?.GetValue(gui) as ItemDrop.ItemData;
            var from = DragInventory?.GetValue(gui) as Inventory;
            if (item == null || from == null)
                return;

            int carried = DragAmount?.GetValue(gui) is int n ? n : item.m_stack;
            int amount = Mathf.Clamp(carried, 1, item.m_stack);

            Recycle(player, from, item, amount);

            SetupDragItem?.Invoke(gui, new object[] { null, null, 0 });
            NotifyChanged(from);
        }

        // The recipe is the receipt. One craft of recipe.m_amount items cost m_resources, so
        // amount/m_amount whole crafts are what was paid; a leftover that does not make a whole
        // craft returns nothing, the same way the bench never sold a half one. Half of that bill
        // comes back - scrapping is meant to cost something - and the half rounds up, so an
        // ingredient that was genuinely used never pays out nothing.
        private static void Recycle(Player player, Inventory from, ItemDrop.ItemData item, int amount)
        {
            Recipe recipe = ObjectDB.instance != null ? ObjectDB.instance.GetRecipe(item) : null;
            int perCraft = recipe != null ? Mathf.Max(1, recipe.m_amount) : 0;
            int crafts = recipe != null ? amount / perCraft : 0;
            int quality = item.m_quality;

            // Take the item out of the bag BEFORE paying for it. Everything below hands control to
            // code we do not own - ItemDrop.DropItem inside Give, and EpicLoot by reflection - and
            // when the payout came first a throw in any of it skipped the removal entirely: the
            // item stayed in the bag, stayed on the cursor, and the materials already handed over
            // were kept. Clicking again repeated it, which is an item duplication bug. Removing
            // first makes the worst case a lost refund instead of a free item.
            if (item.m_equipped)
                player.UnequipItem(item, false);

            if (amount >= item.m_stack)
                from.RemoveItem(item);
            else
                from.RemoveItem(item, amount);

            if (crafts > 0)
            {
                foreach (Piece.Requirement req in recipe.m_resources)
                {
                    // m_recover is the game's own flag for "this comes back out again" - it is
                    // what upgrades and repairs read. Honour it rather than inventing a rule.
                    if (req == null || req.m_resItem == null || !req.m_recover)
                        continue;

                    int give = Half(CostUpTo(req, quality) * crafts);
                    if (give <= 0)
                        continue;

                    Give(player, req.m_resItem.gameObject, give);
                    Plugin.Log.LogDebug($"returned {give} {req.m_resItem.gameObject.name}");
                }
            }

            GiveEpicLootRefund(player, item, amount);
        }

        // What one craft of this item actually cost, all the way up to the quality it is now.
        //
        // Requirement.GetAmount(q) is the price of a SINGLE step, not the running total:
        // InventoryGui.DoCrafting sets its target quality to 1 for a craft and to
        // m_craftUpgradeItem.m_quality + 1 for an upgrade, and hands that one number to
        // Player.ConsumeResources, which charges GetAmount(target) and nothing else. A quality 3
        // sword was therefore billed GetAmount(1) + GetAmount(2) + GetAmount(3) over three
        // separate presses. Refunding only GetAmount(3) paid back less for an upgraded item than
        // for a plain one, because GetAmount(1) is the flat m_amount and the upgrade steps are the
        // usually-smaller m_amountPerLevel.
        //
        // Quality 0 is not a thing the game makes - ItemData starts at 1 - but a modded item at 0
        // must still be worth its base cost rather than nothing, so the loop runs at least once.
        private static int CostUpTo(Piece.Requirement req, int quality)
        {
            int total = 0;
            for (int q = 1; q <= Mathf.Max(1, quality); q++)
                total += req.GetAmount(q);
            return total;
        }

        // Half, rounded up: 5 becomes 3, 1 stays 1. Nothing that was paid for comes back as
        // nothing, which rounding down would do to every single-unit ingredient.
        private static int Half(int n) => (n + 1) / 2;

        // Into the bag, or at the player's feet when it will not fit. Both the recipe refund
        // and the enchant refund hand out items this way.
        //
        // ItemDrop.DropItem's very first instruction is Instantiate(itemData.m_dropPrefab, ...),
        // and m_dropPrefab is written in exactly one place: ItemDrop.Awake. The prefab handed in
        // here comes out of the ObjectDB, which holds prefab assets that are never instantiated,
        // so their Awake has never run and their m_itemData.m_dropPrefab is null. Passing that
        // template straight to DropItem instantiated null and threw. Drop a clone with the field
        // filled in instead - the same GameObject Awake would have looked up - and the clone also
        // keeps the template's own m_itemData out of the world object's hands.
        private static void Give(Player player, GameObject prefab, int amount)
        {
            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            if (amount <= 0 || drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
                return;

            Inventory inv = player.GetInventory();
            string name = drop.m_itemData.m_shared.m_name;
            int max = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);

            // Handed out one stack at a time, because AddItem(GameObject, int) is not a bulk
            // call. Read off the DLL, its second instruction block is
            //   clone.m_stack = Mathf.Min(amount, m_shared.m_maxStackSize)
            // and everything above that is simply never created. Scrapping 20 bread owes 100
            // barley flour; one call handed back 50 and the other 50 did not exist.
            int before = inv.CountItems(name, -1, false);
            for (int left = amount; left > 0; left -= max)
            {
                if (!inv.AddItem(prefab, Mathf.Min(left, max)))
                    break;
            }

            // What the bag took is counted, not asked for. AddItem(ItemData) walks the stackable
            // path one unit at a time, topping up free stacks as it goes, and when it finally
            // runs out of empty slots it returns false having already placed some of them.
            // Trusting that false and dropping the full amount at the player's feet handed back
            // both copies, which is an item duplication bug.
            int rest = amount - (inv.CountItems(name, -1, false) - before);
            if (rest <= 0)
                return;

            ItemDrop.ItemData data = drop.m_itemData.Clone();
            data.m_dropPrefab = prefab;
            ItemDrop.DropItem(data, rest, player.transform.position + player.transform.forward, Quaternion.identity);
        }

        // EpicLoot items pay a second time, on top of the recipe, and which bill they pay
        // depends on whether anyone ever enchanted them.
        //
        // An enchanted item cost materials at the enchanting table, so half of that comes back:
        // EnchantCostsHelper.GetEnchantCost(item, rarity) is the list the table charged.
        //
        // Everything else EpicLoot will sacrifice never cost anything to make - a trophy, a boss
        // drop, an unidentified item - so half of nothing is nothing, and instead they pay
        // EpicLoot's own GetSacrificeProducts, unhalved. That is the table the player already
        // knows from the enchanting UI, and it is EpicLoot's number, not ours.
        //
        // All of it by reflection: EpicLoot is a soft dependency with no build reference. Every
        // lookup below is null when it is not installed, and then this does nothing at all.
        private static readonly Type RarityType = AccessTools.TypeByName("EpicLoot.ItemRarity");
        private static readonly MethodInfo EnchantCostFor = RarityType == null ? null :
            AccessTools.Method(AccessTools.TypeByName("EpicLoot.Crafting.EnchantCostsHelper"),
                               "GetEnchantCost", new[] { typeof(ItemDrop.ItemData), RarityType });
        private static readonly MethodInfo SacrificeProductsFor =
            AccessTools.Method(AccessTools.TypeByName("EpicLoot.Crafting.EnchantCostsHelper"),
                               "GetSacrificeProducts", new[] { typeof(ItemDrop.ItemData) });
        private static readonly MethodInfo IsMagicItem =
            AccessTools.Method(AccessTools.TypeByName("EpicLoot.API"), "IsMagicItem", new[] { typeof(ItemDrop.ItemData) });
        private static readonly MethodInfo IsUnidentifiedItem =
            AccessTools.Method(AccessTools.TypeByName("EpicLoot.API"), "IsUnidentified", new[] { typeof(ItemDrop.ItemData) });
        private static readonly MethodInfo TryGetRarity =
            AccessTools.Method(AccessTools.TypeByName("EpicLoot.API"), "TryGetRarity",
                               new[] { typeof(ItemDrop.ItemData), typeof(int).MakeByRefType() });
        private static readonly FieldInfo CostItem =
            AccessTools.Field(AccessTools.TypeByName("EpicLoot.Crafting.ItemAmountConfig"), "Item");
        private static readonly FieldInfo CostAmount =
            AccessTools.Field(AccessTools.TypeByName("EpicLoot.Crafting.ItemAmountConfig"), "Amount");

        private static void GiveEpicLootRefund(Player player, ItemDrop.ItemData item, int amount)
        {
            if (EnchantCostFor == null || SacrificeProductsFor == null || IsMagicItem == null
                || IsUnidentifiedItem == null || TryGetRarity == null || CostItem == null || CostAmount == null)
                return;

            IEnumerable bill;
            bool halve;
            try
            {
                // IsMagicItem is true for anything carrying a MagicItemComponent with a MagicItem
                // on it, and IsUnidentified reads the IsUnidentified flag off that same object -
                // so every unidentified drop is also "magic", and was taking the halved
                // enchant-cost branch below. Nobody ever paid an enchanting table for one, so it
                // belongs with the trophies in the unhalved sacrifice branch, which is what the
                // comment above and the README have always said it does.
                bool magic = IsMagicItem.Invoke(null, new object[] { item }) is bool m && m;
                bool unidentified = IsUnidentifiedItem.Invoke(null, new object[] { item }) is bool u && u;
                halve = magic && !unidentified;
                if (halve)
                {
                    var args = new object[] { item, 0 };
                    if (!(TryGetRarity.Invoke(null, args) is bool found) || !found)
                        return;

                    object rarity = Enum.ToObject(RarityType, args[1]);
                    bill = EnchantCostFor.Invoke(null, new[] { item, rarity }) as IEnumerable;
                }
                else
                {
                    // Null here is EpicLoot saying it will not sacrifice this item at all.
                    bill = SacrificeProductsFor.Invoke(null, new object[] { item }) as IEnumerable;
                }
            }
            catch (Exception e)
            {
                // Somebody else's mod, called across a version boundary we do not control. A
                // throw here must not eat the item the player just dropped on the can.
                Plugin.Log.LogWarning("EpicLoot cost lookup threw, no magic materials given. " + e.Message);
                return;
            }

            if (bill == null)
                return;

            foreach (object entry in bill)
            {
                string prefabName = CostItem.GetValue(entry) as string;
                int give = (CostAmount.GetValue(entry) is int n ? n : 0) * amount;
                if (halve)
                    give = Half(give);
                if (give <= 0 || string.IsNullOrEmpty(prefabName))
                    continue;

                GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
                if (prefab == null)
                {
                    Plugin.Log.LogWarning("EpicLoot material is not in the ObjectDB: " + prefabName);
                    continue;
                }

                Give(player, prefab, give);
                Plugin.Log.LogDebug($"refunded {give} {prefabName}");
            }
        }

        /// <summary>
        /// The rows the player can actually see.
        ///
        /// Cartur's UI - HUD grows the player's inventory by three rows that the grid never
        /// draws, and keeps the equipment slots, the shield slot, the quiver and the three
        /// quick slot foods down there. A sort that walked the whole grid therefore emptied
        /// the equipment panel and the food diamonds into the bag: the worn kit crawled back
        /// on its own, the quick slot food did not.
        ///
        /// Its Slots.VisibleRows is where that boundary is kept. The type is internal, so it
        /// is read by reflection, and null when that mod is not installed - and then the
        /// inventory's own height is the right answer, because nothing is hidden in it.
        /// </summary>
        private static readonly MethodInfo VisibleRowsGetter =
            AccessTools.PropertyGetter(AccessTools.TypeByName("CarturUIHud.Slots"), "VisibleRows");

        private static bool s_boundaryLogged;

        private static int VisibleRows(Inventory inv)
        {
            if (VisibleRowsGetter != null && VisibleRowsGetter.Invoke(null, null) is int rows && rows > 0)
                return rows;
            return inv.GetHeight();
        }

        /// <summary>
        /// The rows anything may be put into, and the rows worth reading out of.
        ///
        /// The layout already stops at the visible rows; the gather did not, and the two have to
        /// agree. A chest has nothing hidden, so its whole grid counts. The player's does: an
        /// item dropped into a hidden row is sitting on the equipment panel, is never drawn in
        /// the grid, is skipped by the layout - which only collects y &lt; visible - and looks to
        /// the player as though the sort ate it.
        /// </summary>
        private static int PlaceableRows(Inventory inv)
        {
            Player player = Player.m_localPlayer;
            return player != null && inv == player.GetInventory() ? VisibleRows(inv) : inv.GetHeight();
        }

        // Row 0 is the hotbar and stays where the player put it; the visible rows below it are
        // grouped by item type, then name, then best quality first. Anything in a hidden row is
        // in a slot, not in the bag, and is left alone.
        internal static void SortPlayerInventory()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting())
                return;

            Inventory inv = player.GetInventory();
            int visible = VisibleRows(inv);

            // Said once. If the lookup above ever stops resolving, the sort quietly goes back
            // to emptying the equipment panel into the bag, and nothing else would say why.
            if (!s_boundaryLogged)
            {
                s_boundaryLogged = true;
                Plugin.Log.LogInfo("sort covers rows 1-" + (visible - 1) + " of " + visible
                    + " visible ("
                    + (VisibleRowsGetter != null ? "row count from CarturUIHud.Slots"
                                                 : "no slot mod found, using the grid height")
                    + ")");
            }

            // Top up from the chests around the player first, so a sort with a near-empty grid and
            // a full ore chest next door both happen on one press. The bag is not a container, so
            // there is no anchor chest and nothing to skip - the player's own position is the
            // centre of the walk. Anything it brings lands in the first free slot the game picks,
            // which can be a hotbar row; the hotbar is left alone by the layout below either way.
            GatherLikeItems(inv, player.transform.position, null);

            // Collected after the gather, so what arrived is laid out with the rest.
            var loose = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item.m_gridPos.y > 0 && item.m_gridPos.y < visible)
                    loose.Add(item);
            }

            MergeStacks(inv, loose);
            loose.Sort(Compare);

            // The sort can also top the bag up from the chests around it, so the count can exceed
            // what the visible rows hold. Only the first rows get placed; the rest stay wherever
            // they are rather than being written to a row the player cannot see, which is the
            // equipment area and would look like the items had gone.
            int width = inv.GetWidth();
            int room = width * (visible - 1);
            for (int i = 0; i < loose.Count && i < room; i++)
                loose[i].m_gridPos = new Vector2i(i % width, 1 + i / width);

            if (loose.Count > room)
                Plugin.Log.LogWarning($"sort placed {room} of {loose.Count} bag stacks; the rest were left where they were.");

            NotifyChanged(inv);
        }

        // Same order as the player's, but every row counts: a chest has no hotbar to leave alone.
        // The inventory comes from the open Container at click time, not from anything cached -
        // the panel is reused for every chest you walk up to.
        private static readonly FieldInfo CurrentContainer = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");

        internal static void SortOpenContainer()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || !gui.IsContainerOpen())
                return;

            // The game guards its own Take All and Place Stacks the same way: a panel change or a
            // container swap landing mid-click is what the copy is for. Read from OnTakeAll.
            Player player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting())
                return;

            var container = CurrentContainer?.GetValue(gui) as Container;
            if (container == null)
                return;

            SortOne(container, true);
        }

        /// <summary>
        /// Sort All: tidy every chest in range, and consolidate the ones that hold the same thing.
        ///
        /// Tidying alone is what 1.2.1 did, and it moved nothing between chests - two chests of
        /// rubies each kept their own, because neither was given a reason to give any up. This
        /// pass picks one winner per item and pulls every other copy into it, so a base of mixed
        /// chests ends up with one of each thing.
        ///
        /// The winner for an item is the chest that already holds the most of it, ties going to
        /// the first in range. Most-of-it is chosen over first-in-range because a chest with 38
        /// rubies is the obvious home for 2 more, and it keeps a chest that happens to be listed
        /// first from absorbing everything around it.
        ///
        /// What does not fit stays in the chest it was in. A winner is only ever filled to its own
        /// capacity, so a consolidation that cannot finish is resumable rather than lossy - press
        /// again once the winner has room, and the rest follows.
        /// </summary>
        internal static void SortAllContainers()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || !gui.IsContainerOpen())
                return;

            Player player = Player.m_localPlayer;
            if (player == null || player.IsTeleporting())
                return;

            var open = CurrentContainer?.GetValue(gui) as Container;
            if (open == null)
                return;

            float range = Plugin.GatherRange.Value;
            if (range <= 0f)
            {
                SortOne(open, false);
                return;
            }

            long playerId = Game.instance.GetPlayerProfile().GetPlayerID();
            Vector3 at = open.transform.position;
            float rangeSq = range * range;

            // Every chest we are allowed to touch, gathered once. FindObjectsByType has no
            // defined order, and the winner below is decided by counts and ties, so a stable
            // order would only make the same click give the same result twice - worth having,
            // and it costs one sort.
            //
            // The open chest is added outright rather than tested, because Reachable would
            // refuse it. InventoryGui.UpdateContainer calls Container.SetInUse(true) every frame
            // the panel is up, which mirrors ZDOVars.s_inUse = 1 onto the ZDO, and Reachable
            // reads that flag to skip chests somebody else is in. So the one chest the player
            // was actually looking at was the one Sort All left alone - not tidied, never a
            // winner, never a donor. It is in range by definition and the player has access to
            // it by definition, which is the whole of what the other tests ask.
            var inRange = new List<Container>(32) { open };
            int vessels = 0;
            foreach (Container other in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                if (other == null || other == open)
                    continue;

                // Counted separately from the rest so the log says how many boats and carts were
                // in range and left alone. Reachable refuses them again on its own account; this
                // is only here to make the refusal readable.
                if ((other.transform.position - at).sqrMagnitude <= rangeSq && IsVessel(other))
                {
                    vessels++;
                    continue;
                }

                if (Reachable(other, at, rangeSq, playerId))
                    inRange.Add(other);
            }

            inRange.Sort((a, b) => Vector3.SqrMagnitude(a.transform.position - at)
                .CompareTo(Vector3.SqrMagnitude(b.transform.position - at)));

            int moved = Consolidate(inRange);

            // Tidied after the move, so what arrived is laid out with the rest - the same order
            // SortOne uses, and the open chest is one of these, so the panel the player is looking
            // at redraws from its own inventory like any other.
            // Null-checked again: the list was built before Consolidate ran, and a zone can
            // unload between the two. A MissingReferenceException here is swallowed by the
            // UnityEvent that called us, so the remaining chests would go untidied in silence.
            foreach (Container other in inRange)
            {
                if (other != null)
                    SortOne(other, false);
            }

            Plugin.Log.LogInfo($"sort all tidied {inRange.Count} chest(s) within {range:0.#}m, moving {moved} stack(s)"
                + (vessels > 0 ? $"; left {vessels} boat/cart hold(s) alone." : "."));
        }

        /// <summary>
        /// One winner per item across the whole set, then every other copy pulled into it.
        ///
        /// A chest is only ever a winner for a thing it already holds, which is what keeps a
        /// chest of ores from swallowing a chest of wood. The pass is over item names rather
        /// than over the chests, so a donor emptied by one winner is not then asked to donate
        /// to another - a chest can be both a winner and a donor in the same press, and that is
        /// not a conflict because the two never touch the same stack.
        /// </summary>
        private static int Consolidate(List<Container> inRange)
        {
            // What each chest holds, by item name. One pass builds both this and the winner
            // table, and the winner has to compare totals rather than a single stack, or a
            // chest holding 5 rubies across five slots would beat one holding 38 in one.
            var totals = new Dictionary<Container, Dictionary<string, int>>();
            var winners = new Dictionary<string, Container>(StringComparer.Ordinal);
            var winningTotal = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (Container container in inRange)
            {
                Inventory inv = container.GetInventory();
                if (inv == null)
                    continue;

                var mine = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (ItemDrop.ItemData item in inv.GetAllItems())
                {
                    if (item?.m_shared == null)
                        continue;

                    string name = item.m_shared.m_name;
                    mine[name] = item.m_stack + (mine.TryGetValue(name, out int was) ? was : 0);
                }

                totals[container] = mine;

                foreach (KeyValuePair<string, int> entry in mine)
                {
                    // Strictly greater, so a tie keeps the chest found first - and inRange is
                    // sorted by distance, so a tie keeps the nearer of the two.
                    if (!winners.TryGetValue(entry.Key, out Container best) ||
                        entry.Value > winningTotal[entry.Key])
                    {
                        winners[entry.Key] = container;
                        winningTotal[entry.Key] = entry.Value;
                    }
                }
            }

            int moved = 0;

            foreach (KeyValuePair<string, Container> pair in winners)
            {
                Inventory win = pair.Value.GetInventory();
                if (win == null)
                    continue;

                foreach (Container other in inRange)
                {
                    if (other == pair.Value)
                        continue;

                    // Only a chest that actually holds this name is worth walking. The lookup is
                    // what keeps the pass proportional to the base rather than to the square of
                    // it - with 8 chests and 30 kinds of thing this is the difference between a
                    // click and a stall.
                    if (!totals.TryGetValue(other, out Dictionary<string, int> mine) || !mine.ContainsKey(pair.Key))
                        continue;

                    Inventory from = other.GetInventory();
                    if (from == null)
                        continue;

                    bool took = false;

                    // Snapshotted because the move edits the list it is walking.
                    foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(from.GetAllItems()))
                    {
                        if (item?.m_shared == null || item.m_shared.m_name != pair.Key)
                            continue;

                        if (MoveOne(win, from, item))
                        {
                            took = true;
                            moved++;
                        }
                    }

                    if (!took)
                        continue;

                    // Written back for the same reason GatherLikeItems does it: an unsaved donor
                    // refills itself on the next sync, and the claim is what makes that write
                    // stick off a client that does not own the chest.
                    (ContainerNview?.GetValue(other) as ZNetView)?.ClaimOwnership();
                    ContainerSave?.Invoke(other, null);
                    NotifyChanged(from);
                }
            }

            return moved;
        }

        /// <summary>
        /// One chest: gather, merge, lay out, write back.
        ///
        /// The layout is capped at the grid's own size. A chest cannot hold more than it has slots,
        /// so the overflow is a sign the grid and the item list have disagreed - another mod has
        /// resized the container - and writing those positions anyway would put items outside the
        /// grid, where nothing draws them and the next save may drop them.
        /// </summary>
        private static void SortOne(Container container, bool gather)
        {
            Inventory inv = container.GetInventory();
            if (inv == null)
                return;

            // Gather first, then merge, then lay out - so a stack brought in from next door is
            // poured into the partial stack already here rather than sitting beside it.
            if (gather)
                GatherLikeItems(inv, container.transform.position, container);

            var items = new List<ItemDrop.ItemData>(inv.GetAllItems());
            MergeStacks(inv, items);
            items.Sort(Compare);

            int width = inv.GetWidth();
            int room = width * inv.GetHeight();
            for (int i = 0; i < items.Count && i < room; i++)
                items[i].m_gridPos = new Vector2i(i % width, i / width);

            if (items.Count > room)
                Plugin.Log.LogWarning($"placed {room} of {items.Count} stacks in {Utils.GetPrefabName(container.gameObject)}; the rest were left where they were.");

            // A chest being sorted is being written to, so it needs the ZDO claimed for the same
            // reason the donor chests in GatherLikeItems do.
            (ContainerNview?.GetValue(container) as ZNetView)?.ClaimOwnership();
            ContainerSave?.Invoke(container, null);
            NotifyChanged(inv);
        }

        // Container.Save, CheckAccess and m_wagon are all private. Cached once, null-checked at
        // the call, the same way this file already reaches InventoryGui.m_currentContainer.
        private static readonly MethodInfo ContainerSave = AccessTools.Method(typeof(Container), "Save");
        private static readonly MethodInfo ContainerCheckAccess = AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly FieldInfo ContainerWagon = AccessTools.Field(typeof(Container), "m_wagon");
        private static readonly FieldInfo ContainerNview = AccessTools.Field(typeof(Container), "m_nview");

        /// <summary>
        /// Pulls matching items in from the chests around this one.
        ///
        /// Only item types this chest already holds: a chest of ores stays a chest of ores and
        /// gains the ore lying in its neighbours, rather than becoming a bin for everything in
        /// range. That is the difference between tidying and hoarding. Sorted the same way from the
        /// bag, it is the reverse: it fills the bag with what the bag already has room for.
        ///
        /// A stack too big for what is left here comes across in part and leaves a valid stack
        /// behind, rather than being skipped whole.
        ///
        /// A sort the player wants without any reaching is a sort of just the one inventory, so a
        /// range of 0 skips the walk entirely.
        /// </summary>
        private static void GatherLikeItems(Inventory inv, Vector3 at, Container skip)
        {
            // What this inventory already stocks, read only from the part of it that is stock.
            // Worn armour and anything in a hidden row are equipment, not a pile to top up:
            // without those two tests, equipping a bronze sword pulled every bronze sword within
            // range out of its chest and into the bag.
            int rows = PlaceableRows(inv);
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
                if (item?.m_shared != null && !item.m_equipped && item.m_gridPos.y < rows)
                    wanted.Add(item.m_shared.m_name);

            float range = Plugin.GatherRange.Value;
            if (wanted.Count == 0 || range <= 0f || Game.instance == null)
                return;

            long playerId = Game.instance.GetPlayerProfile().GetPlayerID();
            float rangeSq = range * range;
            int moved = 0;

            // No registry to consult and none worth keeping: this runs on a button press, not per
            // frame, so the scene query is paid once by the click that asked for it.
            foreach (Container other in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                // A kg ItemDrawer is not a Container and cannot turn up here. No type in the game
                // derives from one - read off the DLL, the list of Container subclasses is empty -
                // and ItemDrawers builds its own ZNetView list through API.ClientSideV2.AllDrawers,
                // which this query does not reach. Skipped by having no such type, not by a test.
                if (other == null || other == skip || !Reachable(other, at, rangeSq, playerId))
                    continue;

                Inventory from = other.GetInventory();
                if (from == null)
                    continue;

                bool took = false;
                foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(from.GetAllItems()))
                {
                    if (item?.m_shared == null || !wanted.Contains(item.m_shared.m_name))
                        continue;

                    if (MoveOne(inv, from, item))
                    {
                        took = true;
                        moved++;
                    }
                }

                if (!took)
                    continue;

                // The chest that gave items up has to be written back and redrawn too, or its
                // contents come back the next time anyone opens it. The claim is what makes that
                // write stick off the owning client: Container.Save puts s_items straight on the
                // ZDO with no owner check, so a non-owner's write is local only and the owner's
                // copy lands on the next sync with the items back in the chest. ClaimOwnership
                // returns immediately when already owner, so it costs nothing in single player.
                (ContainerNview?.GetValue(other) as ZNetView)?.ClaimOwnership();
                ContainerSave?.Invoke(other, null);
                NotifyChanged(from);
            }

            if (moved > 0)
                Plugin.Log.LogInfo($"sort gathered {moved} stack(s) from the chests within {range:0.#}m.");
        }

        /// <summary>
        /// Moves one stack across, topping up matching piles and taking free slots until either
        /// the whole stack has gone or the target has no room left for it.
        ///
        /// 0,0 is NOT "anywhere". The 5-argument AddItem takes skipValidPositionCheck and
        /// MoveItemToThis passes it false, so the guard
        ///   x >= 0 && y >= 0 && x < m_width && (y < m_height || skip)
        /// runs for real: 0,0 is slot zero. Empty, it Clone()s straight into slot 0,0 and never
        /// looks for a free slot; occupied by a different type, IsSameType fails and the move
        /// returns false having done nothing. 1.2.1 passed 0,0 throughout on the belief that it
        /// meant "anywhere", so every gather wrote into slot zero and silently failed against any
        /// target whose first slot held something else.
        ///
        /// The loop is the other half of that. AddItem fills the ONE slot it is handed and no
        /// other, so a single call can only move that slot's remaining room: 50 wood offered to a
        /// chest holding a 20/50 pile and ten empty slots moved 30, left 20 in the donor, and
        /// returned true. Each pass has to strictly reduce m_stack or it stops, so this is
        /// bounded by the stack size and cannot spin.
        /// </summary>
        private static bool MoveOne(Inventory to, Inventory from, ItemDrop.ItemData item)
        {
            if (to == null || from == null || item?.m_shared == null)
                return false;

            bool moved = false;

            while (item.m_stack > 0)
            {
                int before = item.m_stack;
                MoveSome(to, from, item);

                // m_stack is the truth here, not the returned bool. AddItem returns
                // (moved == amount), so a short move reads as a failure; and a pass that moved
                // nothing would move nothing again, which is the only way out of this loop.
                if (item.m_stack >= before)
                    break;

                moved = true;
            }

            return moved;
        }

        /// <summary>
        /// One pass of the above: fill one destination slot.
        ///
        /// The order is the game's own, from FindFreeStackItem: fill a matching partial pile
        /// first, because that is the slot AddItem would have found on its own, and only take a
        /// new slot when there is no pile to fill. Room is recomputed every pass because each
        /// one changes it.
        /// </summary>
        private static void MoveSome(Inventory to, Inventory from, ItemDrop.ItemData item)
        {
            // 1. A pile of the same thing with room under it. The matching rule is SameStack
            //    rather than the IsSameType AddItem itself uses, because that one drops the
            //    quality test whenever maxQuality is 1 and would happily merge unlike things.
            ItemDrop.ItemData pile = null;
            int room = 0;
            foreach (ItemDrop.ItemData have in to.GetAllItems())
            {
                if (have == null || have.m_shared == null || have.m_stack >= have.m_shared.m_maxStackSize || !SameStack(have, item))
                    continue;

                int free = have.m_shared.m_maxStackSize - have.m_stack;
                if (pile == null || free > room)
                {
                    pile = have;
                    room = free;
                }
            }

            // Clamped to the pile's own room on purpose. AddItem can return false having moved
            // items when it is asked for more than the slot holds, and the callers use the
            // return value to decide whether to claim and save the donor - a short move that
            // read as failure would skip the write and the donor would restore itself.
            if (pile != null)
            {
                to.MoveItemToThis(from, item, Mathf.Min(room, item.m_stack), pile.m_gridPos.x, pile.m_gridPos.y);
                return;
            }

            // 2. A slot with nothing in it, inside the rows this inventory may be written to.
            int rows = PlaceableRows(to);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < to.GetWidth(); x++)
                {
                    if (to.GetItemAt(x, y) != null)
                        continue;

                    to.MoveItemToThis(from, item, Mathf.Min(item.m_stack, item.m_shared.m_maxStackSize), x, y);
                    return;
                }
            }

            // 3. Nothing. A stack with maxStackSize 1 is still worth one whole slot, which the
            //    search above already found; this is the genuinely full case.
        }

        /// <summary>
        /// A hold on a boat or a cart, rather than a chest in the base.
        ///
        /// Container.CheckAccess is no help here. Read off the DLL, it is a three-way switch on
        /// m_privacy: one case compares m_piece.GetCreator() to the player, one returns false,
        /// and one returns true unconditionally - which is the case a ship's cargo takes. So a
        /// longship moored next to the base passed every test Reachable had, and because the
        /// winner is whichever container holds the most of a thing, a well-stocked hold could
        /// pull the base's stores out onto the water. A cart left by the door did the same.
        ///
        /// Three tests because a vessel's hold is reached three ways:
        ///  - m_wagon is a public Vagon field on Container, and Catapult.Start sets it from its
        ///    own Vagon, so it is the cart's own link. 1.2.1 read it but only refused the cart
        ///    while it was being pulled, which is the one time nobody is sorting anyway.
        ///  - GetComponentInParent walks up from the container itself, and ShipEffects.Awake
        ///    resolves its Ship the same way, so a hold parented under the hull is caught.
        /// The per-press log line counts what this refused, so a boat that slips through is one
        /// number away from being visible rather than a guess.
        /// </summary>
        private static bool IsVessel(Container other)
        {
            return (ContainerWagon?.GetValue(other) as Vagon) != null
                   || other.GetComponentInParent<Vagon>() != null
                   || other.GetComponentInParent<Ship>() != null;
        }

        private static bool Reachable(Container other, Vector3 at, float rangeSq, long playerId)
        {
            if ((other.transform.position - at).sqrMagnitude > rangeSq)
                return false;

            // Somebody else has it open, or it is a cart being pulled - either way its contents
            // are in use and moving them out from under the user desyncs their panel.
            //
            // The flag has to come off the ZDO. Container.IsInUse() returns the plain local
            // m_inUse field, and Container.SetInUse returns immediately unless m_nview.IsOwner(),
            // so on a client that is not the owner - the only client this guard protects - the
            // field is never set and IsInUse() can never be true. The owner mirrors it into
            // ZDOVars.s_inUse from Container.UpdateUseVisual, and that is the copy everyone else
            // can see. Craft From Containers carries the same guard and the same reading.
            if ((ContainerNview?.GetValue(other) as ZNetView)?.GetZDO() is ZDO zdo && zdo.GetInt(ZDOVars.s_inUse, 0) == 1)
                return false;
            if (IsVessel(other))
                return false;

            // A chest carried by a player is not part of the base, and a ward that refuses the
            // player refuses the sort.
            if (other.GetComponentInParent<Player>() != null)
                return false;

            // A lookup that stops resolving must not quietly hand out everyone's chests, so a
            // missing CheckAccess counts as no access.
            if (!(ContainerCheckAccess?.Invoke(other, new object[] { playerId }) is bool allowed) || !allowed)
                return false;

            return PrivateArea.CheckAccess(other.transform.position, 0f, false, true);
        }

        /// <summary>
        /// Pours split piles of the same item back together before the sort lays them out.
        /// Half a stack of wood in one slot and half in another is the thing the button was
        /// wanted for; ordering them next to each other only made it easier to see.
        ///
        /// Earlier slots fill first, so the merged pile keeps the position of whichever copy
        /// the sort would have put first anyway, and emptied items are removed from the
        /// inventory rather than left as zero-stack ghosts - Inventory.RemoveItem is what
        /// vanilla calls when a stack is poured out in StackAll.
        /// </summary>
        private static void MergeStacks(Inventory inv, List<ItemDrop.ItemData> items)
        {
            List<ItemDrop.ItemData> emptied = null;

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData target = items[i];

                // An item already poured out by an earlier pass is on the emptied list and is
                // about to be removed. Without this line it is still reached as a target on the
                // next turn of the outer loop, refills itself from the partials after it, and is
                // then removed anyway - with a full stack in it. Four quarter-stacks of wood
                // (25/25/25/25, the exact mess this button exists to clean up) left 50 wood in
                // slot one and deleted a rebuilt 50 in slot two. Container.Save writes that to
                // the ZDO, so the loss is permanent.
                if (target.m_stack <= 0)
                    continue;

                for (int j = i + 1; j < items.Count && target.m_stack < target.m_shared.m_maxStackSize; j++)
                {
                    ItemDrop.ItemData source = items[j];
                    if (source.m_stack <= 0 || !SameStack(target, source))
                        continue;

                    int move = Mathf.Min(source.m_stack, target.m_shared.m_maxStackSize - target.m_stack);
                    target.m_stack += move;
                    source.m_stack -= move;

                    if (source.m_stack == 0)
                        (emptied ?? (emptied = new List<ItemDrop.ItemData>())).Add(source);
                }
            }

            if (emptied == null)
                return;

            foreach (ItemDrop.ItemData item in emptied)
            {
                inv.RemoveItem(item);
                items.Remove(item);
            }
        }

        /// <summary>
        /// The game's own test for "these two are the same pile", copied from
        /// Inventory.FindFreeStackItem: name, quality and world level, with room left under
        /// m_maxStackSize. Durability and crafter name are deliberately not in it - vanilla
        /// merges across both when it stacks, and a rule stricter than the game's would leave
        /// piles apart that the player can merge by hand.
        /// </summary>
        private static bool SameStack(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            return a.m_shared.m_maxStackSize > 1
                   && a.m_shared.m_name == b.m_shared.m_name
                   && a.m_quality == b.m_quality
                   && a.m_worldLevel == b.m_worldLevel;
        }

        private static int Compare(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            int byType = a.m_shared.m_itemType.CompareTo(b.m_shared.m_itemType);
            if (byType != 0)
                return byType;

            int byName = string.Compare(a.m_shared.m_name, b.m_shared.m_name, StringComparison.Ordinal);
            return byName != 0 ? byName : b.m_quality.CompareTo(a.m_quality);
        }
    }
}
