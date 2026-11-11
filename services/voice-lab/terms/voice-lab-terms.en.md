<!-- version: 0.1-draft -->
<!-- Draft (2026-11-11). To be reviewed by a legal professional before release (Voice Lab detailed design 8.2, 17). -->

# Voice Lab Terms of Use and Disclaimer

Voice Lab designs voices and makes the voice models used for voice conversion. Using Voice Lab requires agreeing to these terms. Without agreeing, every feature other than Voice Lab can still be used.

## 1. Prohibited uses

Do not use voices made with Voice Lab, or voice conversion, for:

- impersonating another person
- fraud or any other unlawful act
- creating or spreading disinformation
- reproducing another person's voice without their permission
- infringing others' rights (portrait, publicity, copyright, and the like)

Do not write prompts or added words that ask to imitate a real person's voice.

## 2. Voices used for training

Train voice models only with:

- your own voice
- voices you have the right to use, including those whose speakers gave permission
- voices Voice Lab generated

When importing a recording, confirm before importing that it meets these conditions.

## 3. Disclosing AI-generated voices

When you publish a voice you made, or audio made with voice conversion, state in a way viewers can see that the voice was generated or converted with AI.

## 4. Responsibility

You are responsible for the voices you make and for what comes of using them. Before streaming or publishing, check the terms and guidelines of where you use them.

## 5. No warranty and limitation of liability

Voice Lab and the external components it downloads are provided as is, without any warranty, including fitness for a particular purpose, accuracy, or continued availability. Most external components are provided under the MIT License or the Apache License 2.0, which also disclaim warranty.

To the extent the law allows, the developers of Virtual Vessel Studio are not liable for damage arising from using, or being unable to use, Voice Lab.

## 6. External components downloaded

To make Voice Lab usable, the following are downloaded from the internet and built on this PC. With the Python packages, it is about 13 GB in all once built. Up to about 30 GB of free space is needed while building.

| Component | Source | License |
|---|---|---|
| uv (the tool that builds it) | GitHub (astral-sh/uv) | MIT or Apache-2.0 |
| Python 3.12 | uv's distribution (python-build-standalone) | Python Software Foundation License |
| Python packages (PyTorch and others) | PyPI and PyTorch's distribution | Each package's license |
| RVC (Retrieval-based Voice Conversion WebUI) | GitHub | MIT |
| RVC's pretrained models, RMVPE, HuBERT | Hugging Face (lj1995/VoiceConversionWebUI) | MIT |
| VoxCPM2 | Hugging Face (openbmb/VoxCPM2) | Apache-2.0 |
| The voice template and the shared models streaming uses | Virtual Vessel Studio's GitHub Release | MIT |

When a source cannot be reached, a copy kept in Virtual Vessel Studio's GitHub Release is used, after confirming it is the same file. Every file is checked against its hash after downloading, and one that does not match is not used.

RVC's pretrained models were trained on:

> CSTR VCTK Corpus: English Multi-speaker Corpus for CSTR Voice Cloning Toolkit (version 0.92), Junichi Yamagishi, Christophe Veaux, Kirsten MacDonald, The Centre for Speech Technology Research (CSTR), University of Edinburgh. Creative Commons Attribution 4.0 International (CC BY 4.0).

VoxCPM2's provider prohibits its use for impersonation, fraud, and disinformation, and asks that AI-generated audio be disclosed as such. Sections 1 and 3 include these conditions.

The full license texts are in Credits and licenses in the settings.

## 7. Network and data

- Downloading the components needs an internet connection, and only the sources in section 6 are contacted.
- Designing voices, making training speech, training, and voice conversion all happen on this PC. Recordings, training speech, and the voices made are not sent anywhere.
- The built environment can be deleted from Voice Lab's Made voices. Deleting it does not affect streaming with registered voices.

## 8. Changes

These terms have a version. When they change, you are asked to agree again before making voices in Voice Lab. Streaming with registered voices does not stop.
