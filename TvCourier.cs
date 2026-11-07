using System;
using UnityEngine;

namespace WindowTV
{
    /// <summary>
    /// 送货员：深空电视不凭空刷在柜台上，而是由一位真实到店的客户送进来。
    /// 用官方 AddNextClient 插到队首，保证当天开门第一个进来的就是他。
    /// </summary>
    internal static class TvCourier
    {
        internal const string ClientId = "window_tv_courier";

        private const string Greeting =
            "社区发放新的设备了，深空电视。\n" +
            "就放柜台上，自己搬。";

        private const string Farewell = "东西送到，我先走了。";

        /// <summary>
        /// 把送货员插到当日队列最前面。已经有深空电视、或队列里已经有他，就什么都不做。
        /// </summary>
        internal static void TryQueue(StoreClientManager manager)
        {
            try
            {
                if (manager == null || manager.clientStack == null)
                {
                    return;
                }
                // PlayerStore.IsPlayerOwnThisItem 内部会遍历 EmporiumEntry 的窗口，
                // 场景还没就绪时直接调用会空引用，这里先挡掉。
                if (PlayerStore.Instance == null || EmporiumEntry.Instance == null)
                {
                    return;
                }
                if (PlayerStore.Instance.IsPlayerOwnThisItem(TvItem.ItemId))
                {
                    return;
                }
                if (AlreadyQueued(manager))
                {
                    return;
                }

                StoreClient client = Create();
                manager.AddNextClient(client); // 插到索引 0，下一个进店的就是他

                if (manager.clientStack.Count == 0 || manager.clientStack[0] != client)
                {
                    // 被小混混/黑名单之类的规则挡掉了，别留下半截状态
                    ModEntry.Log.Log((object)"[WindowTV] 送货员没能入队（被规则拦下）");
                    return;
                }

                ModEntry.Log.Log((object)"[WindowTV] 送货员已排到今日第一位");
            }
            catch (Exception ex)
            {
                ModEntry.Log.Log((object)("[WindowTV] 排队送货员失败: " + ex));
            }
        }

        /// <summary>
        /// 兜底守卫：读档时门已经开着、或者错过了当天生成钩子的话，
        /// 由运行时定时调这里补一次。有人正在柜台前谈生意时绝不插队。
        /// </summary>
        internal static void Guard()
        {
            try
            {
                PlayerStore store = PlayerStore.Instance;
                if (store == null || EmporiumEntry.Instance == null)
                {
                    return;
                }
                if (store.currentClientInstance != null)
                {
                    return;
                }
                TryQueue(store.storeClientManager);
            }
            catch (Exception)
            {
            }
        }

        private static bool AlreadyQueued(StoreClientManager manager)
        {
            StoreClientInstance current = PlayerStore.Instance.currentClientInstance;
            if (current != null)
            {
                StoreClient blueprint = current.GetClientBlueprint();
                if (blueprint != null && blueprint.identifier == ClientId)
                {
                    return true;
                }
            }

            for (int i = 0; i < manager.clientStack.Count; i++)
            {
                StoreClient queued = manager.clientStack[i];
                if (queued != null && queued.identifier == ClientId)
                {
                    return true;
                }
            }
            return false;
        }

        private static StoreClient Create()
        {
            StoreClient client = new StoreClient
            {
                identifier = ClientId,
                sourceModId = ModEntry.Id,
                displayName = "送货员",
                spriteName = SpriteDict.GetCourrierSpriteName(),
                clientFaction = StoreClient.FACTION_LOWER,
                isBargainAvailable = false,
                clientIntent = StoreClient.ClientIntent.SELL,
                isRegularSellDisable = true,
                wholesaleDiscount = 0,
                isOfferWholesale = false
            };

            client.mainDialogue
                .SetText(client.displayName, Greeting)
                .SetEndAction(delegate
                {
                    Deliver();
                    if (DialogUIManager.Instance != null)
                    {
                        DialogUIManager.Instance.CurrentClientLeave();
                    }
                });

            client.allDoneDialogue = new Dialogue()
                .SetText(client.displayName, Farewell)
                .SetEndAction(delegate
                {
                    if (DialogUIManager.Instance != null)
                    {
                        DialogUIManager.Instance.CurrentClientLeave();
                    }
                });

            client.CompleteClientCreation();
            return client;
        }

        /// <summary>把深空电视放到前台柜面上；前台满了再退到后仓、最后退到背包。</summary>
        private static void Deliver()
        {
            try
            {
                if (PlayerStore.Instance == null || EmporiumEntry.Instance == null)
                {
                    return;
                }
                if (PlayerStore.Instance.IsPlayerOwnThisItem(TvItem.ItemId))
                {
                    return;
                }

                // 和游戏里免费啤酒快递一样，走官方「直接放柜台」接口
                if (PlayerStore.Instance.TryAddDirectSellingItemToTable(TvItem.ItemId, isOwend: true))
                {
                    ModEntry.Log.Log((object)"[WindowTV] 送货员已把深空电视放在前台柜面");
                    return;
                }

                // 前台没空位：先试试后仓，再试背包
                GameItem item = ModHelper.SetItemOwnerShip(ModHelper.SpawnItem(TvItem.ItemId), true);
                if (item == null)
                {
                    ModEntry.Log.Log((object)"[WindowTV] 送货失败：物品生成返回空");
                    return;
                }

                ModHelper.MoveItemToBackCounter(item);
                if (item.parentInventory == null)
                {
                    ModHelper.MoveItemToInventory(item);
                }

                if (item.parentInventory == null)
                {
                    item.Destroy();
                    ModEntry.Log.Log((object)"[WindowTV] 送货失败：柜台和背包都没有空位");
                    return;
                }

                ModEntry.Log.Log((object)"[WindowTV] 前台满了，深空电视已放到后仓/背包");
            }
            catch (Exception ex)
            {
                ModEntry.Log.Log((object)("[WindowTV] Deliver: " + ex));
            }
        }
    }
}
