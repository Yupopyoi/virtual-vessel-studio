<!-- version: 0.4-draft -->
<!-- Draft (2026-11-11). To be reviewed by a legal professional before release (Voice Lab detailed design 8.2, 17). -->

# Voice Lab Terms of Use

These Terms of Use (these "Terms") set out the conditions for using Voice Lab (the "Feature"), a feature of Virtual Vessel Studio (the "Software"). The User shall use the Feature only after agreeing to these Terms.

## Article 1 (Definitions)

In these Terms, the following terms have the meanings set out below.

1. "User" means any person who uses the Feature.
2. "Output" means voice models made with the Feature, and audio generated or converted with such models.
3. "External Components" means the software, models, and other files obtained under Article 9 to make the Feature usable.
4. "Developers" means the developers of the Software.

## Article 2 (Agreement)

1. The User may use the Feature only after reading these Terms in full and agreeing to them.
2. By performing the operation that starts using the Feature, the User is deemed to have agreed to these Terms.
3. A User who does not agree to these Terms may still use the features of the Software other than the Feature.

## Article 3 (Prohibited Acts)

In using the Feature, the User shall not engage in any of the following acts:

1. impersonating another person;
2. fraud or any other act that violates, or may violate, laws or regulations;
3. creating or spreading false information;
4. reproducing another person's voice without that person's permission;
5. entering instructions (including prompts and added words) intended to imitate the voice of a real person;
6. infringing the portrait rights, publicity rights, copyrights, or other rights of third parties; and
7. any other act contrary to public order and morals, or that the Developers consider inappropriate.

## Article 4 (Audio Used for Training)

1. The User shall train voice models only with audio that falls under any of the following:
   1. the User's own voice;
   2. audio the User has the right to use (including audio whose speaker has given permission); or
   3. audio generated with the Feature.
2. When importing a recording, the User shall confirm, before importing it, that the recording meets the conditions of the preceding paragraph.

## Article 5 (Disclosure of AI-Generated Output)

When publishing Output, the User shall indicate, in a manner that viewers can recognize, that the audio was generated or converted with AI.

## Article 6 (User's Responsibility)

1. The User is responsible for making and using Output and for all consequences arising from them.
2. Before streaming or publishing Output, the User shall check and comply with the terms, guidelines, and other rules of the place where it is streamed or published.
3. If a dispute with a third party arises from the User's breach of these Terms, the User shall resolve it at the User's own responsibility and expense.

## Article 7 (No Warranty)

1. The Feature and the External Components are provided as is. The Developers make no warranty of any kind, express or implied, regarding the Feature and the External Components, including fitness for a particular purpose, accuracy, usefulness, continued availability, or non-infringement of third-party rights.
2. Most External Components are provided under the MIT License or the Apache License 2.0, each of which also disclaims all warranties.

## Article 8 (Limitation of Liability)

To the maximum extent permitted by law, the Developers shall not be liable for any damage incurred by the User or any third party arising from the use of, or the inability to use, the Feature. However, this does not apply where the damage is caused by the Developers' intent or gross negligence.

## Article 9 (Obtaining the External Components)

1\. To make the Feature usable, the following External Components are downloaded from the internet and set up at the location the User specifies. Once set up, they occupy about 13 GB; up to about 30 GB of free space is needed temporarily while setting up.

| Name | Source | License |
|---|---|---|
| uv (the tool used to set up) | GitHub (astral-sh/uv) | MIT or Apache-2.0 |
| Python 3.12 | uv's distribution (python-build-standalone) | Python Software Foundation License |
| Python packages (PyTorch and others) | PyPI and PyTorch's distribution | Each package's license |
| RVC (Retrieval-based Voice Conversion WebUI) | GitHub | MIT |
| RVC's pretrained models, RMVPE, HuBERT | Hugging Face (lj1995/VoiceConversionWebUI) | MIT |
| VoxCPM2 | Hugging Face (openbmb/VoxCPM2) | Apache-2.0 |
| The voice template and the shared models used for streaming | Virtual Vessel Studio's GitHub Release | MIT |

2\. The Software obtains each External Component only from its source. Every file obtained is verified by its hash value, and any file that does not match is not used.

3\. RVC's pretrained models were trained using the following data:

> CSTR VCTK Corpus: English Multi-speaker Corpus for CSTR Voice Cloning Toolkit (version 0.92), Junichi Yamagishi, Christophe Veaux, Kirsten MacDonald, The Centre for Speech Technology Research (CSTR), University of Edinburgh. Creative Commons Attribution 4.0 International (CC BY 4.0).

4\. The provider of VoxCPM2 prohibits its use for impersonation, fraud, and disinformation, and requires that AI-generated audio be indicated as such. Articles 3 and 5 include these conditions.

5\. The full license text of each External Component can be viewed in Credits and licenses in the Software's settings. The User shall comply with the license of each External Component.

## Article 10 (Network and Handling of Data)

1. The Feature connects to the internet to obtain the External Components. It connects only to the sources set out in the preceding Article.
2. Designing voices, making training audio, training, and voice conversion all take place on the User's PC. The Feature does not send recordings, training audio, or Output anywhere.
3. The environment set up can be deleted from the Feature's Made voices. Deleting it does not affect streaming with registered voices.

## Article 11 (Changes to these Terms)

1. The Developers may change these Terms when they consider it necessary. These Terms carry a version.
2. When these Terms are changed, the Software asks the User to agree to the changed Terms before the User makes voices with the Feature.
3. Streaming with registered voices does not stop even without the agreement in the preceding paragraph.

## Article 12 (Governing Law)

These Terms are governed by and construed in accordance with the laws of Japan.
