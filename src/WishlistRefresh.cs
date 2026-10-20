using System;
using EFT;
using EFT.HandBook;
using EFT.UI.Ragfair;

namespace SPTWishlistCategories
{
    /// <summary>
    /// 愿望单侧栏的延迟刷新：官方拖拽/右键增删物品后，等到下一帧再整表重建，
    /// 避免在 UI 回调栈里重入 ManageOffers 引发各种解析异常。
    /// </summary>
    internal static class WishlistRefresh
    {
        private static OfferViewList _view;
        private static bool _pending;
        private static string _pendingId;

        internal static void SetView(OfferViewList view)
        {
            if (view != null)
            {
                _view = view;
            }
        }

        internal static void ClearView(OfferViewList view)
        {
            if (ReferenceEquals(_view, view))
            {
                _view = null;
            }
        }

        internal static void Schedule(string id)
        {
            _pending = true;
            if (!string.IsNullOrEmpty(id))
            {
                _pendingId = id;
            }
        }

        internal static void Tick()
        {
            if (!_pending)
            {
                return;
            }

            OfferViewList view = _view;
            if (view == null || !view.gameObject.activeInHierarchy)
            {
                _pending = false;
                _pendingId = null;
                return;
            }

            _pending = false;
            string wanted = _pendingId;
            _pendingId = null;

            RagFair ragfair = null;
            try
            {
                ragfair = view._browseCategoriesPanel != null ? view._browseCategoriesPanel.Ragfair : null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }

            if (ragfair == null)
            {
                return;
            }

            try
            {
                view.ManageOffers(ragfair.Offers);

                HandbookNodes tree = ragfair._wishlist;
                if (string.IsNullOrEmpty(wanted) &&
                    tree != null &&
                    tree[ragfair.FilterRule.HandbookId] == null)
                {
                    // 之前选中的物品已被移除：退回到任意一个有报价的物品。
                    HandbookNode fallback = WishlistTree.PickItem(tree, true);
                    wanted = fallback?.Data?.Id;
                }

                if (!string.IsNullOrEmpty(wanted) && tree != null && tree[wanted] != null)
                {
                    view.HandbookNodeIdChangedHandler(wanted, true);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        internal static void Shutdown()
        {
            _view = null;
            _pending = false;
            _pendingId = null;
        }
    }
}
