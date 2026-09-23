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
    // Requirement.GetAmount(quality) is what that bench charged. Anything with no recipe -
    // wood, stone, ore, a berry - has nothing to give back, so it is simply removed.
    //
    // Sort sits under the can and orders everything except the hotbar row.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jekkle.valheim.carturwastemanagement";
        public const string PluginName = "Cartur's Waste Management";
        public const string PluginVersion = "1.2.0";

        internal static Sprite CanSprite;
        internal static BepInEx.Logging.ManualLogSource Log;

        internal static BepInEx.Configuration.ConfigEntry<bool> ShowTrashCan;
        internal static BepInEx.Configuration.ConfigEntry<bool> ShowBagSort;
        internal static BepInEx.Configuration.ConfigEntry<bool> ShowChestSort;
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

        /// Where each button was put before any offset was applied. Kept so the offset is always
        /// measured from the same place: adding it to the button's current position instead would
        /// move it again every time the panel opened.
        private static Vector2 s_canHome, s_sortHome, s_containerSortHome;

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
        }

        private static void Place(GameObject go, Vector2 home, bool show, Vector2 offset)
        {
            if (go == null)
                return;
            go.SetActive(show);
            ((RectTransform)go.transform).anchoredPosition = home + offset;
        }

        /// <summary>
        /// The same Sort button for whatever chest is open. Cloned from Take All so it carries
        /// the game's own skin and click sound, and built once and reused: the container panel
        /// is the same object for every chest, only its inventory changes.
        ///
        /// It is placed directly under Place Stacks (InventoryGui.m_stackAllButton) - Cartur's
        /// call - and takes that button's anchors, pivot and size so it reads as one more button
        /// in the same stack however the panel has been moved or scaled.
        /// </summary>
        private static void BuildContainerSort(InventoryGui gui)
        {
            if (s_containerSort != null || gui.m_takeAllButton == null || gui.m_container == null)
                return;

            RectTransform stackAll = gui.m_stackAllButton != null
                ? gui.m_stackAllButton.GetComponent<RectTransform>() : null;
            if (stackAll == null)
                return;

            GameObject go = UnityEngine.Object.Instantiate(gui.m_takeAllButton.gameObject, stackAll.parent);
            go.name = "CarturSortContainerButton";
            go.SetActive(true);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = stackAll.anchorMin;
            rect.anchorMax = stackAll.anchorMax;
            rect.pivot = stackAll.pivot;
            rect.sizeDelta = stackAll.sizeDelta;
            s_containerSortHome = stackAll.anchoredPosition - new Vector2(0f, stackAll.rect.height + Gap);
            rect.anchoredPosition = s_containerSortHome;

            foreach (TMP_Text label in go.GetComponentsInChildren<TMP_Text>(true))
                label.text = "Sort";

            var button = go.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(WasteBin.SortOpenContainer);

            s_containerSort = go;

            // The three vanilla buttons' own positions, so where Sort landed can be read rather
            // than eyeballed - if it sits on top of one of them, this line says so.
            RectTransform takeAll = gui.m_takeAllButton.GetComponent<RectTransform>();
            RectTransform drop = gui.m_dropButton != null ? gui.m_dropButton.GetComponent<RectTransform>() : null;
            Plugin.Log.LogInfo("container sort button at " + rect.anchoredPosition
                + " size " + rect.rect.size
                + "  (place stacks " + stackAll.anchoredPosition + " size " + stackAll.rect.size
                + ", take all " + (takeAll != null ? takeAll.anchoredPosition.ToString() : "?")
                + ", drop " + (drop != null ? drop.anchoredPosition.ToString() : "none") + ")");
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
            if (gui == null || player == null)
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

            if (crafts > 0)
            {
                foreach (Piece.Requirement req in recipe.m_resources)
                {
                    // m_recover is the game's own flag for "this comes back out again" - it is
                    // what upgrades and repairs read. Honour it rather than inventing a rule.
                    if (req == null || req.m_resItem == null || !req.m_recover)
                        continue;

                    int give = Half(req.GetAmount(item.m_quality) * crafts);
                    if (give <= 0)
                        continue;

                    Give(player, req.m_resItem.gameObject, give);
                    Plugin.Log.LogDebug($"returned {give} {req.m_resItem.gameObject.name}");
                }
            }

            GiveEpicLootRefund(player, item, amount);

            if (item.m_equipped)
                player.UnequipItem(item, false);

            if (amount >= item.m_stack)
                from.RemoveItem(item);
            else
                from.RemoveItem(item, amount);
        }

        // Half, rounded up: 5 becomes 3, 1 stays 1. Nothing that was paid for comes back as
        // nothing, which rounding down would do to every single-unit ingredient.
        private static int Half(int n) => (n + 1) / 2;

        // Into the bag, or at the player's feet when it will not fit. Both the recipe refund
        // and the enchant refund hand out items this way.
        private static void Give(Player player, GameObject prefab, int amount)
        {
            if (player.GetInventory().AddItem(prefab, amount))
                return;

            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            if (drop != null)
                ItemDrop.DropItem(drop.m_itemData, amount, player.transform.position + player.transform.forward, Quaternion.identity);
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
                || TryGetRarity == null || CostItem == null || CostAmount == null)
                return;

            IEnumerable bill;
            bool halve;
            try
            {
                halve = IsMagicItem.Invoke(null, new object[] { item }) is bool magic && magic;
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

        // Row 0 is the hotbar and stays where the player put it; the visible rows below it are
        // grouped by item type, then name, then best quality first. Anything in a hidden row is
        // in a slot, not in the bag, and is left alone.
        internal static void SortPlayerInventory()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
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

            var loose = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item.m_gridPos.y > 0 && item.m_gridPos.y < visible)
                    loose.Add(item);
            }

            MergeStacks(inv, loose);
            loose.Sort(Compare);

            int width = inv.GetWidth();
            for (int i = 0; i < loose.Count; i++)
                loose[i].m_gridPos = new Vector2i(i % width, 1 + i / width);

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

            var container = CurrentContainer?.GetValue(gui) as Container;
            Inventory inv = container?.GetInventory();
            if (inv == null)
                return;

            // Gather first, then merge, then lay out - so a stack brought in from next door is
            // poured into the partial stack already here rather than sitting beside it.
            GatherLikeItems(container, inv);

            var items = new List<ItemDrop.ItemData>(inv.GetAllItems());
            MergeStacks(inv, items);
            items.Sort(Compare);

            int width = inv.GetWidth();
            for (int i = 0; i < items.Count; i++)
                items[i].m_gridPos = new Vector2i(i % width, i / width);

            NotifyChanged(inv);
        }

        // Container.Save, CheckAccess and m_wagon are all private. Cached once, null-checked at
        // the call, the same way this file already reaches InventoryGui.m_currentContainer.
        private static readonly MethodInfo ContainerSave = AccessTools.Method(typeof(Container), "Save");
        private static readonly MethodInfo ContainerCheckAccess = AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly FieldInfo ContainerWagon = AccessTools.Field(typeof(Container), "m_wagon");

        // How far Sort reaches for matching items. The same 20m Craft From Containers calls
        // "nearby", so the two features agree about which chests are part of this base.
        private const float GatherRange = 20f;

        /// <summary>
        /// Pulls matching items in from the chests around this one.
        ///
        /// Only item types this chest already holds: a chest of ores stays a chest of ores and
        /// gains the ore lying in its neighbours, rather than becoming a bin for everything in
        /// range. That is the difference between tidying and hoarding.
        ///
        /// Whole stacks only. A stack that will not fit entirely is left where it is instead of
        /// being split, so a sort never leaves a torn remainder in the chest next door.
        /// </summary>
        private static void GatherLikeItems(Container target, Inventory inv)
        {
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
                if (item?.m_shared != null)
                    wanted.Add(item.m_shared.m_name);

            if (wanted.Count == 0 || Game.instance == null)
                return;

            long playerId = Game.instance.GetPlayerProfile().GetPlayerID();
            Vector3 at = target.transform.position;
            float rangeSq = GatherRange * GatherRange;
            int moved = 0;

            // No registry to consult and none worth keeping: this runs on a button press, not per
            // frame, so the scene query is paid once by the click that asked for it.
            foreach (Container other in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                if (other == null || other == target || !Reachable(other, at, rangeSq, playerId))
                    continue;

                Inventory from = other.GetInventory();
                if (from == null)
                    continue;

                bool took = false;
                foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(from.GetAllItems()))
                {
                    if (item?.m_shared == null || !wanted.Contains(item.m_shared.m_name))
                        continue;
                    if (!inv.CanAddItem(item, item.m_stack))
                        continue;

                    inv.MoveItemToThis(from, item);
                    took = true;
                    moved++;
                }

                if (!took)
                    continue;

                // The chest that gave items up has to be written back and redrawn too, or its
                // contents come back the next time anyone opens it.
                ContainerSave?.Invoke(other, null);
                NotifyChanged(from);
            }

            if (moved > 0)
                Plugin.Log.LogInfo($"sort gathered {moved} stack(s) into {Utils.GetPrefabName(target.gameObject)}.");
        }

        private static bool Reachable(Container other, Vector3 at, float rangeSq, long playerId)
        {
            if ((other.transform.position - at).sqrMagnitude > rangeSq)
                return false;

            // Somebody else has it open, or it is a cart being pulled - either way its contents
            // are in use and moving them out from under the user desyncs their panel.
            if (other.IsInUse() && !other.IsOwner())
                return false;
            if (ContainerWagon?.GetValue(other) is Vagon wagon && wagon.InUse())
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
