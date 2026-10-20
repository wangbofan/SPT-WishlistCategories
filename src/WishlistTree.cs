using System;
using System.Collections.Generic;
using EFT.HandBook;
using EFT.UI.Ragfair;

namespace SPTWishlistCategories
{
    /// <summary>
    /// 把 RagFair.Wishlist 这个"扁平的愿望单物品列表"重建成"手册分类树 + 物品叶"的结构。
    /// 结构完全由 _handbook.StructuredItems 里物品的祖先链推导，不碰服务端数据。
    /// </summary>
    internal static class WishlistTree
    {
        /// <summary>本 mod 生成的分类节点（按引用登记，避免影响官方树）。</summary>
        private static readonly HashSet<HandbookNode> Groups = new HashSet<HandbookNode>();

        private static readonly Dictionary<string, HandbookNode> GroupsById = new Dictionary<string, HandbookNode>();

        internal static bool IsGroup(HandbookNode node)
        {
            return node != null && Groups.Contains(node);
        }

        internal static void Reset()
        {
            Groups.Clear();
            GroupsById.Clear();
        }

        // ────────────────────────────────────────────────────────────────
        // 构建
        // ────────────────────────────────────────────────────────────────

        internal static void Rebuild(RagFair ragfair)
        {
            Reset();

            if (ragfair == null || !Plugin.Enabled.Value)
            {
                return;
            }

            Handbook handbook = ragfair._handbook;
            HandbookNodes flat = ragfair._wishlist;
            if (handbook == null || flat == null)
            {
                return;
            }

            // 官方 UpdateWishlist 产出的根条目 = 被愿望单收录且已检视的物品（dummy）。
            List<HandbookNode> items = new List<HandbookNode>(flat.Count);
            foreach (KeyValuePair<string, HandbookNode> pair in flat)
            {
                HandbookNode node = pair.Value;
                if (node == null || node.Data == null)
                {
                    continue;
                }
                if (node.Data.Type == ENodeType.Category)
                {
                    continue;
                }
                items.Add(node);
            }

            if (items.Count == 0)
            {
                return;
            }

            Dictionary<string, HandbookNode> roots = new Dictionary<string, HandbookNode>(items.Count);
            foreach (HandbookNode item in items)
            {
                List<HandbookNode> chain = BuildChain(handbook, item.Data.Id);
                HandbookNode parent = EnsureGroups(roots, null, chain);
                Attach(roots, parent, item);
            }

            HandbookNodes built = new HandbookNodes(roots);
            ragfair._wishlist = built;

            RecountAndNotify(built.Values);
            Plugin.Log.LogDebug($"重建愿望单分类树: {items.Count} 个物品 / {Groups.Count} 个分类。");
        }

        /// <summary>取物品在手册里的分类祖先链（根 → 直接父分类），并按配置裁剪层数。</summary>
        private static List<HandbookNode> BuildChain(Handbook handbook, string itemId)
        {
            List<HandbookNode> chain = new List<HandbookNode>(4);

            HandbookNode source = null;
            try
            {
                source = handbook.StructuredItems[itemId];
            }
            catch
            {
                // 手册里没有这个 id（理论上不会发生），物品保持根级。
            }

            if (source == null)
            {
                return chain;
            }

            HandbookNode current = source.Parent;
            while (current != null)
            {
                if (current.Data != null &&
                    current.Data.Type == ENodeType.Category &&
                    !string.IsNullOrEmpty(current.Data.Id))
                {
                    chain.Add(current);
                }
                current = current.Parent;
            }
            chain.Reverse();

            int max = Plugin.MaxDepth.Value;
            if (max > 0 && chain.Count > max)
            {
                chain.RemoveRange(max, chain.Count - max);
            }

            return chain;
        }

        /// <summary>为分类链逐层查找/创建分类节点。live != null 时同步写入树的索引缓存（不触发事件）。</summary>
        private static HandbookNode EnsureGroups(Dictionary<string, HandbookNode> roots, HandbookNodes live, List<HandbookNode> chain)
        {
            HandbookNode parent = null;

            foreach (HandbookNode category in chain)
            {
                string id = category.Data.Id;
                if (!GroupsById.TryGetValue(id, out HandbookNode group))
                {
                    group = category.CreateDummy();
                    group.Count = 0;
                    group.New = false;
                    Groups.Add(group);
                    GroupsById[id] = group;

                    Attach(roots, parent, group);

                    if (live != null)
                    {
                        AddSilent(live, id, group);
                    }
                }
                parent = group;
            }

            return parent;
        }

        /// <summary>把节点挂到父节点下（parent == null 时作为根）。</summary>
        private static void Attach(Dictionary<string, HandbookNode> roots, HandbookNode parent, HandbookNode node)
        {
            if (node.Data == null)
            {
                return;
            }

            if (parent != null)
            {
                node.Parent = parent;
                node.Depth = parent.Depth + 1;
                node.Data.ParentId = parent.Data.Id;
                parent.ChildrenDict[node.Data.Id] = node;
                return;
            }

            node.Parent = null;
            node.Depth = 0;
            node.Data.ParentId = string.Empty;

            if (roots != null)
            {
                roots[node.Data.Id] = node;
            }
        }

        // ────────────────────────────────────────────────────────────────
        // 动态增删（不重建整棵树，避免 UI 闪烁）
        // ────────────────────────────────────────────────────────────────

        internal static bool AddItem(RagFair ragfair, string id)
        {
            if (ragfair == null || !Plugin.Enabled.Value || string.IsNullOrEmpty(id))
            {
                return false;
            }

            Handbook handbook = ragfair._handbook;
            HandbookNodes tree = ragfair._wishlist;
            if (handbook == null || tree == null)
            {
                return false;
            }

            HandbookNode source = handbook.StructuredItems[id];
            if (source == null)
            {
                return false;
            }

            if (tree[id] != null)
            {
                return false;
            }

            HandbookNode leaf = source.CreateDummy();
            List<HandbookNode> chain = BuildChain(handbook, id);
            HandbookNode parent = EnsureGroups(null, tree, chain);
            Attach(null, parent, leaf);
            AddSilent(tree, id, leaf);

            RecountAndNotify(tree.Values);
            return true;
        }

        internal static bool RemoveItem(RagFair ragfair, string id)
        {
            if (ragfair == null || !Plugin.Enabled.Value || string.IsNullOrEmpty(id))
            {
                return false;
            }

            HandbookNodes tree = ragfair._wishlist;
            if (tree == null)
            {
                return false;
            }

            HandbookNode node = tree[id];
            if (node == null)
            {
                return false;
            }

            HandbookNode parent = node.Parent;
            if (parent != null)
            {
                parent.ChildrenDict.Remove(node.Data.Id);
            }

            RemoveSilent(tree, id);

            // 向上清理空分类
            HandbookNode current = parent;
            while (current != null && IsGroup(current) && current.ChildrenDict.Count == 0)
            {
                HandbookNode up = current.Parent;
                if (up != null)
                {
                    up.ChildrenDict.Remove(current.Data.Id);
                }
                RemoveSilent(tree, current.Data.Id);
                Groups.Remove(current);
                GroupsById.Remove(current.Data.Id);
                current = up;
            }

            RecountAndNotify(tree.Values);
            return true;
        }

        // ────────────────────────────────────────────────────────────────
        // 计数
        // ────────────────────────────────────────────────────────────────

        /// <summary>分类的 Count = 子节点 Count 之和（物品的 Count 由官方 UpdateNodes 写入）。</summary>
        internal static void RecountAndNotify(IEnumerable<HandbookNode> roots)
        {
            List<HandbookNode> all = new List<HandbookNode>();
            foreach (HandbookNode root in roots)
            {
                Recount(root, all);
            }

            for (int i = 0; i < all.Count; i++)
            {
                all[i].CountUpdatedCall();
            }
        }

        private static int Recount(HandbookNode node, List<HandbookNode> all)
        {
            all.Add(node);

            int sum = 0;
            foreach (HandbookNode child in node.ChildrenDict.Values)
            {
                sum += Recount(child, all);
            }

            if (Groups.Contains(node))
            {
                node.Count = sum;
            }

            return node.Count;
        }

        // ────────────────────────────────────────────────────────────────
        // 查询辅助
        // ────────────────────────────────────────────────────────────────

        internal static bool IsCategoryIdInTree(HandbookNodes tree, string id)
        {
            if (tree == null || string.IsNullOrEmpty(id))
            {
                return false;
            }

            HandbookNode node = tree[id];
            return node != null && node.Data != null && node.Data.Type == ENodeType.Category;
        }

        internal static HandbookNode FirstItemUnder(HandbookNode root)
        {
            if (root == null)
            {
                return null;
            }

            if (root.Data != null && root.Data.Type == ENodeType.Item)
            {
                return root;
            }

            foreach (HandbookNode child in root.ChildrenDict.Values)
            {
                HandbookNode found = FirstItemUnder(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>挑一个物品叶：优先 Count &gt; 0（有报价）的，其次第一个。</summary>
        internal static HandbookNode PickItem(HandbookNodes tree, bool preferWithOffers)
        {
            if (tree == null)
            {
                return null;
            }

            HandbookNode first = null;
            foreach (HandbookNode root in tree.Values)
            {
                List<HandbookNode> all = new List<HandbookNode>(8);
                Collect(root, all);
                foreach (HandbookNode node in all)
                {
                    if (node.Data == null || node.Data.Type != ENodeType.Item)
                    {
                        continue;
                    }
                    if (preferWithOffers && node.Count > 0)
                    {
                        return node;
                    }
                    if (first == null)
                    {
                        first = node;
                    }
                }
            }

            return first;
        }

        internal static void Collect(HandbookNode node, List<HandbookNode> into)
        {
            into.Add(node);
            foreach (HandbookNode child in node.ChildrenDict.Values)
            {
                Collect(child, into);
            }
        }

        // ────────────────────────────────────────────────────────────────
        // 静默写字典（绕开 BindableDictionary 的 ItemAdded/ItemRemoved 事件）
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 静默写树：索引缓存(_cachedNodes)放全部节点，根字典(_dictionary)只放根，
        /// 这样侧栏不会为嵌套子项多画一行，同时 nodes[id] 仍能命中任意层级的节点。
        /// </summary>
        internal static void AddSilent(HandbookNodes tree, string id, HandbookNode node)
        {
            if (tree == null || string.IsNullOrEmpty(id) || node == null)
            {
                return;
            }

            if (tree._cachedNodes != null)
            {
                tree._cachedNodes[id] = node;
            }

            if (node.Parent == null)
            {
                tree._dictionary[id] = node;
            }
        }

        internal static void RemoveSilent(HandbookNodes tree, string id)
        {
            if (tree == null || string.IsNullOrEmpty(id))
            {
                return;
            }

            tree._dictionary.Remove(id);
            if (tree._cachedNodes != null)
            {
                tree._cachedNodes.Remove(id);
            }
        }
    }
}
