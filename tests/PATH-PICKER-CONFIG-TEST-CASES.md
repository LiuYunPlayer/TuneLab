# 路径选择 config（`PathPickerConfig`）· 测试用例

> 范围：新增的控件配置 `PathPickerConfig`——插件在任何 config 面（扩展设置 / note、part 属性 / 数组元素 / 脚本入参）
> 声明"这个字段是一条路径"，宿主渲染成**文本框 + 浏览按钮**并开系统文件/文件夹选择器。
> 只测这一个新控件：两种目标（文件 / 文件夹）、类型过滤、对话框标题、值写回与撤销、命令面/脚本面的措辞。
> 扩展设置的落盘、加密、重启回喂等链路已由 `PLUGIN-SETTINGS-TEST-CASES.md` 覆盖，这里只在 C 组顺带确认路径值走的是同一条路。

> **D / E 两组已自动化**：`pwsh tests/headless/path-picker.ps1`（24 项，全绿）在临时沙盒里跑完命令面措辞、
> 脚本入参取值、扩展设置读写这三条。人工只需跑 A / B / C / F——那几组是控件交互与界面绑定，没有自动化路径。

## 前置（已为你备好）

- 已 build + pack + 预装：`tests/tlx/v1-settings.tlx` → `%AppData%/TuneLab/Extensions/v1-settings/`，**直接开 App 即可**。
  - 如需重制：`dotnet build tests/TestPlugins.slnx -c Release` → `pwsh tests/pack-tlx.ps1` → `pwsh tests/install-tlx.ps1 v1-settings`（装之前先关掉 TuneLab）。
- **UI 显示名**：设置窗「扩展」页里的分组标题是 **「V1 引擎设置演示」**（英文界面 "V1 Engine Settings Demo"）。
- 该夹具在原有设置项之外新增两项（原有的模型路径/API 密钥/使用 GPU 保持不变，作回归对照）：
  - **引擎可执行文件**（`engine_path`）：**选文件**模式，过滤器 `*.exe` / `*.bat`，对话框标题「选择引擎可执行文件」。
  - **音源库目录**（`voice_bank_dir`）：**选文件夹**模式，无过滤器。
- 日志：`%AppData%/TuneLab/Logs/TuneLab_<时间>.log`（最新一个），夹具在 `ApplySettings` 里打印这两个值。
- 手边准备：任意一个 `.exe`（如 `C:\Windows\notepad.exe`）和任意一个文件夹。

---

## A · 渲染与选文件

打开「设置」→「扩展」页。

- [ ] 「引擎可执行文件」一行是**标签 + 全宽文本框 + 右侧 `...` 按钮**（与模型路径那种纯文本框明显不同）。
- [ ] 点 `...` → 弹出系统**文件**选择对话框，标题是「选择引擎可执行文件」（英文界面 "Select the engine executable"）。
- [ ] 对话框的文件类型过滤是 `*.exe` / `*.bat`：目录里的 `.txt` 等文件不在默认过滤下显示。
- [ ] 选中一个 `.exe` 确定 → 文本框立刻显示该文件的**完整路径**。
- [ ] 再点 `...`、这次**取消** → 文本框内容**不变**（取消不清空、不写入）。

## B · 选文件夹

- [ ] 「音源库目录」同样是文本框 + `...` 按钮；点它弹出的是**文件夹**选择对话框（不能选到文件），无类型过滤。
- [ ] 选一个文件夹 → 文本框显示该目录路径。

## C · 值与普通文本字段同路（落盘 / 回喂 / 手敲）

- [ ] 直接在「引擎可执行文件」文本框里**手敲**一个**不存在**的路径（如 `D:\nope\x.exe`）→ 宿主**不报错、不校验存在性**，照常接受（合法与否由插件自己判定）。
- [ ] 保存设置、关闭设置窗、再打开 → 两个路径字段都还是刚才的值。
- [ ] 重启 App → 值仍在；日志里能看到
  `[V1.Settings] ApplySettings: engine_path='…', voice_bank_dir='…'`，与界面显示一致。
- [ ] 用文本编辑器看 `%AppData%/TuneLab/Configs/ExtensionSettings.json`：这两个字段就是**普通明文字符串**（与模型路径同形，不是新结构）。

## D · 命令面 / agent 措辞

在 agent 侧栏（或 CLI）对该扩展查设置。

- [ ] `list_extension_settings` 列出的字段类型里，`engine_path` 显示为 **`file path (*.exe, *.bat)`**、`voice_bank_dir` 显示为 **`folder path`**（不是笼统的 `text` 或 `value`）。
- [ ] 让 agent 把 `engine_path` 设成某个路径字符串 → 成功（与文本字段同一条校验路），回到设置窗能看到新值。
- [ ] 让 agent 给 `engine_path` 传一个**布尔** `true` → 报错「is text; got a boolean.」，不写入。

## E · 脚本入参面

新建一个脚本工具，`getInputConfig` 里写：

```js
function getInputConfig(ctx) {
  return {
    exe: PathPickerConfig.createFile('').appendFileType('Executable', ['*.exe', '*.bat']).withPickerTitle('Pick the exe'),
    dir: PathPickerConfig.createFolder(''),
  };
}
function main(inputs) { print(inputs.exe + ' | ' + inputs.dir); }
```

- [ ] 运行脚本 → 入参窗里两项都是文本框 + `...` 按钮，行为同 A / B。
- [ ] 选好路径运行 → `main` 收到的 `inputs.exe` / `inputs.dir` 是所选路径字符串。
- [ ] 把某项写成 `PathPickerConfig.createFile('C:\\default.exe')` → 入参窗初值就是该默认路径。
- [ ] `appendFileType('Audio', '*.wav')`（**单个字符串**而非数组）也接受，不报错。

## F · 回归（既有控件不受影响）

- [ ] 同一面板里的「模型路径」仍是**纯文本框**（无 `...` 按钮）、「API 密钥」仍**掩码**显示。
- [ ] 勾选/取消「使用 GPU」触发面板重算（条件字段增删）时，两个路径字段的值**不丢**、控件不闪、焦点不乱。
- [ ] 导出侧栏的输出目录选择器（宿主自己的路径控件）与设置窗里宿主自带的路径类设置项，行为一切如常。
