using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace SPTWishlistCategories
{
    /// <summary>
    /// 极简补丁基类：子类实现 TargetMethod()，可选实现 static Prefix/Postfix/Transpiler。
    /// </summary>
    internal abstract class ModPatch
    {
        protected abstract MethodBase TargetMethod();

        /// <summary>先按精确签名找，找不到再按名字退化，最大化跨小版本兼容。</summary>
        protected static MethodBase Resolve(Type type, string name, params Type[] parameters)
        {
            bool hasParams = parameters != null && parameters.Length > 0;

            MethodBase found = hasParams
                ? (MethodBase)AccessTools.DeclaredMethod(type, name, parameters)
                : AccessTools.DeclaredMethod(type, name);

            if (found == null)
            {
                found = hasParams
                    ? (MethodBase)AccessTools.Method(type, name, parameters)
                    : AccessTools.Method(type, name);
            }

            if (found == null)
            {
                found = hasParams
                    ? (MethodBase)AccessTools.Method(type, name)
                    : null;
                if (found != null)
                {
                    Plugin.Log.LogWarning($"{type.Name}.{name}: 精确签名未命中，退化为按名字匹配 {found}。");
                }
            }

            return found;
        }

        internal bool TryApply(Harmony harmony)
        {
            string name = GetType().Name;
            MethodBase target;
            try
            {
                target = TargetMethod();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[{name}] 解析目标方法时异常: {e}");
                return false;
            }

            if (target == null)
            {
                Plugin.Log.LogError($"[{name}] 找不到目标方法，跳过。");
                return false;
            }

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            MethodInfo prefix = GetType().GetMethod("Prefix", flags);
            MethodInfo postfix = GetType().GetMethod("Postfix", flags);
            MethodInfo transpiler = GetType().GetMethod("Transpiler", flags);

            if (prefix == null && postfix == null && transpiler == null)
            {
                Plugin.Log.LogError($"[{name}] 没有 Prefix/Postfix/Transpiler 方法，跳过。");
                return false;
            }

            try
            {
                harmony.Patch(target,
                    prefix == null ? null : new HarmonyMethod(prefix),
                    postfix == null ? null : new HarmonyMethod(postfix),
                    transpiler == null ? null : new HarmonyMethod(transpiler));
                string signature = string.Join(", ", target.GetParameters().Select(p => p.ParameterType.Name));
                Plugin.Log.LogInfo($"[OK] {target.DeclaringType?.Name}.{target.Name}({signature}) <- {name}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[{name}] 打补丁失败: {e}");
                return false;
            }
        }
    }

    internal static class HarmonyPatcher
    {
        internal static void ApplyAll(Harmony harmony)
        {
            ModPatch[] patches =
            {
                new Patches.UpdateWishlistPatch(),
                new Patches.CanShowNodeViewPatch(),
                new Patches.CategoryViewShowPatch(),
                new Patches.CategoryViewCountPatch(),
                new Patches.NodeIdChangedPatch(),
                new Patches.UpdateListTypePatch(),
                new Patches.AddToWishListPatch(),
                new Patches.RemoveFromWishListPatch(),
                new Patches.UpdateNodesPatch(),
                new Patches.ClearWishListCachePatch(),
                new Patches.ManageOffersPatch(),
                new Patches.CategoriesComparerPatch(),
            };

            int ok = 0;
            foreach (ModPatch patch in patches)
            {
                if (patch.TryApply(harmony))
                {
                    ok++;
                }
            }
            Plugin.Log.LogInfo($"补丁完成 {ok}/{patches.Length}。");
        }
    }
}
