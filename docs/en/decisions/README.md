# Decision Records

Why the design is as it is. A detailed design (`docs/en/detailed-design/`) describes the current behavior and its values; how each decision was reached is kept here, so the designs stay readable and the history is not lost (development workflow 11.2).

## What Goes Here

- What was tried, what the user saw (their reports and videos), and what was measured.
- Methods compared, and those dropped, with why.
- The date of each decision, and whose decision it was.

## How

- One record per area, such as `avatar-motion.md`. The section of the detailed design it explains links to it.
- A new decision is added at the end of its record, under a heading with its date and the section it explains. Earlier entries are not rewritten.
- Japanese and English are kept together, as for the other documents (`docs/ja/decisions/`).

## Records

| Record | Explains |
|---|---|
| [avatar-motion.md](avatar-motion.md) | Avatar detailed design chapter 7: the upper body, expressions, fingers, keeping the arms out of the body, the hands, and how fast the arms turn |
| [tracking-processing.md](tracking-processing.md) | Tracking detailed design chapters 8 and 9: smoothing, outlier removal, depth corrections, the reference pose, the body in 2D, and background removal |
| [recording.md](recording.md) | Recording detailed design chapters 1, 5.1, and 10: AV1 by default and H.264 as the fallback, the H.264 profile, and the color tags |
