[English](README.md) | [简体中文](README.zh-Hans.md)

<img src="branding/logo.png" />

# Emerde

[![GitHub license](https://img.shields.io/github/license/qzj1472/Emerde)](https://github.com/qzj1472/Emerde/blob/main/LICENSE) [![Actions](https://github.com/qzj1472/Emerde/actions/workflows/build.yml/badge.svg)](https://github.com/qzj1472/Emerde/actions/workflows/build.yml) [![Platform](https://img.shields.io/badge/platform-Windows-blue?logo=windowsxp&color=1E9BFA)](https://dotnet.microsoft.com/en-us/download/dotnet/latest/runtime) [![GitHub downloads](https://img.shields.io/github/downloads/qzj1472/Emerde/total)](https://github.com/qzj1472/Emerde/releases)
[![GitHub downloads](https://img.shields.io/github/downloads/qzj1472/Emerde/latest/total)](https://github.com/qzj1472/Emerde/releases)

With a graphical UI, unattended operation, and live streaming recording capabilities.

Based on FFmpeg and FFplay.

## Screen Shot

<img src="assets/image-20241113165355466.png" alt="image-20241113165355466" style="transform:scale(0.5);" />

## Dependencies Runtime

[.NET Desktop Runtime 9.0 for Windows](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)

## Live Streaming

Support following live site.

| Site            | Status    |
| --------------- | --------- |
| Douyin (抖音)   | Available |
| Tiktok          | Available |

How to add live room:

```bash
# Douyin room URL like following:
https://live.douyin.com/XXX
https://www.douyin.com/root/live/XXX

# Tiktok room URL like following:
https://www.tiktok.com/@XXX/live
```

## Support OS

This project only supports Windows.

| OS      | Framework | Status    |
| ------- | --------- | --------- |
| Windows | WPF       | Available |

## Project Structure

| Path | Purpose |
| ---- | ------- |
| `src/Emerde` | Windows WPF application |
| `build` | Windows packaging assets and scripts |
| `doc` | Cookie setup guides |
| `assets` | README images |
| `branding` | Product icons and branding assets |

## Your Cookie Can

Check it from [GETCOOKIE_DOUYIN.md](doc/GETCOOKIE_DOUYIN.md) or [GETCOOKIE_TIKTOK.md](doc/GETCOOKIE_TIKTOK.md).

## Privacy Policy

See the [Privacy Policy](PrivacyPolicy.md).

## License

This project is licensed under the [MIT License](LICENSE).

## Thanks

To save maintenance costs, refer to the specific string data form [DouyinLiveRecorder](https://github.com/ihmily/DouyinLiveRecorder), just like regex and so on.
