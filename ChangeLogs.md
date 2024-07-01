# TuneLab Extremist Branch ChangeLog

TuneLab功能激进实现分支，主要用于实验性完成某些编辑器级的高级功能。功能不一定会在TuneLab中最终实现。

## Tunelab使用说明

[TuneLab Readme](https://github.com/LiuYunPlayer/TuneLab/blob/master/README.md)
[项目主线](https://github.com/LiuYunPlayer/TuneLab)

## 修订日志
- Patch 2407012

  1.合并i18n线，支持基于TOML的中文化。
  2.可以自行翻译成其他语言，资源文件命名为4字标准。如 zh-CN,en-US,ja-JP等

- Patch 2407011

  1.新增PianoRoll快捷键 Ctrl+Shift。当Ctrl+Shift同时按下时，Note所包含的PitchLine会跟随Note移动。

- Patch 2406301

  1.新增编辑器参数XVS，支持同一轨道加载2个歌手（含引擎）。通过Cross-Voice-Synthesis曲线实现两个歌手的发音交叠
  2.XVS支持多歌词混合，当同一轨道歌词包含<>时，将<>中包含的歌词发往第二个歌手，多用于STD声库拼凑发音。例如 seng< mei > 可通过 XVS曲线实现发音 sei
