[English](README.md) | [简体中文](README.zh-Hans.md)

<img src="branding/logo.png" />

# Emerde

[![GitHub license](https://img.shields.io/github/license/qzj1472/Emerde)](https://github.com/qzj1472/Emerde/blob/main/LICENSE) [![Actions](https://github.com/qzj1472/Emerde/actions/workflows/build.yml/badge.svg)](https://github.com/qzj1472/Emerde/actions/workflows/build.yml) [![Platform](https://img.shields.io/badge/platform-Windows-blue?logo=windowsxp&color=1E9BFA)](https://dotnet.microsoft.com/en-us/download/dotnet/latest/runtime) [![GitHub downloads](https://img.shields.io/github/downloads/qzj1472/Emerde/total)](https://github.com/qzj1472/Emerde/releases)
[![GitHub downloads](https://img.shields.io/github/downloads/qzj1472/Emerde/latest/total)](https://github.com/qzj1472/Emerde/releases)

Emerde is a Windows desktop tool for multi-platform live stream monitoring, recording, notifications, and preview playback.

Recording is powered by FFmpeg. Live preview is powered by LibVLCSharp.

## Features

| Feature | Description |
| ------- | ----------- |
| Live monitoring | Periodically checks whether saved live rooms are streaming |
| Recording | Records supported live streams to local files through FFmpeg |
| Live preview | Opens an embedded preview window before or during recording |
| Notifications | Sends Windows notifications and optional reminder sounds |
| Platform detection | Detects the platform while adding a room URL |
| Platform filter | Filters the room list by platform |
| Segmented output | Supports time-based recording segments |
| Cookie and proxy settings | Supports platform cookies and optional HTTP proxy |

## Runtime

- [.NET Desktop Runtime 9.0 for Windows](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- FFmpeg is required for recording.
- LibVLC is bundled through the Windows package for live preview.

## Supported Sources

| Source | Status |
| ------ | ------ |
| Douyin | Supported |
| TikTok | Supported |
| Bilibili | Supported |
| Kuaishou | Supported |
| Huya | Supported |
| Baidu Live | Supported |
| Bigo | Supported |
| 17Live | Supported |
| CHZZK | Supported |
| MaoerFM | Supported |
| Picarto | Supported |
| Lianjie | Supported |
| LangLive | Supported |
| 6Rooms | Supported |
| VVXqiu | Supported |
| Xiaohongshu | Supported |
| Kugou | Supported |
| Yingke | Supported |
| ShowRoom | Supported |
| AcFun | Supported |
| YY | Supported |
| Netease CC | Supported |
| Qiandu Rebo | Supported |
| Direct `.m3u8` / `.flv` streams | Supported |

## Room URLs

Add the live room URL from a supported platform, or paste a direct `.m3u8` / `.flv` stream URL.

```text
https://live.douyin.com/123456
https://live.bilibili.com/123456
https://live.kuaishou.com/u/example
https://www.huya.com/52333
https://live.baidu.com/m/media/pclive/pchome/live.html?room_id=9175031377
https://17.live/en/live/6302408
https://chzzk.naver.com/live/458f6ec20b034f49e0fc6d03921646d2
https://fm.missevan.com/live/868895007
https://www.picarto.tv/cuteavalanche
https://show.lailianjie.com/10000258
https://www.lang.live/en-US/room/3349463
https://v.6.cn/634435
https://h5webcdn-pro.vvxqiu.com/activity/videoShare/videoShare.html?roomId=LP115924473
https://www.tiktok.com/@example/live
https://example.com/live/index.m3u8
```

Some platforms may require cookies, a proxy, or a specific regional network route. Platform APIs can change over time, so resolver behavior may need updates when a site changes its web interface.

## Windows Only

Emerde is a Windows-only WPF application.

| OS | Framework | Status |
| -- | --------- | ------ |
| Windows | WPF | Supported |

## Project Structure

| Path | Purpose |
| ---- | ------- |
| `src/Emerde` | Windows WPF application |
| `build` | Windows packaging assets and scripts |
| `doc` | Cookie setup guides |
| `branding` | Product icons and branding assets |
| `tests/Emerde.Tests` | Automated tests |

## Cookies

Some platforms require cookies or regional network access. See [GETCOOKIE_DOUYIN.md](doc/GETCOOKIE_DOUYIN.md) and [GETCOOKIE_TIKTOK.md](doc/GETCOOKIE_TIKTOK.md) for the existing cookie setup examples.

## Privacy Policy

See the [Privacy Policy](PrivacyPolicy.md).

## License

This project is licensed under the [MIT License](LICENSE).

## Thanks

Emerde's multi-platform live stream resolver design references ideas and platform behavior from [DouyinLiveRecorder](https://github.com/ihmily/DouyinLiveRecorder). See [Third Party Notices](THIRD_PARTY_NOTICES.md).
