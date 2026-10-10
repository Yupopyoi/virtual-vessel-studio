# UI Guidelines

## 1. Purpose

This document defines the rules to follow when building screens and components (system design 14.37). It applies to both developers and Claude Code.

Visual values are defined in `design-system.md`, and component specifications in `components.md`.

---

## 2. Before You Start

1. Read `design-system.md` and `components.md`.
2. Open `docs/assets/ui/styleboard.html` in a browser and check the appearance.
3. Consider whether the screen can be built from the existing shared components.

---

## 3. Rules

1. Use only the tokens in `design-system.md`. Screens do not decide their own colors, text sizes, or spacing.
2. Prefer the shared components. If a new appearance is needed, add it to `components.md` first.
3. Do not write UI strings directly in C# or UXML; use localization keys (system design 14.6).
4. Do not set fonts individually; use typography styles.
5. Check the display in both Japanese and English. English tends to be longer, so check for clipped or wrapped text.
6. Runtime state is the source of truth, and the UI displays it. Do not treat UI state as runtime state (system design 14.29).
7. Do not operate runtime functions directly from the UI; go through a boundary such as commands.
8. Separate user-facing errors from developer diagnostics. Normal screens do not show internal information such as process IDs, ports, or Python (system design 4.27).
9. Never use color alone for status. Always add a shape icon and text (`design-system.md` 9.1).
10. When adding a new design pattern, reflect it in `design-system.md` or `components.md` and in the preview page.
11. After implementation, check the display with screenshots or similar (system design 14.35). Major screens and large changes get a final review by the user (system design 14.36).

---

## 4. Writing

- Use words users recognize, not names of internal mechanisms (for example "Notifications", not "Webhook settings").
- Buttons state what happens when pressed, with a verb (for example "Start streaming", "Stop recording").
- Errors say what happened and what to do. Avoid apologies and vague wording.
- A tooltip is one sentence saying what the action does (`components.md` 3.12).
- Keep Japanese and English equivalent in meaning. Do not add information to only one of them.

---

## 5. Checking the Display

When the UI changes, check at least the following combinations.

| Item | What to check |
|---|---|
| Background | Dark, light |
| Language | Japanese, English |
| Palette | The default (Lavender) and one palette with a bright accent (such as Lemon) |
| Color view | Statuses remain distinguishable in red–green, blue–yellow, and gray |
| States | Normal, hover, focused, disabled, loading, error |
| Size | Text is not cut off when the UI is scaled up |
| Operation | Everything works from the keyboard |
