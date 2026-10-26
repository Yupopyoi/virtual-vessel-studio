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

- Shape Radius Control (rounded rectangle), text Button style, padding 8px vertical and 18px horizontal.
- Moves down 1px while pressed.
- A Danger action runs only after a confirmation dialog (3.11).

### 3.2 Icon Button

- A 38px circle. Background Surface 3, 1px border.
- For parts with a state, such as mute, the on state uses background Secondary and icon On Accent.
- An icon alone rarely conveys its meaning, so an icon button always has a tooltip.

### 3.3 Toggle

- 44×26px with Radius Round (13px, half the height) and a 20px round thumb.
- Off: background Surface 3, thumb Text 2. On: background Primary, thumb On Accent, Glow.
- On and off differ by thumb position as well as color.
- Always placed with its label text.

### 3.4 Slider

- A 6px track. The part up to the value is Primary; the rest is Surface 3.
- The thumb is an 18px circle (fill Knob, edge Primary). Knob is light on dark backgrounds and white-ish on light ones, so the thumb does not look heavy on light.
- The current value is shown nearby as a number (Numeric or tabular digits) with its unit (for example `−12 dB`, `+4`).

### 3.5 Level Meter

- 24 separated bars showing a level such as volume.
- The normal range is Success, the upper 25% is Warning, and the very top (the top 2 of 24) is Error red, so it is clear the signal is about to clip.
- The lit segments use vivid fill colors (Success Fill, Warning Fill, Error Fill), so they do not look heavy or dull on light backgrounds.
- The amount is readable from the number of lit bars as well as the color.

### 3.6 Dropdown

- Background Background, 1px border, Radius S, a down arrow at the right end.
- Long option text is wrapped or the width grows; it is not truncated.
- The field is as wide as its longest option, whatever is selected, so it keeps its width when the choice changes and the list opens as wide as the field.
- The list opens below the field, above it when there is no room below, and right-aligned with the field when it would pass the right edge of the window; it never leaves the window.
- Option text can be data, such as device names or users' descriptions, so it is never parsed as rich text.

### 3.7 Text Field / Numeric Field

- Background Background, 1px border, Radius S, label above (Caption-like, Text 2).
- Example text (placeholder) is shown in Text 3, so it reads as an example and not as an entered value. It goes away when the field is focused. Left in view, the first characters typed were shown as the example, with the caret where the example ended (the user's report, 2026-10-25). The space before and after the text belongs to the text element, not the box: Unity drew an empty field's caret at the box's inner edge, ignoring the box's padding, so the first click put it left of where the text then started (the user's video, 2026-10-25). A one-line field does not take Enter: Unity moved the caret to the start on Enter, so after Japanese input committed a word with Enter, what was typed next went in near the start (the user's report, 2026-10-25).
- For now, a Text Field takes English letters, digits, symbols, and spaces only (#74). While it is focused, Japanese input (IME) is turned off, and other characters typed or pasted in are dropped. A line under the field says so, in Caption and Text 2. A value set by code (such as a saved Japanese description) is shown as it is. Unity's TextField (taking input through the Input System) left the caret one character short of each word Japanese input committed, so what was typed next went in before the word's last character ("入力欄が" became "入欄が…力", the user's video, 2026-10-25). Moving the caret back after the inserted text on each edit did not help. It is believed to be a Unity bug; this is undone once it is fixed.
- On focus: border Accent Line and a 3px Glow outside. Unity cannot draw an outer glow, so the Accent Line border alone shows focus (UI foundation detailed design 6.7).
- A numeric field uses right-aligned tabular digits with a unit box on the right (for example `Hz`).
- On error: a 2px Error border, and below it a △ icon and the error text. The error text says what is wrong and what to do.

### 3.8 Tab

- Underline style. The selected tab has text Text and a 2px Accent Line underline; others have text Text 2.
- Scrolls horizontally when there is not enough width.

### 3.9 Setting Row

- On the left, the setting name (Body, weight 500; Regular when the font has no medium weight) and its description (Caption, Text 3); on the right, the control (up to 260px).
- Rows are separated by a 1px divider.
- On narrow screens the control moves below the description. The name and description keep at least 240px: UI Toolkit breaks the line by the minimum width, not by the basis alone, so without it the text was squeezed beside the control, one character a line.

### 3.10 Card

- Background Surface 2, 1px border, Radius M, padding M.
- A title at the top (13px, weight 700, Text). A status chip (3.13) can sit at its right end.
- Cards in the same row share padding and layout.
- Add the top line, the title bar, and the wash behind the title. Every other card in a row uses Secondary (`design-system.md` 3.6).
- A collapsible card opens and closes its body when its title band is pressed.
  - A chevron (Text 2, 16px) sits at the right end of the title band. It points down while closed and up while open, and turns Text on hover.
  - The title band takes keyboard focus; Enter or Space opens and closes it. The focus outline is drawn 2px inside the title band (the band reaches the card's edges, so an outline outside it would cross the card's border and be clipped by the scrolling screen).
  - While closed, only the title band shows, with its bottom corners rounded too. The status chip sits in the title band, so it stays visible while closed.

### 3.11 Dialog

- Background Surface, 1px border, Radius L, padding L, a large shadow. Maximum width 420px.
  - The Unity implementation uses a scrim that darkens the whole screen instead of a shadow (UI foundation detailed design 6.4).
- Title (17px, weight 700), body (Text 2), buttons at the bottom right.
- When confirming an action that is hard to undo, the action button is Danger and the other is a Secondary "Cancel".
- The body says what will happen if the action runs.
- Read-only content needed to decide (such as a model's terms of use) may go under the body. Focus moves only between the buttons.

### 3.12 Tooltip

- Appears after the mouse rests on a part for about 0.5 seconds and disappears when it leaves. Also appears on keyboard focus. The time and position numbers are defaults and may change (UI foundation detailed design 7.6).
- Background Surface 3, a 1px border tinted toward Primary, Radius S, Caption-like text. Maximum width 260px.
- Shown above the part, or below it when there is no room above. Never extends beyond the screen edge.
- The content is one sentence saying what the action does. A shortcut key, if any, is added to the right.
- On a disabled part, it says why it cannot be pressed and when it becomes available.
- Do not write content that only restates the control (such as "This is a button").

### 3.13 Status Indicator (Chip)

- A rounded rectangle with Radius S holding an icon, text, and optionally a number (tabular).
- Colors and shapes per status follow `design-system.md` 3.4.
- The text is bold in a vivid status color, so it reads clearly like a lit LED. The background is the status surface (opaque) and the border is the status line.
- Only the live indicator blinks its icon slowly. With reduced motion it does not blink.

### 3.14 Notification

- Background Surface 2, 1px border, Radius M. A status icon on the left, a bold heading and body on the right.
- Errors mix a little Error into the border and background.
- The body says what to do next (for example "Check the USB connection, then try again.").
- By default notices appear at the bottom right; Info and Success hide after about 5 seconds and Warning after 8 seconds. Errors stay until closed. Up to 3 are shown at once. The position and times are defaults and may change (UI foundation detailed design 7.8).
- Notices that do not fit are kept waiting, not dropped, and appear in turn with errors first. The number waiting is shown at the corner end of the stack as "N more notices". It looks different from the notices so that it is not overlooked (a plus icon, bold Caption, Text on Tint, an Accent Line border, Radius S).

### 3.15 Progress Bar

- 8px high with Radius Round. Progress is filled with a gradient from Primary to Secondary.
- Above it, show what is progressing and the current value (for example "RVC training · epoch 120 / 300").

### 3.16 Navigation Item

- A vertical menu on the left with an icon and text.
- Selected: background Tint, text Text, a 3px Accent Line at the left edge.
- Each item has a tooltip describing the screen's role.
- Operated with Tab and Enter. The Up and Down keys also move between items.

### 3.17 Preview

- The stream video preview. Keeps 16:9 and uses Radius M.
- The frame rate is shown at the top right as a status indicator (3.13): Success while it keeps the set frame rate, Warning below it, with a tooltip explaining what it means.
- Resolution and bitrate are shown at the bottom left, such as "Stream preview · 1920×1080 · 6,000 kbps", on a pill with a Surface background and a 1px border so they read on any video. The bitrate is left out while nothing is being encoded.
- While there is no stream video yet, "No stream video yet" is shown in the middle and the numbers are hidden.
- A full-screen icon button sits at the bottom right. In full screen, only the video fills the screen; `Esc`, the same button, or the `F` key returns. The same button or `F` also enters it, so entering and leaving match. Right after entering, "Press Esc or F to return" shows for a moment.
- While a video is selected, `Esc` first clears the selection (placement editing, UI foundation detailed design 9.8); with nothing selected it leaves full screen.
- Full screen is a way of showing the preview and does not affect the stream output (Stream Render Target) (system design 14.23).
- While a video is selected, the mode switch (3.18) is shown at the top left.

### 3.18 Segmented

- Picks one of two to four options. Used for background, the placement editing mode, and similar. Language (which grows with each string table) and UI size (six steps) use a Dropdown.
- The outer box uses Radius Control and each item about Radius S (7px). The selected item has background Primary and text On Accent.
- With five or more options, or long text, use a Dropdown instead.

### 3.19 Media Frame

The frame for editing the placement of a video (game screen, second monitor, and so on) on the stream screen. Operation details are in Chapter 9 of the UI foundation detailed design.

- A selected frame shows a 2px Primary outline and eight handles (12px circles) at the corners and edges.
- Placement editing has two modes, "Layout" and "Crop". The current mode is always shown as "Mode [Layout | Crop]" at the top left of the preview, where it can also be switched.
- In crop mode, the outline and handles turn Secondary and the handles become bars along the edges. The whole video is shown faintly to indicate how far it can be uncropped.
- Pointing at a handle changes the cursor to ↔ ↕ ⤡ ⤢. Inside the frame it becomes the move shape, and over a locked frame the not-allowed shape.
- While operating, position and size (or zoom) are shown small above the frame.
- A locked frame shows "Locked" and no handles.

### 3.20 Layer List

- Lists the videos on the stream screen from front to back. The top is in front.
- A small "Top is in front" note sits above the list.
- Each row shows "≡" to indicate it can be grabbed, a small picture of the video (thumbnail, 40 × 23px, Radius 4px), the name, and its state (locked, hidden). The thumbnail makes it easy to match a row with the video on the preview; without the video it is plain (Surface 3).
- Clicking a row selects it; dragging it up or down changes the stacking order. While dragging, an Accent Line shows where it will land.
- Reordering can be undone (`Ctrl+Z`).
- From the keyboard, up and down move the selection, and `Ctrl`+up or down moves the selected video one step forward or backward, the same as `]` and `[` in the preview.
- The selected row has a Tint background and a 3px Accent Line on the left (as a Navigation Item). Hidden rows show a faint name and thumbnail and cannot be selected. A locked video cannot be reordered.

### 3.21 Key Hint

- A small box showing a shortcut or operation. Background Background, 1px border, radius 5px, Monospace 11px.
- The box fits the text height and is vertically centered with the text next to it. It does not stretch to the row height.

### 3.22 Scroll Bar

- No arrow buttons. An 8px-wide thumb with 4px radius sits in a 12px-wide bar.
- The thumb is Primary at 55% opacity, and 90% while hovered. The track is a rounded Surface 2.
- It stays quiet against the screen while the palette color keeps it cute.

### 3.23 Popup Menu (right-click menu)

- A menu that opens where the user points. Background Surface, 1px border, Radius M, 6px padding, at least 250px wide.
- Each row has a check showing whether it is on (Accent Line), the item name, and its shortcut (Key Hint). Rows are at least 32px high.
- The row under the mouse and the row chosen with the keyboard get a Tint background. Items that cannot be used have Disabled text and cannot be chosen.
- Separators are 1px Border lines.
- Each item has a tooltip saying what it does.
- When it opens it takes keyboard focus: up and down move, Enter runs, Esc closes. Running an item or pressing outside closes it. Focus then returns to where it was.
- Near the edges of the screen it opens to the left or upwards so that it stays on screen.

### 3.24 Table

- Each column sets its width, alignment (left or right), and kind of text. The header and every row use the same column definitions, so columns always line up.
- Text is left-aligned; numbers are monospace and right-aligned so their digits line up.
- Each cell has S (8px) padding on both sides, so neighbouring columns are always 16px apart.
- Headers use Caption and Text 3. Rows are at least 36px high, separated by a 1px Border line.
- A column can take the remaining width (such as a name). Text that does not fit ends with "…".
- When the room is narrower than the columns together, the table scrolls sideways inside itself and never spills out. A vertical wheel still scrolls the whole screen.
- Refreshing a table changes only the texts; rows are not rebuilt.
- Cell text can be data, such as names users typed, so it is never parsed as rich text.

---

## 4. Adding a Component

1. Confirm that the existing components are not enough.
2. Add the specification to this document and `docs/ja/ui/components.md`.
3. Add a sample to `docs/assets/ui/styleboard.html`, and to the component gallery screen in the Unity Editor (UI foundation detailed design 7.7).
4. Check the display in dark, light, Japanese, English, and each color view.
