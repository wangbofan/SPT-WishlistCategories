using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.HandBook;
using EFT.UI;
using EFT.UI.Ragfair;
using HarmonyLib;

namespace SPTWishlistCategories.Patches
{
    // (1) RagFair.UpdateWishlist 之后，把扁平愿望单重建成分类树。
    internal sealed class UpdateWishlistPatch : ModPatch
    {
        protected override MethodBase TargetMethod()
        {
            return Resolve(typeof(RagFair), "UpdateWishlist");
        }

        private static void Postfix(RagFair __instance)
        {
            try
            {
                WishlistTree.Rebuild(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"UpdateWishlist 后重建失败: {e}");
            }
        }
    }

    // (2) 愿望单视图下 Count == 0 的节点本会被 CanShowNodeView 隐藏，分类行需要放行。
    internal sealed class CanShowNodeViewPatch : ModPatch
    {
        protected override MethodBase TargetMethod()
        {
            return Resolve(typeof(HandbookNode), "CanShowNodeView", typeof(EViewListType));
        }

        private static bool Prefix(HandbookNode __instance, ref bool __result)
        {
            if (!WishlistTree.IsGroup(__instance))
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    // (3) 分类行的展开三角：Count == 0 时官方会隐藏它，分类必须常显。
    internal sealed class CategoryViewCountPatch : ModPatch
    {
        protected override MethodBase TargetMethod()
        {
            return Resolve(typeof(CategoryView), "OffersCountUpdatedHandler", typeof(HandbookNode));
        }

        private static void Postfix(CategoryView __instance, HandbookNode node)
        {
            if (__instance == null || !WishlistTree.IsGroup(node))
            {
                return;
            }

            try
            {
                if (__instance._toggle != null)
                {
                    __instance._toggle.gameObject.SetActive(true);
                }
            }
            catch
            {
                // 视图正在销毁
            }
        }
    }

    // (4) 分类行出现时按配置自动展开。
    internal sealed class CategoryViewShowPatch : ModPatch
    {
        protected override MethodBase TargetMethod()
        {
            return Resolve(typeof(CategoryView), "Show",
                typeof(RagFair), typeof(NodeBaseView), typeof(NodeBaseView), typeof(HandbookNode),
                typeof(EViewListType), typeof(EWindowType), typeof(Dictionary<string, NodeBaseView>),
                typeof(string), typeof(Action<NodeBaseView, string>), typeof(Action<NodeBaseView, string>));
        }

        private static void Postfix(CategoryView __instance)
        {
            if (__instance == null || !Plugin.ExpandGroups.Value)
            {
                return;
            }

            HandbookNode node = __instance.Node;
            if (!WishlistTree.IsGroup(node) || node.ChildrenDict.Count == 0)
            {
                return;
            }

            try
            {
                __instance.OpenCategory();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"自动展开分类失败: {e}");
            }
        }
    }

    // (5) 点击分类行：只展开/折叠，绝不把分类 id 交给 SetFilterRule（否则会拉回该分类全部报价）。
    internal sealed class NodeIdChangedPatch : ModPatch
    {
        protected override MethodBase TargetMethod()
        {
            return Resolve(typeof(OfferViewList), "HandbookNodeIdChangedHandler", typeof(string), typeof(bool));
        }

        private static bool Prefix(OfferViewList __instance, string id)
        {
            if (ViewUtil.GetViewListType(__instance) != EViewListType.WishList)
            {
                return true;
            }

            RagfairCategoriesPanel panel = __instance._browseCategoriesPanel;
            if (panel == null)
            {
                return true;
            }

            RagFair ragfair = panel.Ragfair;
            if (ragfair == null || !WishlistTree.IsCategoryIdInTree(ragfair._wishlist, id))
            {
                return true;
            }

            try
            {
                NodeBaseView view = panel.GetNode(id);
                if (view is CategoryView categoryView)
                {
                    categoryView.OpenCategory();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"展开分类 {id} 失败: {e}");
            }

            return false;
        }
    }

    // (6) 切到愿望单页时官方会拿根条目当选中项，分组后根条目是分类；
    //     这里改为自动选中一个物品，保证右侧报价列表和官方行为一致。
    internal sealed class UpdateListTypePatch : ModPatch
    {
        protected override MethodBase TargetMethod()
        {
            return Resolve(typeof(OfferViewList), "UpdateListType");
        }

        private static void Postfix(OfferViewList __instance)
        {
            if (ViewUtil.GetViewListType(__instance) != EViewListType.WishList)
            {
                return;
            }

            try
            {
                RagfairCategoriesPanel panel = __instance._browseCategoriesPanel;
                RagFair ragfair = panel != null ? panel.Ragfair : null;
                HandbookNodes tree = ragfair != null ? ragfair._wishlist : null;
                if (tree == null || tree.Count == 0)
                {
                    return;
                }

                HandbookNode current = tree[ragfair.FilterRule.HandbookId];
                if (current != null && current.Data != null && current.Data.Type == ENodeType.Item)
                {
                    return;
                }

                HandbookNode target = WishlistTree.PickItem(tree, true);
                if (target != null)
                {
                    __instance.HandbookNodeIdChangedHandler(target.Data.Id, false);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"愿望单自动选中失败: {e}");
            }
        }
    }

    // (7) 加入愿望单：自己挂到分类下（静默写字典，不触发 ItemAdded 造成多余行）。
    internal sealed class AddToWishListPatch : ModPatch
    {
        protected override MethodBase TargetMethod()
        {
            return Resolve(typeof(RagFair), "AddToWishList", typeof(string));
        }

        private static bool Prefix(RagFair __instance, string id)
        {
            if (!Plugin.Enabled.Value)
            {
                return true;
            }

            try
            {
                WishlistTree.AddItem(__instance, id);
                __instance.ClearWishListCache();
                __instance.ForceNextUpdate = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"加入愿望单失败: {e}");
            }

            WishlistRefresh.Schedule(id);
            return false;
        }
    }

    // (8) 移出愿望单：从分类下摘掉，顺手清理空分类。
    internal sealed class RemoveFromWishListPatch : ModPatch
    {
        protected override MethodBase TargetMethod()
        {
            return Resolve(typeof(RagFair), "RemoveFromWishList", typeof(string));
        }

        private static bool Prefix(RagFair __instance, string id)
        {
            if (!Plugin.Enabled.Value)
            {
                return true;
            }

            try
            {
                WishlistTree.RemoveItem(__instance, id);
                __instance.ClearWishListCache();
                __instance.ForceNextUpdate = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"移出愿望单失败: {e}");
            }

            WishlistRefresh.Schedule(null);
            return false;
        }
    }

    // (9) 官方 UpdateNodes 会按后端返回的 categories 给命中 id 加计数；
    //     分类 id 一旦命中就会把整类报价数加到分类上，这里统一按"子节点之和"重算。
    internal sealed class UpdateNodesPatch : ModPatch
    {
        protected override MethodBase TargetMethod()
        {
            return Resolve(typeof(RagFair), "UpdateNodes", typeof(HandbookNodes), typeof(Dictionary<string, int>));
        }

        private static void Postfix(HandbookNodes nodes)
        {
            if (nodes == null)
            {
                return;
            }

            try
            {
                bool isWishlistTree = false;
                foreach (HandbookNode root in nodes.Values)
                {
                    if (WishlistTree.IsGroup(root))
                    {
                        isWishlistTree = true;
                        break;
                    }
                }

                if (isWishlistTree)
                {
                    WishlistTree.RecountAndNotify(nodes.Values);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"重算愿望单计数失败: {e}");
            }
        }
    }

    // (10) ClearWishListCache 会把 Wishlist.Last() 写进 LastSelectedIds；分组后那是分类 id，
    //      这里改写成该分类下的第一个物品。
    internal sealed class ClearWishListCachePatch : ModPatch
    {
        protected override MethodBase TargetMethod()
        {
            return Resolve(typeof(RagFair), "ClearWishListCache");
        }

        private static void Postfix(RagFair __instance)
        {
            try
            {
                Dictionary<EViewListType, string> last = __instance.LastSelectedIds;
                if (last == null || !last.TryGetValue(EViewListType.WishList, out string id) || string.IsNullOrEmpty(id))
                {
                    return;
                }

                HandbookNode node = __instance._wishlist != null ? __instance._wishlist[id] : null;
                if (node == null || node.Data == null || node.Data.Type != ENodeType.Category)
                {
                    return;
                }

                HandbookNode item = WishlistTree.FirstItemUnder(node);
                last[EViewListType.WishList] = item != null && item.Data != null ? item.Data.Id : string.Empty;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"修正愿望单默认选中失败: {e}");
            }
        }
    }

    // (11) 记录当前活跃的愿望单视图，供增删后的延迟重建使用。
    internal sealed class ManageOffersPatch : ModPatch
    {
        protected override MethodBase TargetMethod()
        {
            return Resolve(typeof(OfferViewList), "ManageOffers", typeof(OffersWithNodes));
        }

        private static void Postfix(OfferViewList __instance)
        {
            try
            {
                if (ViewUtil.GetViewListType(__instance) == EViewListType.WishList)
                {
                    WishlistRefresh.SetView(__instance);
                }
                else
                {
                    WishlistRefresh.ClearView(__instance);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }
    }

    // (12) 分类之间的排序：分类恒在物品之前，分类之间按手册 Order 降序（与跳蚤市场一致）。
    internal sealed class CategoriesComparerPatch : ModPatch
    {
        protected override MethodBase TargetMethod()
        {
            return Resolve(typeof(CategoriesComparer), "Compare", typeof(HandbookNode), typeof(HandbookNode));
        }

        private static bool Prefix(HandbookNode x, HandbookNode y, ref int __result)
        {
            bool xGroup = WishlistTree.IsGroup(x);
            bool yGroup = WishlistTree.IsGroup(y);

            if (!xGroup && !yGroup)
            {
                return true;
            }

            if (xGroup != yGroup)
            {
                __result = xGroup ? -1 : 1;
                return false;
            }

            if (Plugin.SortByHandbookOrder.Value)
            {
                int order = y.Data.Order - x.Data.Order;
                if (order != 0)
                {
                    __result = order;
                    return false;
                }
            }

            __result = string.CompareOrdinal(x.Data.Name.Localized(), y.Data.Name.Localized());
            return false;
        }
    }

    // (13) 独占高亮：官方只在 HandleNodeSelection 里 Deselect 上一个选中节点，
    //      分类行的点击被本 mod 拦下（不走 SetFilterRule），那条链路不会执行，
    //      于是每点一个分类就多亮一行。这里在任何视图被选中时清掉其它选中态。
    internal sealed class ExclusiveSelectionPatch : ModPatch
    {
        protected override MethodBase TargetMethod()
        {
            return Resolve(typeof(NodeBaseView), "SelectView");
        }

        private static void Postfix(NodeBaseView __instance)
        {
            try
            {
                WishlistSelection.DeselectOthers(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"清除其它高亮失败: {e}");
            }
        }
    }

    internal static class ViewUtil
    {
        private static readonly FieldInfo ViewListTypeField =
            AccessTools.Field(typeof(OfferViewList), "_viewListType");

        internal static EViewListType GetViewListType(OfferViewList view)
        {
            if (view == null || ViewListTypeField == null)
            {
                return EViewListType.AllOffers;
            }

            object value = ViewListTypeField.GetValue(view);
            return value is EViewListType type ? type : EViewListType.AllOffers;
        }
    }
}
