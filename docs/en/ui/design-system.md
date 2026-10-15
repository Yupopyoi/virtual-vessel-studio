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
| Neutral | Backgrounds, surfaces, borders, text | Very slightly tinted with the palette hue |
| Status | Success, warning, error, live | Fixed regardless of the palette |

Status colors do not follow the palette so that "green-ish means success, red-ish means error" keeps its meaning in every palette.

### 3.2 Palettes

| Palette | Hue (H) | Primary | Secondary |
|---|---|---|---|
| Lavender (default) | 268 | `#c3a6ff` | `#ff9ccf` |
| Sakura | 335 | `#ffa3c4` | `#c9a8ff` |
| Mint | 165 | `#34e8b8` | `#6cc4ff` |
| Sky | 212 | `#6bb5ff` | `#c7b2ff` |
| Peach | 18 | `#ff8a5c` | `#ffc04d` |
| Lemon | 48 | `#ffd23f` | `#ff9ec4` |

- The table values are base values. When shown, they are adjusted to the background (computed in HSL). Dark raises lightness by 6% for a brighter tone; light raises saturation by 25% and lowers lightness by 12% for a deeper tone, so the colors stay visible on pale backgrounds.
- Both are soft colors, and text placed on them is always `On Accent` (a dark color).
- When adding a palette, confirm that `On Accent` on Primary has a contrast of at least 4.5:1.

### 3.3 Neutrals

Neutrals are defined in HSL using the palette hue H. Saturation is kept low so that backgrounds and surfaces are close to colorless, because too much color in the background makes the whole screen look muddy. The palette shows through accents and decoration (3.6), not through the background.

| Token | Use | Dark (S / L) | Light (S / L) |
|---|---|---|---|
| Background | Whole-screen background | 8% / 8% | 20% / 97% |
| Surface | Screens, panels | 7% / 12% | 10% / 99% |
| Surface 2 | Cards, input backgrounds | 6% / 16% | 15% / 95% |
| Surface 3 | Pressable surfaces, tracks | 6% / 21% | 14% / 91% |
| Border | Borders, dividers | 6% / 26% | 10% / 86% |
| Text | Body text | 15% / 95% | 12% / 16% |
| Text 2 | Supporting text, labels | 5% / 74% | 8% / 34% |
| Text 3 | Notes, metadata | 4% / 62% | 6% / 40% |
| Disabled | Text and thumbs of disabled parts | 3% / 40% | 4% / 72% |
| On Accent | Text on accent colors | 45% / 14% | 45% / 14% |
| Accent Line | Thin lines, small accent text | Same as Primary | 58% / 38% |

Example: Lavender's dark `Background` is `hsl(268, 8%, 8%)`.

`Accent Line` is used for the focus outline, tab underlines, the navigation selection marker, slider fills, and small heading text. On light backgrounds a soft Primary is hard to tell apart from the background, so a deeper tone is used.

### 3.4 Status

| Token | Meaning | Dark | Light | Shape (9.1) |
|---|---|---|---|---|
| Success | Succeeded, connected, good | `#1affcc` | `#006652` | ✓ |
| Warning | Caution, unstable | `#ffb31a` | `#7a5200` | △ (with !) |
| Error | Failed, not found | `#ff8a9d` | `#b2001e` | × |
| Live | Streaming | `#ff85b1` | `#ad0040` | ● (filled) |
| (none) | Stopped, not connected | Text 2 | Text 2 | ○ (outline) |

- Success leans toward blue-green instead of green so that it is easier to tell apart from Error.
- Status is always shown with a shape and text, never with color alone (9.1).

### 3.5 Derived Colors

| Token | Definition | Use |
|---|---|---|
| Glow | Primary at 35% opacity | Glow of the primary button and toggles |
| Tint | Surface mixed with 14% Primary | Navigation selection background |
| Soft status background | Status color at 18% opacity | Background of status chips |
| Status line | Status color at 65% opacity | Border of status chips |
| Status fill (Success Fill / Warning Fill / Error Fill) | Vivid colors for graphics such as the level meter. Success and Warning use the status color on dark, and `#2ed3a4` and `#ffb224` on light. Error is `#ff5c7a` on dark and `#ff3d68` on light | Graphics such as the level meter |
| Knob | Text on dark; Surface on light | Inside of slider thumbs |
| Status surface | Dark: the soft status background composited over Surface 2, as an opaque color. Light: the status fill composited at 26% over Surface 2, because the light status colors are deep text colors that look grayish as a tint. Live has no fill and keeps the soft background on light too | Background of status chips, which stay readable on a colored band |
| Primary Wash / Secondary Wash | Primary or Secondary at 28% (dark) or 24% (light) opacity | Background of card titles (3.6) |
| Scrim | A very dark color of the palette hue (saturation 50%, lightness 3%) at 70% (dark) or 45% (light) opacity. Dark needs more, because the page is already dark and a lighter scrim barely shows | Darkens the whole screen behind a dialog |

### 3.6 Accent decoration

With near-colorless backgrounds, the palette shows through the following decoration. It adds flair but stays within a range that does not become noisy.

| Place | Decoration |
|---|---|
| Card | A 3px line along the top edge (Primary). A vertical bar left of the title, 4px wide, 16px tall, radius 2px (same color as the line). `Primary Wash` behind the title area |
| Cards in a row | Every other card uses Secondary for the line and bar and `Secondary Wash` for the background |
| Screen header | A 2px line along the bottom edge, as a horizontal gradient from Primary to Secondary |

- Gradients use only Primary to Secondary and are limited to thin lines and small ornaments. Do not use them behind body text or controls.
- Decorative lines, bars, and washes are ornaments and carry no meaning. Status and selection are still shown by shape, text, and `Accent Line`.
- Card titles on a wash use Text, and an automated test confirms at least 4.5:1 (9.2). Status chips at the right end of a title have an opaque background (status surface), so they stay readable on the band.
- The band is the header's background, so its height follows what the header holds, such as a chip.
- Decoration does not move, regardless of the reduce motion setting.

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
| Radius S | 8px | Input fields, status chips, small parts |
| Radius Control | 10px | Buttons, segmented controls |
| Radius M | 14px | Cards, navigation items, notifications |
| Radius L | 22px | Screens, dialogs |
| Radius Round | Half the height | Toggles, round icon buttons |
| Border | 1px | Normal borders |
| Border (error) | 2px | Input fields in the error state |

Buttons and status chips are rounded rectangles; toggles and icon buttons are round. Rounded corners and soft accent colors are the center of the visual impression.

In Unity UI Toolkit, a corner radius larger than half the element's height is drawn as an ellipse. Large values such as `999px` are therefore not used. Round parts (Radius Round) set the radius to exactly half the height.

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

An automated test on the Unity side (`ThemeTokenTests`) confirms these rules for every palette and both backgrounds. Status text must reach 4.5:1 against its soft background composited over a card.

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

- Token values live in `unity/VirtualVesselStudio/Assets/VirtualVessel/UI/Theme/design-tokens.json`. When this document changes, that file gets the same values.
- Tokens are defined as USS custom properties (such as `--vv-color-primary`, `--vv-space-m`, `--vv-radius-control`), and each component's USS refers to them with `var()`.
- USS supports neither `color-mix()` nor HSL calculations. Therefore, neutrals and derived colors are generated as precomputed values for each combination of palette and background (UI foundation detailed design 7.1).
- Fonts are set by a USS generated per font set (UI foundation detailed design 7.2).
- USS has no line-height. The line heights in 4.2 apply to the preview page (browser); in Unity each font's default line height is used.

---

## 11. Change Procedure

1. Update this document and `docs/ja/ui/design-system.md`.
2. Update `docs/assets/ui/styleboard.html` with the same content.
3. Update `design-tokens.json` and run "Virtual Vessel > UI > Generate Theme" in Unity.
4. Run `ThemeTokenTests` to check contrast ratios and agreement with the preview page.
5. Check the display in dark, light, Japanese, English, and each color view.
