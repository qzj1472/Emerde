---
name: emerde-ui
description: Design, implement, restyle, or review Emerde WPF UI with the Emerde.UI layer on WPF-UI. Use for Emerde windows, dialogs, cards, progress bars, settings, installer/uninstaller chrome, UI-X layout, video library, window materials, tokens, control restyles, or any request to match Emerde's visual language. Do not use for unrelated apps unless the user explicitly wants this design system.
---

# Emerde UI

Shared visual language for Emerde. Pair this skill with the `Emerde.UI` project. Together they are the Emerde restyle of WPF-UI, not a second isolated UI kit.

中文：用 `Emerde.UI` 覆盖 WPF-UI 默认主题；控件仍用 WPF-UI，观感必须走 Emerde token 与全局样式。新 UI 不要在页面里私自配色或复制 WPF-UI 默认。UI-X 布局和材质也走本 skill，不要再找 `emergde-ui-x`。

## Stack

Use this stack only:

- WPF
- WPF-UI 4.0.3 for controls (`xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"`)
- WPF-UI.Violeta for dialogs/DataGrid in the main app
- `Emerde.UI` for theme + Emerde restyle (`xmlns:eu="http://schemas.emerde.app/ui/2026/xaml"`)
- FluentWpfCore only for window materials, window corners, and explicitly required popups/smooth scrolling
- ComputedConverters where existing view models already use it

Do not add ComputedBehaviors or ComputedAnimations. Do not merge `FluentWpfCore;component/Themes/Generic.xaml`. Do not use EleCho.WpfSuite, HandyControl, MaterialDesign, or another complete control theme. Do not fork or vendor the WPF-UI source tree.

## Bind the library

Every WPF app in this repo loads WPF-UI first, then Emerde overlays:

```xml
xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
xmlns:eu="http://schemas.emerde.app/ui/2026/xaml"
...
<ui:ThemesDictionary />
<ui:ControlsDictionary />
<eu:ThemesDictionary />
```

Keep `ui:ThemesDictionary` and `ui:ControlsDictionary` as Application `MergedDictionaries` siblings before `eu:ThemesDictionary` and before any Source dictionary that uses `BasedOn="{StaticResource {x:Type ...}}"`. Nesting WPF-UI inside `eu:ThemesDictionary` bakes the stock gray square templates into those BasedOn styles.

`eu:ThemesDictionary` still registers WPF-UI at the application level if it is missing, then applies Emerde tokens and control overlays in code.
Controls stay `ui:Button`, `ui:SymbolIcon`, `ProgressBar`. Visuals come from Emerde.UI.

Read `references/usage.md` when adding a window or a new WPF project.
Read `references/tokens.md` before introducing a color, radius, or brush.
Read `references/wpfui-overrides.md` before restyling a WPF-UI control.
Read `references/ui-x.md` for UI-X layout, materials, video library, and settings surfaces.

## Product

Emerde is a Windows desktop tool for repeated multi-platform livestream monitoring and recording. Optimize for fast scanning, clear state, predictable repeated actions, long localized text, and large room or video collections.

UI-X is optional behind `Configurations.IsUiXEnabled`. Shared chrome (progress, corners, brand fill, dialog mask, control min-height) is not UI-X-only; it lives in Emerde.UI. UI-X owns layout, materials, and UI-X-only surfaces.

## Visual rules

- Quiet operational desktop UI. Neutral cool surfaces. Brand green is for progress, primary actions, and confirmation — never page background.
- Corner radius 8px unless a native circular control requires a circle. Checkboxes in Emerde may stay 10px.
- 1px semantic strokes. Do not combine a visible border and a heavy shadow on the same surface.
- Segoe UI and the WPF-UI icon family. No web fonts, emoji, or Unicode symbols as icons.
- 4/8px spacing. Compact repeated work surfaces.
- Pointer targets at least 34px high; 40px for primary actions where space permits.
- Light and dark independently. Text contrast at least 4.5:1; non-text state boundaries at least 3:1 where practical.
- Do not hardcode `#14B86B` in new views. Use `EmerdeBrandFillBrush` / `EmerdeBrandFillPressedBrush`.
- Do not use Windows accent for progress or primary fill. Progress uses brand green and a theme-aware neutral track.
- Token/key restyles go in `src/Emerde.UI/Themes/EmerdeTheme.xaml`. Control overlays (MinHeight, FocusVisual) go in `src/Emerde.UI/Markup/ThemesDictionary.cs` after WPF-UI is loaded. Do not put `BasedOn="{StaticResource {x:Type ...}}"` in `ThemesDictionary.xaml`. Do not restyle a single view to work around WPF-UI.

## UI-X

Keep UI-X optional. Preserve the legacy UI unless the user explicitly includes it.

- Work in `E:\\Git_Emerde\\yuanma`.
- UI-X resources live in `src/Emerde/Themes/UiXTheme.xaml` and theme-dependent colors in `AppThemeBrushes`.
- Use Mica on Windows 11 and composition Acrylic on Windows 10 only while UI-X is enabled.
- Preserve virtualization for room and video collections. Do not wrap virtualized lists in an outer smooth scroller.
- Prefer native WPF panels in dynamic UI-X layout. Avoid `Wpf.Ui.Controls.StackPanel` where collapsed or detached content can participate in measurement.

## Workflow

1. Read the target XAML, the neighboring surface, `EmerdeTheme.xaml`, `ThemesDictionary.xaml`, and `Resources.xaml` / `UiXTheme.xaml` if the surface is in the main app.
2. Prefer tokens and existing templates. Do not invent a parallel palette.
3. If WPF-UI's default disagrees with Emerde, change Emerde.UI so every current and future control picks it up.
4. Keep installer operation logic, recording logic, and packaging out of visual work.
5. Verify light/dark, UI-X on/off when the surface exists in both, long localized text, and keyboard focus.
6. Follow `$qblam` for Debug publish, installer, commit, and push.

## Do not

- Fork or vendor the WPF-UI source tree.
- Leave a one-off color on one ProgressBar, Button, or dialog that other surfaces will not inherit.
- Restyle by replacing a control template unless the existing Emerde/WPF-UI template is the bug.
- Add comments in XAML or C#.
