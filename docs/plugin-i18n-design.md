# 插件多语言（i18n）设计落地说明

> 自包含设计说明，描述插件文案如何本地化。下半部「实现现状」记录已落地的部分。

## 1. 目标与取向

插件暴露给用户的文案（属性标题、ComboBox 选项、自动化轨名、声库名/简介、包名/简介）原本全硬编码、零翻译。

**取向：插件侧自译。** 插件经既有的方法/属性接口返回**已按当前语言本地化的成品串**，宿主原样渲染。宿主**不**持有、不注册任何插件翻译表。

选择理由：

- **解耦**：插件不依赖宿主的翻译实现。宿主将来如何演进自己的 i18n 都不波及插件。反例（被否决的"宿主侧覆盖层"）会把"插件翻译文件格式 + 注册约定"钉成宿主 i18n 实现的公共契约，迭代受限。
- **统一路径**：静态串（打包期已知）与动态串（运行时获取，如云端拉取的声库）走同一条路径——都由插件按当前语言产出。无静态/动态两套机制。
- **宿主最简**：无命名空间、无注册 API、无保留根、无渲染穿参、无跨插件撞车（不存在共享表，自然无冲突）。

翻译方案对插件**自由**：可用 .resx、自带字典、第三方库，或将来可选发布的目录助手——SDK 的 ABI 地板**不强制任何方案**。

## 2. 三块构成

| 块 | 来源 | 本地化方式 |
|---|---|---|
| A 引擎级串（属性标题 / 自动化名 / ComboBox 选项 / 声库名·简介） | 插件代码经方法/属性返回 | **插件侧自译**：按 `TuneLabContext.Global.Language` 产出成品串 |
| B 包元数据（name / description） | 宿主直接读 `description.json`（早于任何插件代码） | **宿主侧**：description.json 内 `localizations` 字段，宿主按语言挑、回退基础值 |
| C 当前语言 + 日志器 | — | 静态全局 `TuneLabContext.Global`（`Language` + `GetLogger()`） |

## 3. 宿主上下文访问点：静态全局 `TuneLabContext.Global`

插件要按当前语言自译，前提是能读到当前语言。采**静态全局服务定位器**（接口 + 静态点在 `TuneLab.SDK`）：

```csharp
public interface ITuneLabContext
{
    string Language { get; }       // 当前 culture，如 "zh-CN"；全局量，切语言靠重启生效
    ILogger GetLogger();              // 前缀由宿主按调用者 ALC 自动判定
    // 模块级子标签不进协议（消息格式问题，插件自己在消息里拼）；原 GetLogger(subName) 重载已删。
}
public static class TuneLabContext { public static ITuneLabContext Global { get; set; } /* = NullContext */ }
```

- **为何静态可行**：`PluginLoadContext` 把 `TuneLab.Foundation` / `TuneLab.SDK.*` 当共享契约（`Load` 返回 null 落 Default ALC，全程一份、跨边界类型标识相等），故 TuneLab.SDK 里的静态对宿主与**全部插件是同一实例**。（此前"ALC 下静态每-ALC 一份"的顾虑只对插件私有程序集成立、对共享契约不成立。）
- **注入**：宿主启动早期（插件加载之前）`TuneLabContext.Global = new TuneLabContextGlobal()`；赋值前/测试中为 `NullContext`（语言空串、no-op 日志器），插件读 `Global` 永不为 null。
- **日志器自动归属**：`GetLogger()` 由宿主实现反查调用者程序集所属 ALC 的 `Name`（= 插件包目录名，`PluginLoadContext` 构造时设定、插件改不了）作前缀，转发进宿主既有 sink；内置/Default ALC 归 `"host"`。无需插件自报名字。此举也**接通了此前悬空的 `ILogger`**。

## 4. 引擎级串：插件侧自译

- 插件在**能读到 `Language` 的时机**构建 config——在 `Init` 内、或在暴露 config 的 getter 里懒构建（不再 `static readonly` 纯静态单语）。
- `IControllerConfig.DisplayText`（属性标题/自动化名）、`ComboBoxOption.DisplayText`（选项）、`VoiceSourceInfo.Name/Description`（声库）= 按 `TuneLabContext.Global.Language` 取的本地化串。
- 未本地化（插件返回单语）→ 宿主原样显示，无回退逻辑（原串即结果）。

## 5. 显示/标识分离：`IControllerConfig.DisplayText`

- `IControllerConfig` 加可选 `string? DisplayText`；各 config 实现；`AutomationConfig : SliderConfig` 经继承白拿，故**删除了 `AutomationConfig.Name`**（显示名走 `DisplayText`）。
- map 容器维持 `IReadOnlyOrderedMap<string, …>`（key 仍是稳定 id，做数据绑定/路由）；**不引入** key 包装结构体。
- 渲染：标题取 `config.DisplayText ?? key`（**原样显示，不经宿主查表**），数据绑定/路由取 `key`。

## 6. 包元数据本地化（宿主侧）

manifest 是宿主在任何插件代码之前直接读的数据，无法靠插件自译，用 description.json 内本地化字段解决：

```json
{
  "name": "My SVC Pack",
  "description": "A demo effect bundle",
  "localizations": { "zh-CN": { "name": "我的换声包", "description": "示例效果器合集" } }
}
```

宿主加载按当前语言挑 `localizations[lang]`，缺则回退基础 `name`/`description`（`ExtensionDescription.LocalizedName/LocalizedDescription`）。纯数据、自包含、不依赖宿主 i18n 内部。

## 7. 动态内容

与静态同一条路径：插件按 `Language` 产出（含云端拉取的声库名/简介、动态 ComboBox 选项等），宿主原样显示。无特殊机制、无临时文件。

## 8. 切语言 = 重启生效

`Language` 实时反映 `TranslationManager.CurrentLanguage`，但插件 config 在构建时读一次；运行中切语言不强制刷新插件文案（与宿主既有行为一致），重启后整体生效。

## 9. 实现现状

已落地：
- `TuneLab.SDK/Environment/`：`ITuneLabContext`（`Language` + `GetLogger()`）、静态点 `TuneLabContext.Global`、`NullContext`、`ILogger`（命名空间统一 `TuneLab.SDK`）。
- 宿主：`TuneLabContextGlobal`（`Language` 取 `TranslationManager.CurrentLanguage`；`GetLogger` 按调用者 ALC 自动前缀、转发进 Hosting.Foundation 的 `Log`）；`Program` 启动期注入 `TuneLabContext.Global`。**ILogger 随之接通。**
- TuneLab.SDK：`IControllerConfig.DisplayText`；删 `AutomationConfig.Name`；config 家族先期已改对象初始化器风格（init + required）。
- 宿主渲染处改用 `DisplayText ?? key`（`PropertyObjectController` 各控件标题、`AutomationDefaultsController`、`ParameterTabBar`）。
- 宿主：`ExtensionDescription.localizations` + `LocalizedName/LocalizedDescription`；`ExtensionManager` 加载按当前语言挑 manifest，流入 sidebar。

- 测试夹具：独立 `tests/plugins/V1.I18N`（voice 引擎，插件侧自译演示 + description.json `localizations`），用例见 `tests/PLUGIN-I18N-TEST-CASES.md`。

待办：
- 各插件类型开发文档（effect/voice/format）补"如何自译（在 Init / getter 里按 `TuneLabContext.Global.Language` 构建 config）+ manifest 本地化字段"用法 + 面向 AI 的参考。
- （可选）把翻译方案做成独立目录助手包发布，供插件自愿使用，不进 ABI 地板。

## 10. 首版纳入的串

包 name/description（manifest 本地化字段）；属性标题、ComboBox 选项、自动化轨名、声库 Name/Description（插件自译）。`author` 不译（人名/团队名）。
