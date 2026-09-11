[简体中文](README.md) | [English](README.en.md)

<img src="branding/logo.png" alt="Emerde" width="128" />

# Emerde

[![License](https://img.shields.io/github/license/qzj1472/Emerde)](LICENSE) [![Latest release](https://img.shields.io/github/v/release/qzj1472/Emerde)](https://github.com/qzj1472/Emerde/releases/latest) [![Windows](https://img.shields.io/badge/Windows-desktop-1E9BFA?logo=windows)](https://github.com/qzj1472/Emerde/releases/latest) [![Downloads](https://img.shields.io/github/downloads/qzj1472/Emerde/total)](https://github.com/qzj1472/Emerde/releases)

Emerde is a Windows desktop app for multi-platform live monitoring and recording. Add a room, and Emerde records it when the stream goes live. You can also preview the stream and get desktop notifications.

## Download

Get the latest build from [Releases](https://github.com/qzj1472/Emerde/releases/latest):

- **Installer** `Emerde-x.x.x.exe`: install and launch from the Start menu
- **Portable** `Emerde-x.x.x-win-Portable.zip`: unzip and run, no system install

Both are complete, self-contained builds. You do not need to install a separate runtime. The debug zip is only for troubleshooting.

## What it does

| Capability | Details |
| ---------- | ------- |
| Live monitoring | Periodically checks saved rooms and shows whether they are live |
| Auto recording | Starts recording when a room goes live, or start it yourself; long streams can be split by time |
| Embedded preview | Open a preview window before or during recording |
| Notifications | Windows notifications, with an optional sound |
| Recovery | Keeps already recorded footage when a stream stalls or audio goes bad |
| Local-only data | Settings, cookies, and recordings stay on your PC |

Use it when you follow rooms across platforms, do not want to wait for a stream to start, and need a local copy of the broadcast.

## How to use

1. Download the installer or portable build and open Emerde.
2. Paste a live room URL to add it. The platform is detected automatically.
3. Recording starts when the room goes live. You can preview first if you want.
4. Open finished files from the video library.

Some platforms need a cookie or proxy for reliable detection and recording. See the [Wiki](https://github.com/qzj1472/Emerde/wiki).

## Supported platforms

Emerde supports **40+** sources, including Douyin, Bilibili, Twitch, YouTube, Kuaishou, Huya, Douyu, and Xiaohongshu. Direct `.m3u8` / `.flv` URLs work too.

The full list and example URLs are in the [Wiki · Supported platforms](https://github.com/qzj1472/Emerde/wiki/支持平台).

## Privacy · License · Thanks

- **Privacy:** Data stays on your device. See the [Privacy Policy](PrivacyPolicy.md).
- **License:** Released under [GPL-3.0-only](LICENSE).
- **Thanks:** Multi-platform stream resolving draws on [DouyinLiveRecorder](https://github.com/ihmily/DouyinLiveRecorder).
