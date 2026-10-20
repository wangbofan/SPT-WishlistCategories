# SPT-WishlistCategories

给 **SPT 4.1.x** 的跳蚤市场「愿望单（Wishlist）」侧栏加上和跳蚤市场一模一样的**按物品种类分组**功能。

官方愿望单是一大条扁平列表；这个 mod 把它变成 `武器 → 突击步枪 → 具体枪` 这样可展开的分类树，层级、图标、名字、排序都跟左侧跳蚤市场分类树一致。

- ✅ 纯客户端插件（BepInEx 5 / Harmony），**不改存档、不改服务端**
- ✅ 适用 **SPT 4.1.x**
- ✅ 12 个补丁全部按精确签名解析，签名变了会跳过并在日志里说明，不会崩游戏

---

## 安装

1. 下载 `SPT-WishlistCategories-x.y.z.zip`。
2. 把 `BepInEx\plugins\SPT-WishlistCategories.dll` 放进你的 `SPT目录\BepInEx\plugins\`。
3. 启动游戏 → 跳蚤市场 → **Wishlist** 标签 → 左侧就是分类树。

卸载：删掉这个 dll 即可，不留任何痕迹。

> 不需要往 `SPT_Runtime\user\mods` 放任何东西。

---

## 效果细节

- **物品行保持官方原样**：图标、名字、报价数、右键菜单、拖拽行为都不变，只是被收进了对应的分类文件夹。
- **报价数**：物品行是它自己的报价数，分类行是它下面所有物品的合计。
- **分类可展开/收起**，默认打开愿望单时自动全部展开（可在配置里关）。
- **加入 / 移出愿望单即时生效**：在背包右键、Handbook、跳蚤市场任意位置增删愿望单，侧栏都会自动重建，不用切标签页。
- **空分类自动消失**：最后一个物品被移走后，那层分类一起清掉。
- 官方那 5 个用途分组（Trading / Quests / Hideout / Equipment / Other）**完全不受影响** —— 那是「愿望单用途」数据，本 mod 只改跳蚤市场侧栏的显示结构，`WishlistManager` 里的用途一个字节都没动。

---

## 配置

配置文件：`BepInEx\config\com.yg.sptwishlistcategories.cfg`（也可在 ConfigurationManager 里改）。

| 项 | 默认 | 说明 |
| --- | --- | --- |
| `启用` | `true` | 总开关。关掉后恢复官方扁平列表（重新打开跳蚤市场生效）。 |
| `分类层数` | `0` | `0` = 与跳蚤市场一样显示完整层级；`1` = 只按一级分类分（武器 / 弹药 / 装备…）；`2` = 一级 + 二级。 |
| `自动展开分类` | `true` | 打开愿望单时自动展开所有非空分类。 |
| `按手册顺序排序` | `true` | 分类之间按 Handbook 原始 `Order` 排序，与跳蚤市场分类树一致。 |

---

## 实现原理

全部是 Harmony 客户端补丁，作用在 `RagFair.Wishlist` 这棵 `HandbookNodes` 树上。数据源是 `WishlistManager.GetWishlist()`，分类结构从 `Handbook.StructuredItems` 里物品的**祖先链**推导，所以永远和游戏本体的物品种类一致。

| # | 目标方法 | 作用 |
| --- | --- | --- |
| 1 | `RagFair.UpdateWishlist` (postfix) | 官方产出的扁平 dummy 列表 → 按祖先链重组成分类树（分类节点用 `CreateDummy()` 克隆，保留 `Data.Order` / `Icon` / `Name`） |
| 2 | `HandbookNode.CanShowNodeView` (prefix) | 愿望单视图下 `Count == 0` 的节点会被隐藏，分类行必须常显 |
| 3 | `CategoryView.OffersCountUpdatedHandler` (postfix) | `Count == 0` 时官方会隐藏展开三角，分类行强制常显 |
| 4 | `CategoryView.Show` (postfix) | 按配置自动 `OpenCategory()` |
| 5 | `OfferViewList.HandbookNodeIdChangedHandler` (prefix) | 点分类只展开，不把分类 id 交给 `SetFilterRule` |
| 6 | `OfferViewList.UpdateListType` (postfix) | 官方切到 Wishlist 时拿「根条目」当选中项，分组后那是分类；改选一个有报价的物品 |
| 7 | `RagFair.AddToWishList` (prefix) | 自己挂到分类下，静默写字典（不触发 `ItemAdded`，否则侧栏顶层会多一行） |
| 8 | `RagFair.RemoveFromWishList` (prefix) | 摘掉物品并清理空分类 |
| 9 | `RagFair.UpdateNodes` (postfix) | 后端 categories 计数命中分类 id 会把整类报价数加进来，改为按子节点求和重算 |
| 10 | `RagFair.ClearWishListCache` (postfix) | 它会把 `Wishlist.Last()` 写进 `LastSelectedIds`，分组后那是分类 id，改写为分类下第一个物品 |
| 11 | `OfferViewList.ManageOffers` (postfix) | 记录当前愿望单视图，供增删后延迟重建 |
| 12 | `CategoriesComparer.Compare` (prefix) | 分类恒在物品之前；分类之间按 `Data.Order` 降序（官方对 dummy 节点会退化成按名字排） |

几个关键点：

- **点分类绝不能进 `SetFilterRule`**：跳蚤市场是按 `handbookId` 拉报价的，`RagFair.IsOfferAcceptable` 并不校验「是否在愿望单里」，分类 id 进去会把整类报价全拉出来。
- **`HandbookNodes` 的根字典只放根节点，`_cachedNodes` 索引放全部节点**：`RagfairCategoriesPanel` 按根字典逐行渲染，嵌套子项写进根字典会多画一行；而 `nodes[id]` 读的是 `_cachedNodes`，所以索引里必须有全部节点。
- **增删走静默写**：`BindableDictionary.ItemAdded` 会让 `RagfairCategoriesPanel` 在顶层再插一行，所以直接写 `_dictionary` / `_cachedNodes`，然后下一帧重建视图。

---

## 从源码构建

需要 .NET SDK 8+（工程本身是 `netstandard2.1`）。

```powershell
# 1) 改 csproj 里的游戏路径
#    <TarkovDir>E:\TKF</TarkovDir>
dotnet build SPT-WishlistCategories.csproj -c Release
# 产物：bin\Release\SPT-WishlistCategories.dll
```

工程引用（全部 `Private=false`，不会被打包进 dll）：`BepInEx.dll`、`0Harmony.dll`、`Assembly-CSharp.dll`、`UnityEngine*.dll`、`Sirenix.Serialization.dll`、`Sirenix.Utilities.dll`。

目录结构：

```
SPT-WishlistCategories/
├─ SPT-WishlistCategories.csproj
├─ src/
│  ├─ Plugin.cs          # BepInEx 入口 + 配置项
│  ├─ PatchBase.cs       # ModPatch 基类（精确签名解析）+ 补丁注册表
│  ├─ WishlistTree.cs    # 扁平列表 → 分类树的重建/增删/计数
│  ├─ WishlistRefresh.cs # 增删后的延迟视图刷新
│  └─ Patches.cs         # 12 个 Harmony 补丁
└─ README.md
```

---

## 兼容性

- 与只改 UI 的客户端 mod（UIFixes、TierColors 等）不冲突。
- 与同样改 `RagFair.UpdateWishlist` 或愿望单侧栏的 mod 会互相覆盖，只能二选一。
- 日志里可以确认每个补丁是否命中：`[OK] RagFair.UpdateWishlist() <- UpdateWishlistPatch`；某个 SPT 小版本改签名后会打印 `[xxx] 找不到目标方法，跳过`，其余补丁照常工作。

出问题请附上 `BepInEx\LogOutput.log` 里所有含 `WishlistCategories` 的行。

## 致谢

- SPT 团队与 BepInEx / HarmonyX
- 感谢 Tyfon.UIFixes 等开源 mod 提供的客户端补丁范式参考
