# Recording: Decisions and History

Why recording (recording detailed design) is as it is: the formats compared, the measurements, and what was dropped. The detailed design keeps only the current behavior and links here.

## 2026-10-28: AV1 by default, H.264 as the fallback (recording detailed design 1, 5.1, 10)

**Decision (the user's, 2026-10-28):** recordings are AV1 where the GPU has an AV1 encoder, and H.264 elsewhere. The user can choose H.264. H.264 is encoded only by the encoders of Windows (Media Foundation) and of the GPU driver; the application never ships an H.264 encoder of its own.

**Why:**

- H.264 is covered by patents licensed through a pool (Via LA). Its last patents run to about November 2027 in the United States and to about 2030 elsewhere. The user was concerned about distributing an application that encodes H.264, and about users who record and stream with it. AV1 is offered royalty-free by its developers (the Alliance for Open Media). A separate party (Sisvel) has announced a patent pool for AV1, so it is not free of every claim, but it is the safer side.
- Calling the encoders of Windows and the GPU driver leaves the H.264 implementation, and its licensing, with Microsoft and the GPU maker.
- At the same bitrate, AV1 was as good as H.264 High or better (below).

**Compared and not chosen:**

- H.265 (HEVC): several patent pools, more involved than H.264.
- VP9: the GPU's encoder (NVENC) does not encode it.
- Keeping H.264 only: the patent concern stays.

**The spike (2026-10-28; a probe program that was not kept, so its results are recorded here):** on the development PC (Core i7-13700F, RTX 4070, Windows 11):

- Media Foundation lists "NVIDIA AV1 Encoder MFT" (hardware). The Sink Writer the recording already used writes AV1 into MP4 when only the output subtype is changed; ffprobe reads it as AV1 Main.
- Load at 1080p30 for 20 s, frames pushed in real time: the same for both. CPU 1.4% of 24 threads, the GPU's encoder 5% for H.264 and 4.5% for AV1.
- Quality: the same raw video was encoded at 6, 3, and 1.5 Mbit/s and compared with the source in YUV (PSNR, dB). Comparing through RGB measured the color conversion instead of the codec, so the source was fed to the encoder as NV12.

| Video | Bitrate | H.264 (default profile) | H.264 High | AV1 |
|---|---|---|---|---|
| Detailed (fractal zoom) | 6 Mbit/s | 36.7 | 37.5 | 37.9 |
| | 3 Mbit/s | 33.7 | 34.2 | 34.9 |
| | 1.5 Mbit/s | 31.3 | 31.9 | 33.2 |
| Flat graphics (test pattern) | 6 Mbit/s | 44.1 | 45.2 | 45.3 |
| | 3 Mbit/s | 40.3 | 40.8 | 40.6 |
| | 1.5 Mbit/s | 37.5 | 38.0 | 38.1 |

- The "quality vs. speed" setting of the encoders (`CODECAPI_AVEncCommonQualityVsSpeed` 100) changed almost nothing, so it is left at the default.

**Found by the spike, fixed with it:**

- Without a profile, the H.264 encoder chose Constrained Baseline: about 0.5 dB worse than High at the same bitrate. H.264 is now written in High.
- Files had no color information. Players took them for other colors, and some showed them darker. The video is now tagged BT.709, limited range. Six color bars recorded and read back with ffmpeg came within 3 of 255 per channel.

**Not yet checked:** whether AV1 files play on PCs without the "AV1 Video Extension" (the development PC has it), and a GPU without an AV1 encoder (covered by tests with a fake encoder).
