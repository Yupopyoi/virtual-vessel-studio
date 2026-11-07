# Voice Lab Screen

## 1. Purpose

The screen for making the voice the user wants (system design 14.13 to 14.18). The user picks a voice close to the ideal, moves it a little at a time, makes it, and registers it. The workings (seeds, instructions, the training data, training runs) are not shown.

- Its identifier is `lab`, and its string keys are `nav.lab` and `lab.*`.
- It talks to Voice Lab only through `IVoiceLab` (Voice Lab detailed design 5). Candidates and previews play through `IAudioPreview`, on the monitor only (audio detailed design 5.5).

## 2. Layout

At the top, the Step Indicator (`components.md` 3.26) with STEP 1 to STEP 5. Under it, the open step's panel: a lead paragraph, its Cards, and a row of buttons at the bottom. The button that moves on names the step it leads to, such as "STEP 2: Shape the timbre".

| Step | Key | Content |
|---|---|---|
| STEP 1 Pick a base | `lab.step.base` | Chapter 3 |
| STEP 2 Shape the timbre | `lab.step.timbre` | Chapter 4 |
| STEP 3 Make it | `lab.step.build` | Chapter 5 |
| STEP 4 Try it with your voice | `lab.step.try` | Chapter 6 |
| STEP 5 Register | `lab.step.register` | Chapter 7 |

- Steps already reached can be pressed to go back. The steps ahead are disabled.
- Changing an earlier choice (another candidate, another timbre) closes the steps after it, since they no longer hold. A voice made from an earlier choice is not registered by mistake.
- While a voice is being made, the Step Indicator is disabled. The user can still open other screens, and the build goes on. The screen keeps its state while hidden.
- Leaving the screen stops a sample that is playing.
- Without a training environment (`Availability` is `NotSetUp`), the screen shows only a Card titled "Not ready to make voices yet" explaining it (`lab.not-set-up.*`). Activation (Voice Lab detailed design 8.1) will set it up from there.

## 3. STEP 1 Pick a Base

The user starts from an example, moves it with words, and picks a candidate by ear (the user's decision, 2026-11-07). The lead says that words only give a direction: the same words sound different in each candidate.

- Samples: until they are made, a line says so with Make the samples, a progress bar (Making the samples, "12 / 27"), and Stop. Play buttons are always shown, so the user sees where samples will be; one without its sample is disabled and its tooltip says to make the samples (the user's review, 2026-11-07; Voice Lab detailed design 8.3.1).
- Start from an example: one row per example, with play, its name (pressing it puts its words on the shelf), and its words.
- Move it with words: one setting row per group (`lab.group.*`), saying Pick one or Pick any, with its words as buttons, each with a play button for the sample of that word alone. A picked word is filled (Primary), so the state shows by shape and weight as well as color. Voice, Person, Pitch, and Energy are shown; Voice quality, Character, and Delivery are folded under More detail. A word replaces the one in its slot; pressing a picked word takes it off, except in a group that takes one word.
- The voice you ask for: the picked words read back as one sentence (`lab.ask.*` and each word's phrase), with the user's words after it.
- Add words: optional, up to 200 characters; a line says brackets, line breaks, and invisible characters are removed and imitating a real person is not allowed.
- Candidates per round: 1, 3, 6 (default), or 12, and Make candidates. While they are being made, a progress bar says Getting ready while the voice model loads, then Making candidates with "2 / 6", read every 0.3 s, with Stop; the buttons are disabled.
- Making candidates again replaces them; Make more adds as many again. Candidates are numbered in the order made.
- The candidates Card: Candidate Tiles (`components.md` 3.27) with pitch and range in Hz, and a sort control (As made, Lowest first, Highest first). Without `IAudioPreview`, the tiles have no play button. Under them, a folded Card shows the English instruction the last candidates were made from.
- Make candidates near this voice (enabled once a candidate is picked) adds six made with the picked one as the reference (Voice Lab detailed design 8.3.1). Their tiles say which candidate they came from ("From candidate 3"). The picked one stays picked.
- STEP 2 opens once a candidate is picked.
- When candidates or samples could not be made, an error status chip says so (`lab.base.failed`, `lab.base.samples-failed`).
- Words give no pitch in Hz. In a measurement with six seeds each, pitch levels from Low to Raised gave the same medians (about 260 Hz), and a pitch in Hz written in the words had no effect (2026-11-05). The pitch in Hz is set in STEP 2.

## 4. STEP 2 Shape the Timbre

- The timbre Card: the base (its candidate name) and its pitch change in Hz ("268 Hz → 338 Hz"), then five sliders (Pitch, Age, Brightness, Softness, Breathiness; softness from 0 to +6, the others from -6 to +6) with their ends and a tooltip each.
- When pitch, age, and brightness are all moved by 4 or more, a warning chip and a line say it may sound mechanical (`lab.timbre.mechanical`).
- Beyond the age that still sounded like the same person (more than 4 steps either way), a warning chip and a line say it may sound like someone else.
- Compare: Original plays the base; Shaped makes the preview (seconds) and plays it. The last preview is kept while nothing changes. While it is being made, a line says so. Pressing Shaped again with the same values keeps the one being made, which plays when ready. Only pressing it with changed values stops that one and starts again, and the line says the earlier one was stopped (`lab.timbre.reshaping`). A stopped one is not shown as a failure.
- Hold (the user's request, 2026-11-07): keeps the current timbre, up to six and each once. Each held one has play (its own preview), Go back to this (sets the knobs), and Remove, with a summary of its steps. Held timbres belong to the base, so picking another base clears them.
- Reset sets the five back to 0. STEP 3: Make this voice moves on.
- There are no intonation or pace sliders (system design 14.15).

## 5. STEP 3 Make It

- A note on the time (about 20 minutes per voice on an RTX 4070), and Start. A build does not start until Start is pressed, since it takes long.
- While building: a progress bar (`components.md` 3.15) with the stage (Making training speech, Learning the voice, Finishing) and the percent, read every 0.5 s, and Stop.
- While a stream is starting or running, Start is disabled and a warning chip and a line say why (system design 14.26).
- Completed: STEP 4 opens. Failed: an error chip, with "Not enough GPU memory…" when it ran out of GPU memory. Stopped: a chip saying so. The user stays on STEP 3 and can start again.
- The time left is not shown yet; the build reports a fraction but no estimate.

## 6. STEP 4 Try It with Your Voice

Trying a voice through the real-time conversion is the next stage (Voice Lab detailed design 8.3.4). Until then the step says so (`lab.try.later`) and has STEP 2: Back to the timbre and STEP 5: Use this voice.

## 7. STEP 5 Register

- The register Card: the voice name (required, up to 60 characters), a summary of the base (candidate name and pitch) and the timbre ("Pitch +4 · Age +4 · Brightness 0 · Breathiness 0"), and a note that what it was made from is kept.
- Register without a name shows "Enter a name" under the field.
- Registered: a success chip, the register button disabled, and Make another, which goes back to STEP 1 keeping the mood and the candidates.
- Use it now and the list of registered voices (system design 14.18) come with the next stage, with trying a voice.

## 8. Strings

Keys are `lab.*`; the Step Indicator's number is `common.step`, and the Candidate Tile's texts are `common.candidate.*`. The mood sliders' names and ends are `lab.mood.<key>`, `.low`, and `.high`, where `<key>` is the slider's key in the request (Voice Lab detailed design 8.3.1).

## 9. Open Issues

- The terms and disclaimers (Voice Lab detailed design 8.2) must be readable from this screen. They come with activation, before Voice Lab is offered outside development.
- The time left in STEP 3.
