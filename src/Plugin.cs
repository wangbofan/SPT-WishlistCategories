using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace SPTWishlistCategories
{
    /// <summary>
    /// 让跳蚤市场"愿望单(Wishlist)"侧栏像跳蚤市场分类树一样按物品种类分组的客户端补丁。
    /// </summary>
    [BepInPlugin(Guid, "SPT-WishlistCategories", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.yg.sptwishlistcategories";
        public const string Name = "SPT-WishlistCategories";
        public const string Version = "1.0.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> MaxDepth;
        internal static ConfigEntry<bool> ExpandGroups;
        internal static ConfigEntry<bool> SortByHandbookOrder;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Enabled = Config.Bind("1. 常规", "启用", true,
                "总开关。关闭后愿望单侧栏恢复为官方的扁平列表（重启/重新打开跳蚤市场后生效）。");
            MaxDepth = Config.Bind("1. 常规", "分类层数", 0,
                new ConfigDescription(
                    "最多显示几层分类（0 = 与跳蚤市场一样，显示完整层级；1 = 只按一级分类分；2 = 一级 + 二级）。",
                    new AcceptableValueRange<int>(0, 6)));
            ExpandGroups = Config.Bind("1. 常规", "自动展开分类", true,
                "打开愿望单时自动展开所有非空分类，不需要逐层点三角。");
            SortByHandbookOrder = Config.Bind("1. 常规", "按手册顺序排序", true,
                "true = 分类按手册(Handbook)里的原始顺序排序（与跳蚤市场分类树一致）；false = 按名称排序。");

            _harmony = new Harmony(Guid);
            HarmonyPatcher.ApplyAll(_harmony);

            Log.LogInfo($"{Name} {Version} loaded (harmony id {Guid}).");
        }

        private void Update()
        {
            WishlistRefresh.Tick();
        }

        private void OnDestroy()
        {
            try
            {
                _harmony?.UnpatchSelf();
            }
            catch (Exception e)
            {
                Log?.LogWarning(e);
            }
        }
    }
}
