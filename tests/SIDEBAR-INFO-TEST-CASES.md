# 扩展侧边栏信息增强 · 测试用例

> 独立需求独立文档。本文只覆盖「扩展侧边栏卡片信息」这一改动影响的范围（卡片布局、图标、作者/简介、类别徽标、状态徽标）。
> **不涉及** format 导入导出、voice 合成、ALC 隔离等已有功能——那些的回归见 [PLUGIN-TEST-CASES.md](PLUGIN-TEST-CASES.md)，本次改动不触及，无需重跑。

验证点全部在**扩展侧边栏**用肉眼观察，不需要导入/合成。

## 准备
- 无代码改动的纯展示夹具无需 build，直接打包：
  ```powershell
  powershell -File tests/pack-tlx.ps1
  ```
- 装 `tests/tlx/v1-sidebar-info.tlx`（拖进窗口或侧边栏 → Install Extension）。

## 卡片布局（3 行）+ 简介进 tooltip
装 `v1-sidebar-info` 后，卡片应为：

```
┌────────┐ Sidebar Info Showcase …          v2.3.1   ← 第1行：名称(左,截断) + 版本(右)
│  矢量   │ 👤 TuneLab Tests · …                       ← 第2行：作者(中部,截断)
│  图标   │ [Voicebank]                       [卸载]   ← 第3行：类别(左) + 卸载(右)
└────────┘
简介不在卡片上，hover 整张卡片由 tooltip 给出完整信息。
```

- [ ] **图标**：左侧显示包内 `icon.svg` 的矢量图标（蓝底人像），**不是**名称首字母占位块。
- [ ] **卡片三行**：名称+版本 / 作者 / 类别+卸载；整体高度与左侧 64px 图标相当，没有被简介行撑高。
- [ ] **作者贴近底行**：留白在大字名称与小字之间，作者（小字）与第三行类别（小字）靠得近，而不是贴着名称。
- [ ] **简介进 tooltip**：把鼠标悬在卡片上 → tooltip 显示「完整名称 + 版本 + 作者 + 简介」（多行），卡片本身看不到简介文字。

## 过长截断
- [ ] **名称**过长 → 省略号（完整名见卡片 tooltip）。
- [ ] **作者**过长 → 省略号（完整作者见卡片 tooltip）。

## 图标退回占位
- [ ] 装任意**无 `icon` 字段**的包（如 `v1-format.tlx`）→ 左侧显示名称首字母占位块（如 `VT`），不报错。
- [ ] （可选）位图图标：把某包的 `icon` 改成一张 `.png` 再打包安装 → 左侧显示该位图（验证位图路径，与矢量路径并行支持）。

## Legacy 显示真实类别（不再笼统 "Legacy"）
> 需 Compat.Legacy 已部署（`dotnet build TuneLab.sln -c Debug` 会连带产出）。

- [ ] 装 `legacy-format.tlx` → 底行类别徽标 **`Format`**。
- [ ] 装 `legacy-voice.tlx` → 底行类别徽标 **`Voice`**。
- [ ] 装 `legacy-multi.tlx` → 底行**两枚独立徽标 `Format` `Voice`**（一种 type 一枚，不是逗号拼进一枚）。
- [ ] 三者都**不**再显示笼统的 "Legacy" 作为类别。

## Skipped / Failed：不显示类别、只显示状态徽标
- [ ] `v1-sdkver-high.tlx` / `v1-platform-mismatch.tlx` / `v1-effect.tlx` → 底行**只有** `Skipped`（黄），**没有**类别徽标。
- [ ] `v1-bad-manifest.tlx` → 底行 `Failed`（红），无类别徽标。
- [ ] 把鼠标悬在状态徽标上 → tooltip 显示跳过/失败原因。

## PartiallyLoaded（部分加载）
- [ ] 若有部分加载的包（例如一包多插件、其中一个平台不匹配被跳过）→ 底行同时显示 **类别徽标 + `Partial`（橙）** 两枚并排。
