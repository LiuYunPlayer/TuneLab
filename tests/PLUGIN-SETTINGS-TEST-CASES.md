# 扩展设置（IExtensionSettings）测试用例

验证通用「扩展设置系统」：扩展声明 `IExtensionSettings` → 宿主在「设置 > 扩展」分页渲染 → 按 extension 加密落盘 → 重启回喂。
只测本需求受影响范围，不涉及 voice 合成 / effect 处理本身。

## 前置（已为你备好）

- 已 build + pack：`tests/tlx/v1-settings.tlx`（夹具 `V1.Settings`，一个**设置专用** voice 引擎，不参与实际合成）。
  - 已直接预装到 `%AppData%/TuneLab/Extensions/v1-settings/`，**直接开 App 即可**（无需拖拽）。
  - 如需重制：`dotnet build tests/TestPlugins.slnx -c Release` 后 `pwsh tests/pack-tlx.ps1`。
- **UI 显示名**：扩展侧边栏与设置页里显示为 **「V1 引擎设置演示」**（英文界面 **"V1 Engine Settings Demo"**）。其身份 id 是 `TLSettingsDemo`（落盘键用，不在界面出现）。
- 相关路径（Windows）：
  - 落盘文件：`%AppData%/TuneLab/Configs/ExtensionSettings.json`（单文件，根对象直接是 `kind:extensionId` → 该扩展的原生设置）
  - 日志：`%AppData%/TuneLab/Logs/TuneLab_<时间>.log`（最新一个）

声明的设置项：**模型路径**（普通文本）、**API 密钥**（掩码 + 加密）、**使用 GPU**（开关）；勾选 GPU 后**动态**出现 **GPU 设备**（文本）。

---

## 用例

### 1. 设置面板渲染
- 开 App 后打开「设置」窗口 → 左侧选「扩展」分页。
- **预期**：见一段标题 **「V1 引擎设置演示」**，其下控件：模型路径（文本框）、API 密钥（**掩码**文本框）、使用 GPU（勾选框）。中文界面下标题为「模型路径 / API 密钥 / 使用 GPU」。

### 2. 动态设置项（条件显隐）
- 勾选「使用 GPU」。
- **预期**：下方**即时出现**「GPU 设备」文本框（不闪、其它控件不丢输入焦点）。取消勾选 → 该字段消失。

### 3. 编辑 → 关窗落盘 → 重开回显
- 填：模型路径 = `D:\models\demo`，API 密钥 = `sk-test-123456`，勾上「使用 GPU」，GPU 设备 = `cuda:0`。
- 关闭设置窗口（或 Esc）。再次打开「设置 > 扩展」。
- **预期**：模型路径、GPU 勾选、GPU 设备值原样保留；API 密钥框显示为掩码（已填、非空）。

### 4. 切 tab 也落盘
- 在「扩展」分页改任一值，**不关窗**，切到「常规」分页再切回「扩展」。
- **预期**：改动保留（关窗/切 tab 都统一落盘）。

### 5. 落盘内容与密钥加密
- 打开 `%AppData%/TuneLab/Configs/ExtensionSettings.json`（用 UTF-8 读，勿用会按 GBK 误读的工具）。
- **预期**（**原生 JSON**：值就是 string/number/bool，无 `Kind/Sec/...` 包装）：
  - 根对象有键 **`voice:TLSettingsDemo`**，其下直接是各字段。
  - `"model_path": "D:\\models\\demo"`（明文字符串）、`"use_gpu": true`（原生 bool）、`"gpu_device": "cuda:0"`。
  - `api_key`：**Windows 上是一段 base64 密文字符串（非明文 `sk-test-123456`）**；macOS 上为 `""`（空串，真密钥在钥匙串）。无安全存储的平台（官方未支持）则该字段**根本不写**（绝不明文）。

### 6. 重启回喂（ApplySettings）
- 完全退出并重启 TuneLab。
- 看最新日志文件，**预期**含一行：
  `[V1.Settings] ApplySettings: model_path='D:\models\demo', api_key=<set>, use_gpu=True`
  - `api_key=<set>` 证明密钥**解密成功**（回喂时拿到了非空明文）；`<empty>` 则说明解密失败或未存。

### 7. 空态（可选）
- 卸载本插件（且无其他实现 `IExtensionSettings` 的扩展）后打开「设置 > 扩展」。
- **预期**：显示「没有可配置的扩展。」占位文案，不报错。

---

## 注意

- 本夹具是 voice 引擎，但**仅供设置演示**，不要选它去合成（`CreateSession` 会抛异常，符合预期）。
- agent 模型的设置走其侧边栏自有入口，**不在**「设置 > 扩展」分页里——本用例不涉及 agent。
- 撤掉夹具：删 `%AppData%/TuneLab/Extensions/v1-settings/`。
