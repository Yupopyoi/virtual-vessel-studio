# Voice Conversion: Decisions and History

A record of what the real-time voice conversion prototype tried and what came of it. Voice has no detailed design yet; one will be written from these results.

## 2026-11-01 A real-time RVC prototype in Unity (system design 9.5, 9.10)

### Purpose

System design 9.5 runs RVC on Unity Inference Engine, with 200 ms as the standard. The prototype checks whether that holds on the real path, as the processing stage of audio detailed design 8.3.

### Measurement conditions

- RTX 4070, the Unity 6000.6.4f1 Editor (Mono), `com.unity.ai.inference` 2.6.1, GPUCompute
- The input is 12 s of a male voice made with VoxCPM2 (median fundamental 115 Hz). The target is RVC v2 (40 kHz, 150 epochs, no index) trained on 441 s of a female voice made with VoxCPM2. Transposed by +13
- The numbers are measured on recorded speech. The user listened with a real microphone and monitor (2026-11-01, including the model with the speaker as an input): "mostly good", "no problem for now". Latency remains to be improved

### What changed from VCBM

VCBM converts in overlapping 2 s windows, the shape of a batch WAV conversion. The prototype takes the shape of upstream RVC's realtime path (`infer/rtrvc.py`).

| | VCBM | Prototype | Why |
|---|---|---|---|
| HuBERT | 2 s | 1 s | Enough context for the features (speaker similarity does not fall) |
| RMVPE | 224 frames | 32 frames, with cached pitch | As upstream; the whole window is not estimated per block |
| Generator | The whole window | Flow on the tail, the decoder on one block | As upstream; the costly vocoder runs on a block only |
| Joining | Fixed overlap | Crossfade placed by correlation | As upstream, with the level corrected by the correlation |

VCBM's RMVPE export, which unrolls the GRU, is used as it is, because Inference Engine has no GRU.

### Processing time

Waiting for each block to complete (100 ms, in ms):

| Stage | Median |
|---|---|
| Whole | 50.7 (60.0 at most) |
| Of which GPU wait | HuBERT 9, RMVPE 21, generator 8 |
| Of which issuing on the main thread | About 18 (RMVPE 8.4) |

At real time on a 60 Hz frame loop (`VoiceServiceRealtimeBenchmark`, 20 s):

| Item | Result |
|---|---|
| Blocks processed | 199, none dropped |
| One block's work | Median 85.9 ms, at most 90.3 ms |
| Cost of an ordinary frame | 0.1 ms |
| Cost of a frame that issues work | About 17 ms, at most 22.5 ms |

The work takes longer than when waiting because issuing, reading RMVPE and HuBERT, and reading the generator each wait for a frame boundary.

### Latency: the standard is not met

A sample takes **about 270 ms** from input to output: the 100 ms block, 86 ms of work, and 85 ms behind in the file (61 ms of crossfade and guards, 30 ms of burst margin). Device buffers add to this. It is over the 200 ms standard.

Candidates, none measured:

- A tail guard of 10 ms instead of 30 ms: -20 ms, with a larger level ripple at the joins (next section)
- A 50 ms block: -50 ms. Waiting, the work takes 49 ms, so at real time it is not expected to keep up; blocks would have to overlap
- A path that does not wait for frame boundaries: the constraint is that GPU work is issued on the main thread

### Quality

Speechbrain ECAPA speaker similarity to the target voice, and pitch correlation with the input:

| | Speaker similarity | Pitch correlation |
|---|---|---|
| Upstream RVC's offline conversion | 0.881 | 0.996 |
| Prototype, waiting per block | 0.885 | 0.995 |
| Prototype, at real time | 0.886 | 0.993 |
| For reference: the training data itself | 0.836 | - |
| For reference: the input voice | 0.260 | - |

### Findings

- The level falls by about 2 dB at the start of each block. Causes were checked in turn: guards at the decoder's edges changed nothing; noise tied to time, with the crossfade's level corrected by correlation, changed nothing; a tail guard of 30 ms instead of 10 ms improved it from -2.3 dB to -1.6 dB and halved the 10 Hz ripple. Weak features at the window's end look like part of the cause. At real time -1.2 dB remains. **This needs listening to judge**
- A `ReadbackRequest` result can only be read during the frame it completes. Waiting for the slower model and reading both loses the faster one's data and throws. Each is read as soon as it is done
- Asynchronous readback cannot be checked in a batch EditMode run; the real-time benchmark runs in PlayMode

### How to try it

Models and speech are outside the repository. Put the `.sentis` files (`hubert_16000`, `rmvpe_32`, `gen_b10`, and the speaker's `gen_b10.spk`) in `VoiceModels/Prototype` in the Data Root, and turn it on under Voice conversion (prototype) on the Diagnostics screen in Developer Mode. The Editor must restart after the plugin is updated.

## 2026-11-01 Activation and the training environment (system design chapter 13)

The user's decision: the feature for making voices does not work until the user activates it, and activation builds the training environment. An internet connection may be assumed.

This way PyTorch, CUDA, and the models come to the user's PC from their sources instead of inside the application. The download stays small, and NVIDIA's proprietary libraries are not redistributed.

## 2026-11-01 License check

This is not a legal review. It records what the sources said on this date.

| Component | License | Evidence | Action |
|---|---|---|---|
| RVC (code) | MIT | The repository's LICENSE | Keep the notice |
| Pretrained models, `rmvpe.pt`, HuBERT | MIT | Shown on `lj1995/VoiceConversionWebUI`; its README is empty, with nothing per file | Keep the notice |
| The pretraining data (VCTK) | CC BY 4.0 | RVC's README and VCTK's release notes | Credit VCTK |
| ContentVec | MIT | The repository's LICENSE | Keep the notice |
| RMVPE (code) | Apache-2.0 | The repository's LICENSE | Keep the notice |
| VoxCPM2 | Apache-2.0 | The repository and model card | See the conditions below |
| `praat-parselmouth` | GPLv3 | Package metadata | Left out of the training environment (the user's decision, 2026-11-01) |
| `soxr`, `FreeSimpleGUI` | LGPL | Package metadata | FreeSimpleGUI is left out; soxr stays replaceable |
| NVIDIA libraries | Proprietary | Package metadata | Not redistributed; fetched to the user's PC |

**VoxCPM2's model card forbids impersonation, fraud, and disinformation, and asks that AI-generated content be labeled.** The voice feature's UI and terms will reflect this.

RVC imports `praat-parselmouth`, but only the `pm` pitch method uses it, and this prototype uses `rmvpe`. CLAUDE.md chapter 35 requires the user's approval for GPL, and the user decided to leave it out. How to avoid the import without changing RVC itself is checked when the training environment is built. If RVC itself would have to change, the user's approval is obtained under CLAUDE.md 3.5.

The four unconfirmed packages were checked (2026-11-01).

| Package | License | Environment | Action |
|---|---|---|---|
| `kaldiio` | **NTT's evaluation license; not open source** | VoxCPM2 only, through `funasr` | **Left out** |
| `llvmlite` | BSD-2-Clause, Apache-2.0 WITH LLVM-exception | RVC, through `librosa` | Keep the notice |
| `torchcodec` | BSD-3-Clause | VoxCPM2 only | Keep the notice; FFmpeg's handling unconfirmed |
| `gradio` | Apache-2.0 | None (left out of the minimal environment) | Not needed |

`kaldiio`'s license limits use to internal evaluation and forbids distribution to third parties. It is treated as unusable even when fetched to the user's PC. `voxcpm` lists `funasr` as a dependency, but the package's code does not import it, and `funasr`'s `load_utils` is what imports `kaldiio`. Installing `voxcpm` without dependencies and locking only what is needed should avoid it, but that has not been tried. It is checked before the second stage, which uses VoxCPM2.

All license texts were searched for words such as `non-commercial`. The hits in `numpy`, `scipy`, `torch`, and `pillow` are general clauses for bundled parts, not limits on use. VoxCPM2's environment is checked again once its dependencies are narrowed down.

## 2026-11-01 Making a voice's `.sentis` without the Editor

Converting ONNX to `.sentis` exists only in the Unity Editor, so a distributed application cannot do it. Voices share a model structure, so the question was whether only the weights can be swapped.

### Method

Inference Engine's `Constant.array` is `internal`, and the public API cannot change it. So the weight bytes inside the `.sentis` file are written directly.

1. Developers make a template `.sentis` from the pretrained model in the Editor
2. From the template and one voice, a table gives each weight's place in the file (`tools/voice/weight_manifest.py build`)
3. On the user's PC after training, the trained weights are written at those places (`apply`). This needs only Python and takes 1.3 s

### Results

**For a voice not used to make the table, the Editor's file and Python's are identical by SHA-256.** All 352 weights were placed. Converting with the result in Unity gives speaker similarity 0.888 and pitch correlation 0.995, the same as a voice from the Editor, and 199 blocks ran at real time with none dropped.

### Findings

- With a fixed speaker, the conditioning weights are folded into constants on import and cannot be swapped. **The speaker vector becomes a model input**, kept apart in a `.spk` file
- `emb_rel_k` is stored transposed and padded to the window, so a new window length needs a new table
- A weight of a few bytes also matches elsewhere by chance, so building the table also checks that the Editor's file holds the value there

### Caution

The `.sentis` format is not a published specification. After updating `com.unity.ai.inference`, make the template and table again and repeat this comparison.

## 2026-11-01 Showing the terms of use and disclaimers (requirement)

The user's decision: the feature for making voices must always be able to show the information its terms of use need, and its disclaimers. Voice's detailed design treats this as a requirement.

- **Shown before activation.** The content is shown and agreed to before building the training environment starts. The version agreed to and the time are saved
- **Readable at any time afterwards.** The application has a place showing the same content, reached from Credits and licenses in Setup
- **In Japanese and English.** It lives in string tables or bundled documents, not in code
- **Agreement is asked again when the content changes.** The terms carry a version

It includes at least the following. The wording is reviewed by a legal professional before release.

- Prohibited uses: impersonation, fraud, and disinformation (VoxCPM2's model card), and using another person's voice without permission
- The request to label AI-generated content (likewise)
- Training only on your own voice, or a voice you hold the rights to
- That responsibility for a voice made and its use lies with the user
- No warranty and limited liability, including those of Apache-2.0 and MIT
- The external components fetched, their licenses, and required notices (the license table above, including VCTK's attribution)
- That an internet connection is needed and large files are fetched, and from where
- That training audio is processed on the user's PC and not sent outside

## 2026-11-01 How the training environment is made (system design 13.4)

The user's decision: instead of distributing a Runtime Package built by the project, the environment is built on the user's PC from uv and a lock at activation.

Compared:

| | Distributing a Runtime Package | Building on the user's PC (adopted) |
|---|---|---|
| Reproducibility | High | Kept by pinned hashes |
| Redistributing PyTorch and CUDA | Needed | Not needed |
| What the project hosts | Packages of several GB | Information and copies only |
| When a source disappears | Not affected | New users are affected |

Countermeasures for disappearing sources are in Voice Lab detailed design 8.1. Limiting training to NVIDIA GPUs, silence as the default on conversion failure, and putting SQLite off were approved at the same time.

## 2026-11-01 Verifying the minimal training environment

The same steps as distribution: an environment built into an empty folder with uv alone, then a voice made from start to end.

- Dependencies fell from 136 packages to 66. `praat-parselmouth`, `gradio`, and `kaldiio` are not among them.
- In place of `praat-parselmouth`, an empty `parselmouth.py` raises an error if used. RVC itself is unchanged. Preprocessing, RMVPE, HuBERT, and training (150 epochs) all ran to the end without calling it.
- The voice exported from this environment, written into the template by the table, is identical by SHA-256 to the Editor's conversion.
- Unity's real-time test ran 199 blocks with none dropped, at 86 ms per block.
- Speaker similarity is 0.869, a little under the earlier environment's 0.888. Pitch correlation is the same at 0.994. Whether that is training's randomness or dependency versions has not been separated, and it has not been checked by listening.
- The dependency definition is in `services/voice-lab/`.

## 2026-11-01 The wobble on held notes

The user's report: speech is fine, but a long held note goes wrong.

Level and upper-band variation were measured on held notes in a real recording (lower is steadier).

| Method | Level | Timbre |
|---|---|---|
| Upstream RVC, offline | 0.126 | 0.197 |
| Upstream RVC, realtime (default, 2.5 s history) | 0.251 | 0.418 |
| Upstream RVC, realtime (5.36 s history) | 0.143 | 0.377 |
| Unity (1 s window) | 0.231 | 0.417 |
| Unity (5.4 s window; later withdrawn, next section) | 0.126 | 0.356 |

Pitch is steady; the level and timbre wander. Upstream's realtime code wanders the same way, so this is the nature of running on short context, not an implementation error. In speech the sound keeps changing, which hides it.

A longer history steadies upstream too, matching the earlier prototype's note of 5.36 s. Lengthening only HuBERT gives 0.207, so the generator needs the long context as well. Timbre improves less, and a gap to offline remains.

The cost in Unity: waiting, a block's work goes from 51 ms to 72 ms, but at real time issuing lines up with frames, so it stays at 85 ms, with no blocks dropped and no added delay.

Not shown to help: a continuous sine phase (measured in Unity). The comparisons in a home-made Python simulation (averaging features over time, index, fixed noise, and others) did not match the real thing and are not firm conclusions. Upstream's code should have been the reference from the start.

Upstream's measurements replayed old CUDA graphs, which ignored changed conditions. They were redone with `run_cuda_graph` bypassed.

## 2026-11-01 Noise removal (RNNoise)

Approved by the user (2026-11-01). It is built into the audio plugin and runs in the processing stage before conversion, for both the microphone and WAV files. It is BSD-3-Clause, and `tools/deps/fetch-rnnoise.ps1` fetches it at a fixed commit with hashes.

Measured: 3.2 ms median and 9.3 ms at most per block, on the main thread. Speech level changes by -0.5 dB. Synthetic clicks in pauses fall by about 70 dB.

These are synthetic clicks. Real keyboards, and sounds during speech, have not been tried, nor has the effect on converted quality been measured.

## 2026-11-02 Correcting the 5.4 s window, and overlapping two stages

The earlier record's "5.4 s window adopted" was wrong. The user heard the voice break up during speech, and held notes no better. The totals at the application's exit had about 1608 microphone underruns in 3 minutes, against 1 to 8 in earlier runs with 1 s. The benchmark had measured an Editor with no stream video, tracking, or avatar, and missed the contention for the GPU. The window went back to 1 s.

The benchmark was rebuilt: frames vary from 20 to 36 ms (the application's logged values), another model runs on the GPU every frame, and underruns are counted by the mix's rules (play once 20 ms waits, take 10 ms at a time, drop the rest and wait when it runs dry). With it, 20 s gave 119 underruns with 1 s, 120 with 2.5 s, and 140 with 5.4 s, reproducing the problem.

The cause was how the work moved. A block's three steps (HuBERT and RMVPE, the generator, collecting) ran one after the other, each waiting for a frame boundary, so long frames made a block take more than 100 ms. GPU use was 47%, leaving room (checked by the user).

The next block's HuBERT and RMVPE now run while this block's generator does.

| Window | Before | After |
|---|---|---|
| 1 s | 119 underruns, 26 dropped | 1 underrun, 0 dropped |
| 2.5 s | 120, 26 | 0, 0 |
| 5.4 s | 140, 36 | 0, 0 |

Under this load a block takes 115 to 122 ms, so the delay is expected to be about 0.3 s. The load seems heavier than the real application's, as even 1 s breaks up more than it did there.

The window is chosen on the Diagnostics Card from 1, 2.5, and 5.4 s, and which sounds right waits for the user. The totals at exit include each block's time (`Voice/BlockWork`) and blocks left out (`Voice/DroppedBlocks`).

From now on, improvements are judged by underruns in the application's totals and by the user's listening, never by benchmark numbers alone.

## 2026-11-02 One trip to the GPU

After overlapping the two stages, the user's application still took 150 ms per block on average, with 74 ms on the main thread, and the 5.4 s window's delay was noticeable. Split by stage under the load benchmark, the main thread took about 20 ms per block, and most of the time was spent waiting for GPU results. HuBERT's and RMVPE's results came back to be prepared on the CPU, then the generator went out and came back: two trips, each waiting for a frame boundary.

That preparation moved into the generator's model: doubling HuBERT's output to 100 Hz, and turning RMVPE's output into pitch. The CPU keeps the pitch of earlier blocks, and the GPU works out and returns only the 28 newest frames. A frame sends all three models and only the last comes back.

Short unvoiced gaps now hold the last pitch instead of interpolating, which would need later values and does not suit doing it all on the GPU at once. The held-note scores show no loss.

| Window | Two trips | One trip |
|---|---|---|
| 1 s | 114 ms | 66 ms |
| 2.5 s | 115 ms | 70 ms |
| 5.4 s | 122 ms | 86 ms |

Medians under the load benchmark, with no blocks dropped. The generator's shape changed, so the template and table were remade, and a voice not used to make them is identical by SHA-256 to the Editor's conversion. Inference Engine's Clip needs both bounds, so one-sided `clamp` is not used.

Listening by the user is still to come.

## 2026-11-03 The 50 ms block is set aside, and the tail guard can be chosen

The user still noticed the delay with a 1 s window. With approval to use more GPU memory, two things were tried:

1. A 50 ms block, with three blocks on the GPU at once (each lane with its own Workers and tensors).
2. A tail guard of 10 ms instead of 30 ms.

| Window | Block | Tail guard | Delay (without the work) | One block's work (median) | Dropped | Underruns (20 s) |
|---|---|---|---|---|---|---|
| 1 s | 100 ms | 30 ms | 161 ms | 65 ms | 0 | 0 |
| 1 s | 100 ms | 10 ms | 141 ms | 64 ms | 0 | 1 |
| 1 s | 50 ms | 30 ms | 111 ms | 180 ms | 68 | 155 |
| 5.4 s | 100 ms | 30 ms | 161 ms | 86 ms | 0 | 1 |
| 5.4 s | 100 ms | 10 ms | 141 ms | 86 ms | 0 | 0 |
| 5.4 s | 50 ms | 30 ms | 111 ms | 244 ms | 153 | 209 |

These are the load benchmark's values. The 50 ms block does not work. Every block runs HuBERT and the generator over the whole window, so halving the block doubles the GPU's work per second. One block takes about 60 ms of GPU even with no other load, longer than 50 ms. Lanes only queue the work inside the GPU, and the waits pile up. The limit is the GPU's speed, not its memory. The lane implementation was taken out again.

A 10 ms tail guard lowers the delay by 20 ms without more underruns. The Diagnostics Card chooses 30 or 10 ms. Whether the joins ripple audibly waits for the user's listening.

A shorter block needs work per block that does not grow with the window, for example reusing HuBERT's earlier output. That is not done now.

The user's remark (2026-11-02): this feature needs a high-end PC, and users must be told (Voice Lab detailed design chapter 1 and 8.1).

The user's listening (2026-11-03): a 10 ms tail guard added no sounds at the joins, but the delay did not feel shorter, and held notes wavered more than with 30 ms. The default stays 30 ms.

## 2026-11-03 Measuring where the delay comes from

The user agreed to remove the 10 ms tail guard choice (2026-11-03): it did little, and held notes wavered.

The real application can now measure the delay's parts. The totals at exit show `Voice/Delay.*`: CaptureDevice, StageInput, Waiting, Work, Algorithm, StageOutput, Monitor, OutputBuffer, OutputDevice, and Total, from the microphone to the monitor (audio detailed design 8.3). The hardware's own delay (USB, Bluetooth, the converters) is not included.

Candidates noticed while reading the code:

- The monitor output fills the device's buffer (about 30 ms) at every wake, so that much wait is always there.
- The stage's output arrives 100 ms at a time, so on average it waits about 50 ms for the mix. That cannot be avoided while working in blocks.
- The stage's output is not corrected for drift (`follow` is false). If the microphone's clock runs faster than the mix's, the wait may grow slowly.

The measured values decide what to cut.

### Measured (2026-11-03, 5.4 s window, the user's PC)

| Part | Average |
|---|---|
| Input device (as Windows reports) | 0 ms |
| Until the stage reads | 19 ms |
| Until 100 ms has gathered | 50 ms |
| GPU work | 80 ms |
| The algorithm | 61 ms |
| After the stage, until the mix | 100 ms |
| From the mix to the monitor output | 15 ms |
| Output device buffer | 25 ms |
| Output device (as Windows reports) | 0 ms |
| Total | 350 ms |

The hardware adds its own delay to this total: the USB camera's microphone, and the display's speakers over HDMI. Windows reported 0 for the devices.

The wait after the stage is about 30 ms longer than the 70 ms expected from half a block plus the mix's 20 ms. Nothing corrects that wait (`follow` is false), so whatever each early block adds seems to stay.

The user's impression: held notes with the 5.4 s window were better before, and the delay feels about the same.

The user's decisions (2026-11-03):
- "Better before" compared with the previous run of the same processing. With the same processing, held notes sound different from run to run.
- Moving the monitor to the PC's earphone jack partway through did not change how the delay felt.
- Cutting about 50 ms would hardly be felt, so work on the delay stops here, and sound quality comes first.

## 2026-11-03 Held notes: a 50 ms crossfade

The user found the pitch wobbling on held notes very unpleasant. Following the user's suggestion, the measure is whether the output's pitch is exactly an octave above the input's (+12 semitones).

The user's recording (about 2 minutes) was converted and compared. `pitch_track.py` finds the pitch with pYIN. `join_profile.py` folds the spectral change by its position within the block and averages it. Both are in `voice-work`.

| | Distance from input + an octave on held notes | Output wobble | Join score (1.00 is no change) | Added delay |
|---|---|---|---|---|
| Input | — | 10.7 cents | — | — |
| Offline | 8.8 cents | 10.8 cents | 1.01 | — |
| 20 ms crossfade (until now) | 9.9 cents | 12.4 cents | 1.06 | 0 |
| 100 ms of earlier context | — | — | 1.07 | 0 |
| 60 ms tail guard | — | — | 1.05 | +30 ms |
| 50 ms crossfade | 10.3 cents | 11.4 cents | 1.01 | +30 ms |

- On average the pitch itself followed the input an octave up almost exactly. The input itself wavers by about 11 cents on held notes.
- The joins caused the wobble. With a 20 ms crossfade, the sound changed just after each join every 100 ms, a 10 Hz wobble.
- More earlier context for the generator changed nothing, and a longer tail guard helped little.
- With a 50 ms crossfade, the join score matched offline, and the output's wobble came closer to the input's. The delay grows by 30 ms (the algorithm's delay goes from 61 to 91 ms).

The user put sound quality first (2026-11-03), so 50 ms is adopted. The generators return 21 frames instead of 18, so they were exported again. The template for swapping weights must be made again as well. The user has not listened yet.

## 2026-11-03 Long held notes vanished in noise removal

The user tried it in the application (5.4 s window, noise removal on). On long notes, the output went silent while the user kept singing. In the recording, the output faded and then vanished.

RNNoise caused it: it takes a steady sound for noise. A held part of the user's voice, stretched to 15 s, fell to -15 dB in the first second, -48 dB in the second, and -75 dB by the fourth.

The fix: while the input is voiced (periodic within a voice's pitch range and louder than -50 dBFS), the original is blended back over RNNoise's output. RNNoise's output lags its input by 20 ms (measured), so the original is delayed by the same amount to line up. When the voice stops, the blend lets go within a few frames. Key and mouse clicks are not periodic, so between words they are still removed.

| | Held note | Noise in pauses | Speech level |
|---|---|---|---|
| RNNoise alone | -48 dB after 2 s | -30.1 dB | -2.1 dB |
| Original blended back | Same as the input throughout | -29.1 dB | -1.1 dB |

While voiced, noise under the voice stays too. The user has not listened yet.

## 2026-11-03 Templates made again for the 50 ms overlap

With the 50 ms overlap the generator returns 21 frames instead of 18. The template for writing trained weights, and its placement table, were made again for the 1 s, 2.5 s, and 5.4 s windows. For all three, the `.sentis` made by writing a voice unused in the template had the same SHA-256 as the one the Editor made from that voice. All 352 weights were placed.

A template is needed for each generator shape (window and returned frames). Offering both a 20 ms and a 50 ms overlap needs a template for each.

## 2026-11-03 Checked with the user's recordings

The user recorded their voice on a phone (M4A, 48 kHz, mono): long held notes (three, of about 9 s, 11 s, and 9 s; 39 s in all) and two clips of bright speech (5 s and 7 s). M4A can now be read (audio detailed design 5.4), so these play straight from the Diagnostics screen too.

Through noise removal alone, the level of each 100 ms was compared with the input's (where the input is louder than -34 dBFS):

| | Held notes | Speech |
|---|---|---|
| Noise removal before the fix | 50 of 330 blocks fall by 12 dB or more, from 26.5 s in the third note | 2 of 65 |
| Noise removal after the fix | No falls | The same 2 (no change) |

Then through the application's conversion (the benchmark under load, +9 semitones):

- No block ran short and nothing broke off, with the 1 s, 2.5 s, and 5.4 s windows.
- The first and second notes came out at about the input's level to the end. Pitch wobble (the distance from the 300 ms median) was 9.0 cents out for 8.5 cents in (5.4 s window): the conversion adds none.
- Late in the third note (28 to 31 s) the output was 10 to 15 dB quieter than the input, whatever the window. There the input voice turns rough, with a part at half the pitch (about 75 Hz). The converted pitch stayed right.
- With the 5.4 s window, the first 2 s or so after starting are quiet, while most of the window is still silence.

## 2026-11-03 Short gaps inside sung notes, from noise removal

In the third note of the same recording, noise removal alone still cut short gaps: in 10 ms steps, single moments fell by 10 to 56 dB (late in the note, the second's level fell by 2.5 to 3.9 dB). Each gap is shorter than 100 ms, so the comparison above, in 100 ms blocks against a 12 dB fall, did not catch them. They are heard as the note breaking up.

The cause: where the voice turns rough, single frames lose their periodicity, and the blend of the original let go within a few frames at each one.

The fix: the blend is held for 100 ms after the last voiced frame, then lets go as before. Measured by simulation on the recording, and on speech with noise added (-40 dBFS):

| Hold | Worst 100 ms inside notes | Noise in pauses (all) | Noise in pauses longer than 300 ms |
|---|---|---|---|
| None (before) | -59.0 dB | -30.1 dB | -46.7 dB |
| 50 ms | -2.0 dB | -15.8 dB | -46.7 dB |
| 100 ms (adopted) | 0.0 dB | -12.8 dB | -46.7 dB |
| 200 ms | 0.0 dB | -8.7 dB | -46.7 dB |

With the built plugin, every second of the recording's notes keeps the input's level. More noise stays only in the short gaps between syllables, right after the voice; in real pauses it is removed as before. A hold that also let go when the level fell (by 6, 10, or 15 dB) gave the same results, so it was not taken.

The application's benchmark under load does not pass through noise removal, so the 10 to 15 dB fall of the converted third note (above) is the conversion's own, where the input turns rough. It is not solved by this. The user has not listened yet.

## 2026-11-03 The vowel changed partway through long held notes

The user heard the vowel change partway through a long held note. In the recording's second note (a held "i"), the input's second formant stayed at about 2,600 Hz. With the 5.4 s window, the converted voice's fell from about 3,100 to about 1,450 Hz after about 3 s (nearer "u" or "o"); with the 1 s window, almost at once.

HuBERT causes it: when its whole input is one held vowel, it reads another vowel. Upstream's offline conversion of the held "i" alone went wrong the same way, from the start. It is not the first layer's GroupNorm (fixing its statistics changed nothing). With 1 s of anything else before the vowel (speech, another speaker's speech, or noise), the vowel stayed right for all 7 s; 0.5 s was not enough. In streaming, the vowel changes when what came before the note leaves the window.

The fix: 1 s of fixed noise (standard deviation 0.01, seed 0) goes before every HuBERT window, and its 50 feature frames are dropped. It is built into the exported HuBERT model, so the model's input and output, and the rest of the conversion, stay as they were. The delay does not change.

The level was chosen for speech not to get worse. Distance from upstream's offline conversion of the whole file (timbre, MFCC; lower is closer), for the user's held notes and speech (+9 semitones):

| Window | Held notes: none / noise 0.05 / noise 0.01 | Speech: none / noise 0.05 / noise 0.01 |
|---|---|---|
| 1 s | 109.5 / 58.2 / 54.8 | 40.4 / 42.5 / 41.2 |
| 5.4 s | 57.8 / 48.0 / 54.3 | 32.9 / 34.8 / 33.5 |
| Realtime 2.5 s | 82.3 / 58.0 / 65.4 | 37.5 / 36.3 / 37.5 |
| Realtime 5.4 s | 57.4 / 48.0 / 55.0 | 32.7 / 36.1 / 34.0 |

Noise at 0.05 made speech 5 to 10% worse, so 0.01 is used: speech differs by 2 to 4%, about as much as runs of the same setting differ. Held notes with the 1 s window come to half the distance; with the 5.4 s window they gain little. Another speaker's speech as the anchor came out better for both in HuBERT's features, but needs a recording that may be distributed; not decided. Processing time per block grew by about 2 ms. The user has not listened yet.

## 2026-11-03 The user's own speech before the HuBERT window, in place of noise

The user suggested putting what they said before the window instead of noise. It is adopted (option A: the same way for every window).

The anchor is the latest unbroken second of speech that has left the window. A 100 ms block counts as speech when RMVPE's log mel changes by more than 0.32 on average per frame (on the user's recordings, held notes 0.22 to 0.29, speech 0.35 to 0.67) and it is louder than -40 dBFS. Taken from inside the window, the same audio twice made speech worse; pieces joined across breaks did too. Until there is such a second, quiet noise (0.01) stands in. The anchor is kept only in memory and never saved.

HuBERT is exported to take the anchor and the window and to return the window's frames only (`hubert_<samples>_anchored.sentis`), so the generator is unchanged. The delay does not change; a block takes about 2 ms more.

Measured on the user's speech and held notes alternating (three of each, +9 semitones; distance from upstream's offline conversion of the whole file, lower is closer):

| Window | Held notes: none / noise / own speech | Speech: none / noise / own speech |
|---|---|---|
| 1 s | 102.3 / 57.8 / 66.3 | 36.3 / 37.8 / 35.9 |
| 5.4 s | 72.0 / 54.7 / 56.1 | 32.9 / 32.7 / 32.9 |
| Realtime 1 s | 101.5 / 57.5 / 66.4 | 36.5 / 38.4 / 36.8 |
| Realtime 2.5 s | 97.1 / 65.6 / 61.2 | 33.9 / 34.1 / 34.1 |
| Realtime 5.4 s | 72.5 / 53.7 / 55.7 | 32.6 / 33.0 / 33.2 |

With the user's own speech, speech stays where it was without an anchor for every window; noise made it worse with the 1 s window. Held notes improve with both; with the 1 s window noise keeps them a little better. In HuBERT's features alone, own speech had looked clearly better with the 5.4 s window; in the converted voice it came out about the same as noise. The user has not listened yet.

## 2026-11-03 "u" still drifts toward "a"; held notes as the anchor tried and not adopted

The user heard "a" and "i" pass, but a held "u" drift toward "a". In the recording the third note is "u" and the fourth "e". Judged by HuBERT on the converted voice (which follows the vowel more than the speaker; each second against the input's four vowels), upstream's offline conversion of the whole file got every second right. With the user's own speech as the anchor, "u" was right for 38 to 75% of seconds (alternating input), and with the 5.4 s window "e" turned toward "i". So it is still the window, not the voice model.

HuBERT judges a vowel against what is around it. Upstream with "u" alone was right; 1 to 12 s of speech before it pulled it toward "a", and noise did too. With 0.7 s of each of the user's held "a" and "i" before it, "u" was right throughout. Recording the five vowels for the anchor was set aside: the user does not want to ask anything of users.

Tried: the user's last three finished held notes (0.7 s each, collected automatically) and their latest second of speech as a 3.1 s anchor. In HuBERT's features it looked better; in the converted voice it was mixed:

| Right seconds | Own speech (adopted) | Held notes and speech |
|---|---|---|
| "u", alternating input, 1 s / 2.5 s / 5.4 s | 75 / 38 / 38% | 88 / 88 / 62% |
| "i" and "e", held notes only, 1 s | 100 / 100% | 70 / 60% |
| "u", held notes only, 5.4 s | 100% | 57% |

A block took 15 to 20 ms more (HuBERT's input 3.1 s longer). Not clearly better and costly, so it was not adopted; the change is kept outside the repository as a patch. Late in the "u" note, where the user's voice turns rough, every streaming variant drifted toward "a".
