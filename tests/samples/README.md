# Sample models

Models kept in the repository for tests and for trying the application. They are stored with Git LFS;
run `git lfs install` once, then `git lfs pull` if a clone shows small pointer files instead of the
models.

| File | Author | License |
|---|---|---|
| `otoha-chan.vrm` | Yupopyoi | VRM Public License 1.0 (<https://vrm.dev/licenses/1.0/>), with the terms set in the model: avatar use by everyone; commercial use including corporations; redistribution allowed; modification and redistribution of the modified model allowed; credit not required; no excessively violent or sexual use, no political or religious use, and no antisocial or hateful use. |

`otoha-chan.vrm` was made with VRoid Studio. Releases ship it as `Samples/otoha-chan.vrm`, with its terms in
`otoha-chan-LICENSE.md` (shipped as `licenses/otoha-chan-LICENSE.md`, development workflow 21). The license above is the model's own and is separate from the
Apache License 2.0 of the source code (`LICENSE`).

The VRM tests (`VrmLoaderTests`, `VrmRecordingTests`, avatar detailed design 14) look for models in the
folder named by `VV_SAMPLES_DIR`, else in `samples` next to the repository, else here.
