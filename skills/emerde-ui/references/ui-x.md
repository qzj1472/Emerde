# Emerde UI-X

UI-X layout, materials, and UI-X-only surfaces. Shared tokens, WPF-UI restyle, progress, corners, brand fill, and installer chrome stay in Emerde.UI and the main skill body.

## Authority

Use Impeccable as the design lead. Use UI UX Pro Max only to query spacing, hierarchy, accessibility, state, and responsive guidance. Reject web, mobile, marketing, palette, or typography output that conflicts with a Windows desktop recording tool. Let the user's current brief override this skill.

## Boundary

- Primary UI-X boundary is `src/Emerde`. Installer dialogs may match Emerde modal language when the user explicitly asks; keep installer operations and packaging out.
- FluentWpfCore is only for window materials, window corners, native animations, popups when explicitly needed, and smooth scrolling.
- Do not merge `FluentWpfCore;component/Themes/Generic.xaml`.
- Keep reusable UI-X templates on native WPF primitives and semantic project resources.

## Surfaces

### Main window

- Keep navigation quieter than the active work surface.
- Keep room cards centered and bounded by existing card metrics.
- Preserve avatar, name, title, live state, and recording state hierarchy.
- Show hover, selection, monitoring, and recording states without relying on color alone.

### Video library

- Thumbnail-first card grid with concise metadata.
- Preserve recycling virtualization, selection rectangle behavior, keyboard selection, context actions, and long filename trimming.
- Keep action density low until hover, selection, or an explicit command requires controls.
- Do not unload already-loaded video covers just because a card left the viewport.

### Settings

- Two columns only when content width is at least 820px; otherwise one column.
- Group settings semantically rather than balancing columns by item count.
- Keep sections expanded in UI-X while preserving dependent option visibility.
- Place cookie settings last.
- Use native WPF StackPanel for dynamically regrouped settings.

## Interaction

- Preserve visible keyboard focus, logical tab order, mouse wheel behavior, and localized text wrapping.
- Pointer targets at least 34px; 40px for primary actions where space permits.
- 140-240ms state transitions with ease-out entry and shorter exits.
- Avoid layout-transform animations for large settings content.
- Provide empty, disabled, selected, recording, monitoring, loading, and error states where the surface can encounter them.
