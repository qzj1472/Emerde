# Using Emerde.UI

## New or existing WPF project

1. ProjectReference `src/Emerde.UI/Emerde.UI.csproj`.
2. Keep `WPF-UI` 4.0.3 if the project uses `ui:` controls in XAML.
3. In `App.xaml`:

```xml
<Application ...
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
             xmlns:eu="http://schemas.emerde.app/ui/2026/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary />
                <ui:ControlsDictionary />
                <eu:ThemesDictionary />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

Main app also merges Violeta, `Resources.xaml`, and `Themes/UiXTheme.xaml` after `eu:ThemesDictionary`.

Do not nest `<ui:ThemesDictionary />` or `<ui:ControlsDictionary />` inside `eu:ThemesDictionary`. They must stay one Application `MergedDictionaries` level deep so `ThemeManager.Apply` and later Source dictionaries can see them. `eu:ThemesDictionary` will add them at that level if they are missing.
## XAML namespaces

- `xmlns:eu="http://schemas.emerde.app/ui/2026/xaml"` — Emerde theme entry
- `xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"` — WPF-UI controls
- `xmlns:vio="http://schemas.lepo.co/wpfui/2022/xaml/violeta"` — Violeta dialogs (main app)

Controls stay `ui:Button`, `ui:SymbolIcon`, `ProgressBar`. Visuals come from Emerde.UI.

## Where to put what

| Change | Put it here |
| --- | --- |
| Brand color, radius, progress keys, dialog mask | `src/Emerde.UI/Themes/EmerdeTheme.xaml` |
| Control overlays (MinHeight, FocusVisual) | `src/Emerde.UI/Markup/ThemesDictionary.cs` |
| App-only templates (checkbox glyph, CompactNumberBox, MotionAssist, tooltips) | `src/Emerde/Resources.xaml` |
| UI-X surfaces and materials | `src/Emerde/Themes/UiXTheme.xaml` + `AppThemeBrushes` |
| One window's layout | that window's XAML |

`EmerdeTheme.xaml` is loaded as a Source dictionary. Do not put `BasedOn="{StaticResource {x:Type ...}}"` styles there or in `ThemesDictionary.xaml`; that bakes the stock WPF template and replaces WPF-UI. Apply control overlays in `ThemesDictionary.cs` after WPF-UI is in Application resources.
## Theme switching

Keep WPF-UI's theme dictionary as an Application `MergedDictionaries` sibling so `ThemeManager.Apply` still finds `Wpf.Ui` + `theme` in Source. Do not wrap it in `eu:ThemesDictionary` or another Source dictionary. App-level `AppThemeBrushes` still owns Emerde shell/card brushes that animate with theme.

UI-X layout and materials are part of this same skill. Do not look for `$emergde-ui-x`.
