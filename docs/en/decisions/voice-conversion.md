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

The licenses of `kaldiio`, `llvmlite`, `torchcodec`, and `gradio` could not be read from package metadata. They are checked when the training environment's dependencies are fixed.

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
