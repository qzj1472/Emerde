[简体中文](README.md) | [English](README.en.md)

<img src="branding/logo.png" alt="Emerde" width="128" />

# Emerde

[![许可证](https://img.shields.io/github/license/qzj1472/Emerde)](LICENSE) [![最新版本](https://img.shields.io/github/v/release/qzj1472/Emerde)](https://github.com/qzj1472/Emerde/releases/latest) [![Windows](https://img.shields.io/badge/Windows-桌面应用-1E9BFA?logo=windows)](https://github.com/qzj1472/Emerde/releases/latest) [![下载](https://img.shields.io/github/downloads/qzj1472/Emerde/total)](https://github.com/qzj1472/Emerde/releases)

Emerde 是 Windows 上的多平台直播监控与录制工具。把直播间加进来，开播后自动录到本地，也可以预览画面、接收开播通知。

## 下载

到 [Releases](https://github.com/qzj1472/Emerde/releases/latest) 获取最新版：

- **安装包** `Emerde-x.x.x.exe`：安装后从开始菜单打开
- **便携版** `Emerde-x.x.x-win-Portable.zip`：解压即用，不写系统安装项

安装包和便携版都是完整可用版本，不需要再装运行库。调试包只给排查问题用，日常请用上面两个。

## 能做什么

| 能力 | 说明 |
| ---- | ---- |
| 监控开播 | 定时检查已添加的直播间，开播、下播状态一目了然 |
| 自动录制 | 开播后自动开始录制，也可手动开始；长直播可按时间分段 |
| 内嵌预览 | 录制前或录制中打开预览窗口，确认是不是你要的那场 |
| 开播通知 | Windows 通知，可选提示音 |
| 异常恢复 | 遇到卡顿或音频异常时尽量保住已录内容，而不是整场作废 |
| 本机保存 | 配置、Cookie、录像都只在你的电脑上 |

适合同时盯多个平台、不想守着开播、需要把直播留成文件的场景。

## 怎么用

1. 下载安装包或便携版并打开 Emerde。
2. 粘贴直播间链接，添加房间。软件会自动识别平台。
3. 开播后自动开始录制；也可以先预览再决定是否录。
4. 在视频列表里查看、打开已经录好的文件。

部分平台需要 Cookie 或代理才能稳定检测和录制，说明见 [Wiki](https://github.com/qzj1472/Emerde/wiki)。

## 支持平台

支持 **40+** 平台，包括抖音、哔哩哔哩、Twitch、YouTube、快手、虎牙、斗鱼、小红书等，也可以直接录制 `.m3u8` / `.flv` 直链。

完整名单和链接示例见 [Wiki · 支持平台](https://github.com/qzj1472/Emerde/wiki/支持平台)。

## 隐私 · 许可 · 致谢

- **隐私：** 数据只保存在本机，不会上传账号或录像。详见 [隐私政策](PrivacyPolicy.zh-Hans.md)。
- **许可：** 以 [GPL-3.0-only](LICENSE) 发布。
- **致谢：** 多平台直播解析参考了 [DouyinLiveRecorder](https://github.com/ihmily/DouyinLiveRecorder)。
