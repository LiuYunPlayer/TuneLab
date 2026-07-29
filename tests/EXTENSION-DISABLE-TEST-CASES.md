# 扩展启停（disable / enable）测试用例

覆盖新增的**两级启停**——包级（详情窗 header）与条目级（详情窗各 tab）——的存取、
加载期效果、下游联动（routing / 扩展设置 / 能力枚举）、跨视图同步，以及 agent 的读面与写面
（`list_extensions` 的结局注记 + `set_extension_enabled`）。

只测本切片。扩展加载、routing 选择本身、introduction 渲染都已各有测试文档，不复测。

## 前置

**未改动任何 SDK 表面**，样例插件无需重建/重装——沿用已装的那批夹具即可。用到的包（侧栏显示名）：

| 包 | 形态 | 本文用它测什么 |
|---|---|---|
| **V1 Test Suite** | 一包两条目（format `tlsuite` + voice `TLSuiteVoice`），只有 voice 声明了扩展设置 | 条目级启停、只关坏的一半 |
| **V1 Test Multi-Suffix** | 一个 format 条目 + `suffixes: ["mtest","mtst"]` | 多身份条目一起开关 |
| **V1 Test Voice** | 单条目 voice（engine `TLTestVoiceV1`） | 包级启停 + 能力真的消失 |
| **V1 Engine Settings Demo** | 声明了 `IExtensionSettings` 的 voice | 禁用后齿轮/设置页条目消失 |
| **Voice Conflict A / B** | 两包提供同一 voice 身份 | 禁用一方后冲突行消失 |
| **Legacy Test Format** | legacy 包（无 manifest 条目） | 只有包级开关这一档 |
| **v1-bad-manifest** | manifest 解析失败（无条目、无 tab） | 开关在 header、正文无 tab |
| **V1 Resource Pack** | 资源类条目（无身份） | 不给条目开关 |
| **V1 SDK-Version Too High** | sdk-version 高于宿主 | 禁用门先于一切校验 |
| **V1 Missing-Assembly (negative)** / **V1 Platform Mismatch** | 条目 Failed / Skipped | 逐条目结局注记（非启停，但同一处输出） |

多处用到「改完要重启」，为省时间可把同一轮里的多个开关一次拨完再重启。

## 1. 包级开关在详情窗 header + 卡片只展示状态

1. 开扩展侧栏，找到 **V1 Test Voice** 卡片。
   **期望**：卡片上**没有任何开关**（也没有齿轮）——卡片只有名/版本/作者/徽标/卸载键。
   卡片就那么点高，右栏已有版本徽标与卸载键；开关挨着卸载会"邀请误点"，摆到中间又让右栏变三层堆叠。
2. 点卡片打开详情窗 → **期望** header 右上角有包级开关，当前为**开**，旁边写着 **已启用**；
   悬浮提示为「启用或禁用该扩展。重启 TuneLab 后生效。」
3. 关掉它。**期望**：开关变关、字样变 **已禁用**、旁边出现「需重启」；**侧栏卡片底行同时亮起蓝色「需重启」徽标**
   （卡片列表里唯一能看出"这个包被改过、还没生效"的地方）。
4. 卡片的状态徽标此刻**不变**（本次运行它确实还加载着，谎称已生效才是错的）。
5. 再拨回开 → **期望**两处「需重启」同时消失（存的选择又与本次运行一致了）。
6. 开关的 pill：开=高亮色、关=灰。两态只差位置的话太容易读反，故必须换色。

## 2. 重启后真的不加载

1. 关掉 **V1 Test Voice** 的包级开关，重启 TuneLab。
2. **期望**卡片：状态徽标为灰色 **已禁用**；**类别徽标（Voice）仍在**——它不是故障，那个包依然是个 voice 插件；「需重启」徽标已消失。
3. **期望**：右键 part →「设置音源」里**没有** `TLTestVoiceV1` 的任何声库；agent 的 `list_sound_sources` 也列不到该引擎。
4. 打开详情窗 → **期望**仍有它那一个 tab（introduction 照常渲染）——那正是把它重新打开的地方。
5. 重新启用 + 重启 → 一切恢复。

## 3. 条目级：一包多能力，只关坏的那个

1. 开 **V1 Test Suite** 详情窗 → 切到 **Suite Voice** tab。
2. **期望** tab 条右端从左到右是：条目开关、Settings 齿轮、Open in External Editor。
3. 关掉该开关（**只关这个条目**），切到 **Suite Format (Import)** tab → **期望**它的开关仍是开。（`.tlsuite` 的导入与导出现在是两个条目、两个 tab。）
4. 重启后再看该包：
   - 卡片状态徽标 = **部分**（一个条目起来了、一个被关），tooltip 里能读到 `voice 'TLSuiteVoice': disabled by the user`。
   - `.tlsuite` 文件仍能导入导出；`TLSuiteVoice` 声库在「设置音源」里**已消失**。
   - Suite Voice 那一页的 **Settings 齿轮不见了**——它这次没注册、没有实例可配置（重新启用并重启后齿轮回来）。设置窗「扩展」页里同样少了这一条。
5. 把两个条目都关掉并重启 → **期望**卡片状态徽标是 **已禁用**（不是"部分"、更不是"失败"）。

## 4. 整包关掉时，条目开关锁死

1. 在 **V1 Test Suite** 详情窗 header 把**包级**开关关掉（不重启）。
2. **期望**：当前 tab 的条目开关**就地**变成关且**点不动**，悬浮提示为「整个扩展已被禁用，其中的单个能力无法单独启用。」；切到另一个 tab 也是同样。
3. 把包级开关拨回开 → **期望**条目开关立即恢复可用，并回到各自存下来的状态（第 3 节关掉的那个仍是关）。

## 5. 多身份条目一起开关

1. 开 **V1 Test Multi-Suffix** 详情窗（一个 tab，页内列 `.mtest  .mtst`）。
2. 关掉该条目的开关并重启。
3. **期望**：两个后缀**同时**失效（`x.mtest` 与 `x.mtst` 都不再能导入）——它们共用同一份实现，本就一起开关。
4. 查 `%AppData%/TuneLab/Configs/ExtensionActivation.json` → **期望**该包名下是**两条**记录：
   `"com.tunelab.test.v1multisuffix": ["format:mtest", "format:mtst"]`。
   （逐身份各记一条、判定时任一命中即算禁用：作者日后增删后缀不会让用户的禁用悄悄失效。）
5. 重新启用 → **期望**两条一起被移除，且该包的键整个消失（不留空数组）。

## 6. legacy 包只有包级这一档

1. 打开 **Legacy Test Format** 的详情窗 → **期望** header 上同样有包级开关（legacy 没有 manifest 条目，这是唯一能关它的粒度，也正是"老插件老崩又不想卸"最常见的场景）。
2. 关掉并重启 → **期望**状态徽标 **已禁用**；打开详情窗 → 仍是"legacy 无文档"那段说明、无 tab（本来就没有条目）；`.tllegacy` 之类的格式不再可用。

## 7. 无条目的包：开关在 header，正文无 tab

1. 打开 **v1-bad-manifest**（manifest 解析失败）的详情窗。
2. **期望**：header 有包级开关；正文**没有** tab 条（无条目可分）；关掉并重启后状态徽标从 **失败** 变成 **已禁用**——用户就是用它来让一个坏包彻底闭嘴。

## 8. 资源类条目不给条目开关

1. 打开 **V1 Resource Pack** 详情窗。
2. **期望**：header 有包级开关；那一页 **没有**条目开关——资源条目没有身份、无法成键，给一个点了没效果的控件才是错的。整包关掉即可。

## 9. 禁用门先于一切校验

1. **V1 SDK-Version Too High** 当前状态徽标应为 **跳过**，tooltip 写着 `Requires SDK …`。
2. 关掉它的包级开关并重启。
3. **期望**：状态徽标变成 **已禁用**，**不再**显示 SDK 版本错误——它压根没被尝试，报"SDK 不兼容"是误导。

## 10. 下游联动（不需要在每处各写一遍过滤）

1. **routing**：**Voice Conflict A** 与 **B** 争用同一 voice 身份，设置窗「Extension Routing」里应有一行。关掉 A 并重启 → **期望**那一行**整行消失**（只剩一个提供者，无从冲突），且该身份由 B 提供。
2. **扩展设置**：关掉 **V1 Engine Settings Demo** 并重启 → **期望**设置窗「扩展」页里它不见了；agent 的 `list_extension_settings` 也列不到。
3. **能力枚举**：上述任一被禁的 voice/format 都不出现在 `list_sound_sources` / 导入导出的格式列表里。

## 11. 详情窗 → 卡片的同步

开关只有一处（详情窗），故不存在两个开关互相同步的问题；要验的是卡片上的提示跟得上。

1. 详情窗里拨**包级**开关 → **期望**该包卡片的「需重启」徽标立即亮/灭。
2. 拨**条目级**开关 → **期望**卡片的「需重启」徽标同样会亮（该提示比对的是整包的存值与运行态，逐条目也算在内）。
3. 关掉详情窗再重开 → **期望**开关停在刚才拨到的位置（读的是落盘的选择，不是运行态）。

## 12. 持久化

1. 存在**独立文件** `%AppData%/TuneLab/Configs/ExtensionActivation.json`（**不在 settings.json 里**——启停在设置窗没有 UI，属交互内的宿主记忆，同 `ParameterPins.json`）。形状：`packageId → 被禁 entryKey 列表`，整包为 `["*"]`。
2. **外层键必须是 packageId**：同时装了 **Voice Conflict A** 与 **B**（两包同一身份），只关 A → **期望**只有 A 的键落盘、重启后只有 A 被关，B 照常工作。
3. 关掉应用再打开 → 选择保持；settings.json 的 `ExtensionRouting` 不受影响、也不该出现任何启停字段（两根轴各存各的）。
4. **卸载会带走它的记录**：禁用 **V1 Test Voice** → 卸载它（重启完成卸载）→ **期望**下次启动后文件里不再有 `com.tunelab.test.v1voice` 这个键（加载完成后按"当前已装包全集"清理指向不存在的包的整条记录）。
   把它重新装回来 → **期望默认是启用的**，而不是静默地装完就是关的。手工删掉扩展目录、或手工往文件里写一个不存在的 packageId，也应在下次启动后被清掉。
5. **文件坏了不该把用户挡在门外**：把该 JSON 改成非法内容 → **期望**启动时日志记一条错误、当作"什么都没禁"（全部插件照常加载），而不是崩溃或全禁。

## 13. agent 读面：`list_extensions` 的结局注记

1. 让 agent 调 `list_extensions`。
2. **期望**（整包被禁的包）：包行 `status=Disabled`，其下一行明说 *DISABLED by the user — installed but switched off, so NONE of its capabilities exist in this session*，并指向侧栏或 `set_extension_enabled`；每个 `provides` 行也带 `[NOT AVAILABLE: the whole package is disabled]`。
3. **期望**（只关了一个条目的 **V1 Test Suite**）：被关那行带 `[DISABLED by the user: this capability alone is switched off (the rest of the package still works)…]`，另一行**干净无注记**。
4. **期望**（**V1 Missing-Assembly**）：那一行带 `[FAILED to load: … assembly … not found — this capability does NOT exist in this session]`；**V1 Platform Mismatch** 带 `[SKIPPED: … platform not available …]`。
5. 追问 agent「我装的 X 能用吗」→ **期望**它据此回答"装了但被关了/坏了"，而不是只看 `kinds:` 那行说"装好了应该能用"。

## 14. agent 写面：`set_extension_enabled`

授权档位设为 **Confirm** 再测（`Auto` 下不弹卡片）。

1. **整包**：让 agent 关掉 V1 Test Voice → **期望**卡片文案「Agent 想禁用扩展「V1 Test Voice」（重启后生效）。」；同意后回报里带"重启后生效"以及"在那之前它这一轮仍然可用"。侧栏卡片**不会**立刻亮「需重启」（那是 UI 拨动时算的），但**重开详情窗**应看到开关已是关——agent 与 UI 写的是同一份存储。
2. **单个能力**：`capability: "voice:TLSuiteVoice"` → **期望**文案「Agent 想禁用「V1 Test Suite」的「voice:TLSuiteVoice」能力（重启后生效）。」
3. **拒绝**：点「拒绝」→ **期望**什么都没落盘，回报里引导用户去扩展侧栏自己拨。
4. **no-op**：对已经是该状态的对象再来一次 → **期望**直接回"已经是…，什么都没做"、**不弹卡片**。
5. **整包已关时的单能力**：先整包关掉，再让 agent 单独启用其中一个能力 → **期望**报"整包被禁、请先启用整包"，**不写**一个看不出效果的选择。
6. **错参**：不存在的 `packageId` → 报错并列出已装 id；不存在的 `capability` → 报错并列出该包提供了什么；对资源条目 → 提示改用整包。
7. **多身份**：`capability: "mtest"`（裸身份）→ **期望**命中那个多后缀条目，落盘两条键（同第 5 节）。

## 15. 整包禁用真的省启动时间（弱验证）

1. 把若干重包（如 **V1 Test Suite**、**V1 Engine Settings Demo**）整包关掉后重启。
2. **期望**：日志里没有它们的程序集加载/注册记录——整包禁用是在 ALC 建立之前短路的，这是"加载慢又不常用"那个诉求真正被满足的一档。
   （只关部分条目时**不省**加载时间：程序集是条目级声明的，只有全禁或包级禁才跳得过。）
