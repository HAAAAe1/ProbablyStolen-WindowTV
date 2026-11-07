using System;
using System.Collections.Generic;
using UnityEngine;

namespace WindowTV
{
    /// <summary>电视物品：放在柜台/背包里，点击打开窗口。</summary>
    internal static class TvItem
    {
        internal const string ItemId = "window_tv_item";

        internal static void Register(ModItemDirectory directory)
        {
            directory.Add(ItemId, Create);
        }

        private static GameItem Create()
        {
            GameItem item = ItemDirectory
                .CreateEmptyItem(ItemId)
                .SetName("深空电视")
                .SetSpriteAndShapeFromMod(ModEntry.Id, "television");

            item.unitValue = 0;
            item.SetGameItemType("DOCUMENT");
            item.activateDefault = false;
            item.shortDescription = "深空电视：可能是看视频的。\n单击打开，把电脑上任意窗口的画面投进来。\n右边两个旋钮：上面拧画面大小，下面拧着换窗口。";

            item.canActivateSlotItemFunc = (GameItem i, GameInventory inv, SlotMarker slot) => GeneralHelper.IsItemOwned(i);
            item.mayActivateSlotItemFunc = (GameItem i, GameInventory inv, SlotMarker slot) => GeneralHelper.IsItemOwned(i);
            item.onActivateSlotItemFunc = (GameItem i, GameInventory inv, SlotMarker slot) => TvWindow.Show();

            item.SetShape(new GridShapeBuilder().SetData(new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 1 }, 3));
            return item;
        }

        internal static void GiveItem()
        {
            try
            {
                if (PlayerStore.Instance == null)
                {
                    return;
                }
                if (PlayerStore.Instance.IsPlayerOwnThisItem(ItemId))
                {
                    ModEntry.Log.Log((object)"[WindowTV] 你已经有一台深空电视了");
                    return;
                }
                GameItem item = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(ItemId), true);
                if (item == null)
                {
                    ModEntry.Log.Log((object)"[WindowTV] 生成深空电视失败");
                    return;
                }
                ModHelper.MoveItemToInventory(item);
                if (item.parentInventory == null)
                {
                    ModHelper.MoveItemToBackCounter(item);
                }
                if (item.parentInventory == null)
                {
                    item.Destroy();
                    ModEntry.Log.Log((object)"[WindowTV] 没有空间放深空电视，请清出空位");
                }
            }
            catch (Exception ex)
            {
                ModEntry.Log.Log((object)("[WindowTV] GiveItem: " + ex));
            }
        }

        /// <summary>调试用：清掉店里所有电视，方便验证送货员流程。</summary>
        internal static void RemoveAll()
        {
            try
            {
                PlayerStore store = PlayerStore.Instance;
                if (store == null || EmporiumEntry.Instance == null)
                {
                    ModEntry.Log.Log((object)"[WindowTV] 清空深空电视失败：场景未就绪");
                    return;
                }

                List<GameItem> all = store.FindAllItem();
                int removed = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    GameItem item = all[i];
                    if (item != null && item.identifier == ItemId)
                    {
                        item.Destroy();
                        removed++;
                    }
                }
                ModEntry.Log.Log((object)("[WindowTV] 已清掉 " + removed + " 台深空电视"));
            }
            catch (Exception ex)
            {
                ModEntry.Log.Log((object)("[WindowTV] RemoveAll: " + ex));
            }
        }
    }
}
