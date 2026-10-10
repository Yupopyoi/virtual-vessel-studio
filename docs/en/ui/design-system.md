# Design System

## 1. Purpose

This document defines the visual values (design tokens) shared by every screen of Virtual Vessel Studio (system design 14.4, 14.34).

Every screen and component uses only the values defined here. Screens do not define their own colors, text sizes, or spacing.

The visual reference is the following preview page:

```text
docs/assets/ui/styleboard.html
```

Opened directly in a browser, it lets you switch the palette, dark or light, Japanese or English, and color view. This document and the preview page are always kept in agreement. When either one changes, update the other with the same change.

Related documents:

- `components.md`: specifications of the shared components
- `ui-guidelines.md`: rules for building screens

---

## 2. Design Direction

- As an application for VTubers, it should feel soft and cute.
- Cuteness comes from **rounded shapes** and **soft accent colors**. Text sizes and spacing stay calm so that the UI remains easy to read while streaming.
- The default color scheme is purple to pink (Lavender), and users can choose from several palettes.
- A dark background is the default, and a light background can be selected.
- Being usable regardless of how a person sees color or their physical conditions takes priority over cuteness (Chapter 9).

---

## 3. Color

### 3.1 Structure

Colors fall into three groups.

| Group | Contents | Change with the palette |
|---|---|---|
| Accent | Primary, Secondary | Change per palette |
| Neutral | Backgrounds, surfaces, borders, text | Slightly tinted with the palette hue |
| Status | Success, warning, error, live | Fixed regardless of the palette |

Status colors do not follow the palette so that "green-ish means success, red-ish means error" keeps its meaning in every palette.

### 3.2 Palettes

| Palette | Hue (H) | Primary | Secondary |
|---|---|---|---|
| Lavender (default) | 268 | `#c3a6ff` | `#ff9ccf` |
| Sakura | 335 | `#ffa3c4` | `#c9a8ff` |
| Mint | 165 | `#7fe0c1` | `#a9d8ff` |
| Sky | 212 | `#8cc4ff` | `#c7b2ff` |
| Peach | 18 | `#ffb08f` | `#ffd88a` |
| Lemon | 48 | `#ffe07a` | `#ffb3cf` |

- Primary and Secondary use the same values in dark and light. Both are soft colors, and text placed on them is always `On Accent` (a dark color).
- When adding a palette, confirm that `On Accent` on Primary has a contrast of at least 4.5:1.

### 3.3 Neutrals

Neutrals are defined in HSL using the palette hue H. Switching the palette also gives backgrounds and surfaces a faint tint.

| Token | Use | Dark (S / L) | Light (S / L) |
|---|---|---|---|
| Background | Whole-screen background | 26% / 8% | 60% / 97% |
| Surface | Screens, panels | 22% / 12% | 40% / 99% |
| Surface 2 | Cards, input backgrounds | 20% / 16% | 45% / 95% |
| Surface 3 | Pressable surfaces, tracks | 18% / 21% | 40% / 91% |
| Border | Borders, dividers | 18% / 26% | 30% / 86% |
| Text | Body text | 40% / 95% | 30% / 16% |
| Text 2 | Supporting text, labels | 14% / 74% | 16% / 34% |
| Text 3 | Notes, metadata | 12% / 62% | 12% / 42% |
| Disabled | Text and thumbs of disabled parts | 10% / 40% | 10% / 72% |
| On Accent | Text on accent colors | 45% / 14% | 45% / 14% |
| Accent Line | Thin lines, small accent text | Same as Primary | 55% / 42% |

Example: Lavender's dark `Background` is `hsl(268, 26%, 8%)`.

`Accent Line` is used for the focus outline, tab underlines, the navigation selection marker, slider fills, and small heading text. On light backgrounds a soft Primary is hard to tell apart from the background, so a deeper tone is used.

### 3.4 Status

| Token | Meaning | Dark | Light | Shape (9.1) |
|---|---|---|---|---|
| Success | Succeeded, connected, good | `#52d6bd` | `#0e7d68` | ✓ |
| Warning | Caution, unstable | `#ffc864` | `#8f5c00` | △ (with !) |
| Error | Failed, not found | `#ff6f8e` | `#c41f4b` | × |
| Live | Streaming | `#ff4f84` | `#b80f4a` | ● (filled) |
| (none) | Stopped, not connected | Text 2 | Text 2 | ○ (outline) |

- Success leans toward blue-green instead of green so that it is easier to tell apart from Error.
- Status is always shown with a shape and text, never with color alone (9.1).

### 3.5 Derived Colors

| Token | Definition | Use |
|---|---|---|
| Glow | Primary at 35% opacity | Glow of the primary button and toggles |
| Tint | Surface mixed with 14% Primary | Navigation selection background |
| Soft status background | Status color at 14% opacity (18% for Live) | Background of status chips |

---

## 4. Type

### 4.1 Fonts

| Use | Font | License |
|---|---|---|
| All UI | Noto Sans JP | SIL Open Font License 1.1 |
| Numbers, logs, identifiers | Noto Sans Mono | SIL Open Font License 1.1 |

- Fonts are not set on individual UI elements; they are used through typography styles (system design 14.8).
- The setup allows fallback fonts so that more languages can be added later (system design 14.9).

### 4.2 Typography Styles

| Style | Size / weight | Line height | Use |
|---|---|---|---|
| Heading | 22px / 700 | 1.3 | Screen headings |
| Subhead | 16px / 700 | 1.4 | Sections, setting names |
| Body | 14px / 400 | 1.6 | Body text, labels |
| Caption | 12px / 400 | 1.5 | Supporting text, notes |
| Button | 13px / 700 | 1.0 | Buttons |
| Numeric | 20px / 600, Mono | 1.2 | Displayed numbers (tabular digits) |
| Monospace | 12px / 400, Mono | 1.5 | Logs, identifiers |

- No text is smaller than 12px.
- Numbers lined up in columns use tabular digits.

---

## 5. Spacing

| Token | Value | Main use |
|---|---|---|
| XS | 4px | Between an icon and text |
| S | 8px | Between parts in the same group |
| M | 16px | Card padding, between cards |
| L | 24px | Screen padding, between sections |
| XL | 40px | Between large groups |

Spacing is chosen only from these five steps.

---

## 6. Shape

| Token | Value | Use |
|---|---|---|
| Radius S | 8px | Input fields, small parts |
| Radius M | 14px | Cards, navigation items, notifications |
| Radius L | 22px | Screens, dialogs |
| Radius Pill | 999px | Buttons, toggles, status chips |
| Border | 1px | Normal borders |
| Border (error) | 2px | Input fields in the error state |

Round pill-shaped buttons and toggles are the center of the visual impression.

---

## 7. Motion

- State changes (hover, toggles) are short transitions of about 0.12 to 0.15 seconds.
- Continuous motion is limited to meaningful cases, such as the blinking live indicator.
- When the OS setting to reduce visual effects is on, blinking and movement stop.

---

## 8. Theme

Users can choose the following appearance settings. All are saved as UI-specific settings (system design 14.38).

| Setting | Options | Default |
|---|---|---|
| Palette | Lavender, Sakura, Mint, Sky, Peach, Lemon | Lavender |
| Background | Dark, light | Dark |
| UI scale | Steps decided at implementation | 100% |

---

## 9. Easy for Everyone to Use

### 9.1 Never Use Color Alone for Status

- Every status carries the shape icon from 3.4 and text.
- Distinguishability for people who see color differently is checked with the "color view" switch on the preview page (red–green, blue–yellow, gray). That switch is a development check only and is not part of the application's screens.

### 9.2 Readability

- Body text has a contrast of at least 4.5:1 with its background; large text, icons, and component boundaries at least 3:1.
- Text on accent colors uses `On Accent`.

### 9.3 Ease of Operation

- Anything that can be pressed is at least 24px.
- Every operation is possible from the keyboard, and the focused part always shows an outline.
- Descriptions (tooltips) appear on keyboard focus as well as on mouse hover.

### 9.4 Following Settings

- The UI follows UI scale, palette, dark or light, and the reduced-motion setting.
- Widths follow their content instead of being fixed, so that text is not cut off when scaled up (system design 14.7).

---

## 10. Implementation in Unity

Details are defined in the UI foundation detailed design. Within the scope of this document, the following is assumed.

- Tokens are defined as USS custom properties (such as `--color-primary`), and each component's USS refers to them with `var()`.
- USS supports neither `color-mix()` nor HSL calculations. Therefore, neutrals and derived colors are prepared as precomputed values for each combination of palette and background. How they are generated is decided in the detailed design.
- Fonts are set from the theme style sheet using font assets of Noto Sans JP and Noto Sans Mono.

---

## 11. Change Procedure

1. Update this document and `docs/ja/ui/design-system.md`.
2. Update `docs/assets/ui/styleboard.html` with the same content.
3. Update the tokens on the Unity side (after implementation).
4. Check the display in dark, light, Japanese, English, and each color view.
