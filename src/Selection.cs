using System.Collections.Generic;
using EFT.UI.Ragfair;

namespace SPTWishlistCategories
{
    /// <summary>
    /// 独占高亮。
    /// 官方取消上一个选中节点的唯一位置是 OfferViewList.HandleNodeSelection 里的
    /// _selectedItemPanel.DeselectView()，而这条链路只有"点击后成功拉到报价"才会走到。
    /// 本 mod 拦下了分类行的选中（不能把分类 id 交给 SetFilterRule），于是那条链路不执行，
    /// 结果每点一个分类就多亮一行。这里在任意视图被选中时，把同一面板里其它选中态全部清掉。
    /// </summary>
    internal static class WishlistSelection
    {
        private static readonly HashSet<NodeBaseView> Visited = new HashSet<NodeBaseView>();

        internal static void DeselectOthers(NodeBaseView keep)
        {
            if (keep == null)
            {
                return;
            }

            Dictionary<string, NodeBaseView> registry = null;
            try
            {
                registry = keep.ViewNodes;
            }
            catch
            {
                // 视图正在销毁
                return;
            }

            if (registry == null)
            {
                return;
            }

            Visited.Clear();
            Visited.Add(keep);

            try
            {
                foreach (NodeBaseView view in registry.Values)
                {
                    Walk(view, keep);
                }
            }
            catch
            {
                // 遍历途中面板被重建，放弃本轮清理即可
            }
        }

        private static void Walk(NodeBaseView view, NodeBaseView keep)
        {
            if (view == null || !Visited.Add(view))
            {
                return;
            }

            if (!ReferenceEquals(view, keep))
            {
                Deselect(view);
            }

            CategoryView categoryView = view as CategoryView;
            if (categoryView == null || categoryView._views == null)
            {
                return;
            }

            // 展开过的子行不一定都登记在 ViewNodes 里，顺着视图树再走一遍。
            List<NodeBaseView> children = categoryView._views;
            for (int i = 0; i < children.Count; i++)
            {
                Walk(children[i], keep);
            }
        }

        private static void Deselect(NodeBaseView view)
        {
            try
            {
                if (view.Selected)
                {
                    view.DeselectView();
                }
            }
            catch
            {
                // 已销毁 / Node 为空，跳过
            }
        }
    }
}
