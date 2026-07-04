[English](README.md) | [简体中文](README.zh-Hans.md)

<img src="branding/logo.png" />

# Emerde

[![GitHub license](https://img.shields.io/github/license/qzj1472/Emerde)](https://github.com/qzj1472/Emerde/blob/main/LICENSE) [![Actions](https://github.com/qzj1472/Emerde/actions/workflows/build.yml/badge.svg)](https://github.com/qzj1472/Emerde/actions/workflows/build.yml) [![Platform](https://img.shields.io/badge/platform-Windows-blue?logo=windowsxp&color=1E9BFA)](https://dotnet.microsoft.com/zh-cn/download/dotnet/9.0) [![GitHub downloads](https://img.shields.io/github/downloads/qzj1472/Emerde/total)](https://github.com/qzj1472/Emerde/releases)
[![GitHub downloads](https://img.shields.io/github/downloads/qzj1472/Emerde/latest/total)](https://github.com/qzj1472/Emerde/releases)

Emerde 是一个 Windows 桌面端多平台直播监控、录制、通知和预览工具。

录制由 FFmpeg 提供支持。直播预览由 LibVLCSharp 提供支持。

## 功能

| 功能 | 说明 |
| ---- | ---- |
| 直播监控 | 定时检测已添加直播间是否开播 |
| 直播录制 | 通过 FFmpeg 将支持的直播流录制到本地 |
| 直播预览 | 支持在录制前或录制中打开内嵌预览窗口 |
| 开播通知 | 支持 Windows 通知和可选提示音 |
| 平台识别 | 添加直播间时自动识别平台 |
| 平台筛选 | 支持按平台筛选直播间列表 |
| 分段输出 | 支持按时间分段录制 |
| Cookie 和代理 | 支持平台 Cookie 与可选 HTTP 代理 |

## 运行环境

- [Windows .NET Desktop Runtime 9.0](https://dotnet.microsoft.com/zh-cn/download/dotnet/9.0)
- 录制需要 FFmpeg。
- 直播预览通过 Windows 包内的 LibVLC 支持。

## 支持来源

| 来源 | 状态 |
| ---- | ---- |
| 抖音 | 支持 |
| TikTok | 支持 |
| 哔哩哔哩 | 支持 |
| 快手 | 支持 |
| 虎牙 | 支持 |
| 斗鱼 | 支持 |
| 百度直播 | 支持 |
| Bigo | 支持 |
| 17Live | 支持 |
| CHZZK | 支持 |
| 猫耳FM | 支持 |
| Picarto | 支持 |
| 连接直播 | 支持 |
| LangLive | 支持 |
| 六间房 | 支持 |
| VV星球 | 支持 |
| Blued | 支持 |
| 流星直播 | 支持 |
| 畅聊直播 | 支持 |
| 音播直播 | 支持 |
| 知乎直播 | 支持 |
| 飘飘直播 | 支持 |
| 花猫直播 | 支持 |
| 来秀直播 | 支持 |
| 京东直播 | 支持 |
| PandaTV | 支持 |
| WinkTV | 支持 |
| Twitch | 支持 |
| YouTube | 支持 |
| Shopee | 支持 |
| TwitCasting | 支持 |
| Faceit | 支持 |
| 微博直播 | 支持 |
| 花椒直播 | 支持 |
| SOOP | 支持 |
| FlexTV | 支持 |
| PopkonTV | 支持 |
| Look | 支持 |
| 淘宝直播 | 支持 |
| LiveMe | 支持 |
| 小红书 | 支持 |
| 酷狗 | 支持 |
| 映客 | 支持 |
| ShowRoom | 支持 |
| AcFun | 支持 |
| YY | 支持 |
| 网易 CC | 支持 |
| 千度热播 | 支持 |
| 直链 `.m3u8` / `.flv` 直播流 | 支持 |

## 直播间链接

添加受支持平台的直播间链接，或者直接粘贴 `.m3u8` / `.flv` 直播流地址。

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
https://app.blued.cn/live?id=Mp6G2R
https://www.7u66.com/100960
https://live.tlclw.com/106188
https://live.ybw1666.com/800002949
https://www.zhihu.com/people/ac3a467005c5d20381a82230101308e9
https://m.pp.weimipopo.com/live/preview.html?anchorUid=91625862
https://h.catshow168.com/live/preview.html?anchorUid=18895331
https://www.imkktv.com/h5/share/video.html?roomId=1710496
https://3.cn/28MLBy-E
https://www.pandalive.co.kr/live/play/bara0109
https://www.winktv.co.kr/live/play/anjer1004
https://www.twitch.tv/example
https://www.youtube.com/watch?v=example
https://live.shopee.sg/share?from=live&session=802458
https://twitcasting.tv/example
https://www.faceit.com/zh/players/qpjzz/stream
https://weibo.com/l/wblive/p/show/1022:2321325026370190442592
https://www.huajiao.com/l/345096174
https://play.sooplive.co.kr/sw7love
https://www.flextv.co.kr/channels/593127/live
https://www.popkontv.com/live/view?castId=wjfal007&partnerCode=P-00117
https://look.163.com/live?id=65108820
https://tbzb.taobao.com/live?liveId=532359023188
https://www.liveme.com/zh/v/17141543493018047815/index.html
https://www.tiktok.com/@example/live
https://example.com/live/index.m3u8
```

部分平台可能需要 Cookie、代理或对应地区网络环境。平台网页和接口可能变化，站点调整后解析逻辑也可能需要同步更新。

## 仅支持 Windows

Emerde 是 Windows-only WPF 应用。

| 操作系统 | 开发框架 | 状态 |
| -------- | -------- | ---- |
| Windows | WPF | 支持 |

## 项目结构

| 路径 | 用途 |
| ---- | ---- |
| `src/Emerde` | Windows WPF 应用 |
| `build` | Windows 打包资源和脚本 |
| `doc` | Cookie 配置文档 |
| `branding` | 产品图标和品牌资源 |
| `tests/Emerde.Tests` | 自动化测试 |

## Cookie

部分平台需要 Cookie 或对应地区网络访问。现有 Cookie 获取示例见 [GETCOOKIE_DOUYIN.md](doc/GETCOOKIE_DOUYIN.md) 和 [GETCOOKIE_TIKTOK.md](doc/GETCOOKIE_TIKTOK.md)。

## 隐私政策

[查看隐私政策](PrivacyPolicy.zh-Hans.md)。

## 许可证

本项目基于 [MIT 许可证](LICENSE)。

## 鸣谢

Emerde 的多平台直播流解析设计参考了 [DouyinLiveRecorder](https://github.com/ihmily/DouyinLiveRecorder) 的思路和平台行为。详见 [Third Party Notices](THIRD_PARTY_NOTICES.md)。
