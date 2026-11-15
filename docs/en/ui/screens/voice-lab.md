# Voice Lab Screen

## 1. Purpose

The screen for making the voice the user wants (system design 14.13 to 14.18). The user picks a voice close to the ideal, moves it a little at a time, makes it, and registers it. The workings (instructions, the training data, training runs) are not shown. The seed appears only in STEP 1's details and on the candidate tiles (the user's decision, 2026-11-14).

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
- While streaming or recording, a Card "Not available while streaming or recording", saying it can be used once they end, replaces the whole screen, tabs included (`lab.live-lock.*`; system design 14.26). Playing sound stops. When streaming and recording stop, the screen comes back.
- Without a training environment (`Availability` is `NotSetUp`), the preparation (2.1) shows in place of the steps. Only where preparing is not available (such as a development setup) does a Card titled "Not ready to make voices yet" show (`lab.not-set-up.*`).

### 2.1 Preparing

Activation (Voice Lab detailed design 8.1) happens in the Make a voice tab (`VoiceLabSetupView`; the mock-up was confirmed on 2026-11-11). One Card shows at a time, switched by the state.

| Page | When | Content |
|---|---|---|
| Check | Nothing built yet | One opening sentence (streaming and conversion with registered voices do not need it). Four rows: GPU, driver, location, internet connection. Each row shows its value and a Chip (OK / Action needed), with the condition in its help icon. Under a row that fails, the reason and what to do. The location row has Change (pick a folder). Read the terms can be pressed once all are met. Under it, a note that agreeing to the terms is required, and on its own line the time building the environment takes. |
| Terms | Read the terms was pressed, continuing or updating began after the version changed, or the version changed once built | The version and the full text (scrolling in a frame). Agree and start cannot be pressed until the agreement Toggle is on. Back. When built and only agreement is needed, the button is Agree and there is no Back. This holds with an update waiting too: the update is chosen afterwards on its own card, so agreeing never starts a download of several GB. When the user pressed Update and the terms showed, the buttons are Agree and start, and Back; the steps stay hidden until then. |
| Progress | Building, stopped, or failed | A state Chip. The four steps (downloading, unpacking, building the environment, checking it works), each with a Chip: done, running, paused, failed, or not yet. A progress bar (the whole build's share, written "44% overall"; while downloading, also the bytes and their share, the speed averaged over the last 5 seconds, and the time left at it). Under it, the record of the work (monospaced, one line each, the newest at the bottom, scrolling in its frame). Stop while building, Continue otherwise. On a failure, by its kind (network, damaged file, free space, building, check), what happened and what to do. |
| Done | The first build completed | That the voice made in the check was not kept. Make a voice opens STEP 1. |
| Update | Built, with a newer version | Shown above the steps. That voices can still be made while it updates, Update, and the download size. A Chip when it failed. |

- The check runs each time the Check page opens (and when the location changes).
- The terms are the bundled `terms/voice-lab-terms.<language>.md`, shown as headings, paragraphs, numbered items, and table rows. After agreeing, the Terms tab (2.3) reads the same files. The terms are not a license, so Credits and licenses in Setup does not list them (the user's decision, 2026-11-15).
- The record has one line per component downloaded and per step, with the time and what happens in the app's words (such as "22:14:05  Downloading: VoxCPM2 (4.3 GB)", "Building: installing the packages"). Output from uv or Python is not shown as it is (system design 4.27; the user's decision, 2026-11-11). The record lasts while the app is open.
- A build paused by streaming or recording says so.
- The progress is read every 0.25 seconds. If the app is closed, it continues the next time it opens.

### 2.2 Continuing

Above the steps of Make a voice, notices offer to continue (the user's request, 2026-11-15; Voice Lab detailed design 6.6).

- Unfinished voice: when STEP 1 or STEP 2 was left unfinished last time and this session has made no candidates yet. Unfinished voice, where it stopped ("Stopped at STEP 2 Shape the timbre") and when it was kept, and that making new candidates replaces it. Continue puts back the candidates, the one picked, the words, the line and the seed, the timbre, and the timbres held, and opens that step. Discard asks, then forgets it.
- A voice not registered yet: when a voice was made but not registered, while in STEP 1 or STEP 2 and nothing is being built. When the newest was made, and with more than one, how many more and that Made voices lists them. Continue (STEP 4) opens the newest in STEP 4.

### 2.3 The Terms Tab

A Terms tab sits beside Make a voice and Made voices, when the setup parts are there (the user's decision, 2026-11-15). The Card Voice Lab terms of use shows the version, a chip saying whether they were agreed to (✓ Agreed / Not agreed), and the terms (scrolling in a frame; headings, paragraphs, numbered items, and table rows, as in 2.1), in the screen's language, read again when the language changes.

## 3. STEP 1 Pick a Base

The user starts from an example, moves it with words, and picks a candidate by ear (the user's decision, 2026-11-07). The lead says that words only give a direction: the same words sound different in each candidate.

- How to make the prompt: the first setting row switches between From words and Write it (Segmented). With Write it, the samples, examples, word shelf, and own-words field are hidden, and a field for The prompt used to make the voice shows (several lines, up to 500 characters, empty with a placeholder only). A line under it says brackets, line breaks, and invisible characters are removed, and not to ask for a real person's voice. Make voice candidates with it empty marks the field with an error and makes nothing (the user's decision, 2026-11-11; Voice Lab detailed design 8.3.1).
- A Help Hint (components.md 3.28) after the heading The prompt used to make the voice says the voices are made with VoxCPM2 (OpenBMB) (the user's decision, 2026-11-11).
- Samples: until they are made, a line says so with Make the samples, a progress bar (Making the samples, "12 / 27"), and Stop. Play buttons are always shown, so the user sees where samples will be; one without its sample is disabled and its tooltip says to make the samples (the user's review, 2026-11-07; Voice Lab detailed design 8.3.1). When a later version adds words and only theirs are missing, a line says some words have no sample, with Make the samples (those already made are kept; `lab.base.samples-partial`). The examples show once the base sample is made.
- Start from an example: one row per example, with play, its name (pressing it puts its words on the shelf), and its words. Without the samples there is nothing to play, so the Card is hidden (the user's review, 2026-11-11).
- Move it with words: one setting row per group (`lab.group.*`), saying Select one or Select any, with its words as buttons, each with a play button for the sample of that word alone. A picked word is filled (Primary), so the state shows by shape and weight as well as color. Voice, Person, Pitch, and Energy are shown; Voice quality, Character, and Delivery are folded under Details. A word replaces the one in its slot; pressing a picked word takes it off, except in a shown group that takes one word. A folded group's word can be taken off, and taking off the character puts no character in the instruction (the user's decision, 2026-11-14). There are 11 characters (Friendly, Sweet and playful, Reserved and cool, Graceful, Teasing and cheeky, Shy, Caring, Intelligent, Dignified, Dreamy, Aloof), and the screen starts with Friendly picked.
- The prompt used to make the voice: the picked words read back as one sentence (`lab.ask.*` and each word's phrase), with the user's words after it.
- Add words: optional, up to 200 characters; a line says brackets, line breaks, and invisible characters are removed and imitating a real person is not allowed.
- Candidates per round: 1, 3, 6 (default), or 12, and Make voice candidates. While they are being made, a progress bar says Getting ready while the voice model loads, then Making candidates with "2 / 6", read every 0.3 s, with Stop; the buttons are disabled.
- Candidate details (`lab.base.details`, folded; the user's decision, 2026-11-14): Line to read (several lines, up to 200 characters, filled with the default line) and Reset to default (disabled while it is the default). One line says the line is for comparing the candidates and has nothing to do with voice conversion itself, that a longer line takes longer to make the candidates, and that brackets and line breaks are removed. Making with an empty line shows an error on the field, opens the details, and makes nothing. Seed (a number) is the first seed of the next candidates: it starts from a value that changes every session and moves on by the number made each time. One line says the same prompt, line, and seed make the same candidates again. Neither can be changed while candidates are made. Make again puts the run's line and seed back.
- Each candidate tile shows its seed under its pitch.
- Pressing Make voice candidates again replaces them; Make more adds as many again. Candidates are numbered in the order made.
- The candidates Card: Candidate Tiles (`components.md` 3.27) with pitch and range in Hz, and a sort control (As made, Lowest first, Highest first). Without `IAudioPreview`, the tiles have no play button. Under them, a folded Card shows the English instruction the last candidates were made from. Copy under it puts the instruction on the clipboard, and the button says Copied for two seconds (the user's request, 2026-11-15; to take it into a prompt written whole).
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
- A folded Details card with Training length (epochs): 150 (the default), 300, 600. The time cannot be promised, so the choices show epochs, not minutes (user's check, 2026-11-08). A line says more learns more of the voice's softness and roundness. Choosing one changes the time note above (about 20 minutes at 150, 30 at 300, 50 at 600). It cannot be changed while building (Voice Lab detailed design 8.3.3). In Unity's Editor only, a Test choice is added; choosing it, the time note says it takes minutes and the voice is not one to use (`lab.build.note.test`).
- While building: a progress bar (`components.md` 3.15) with the stage (Making training speech, Learning the voice, Finishing) and the percent, read every 0.5 s, and Stop.
- A record of the work: in the same monospace frame as the setup's (2.1), a line each with the time (the user's request, 2026-11-15; `VoiceLabBuildLog`). A line when a stage begins, and each time its count (lines of training speech, epochs) moves on by a twentieth of its total (for example "14:20:05  Learning the voice  30 / 600 epochs"); at the end, Done, Could not be made, or Stopped. The scripts' output is never shown as it is (system design 4.27). The record stays until the next build and is rewritten when the language changes.
- Completed: STEP 4 opens. Failed: an error chip, with "Not enough GPU memory…" when it ran out of GPU memory. Stopped: a chip saying so. The user stays on STEP 3 and can start again.
- The time left is not shown yet; the build reports a fraction but no estimate.

## 6. STEP 4 Try It with Your Voice

Tries the voice made in STEP 3, not yet registered (system design 14.17; Voice Lab detailed design 8.3.4). Leaving STEP 4 (for another step or screen) stops what plays and puts the voice conversion back as it was before the trial (the chosen voice, on or off, the pitch settings). Voice Lab is not used while streaming or recording, so a trial never reaches the stream.

- Voice to try card: Recording or Microphone (Segmented).
  - Recording: the file's name and Choose file (WAV, M4A, MP3). Play the original plays the recording as it is; Play the converted voice converts it with the stream's processing first; both play to the monitor only (Voice detailed design 8.5; user's decision, 2026-11-08). A progress bar shows while converting. The same recording, voice, and pitch settings play again without converting again. Stop stops playing and converting.
  - Microphone: Try with the microphone runs the stream's conversion with the voice made. With the microphone's Hear off, a warning chip and "Turn Hear on" show, with a line to use headphones (user's decision, 2026-11-08).
  - A failed conversion shows an error chip (`lab.try.failed`).
- Converting a recording goes on after leaving STEP 4: switching steps cancelled it, and it could not be started again once back (the user's report, 2026-11-15). What it makes while unseen is not played; back in STEP 4, Play the converted voice plays it without converting again. Only a stream or a recording starting cancels it (system design 14.26).
- The pitch sits inside the Voice to try card, followed at the bottom by Play the converted voice, Stop playing, and the conversion's progress with Stop converting (only while converting). Stopping playback and stopping a conversion are separate buttons: one Stop did both, and what it stopped was not clear (the user's review, 2026-11-15). After the pitch, Finish (a subheading): Body, Clarity, Brightness, and Air sliders (-6 to +6 dB, with what each end means) and Reset the finish. A change is kept at once with the run, or with the registered voice being tried. Play the converted voice applies it without converting again, and the microphone trial hears it at once (Voice detailed design 8.6; the user's decision, 2026-11-15). Pitch (a subheading, with a line and space above to part it from what comes before; the user's review, 2026-11-15): Match the pitch automatically (on by default), the line "Your voice 200 Hz → this voice 285 Hz (+6)" ("not measured" before measuring), Measure again, and an Adjustment slider (-24 to +24). With recordings, the pitch measured once is used by the next conversion.
- STEP 2 Fix the timbre, and STEP 5 Use this voice.

## 7. STEP 5 Register

- The register Card: the voice name (required, up to 60 characters), a summary of the base (candidate name and pitch) and the timbre ("Pitch +4 · Age +4 · Brightness 0 · Breathiness 0"), and a note that what it was made from is kept.
- Register without a name shows "Enter a name" under the field.
- When Register cannot be pressed, a line under the buttons says why (the user's review, 2026-11-15; it was greyed out without a word). Trying a registered voice opened from Made voices: it is registered already and needs no registering again; rename or delete it in Made voices. No voice made: make it in STEP 3 to register it.
- Registered: a success chip, the register button disabled, Make another, which goes back to STEP 1 keeping the mood and the candidates, and See made voices, which opens the Made voices tab (the user's request, 2026-11-15). The success chip has room between it and the toggle above.
- STEP 4 Try it with your voice goes back to STEP 4, before or after registering, since the run keeps the voice's files (user's check, 2026-11-08).
- Use it for the stream once registered (on by default; system design 14.18). On, the registered voice becomes the Voice Profile's voice for the stream, and the success chip says it was registered and set as the voice for the stream. Hidden without the voice conversion.

## 8. Made Voices

A tab bar at the top of the screen: Make a voice (chapters 3 to 7) and Made voices (the user's decision, 2026-11-07). Each tab keeps its state while the other is open, and a build goes on in Make a voice.

- Registered voices Card: one row per voice, newest first, with play (the base it was made from), its name, its pitch in Hz, the date, Try, Rename, and Delete. The name, the pitch, the date, and In use or Use for the stream each sit in a column of fixed width, so the buttons do not move with the name's length or with whether the voice is in use (the user's request, 2026-11-15). A long name is cut with an ellipsis, and its tooltip shows it whole. Rename turns the name into a text field where the name was, by the play button, and shows Save in Rename's place (the user's request, 2026-11-15), with the register rules (required, up to 60 characters).
- A registered voice's row also has Use for the stream, which makes it the voice for the stream. The voice for the stream shows an In use chip in its place, and its Delete is disabled with a tooltip saying why (use another voice first; Voice Lab detailed design 8.6).
- Training runs Card: one row per run, newest first, with play (its base), the date, a status chip (Completed, Failed, Cancelled midway, Making), the words picked and the timbre as one line, the voices registered from it, a note field (up to 500 characters, saved when it loses focus), Try (completed runs only), Make again, and Delete.
- Try opens Make a voice's STEP 4 with that voice (user's check, 2026-11-08). The run's words, base, and timbre come back as with Make again, so STEP 2 can adjust it and STEP 5 register it. A registered voice is tried by its own folder, so it can be tried even when its run is gone. Make again still opens STEP 2.
- Delete asks first, in a dialog that names what goes and what stays ("The voice is deleted. The run it was made from stays."). A run being made has Delete disabled with the reason.
- Make again opens Make a voice at STEP 2 with the run's words, base, and timbre; without the base's audio it opens STEP 1 with the words and a line saying the base is gone.
- Empty: a line saying nothing has been made yet, with Make a voice.
- Without a training environment the tab still lists what was made; Make again is disabled.
- Stored data Card: rows for Samples and Voice candidates, each with its size (KB or MB) and Delete, explained in a Help Hint after the name. Each is deleted on its own after a confirmation, and Delete is disabled when there is nothing to delete (the user's request, 2026-11-11: to save space, and for testing; Voice Lab detailed design 8.6). Back in Make a voice after deleting the samples, STEP 1 offers Make the samples.
- The same Card has a row for the Voice making environment (Python). Its size is counted off the main thread when the tab opens, showing Measuring meanwhile. Only an environment the app built can be deleted, after a confirmation; one set up elsewhere, such as a developer's, cannot, and its tooltip says why. Without an environment the row is not shown (the user's request, 2026-11-11; Voice Lab detailed design 11).

## 9. Strings

Keys are `lab.*`; the Step Indicator's number is `common.step`, and the Candidate Tile's texts are `common.candidate.*`. The mood sliders' names and ends are `lab.mood.<key>`, `.low`, and `.high`, where `<key>` is the slider's key in the request (Voice Lab detailed design 8.3.1).

## 10. Open Issues

- A build already running goes on while streaming or recording. Whether downloading and the check (which uses the GPU) affect the stream is not measured.
- The time left in STEP 3.
