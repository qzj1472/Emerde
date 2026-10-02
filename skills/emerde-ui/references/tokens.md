# Emerde tokens

Prefer these keys over raw colors in new UI.

## Brand

| Key | Light value | Use |
| --- | --- | --- |
| `EmerdeBrandFillBrush` | `#14B86B` | Progress fill, primary action fill |
| `EmerdeBrandFillPressedBrush` | `#0A9553` | Pressed primary |
| `EmerdeBrandFillSubtleBrush` | `#0B14B86B` | Quiet brand wash |
| `EmerdeBrandStrokeSubtleBrush` | `#3514B86B` | Quiet brand stroke |

Do not make brand green the shell, card, or page background.

## Progress

| Key | Value |
| --- | --- |
| `ProgressBarForeground` | Brand green `#14B86B` |
| `ProgressBarBackground` | `{DynamicResource ControlAltFillColorQuarternary}` |
| `ProgressBarIndeterminateBackground` | same track, never transparent |

Keep existing heights: installer/merge 6, loading 4, notification 3. Do not globally force Height.

## Radius

WPF-UI default control radius is too tight. Emerde sets the shared keys to 8: `ControlCornerRadius`, `ButtonCornerRadius`, `TextControlCornerRadius`, `CardCornerRadius`, `ContentDialogCornerRadius`, and the other 8px keys in `EmerdeTheme.xaml`. CheckBox may stay 10.

## Dialog chrome

| Key | Typical light |
| --- | --- |
| `DialogMaskBrush` | `#33FFFFFF` |
| `EmerdeDialogSurfaceBrush` | `#FFF1F4F5` |
| `EmerdeDialogBorderBrush` | `#24000000` |
| `EmerdeDialogWarningBrush` | `#FFF3E2CE` |
| `EmerdeDialogTextPrimaryBrush` | `#E8171B20` |
| `EmerdeDialogTextSecondaryBrush` | `#A8171B20` |

Main-app dialog background that follows theme is `EmerdeDialogBackgroundBrush` in `Resources.xaml` / `AppThemeBrushes`.

## Type and size

- Font: Segoe UI
- Compact control min height: 34
- Primary action min height: 40 where space permits
- Spacing rhythm: 4 / 8