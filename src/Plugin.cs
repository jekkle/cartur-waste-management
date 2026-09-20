using System;
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
    // Drop an item on the can and it goes back to what it was made of. Valheim already knows
    // the answer: ObjectDB.GetRecipe(item) is the same recipe the crafting bench used, and
    // Requirement.GetAmount(quality) is what that bench charged. Anything with no recipe -
    // wood, stone, ore, a berry - has nothing to give back, so it is simply removed.
    //
    // Sort sits under the can and orders everything except the hotbar row.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jekkle.valheim.carturwastemanagement";
        public const string PluginName = "Cartur's Waste Management";
        public const string PluginVersion = "1.0.0";

        internal static Sprite CanSprite;
        internal static BepInEx.Logging.ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            CanSprite = LoadSprite("trashcan_128.png");

            try
            {
                Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
                Logger.LogInfo($"{PluginName} {PluginVersion} loaded - trash can recycles through the item's own recipe, sort button added.");
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

        private static void Postfix(InventoryGui __instance)
        {
            if (__instance == null)
                return;

            BuildContainerSort(__instance);

            if (s_can != null || __instance.m_weight == null)
                return;

            RectTransform weight = __instance.m_weight.rectTransform;
            RectTransform parent = weight.parent as RectTransform;
            if (parent == null)
                return;

            // Stacked upward from the weight box: weight, then sort, then the can on top.
            float sortY = weight.anchoredPosition.y + ButtonHeight + Gap;
            float canY = sortY + ButtonHeight * 0.5f + CanSize * 0.5f + Gap;

            s_can = BuildCan(parent, new Vector2(weight.anchoredPosition.x, canY));
            s_sort = BuildSort(__instance, parent, new Vector2(weight.anchoredPosition.x, sortY));
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
            rect.anchoredPosition = stackAll.anchoredPosition - new Vector2(0f, stackAll.rect.height + Gap);

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
        // amount/m_amount whole crafts are what can honestly be given back; a leftover that does
        // not make a whole craft returns nothing, the same way the bench never sold a half one.
        private static void Recycle(Player player, Inventory from, ItemDrop.ItemData item, int amount)
        {
            Recipe recipe = ObjectDB.instance != null ? ObjectDB.instance.GetRecipe(item) : null;
            int perCraft = recipe != null ? Mathf.Max(1, recipe.m_amount) : 0;
            int crafts = recipe != null ? amount / perCraft : 0;

            if (crafts > 0)
            {
                Inventory to = player.GetInventory();
                foreach (Piece.Requirement req in recipe.m_resources)
                {
                    // m_recover is the game's own flag for "this comes back out again" - it is
                    // what upgrades and repairs read. Honour it rather than inventing a rule.
                    if (req == null || req.m_resItem == null || !req.m_recover)
                        continue;

                    int give = req.GetAmount(item.m_quality) * crafts;
                    if (give <= 0)
                        continue;

                    string prefab = req.m_resItem.gameObject.name;
                    if (!to.AddItem(req.m_resItem.gameObject, give))
                        ItemDrop.DropItem(req.m_resItem.m_itemData, give, player.transform.position + player.transform.forward, Quaternion.identity);

                    Plugin.Log.LogDebug($"returned {give} {prefab}");
                }
            }

            if (item.m_equipped)
                player.UnequipItem(item, false);

            if (amount >= item.m_stack)
                from.RemoveItem(item);
            else
                from.RemoveItem(item, amount);
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

            var items = new List<ItemDrop.ItemData>(inv.GetAllItems());
            MergeStacks(inv, items);
            items.Sort(Compare);

            int width = inv.GetWidth();
            for (int i = 0; i < items.Count; i++)
                items[i].m_gridPos = new Vector2i(i % width, i / width);

            NotifyChanged(inv);
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
