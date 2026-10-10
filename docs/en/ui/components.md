# Shared Components

## 1. Purpose

This document defines the appearance and behavior of the shared components that screens are built from (system design 14.5, 14.34).

All values (color, spacing, radius, type) use the tokens in `design-system.md`. The visual reference is `docs/assets/ui/styleboard.html`.

For each component, the following states are defined:

- Normal
- Hover
- Focused (keyboard focus)
- Pressed
- Disabled
- Error (input components only)

---

## 2. Common Behavior

| Item | Behavior |
|---|---|
| Focus | A part selected with the keyboard shows a 2px `Accent Line` outline, 2px outside |
| Disabled | Background `Surface 2`, text `Disabled`, no shadow. A tooltip tells why it cannot be pressed |
| Tooltip | Added to parts that need an explanation (3.12) |
| Text | Always taken from localization keys (`ui-guidelines.md`) |
| Size | Anything that can be pressed is at least 24px |

---

## 3. Components

### 3.1 Button

| Kind | Appearance | Use |
|---|---|---|
| Primary | Background Primary, text On Accent, Glow shadow | The most important action on a screen (about one per screen) |
| Secondary | Background Surface 3, text Text, 1px border | Other actions |
| Danger | Soft Live background, text Live, Live border | Actions that are hard to undo, such as ending the stream |

- Shape Radius Pill, text Button style, padding 9px vertical and 18px horizontal.
- Moves down 1px while pressed.
- A Danger action runs only after a confirmation dialog (3.11).

### 3.2 Icon Button

- A 38px circle. Background Surface 3, 1px border.
- For parts with a state, such as mute, the on state uses background Secondary and icon On Accent.
- An icon alone rarely conveys its meaning, so an icon button always has a tooltip.

### 3.3 Toggle

- A 44×26px pill with a 20px round thumb.
- Off: background Surface 3, thumb Text 2. On: background Primary, thumb On Accent, Glow.
- On and off differ by thumb position as well as color.
- Always placed with its label text.

### 3.4 Slider

- A 6px track. The part up to the value is `Accent Line`; the rest is Surface 3.
- The thumb is an 18px circle (fill Text, edge Accent Line).
- The current value is shown nearby as a number (Numeric or tabular digits) with its unit (for example `−12 dB`, `+4`).

### 3.5 Level Meter

- 24 separated bars showing a level such as volume.
- The normal range is Success; near the top (upper 25%) it is Warning.
- The amount is readable from the number of lit bars as well as the color.

### 3.6 Dropdown

- Background Background, 1px border, Radius S, a down arrow at the right end.
- Long option text is wrapped or the width grows; it is not truncated.

### 3.7 Text Field / Numeric Field

- Background Background, 1px border, Radius S, label above (Caption-like, Text 2).
- On focus: border Accent Line and a 3px Glow outside.
- A numeric field uses right-aligned tabular digits with a unit box on the right (for example `Hz`).
- On error: a 2px Error border, and below it a △ icon and the error text. The error text says what is wrong and what to do.

### 3.8 Tab

- Underline style. The selected tab has text Text and a 2px Accent Line underline; others have text Text 2.
- Scrolls horizontally when there is not enough width.

### 3.9 Setting Row

- On the left, the setting name (Body, weight 500) and its description (Caption, Text 3); on the right, the control (up to 260px).
- Rows are separated by a 1px divider.
- On narrow screens the control moves below the description.

### 3.10 Card

- Background Surface 2, 1px border, Radius M, padding M.
- A title at the top (13px, weight 700, Text 2). A status chip (3.13) can sit at its right end.
- Cards in the same row share padding and layout.

### 3.11 Dialog

- Background Surface, 1px border, Radius L, padding L, a large shadow. Maximum width 420px.
- Title (17px, weight 700), body (Text 2), buttons at the bottom right.
- When confirming an action that is hard to undo, the action button is Danger and the other is a Secondary "Cancel".
- The body says what will happen if the action runs.

### 3.12 Tooltip

- Appears after the mouse rests on a part for about 0.5 seconds and disappears when it leaves. Also appears on keyboard focus.
- Background Surface 3, a 1px border tinted toward Primary, Radius S, Caption-like text. Maximum width 260px.
- Shown above the part, or below it when there is no room above. Never extends beyond the screen edge.
- The content is one sentence saying what the action does. A shortcut key, if any, is added to the right.
- On a disabled part, it says why it cannot be pressed and when it becomes available.
- Do not write content that only restates the control (such as "This is a button").

### 3.13 Status Indicator (Chip)

- A pill with an icon, text, and optionally a number (tabular).
- Colors and shapes per status follow `design-system.md` 3.4.
- Only the live indicator blinks its icon slowly. With reduced motion it does not blink.

### 3.14 Notification

- Background Surface 2, 1px border, Radius M. A status icon on the left, a bold heading and body on the right.
- Errors mix a little Error into the border and background.
- The body says what to do next (for example "Check the USB connection, then try again.").

### 3.15 Progress Bar

- An 8px pill. Progress is filled with a gradient from Primary to Secondary.
- Above it, show what is progressing and the current value (for example "RVC training · epoch 120 / 300").

### 3.16 Navigation Item

- A vertical menu on the left with an icon and text.
- Selected: background Tint, text Text, a 3px Accent Line at the left edge.
- Each item has a tooltip describing the screen's role.

### 3.17 Preview

- The stream video preview. Keeps 16:9 and uses Radius M.
- The frame rate is shown at the top right, and resolution and bitrate at the bottom left.
- A full-screen icon button sits at the bottom right. In full screen, only the video fills the screen; `Esc`, the same button, or the `F` key returns.
- Full screen is a way of showing the preview and does not affect the stream output (Stream Render Target) (system design 14.23).

---

## 4. Adding a Component

1. Confirm that the existing components are not enough.
2. Add the specification to this document and `docs/ja/ui/components.md`.
3. Add a sample to `docs/assets/ui/styleboard.html`.
4. Check the display in dark, light, Japanese, English, and each color view.
