# Voice Conversion: Decisions and History

A record of what the real-time voice conversion prototype tried and what came of it. Voice has no detailed design yet; one will be written from these results.

## 2026-11-01 A real-time RVC prototype in Unity (system design 9.5, 9.10)

### Purpose

System design 9.5 runs RVC on Unity Inference Engine, with 200 ms as the standard. The prototype checks whether that holds on the real path, as the processing stage of audio detailed design 8.3.

### Measurement conditions

- RTX 4070, the Unity 6000.6.4f1 Editor (Mono), `com.unity.ai.inference` 2.6.1, GPUCompute
- The input is 12 s of a male voice made with VoxCPM2 (median fundamental 115 Hz). The target is RVC v2 (40 kHz, 150 epochs, no index) trained on 441 s of a female voice made with VoxCPM2. Transposed by +13
- **A real microphone, the monitor output, and listening have not been tried.** Quality was compared in numbers

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

Models and speech are outside the repository. Start with `VV_VOICE_MODELS` naming the folder of `.sentis` files, and turn it on under Voice conversion (prototype) on the Diagnostics screen in Developer Mode. The Editor must restart after the plugin is updated.
