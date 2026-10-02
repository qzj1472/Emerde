# WPF-UI defaults Emerde replaces

Change these in Emerde.UI so every app and every future control inherits them.

| WPF-UI default | Emerde | Where |
| --- | --- | --- |
| Progress fill = Windows accent | Brand green `#14B86B` | `ProgressBarForeground` in `EmerdeTheme.xaml` |
| Progress track = `ControlStrongStrokeColorDefault` | Neutral `ControlAltFillColorQuarternary` | `ProgressBarBackground` |
| Indeterminate track = transparent | Same visible track | `ProgressBarIndeterminateBackground` |
| Control corner radius 4 | 8 | radius keys in `EmerdeTheme.xaml` |
| Focus visual rectangle on buttons | None | overlay in `ThemesDictionary.cs` |
| Short buttons | MinHeight 34 | overlay in `ThemesDictionary.cs` |
| `ControlElevationBorderBrush` strong/theme default | `#24000000` (theme-animated in the main app) | `EmerdeTheme.xaml` + `AppThemeBrushes` |

Do not patch a single `ProgressBar` or `Button` in a view to work around these. Do not replace the WPF-UI ProgressBar template; override the three keys only.

If a new WPF-UI default still disagrees with Emerde, add the override to Emerde.UI and this table together.

Still WPF-UI, on purpose:

- Control types and templates (`ui:Button`, `ui:SymbolIcon`, `ui:FluentWindow`, `ui:TextBox`, ...)
- Icon family
- Light/Dark theme dictionary switching
- Violeta ContentDialog host in the main app (styled further in `Resources.xaml`)
