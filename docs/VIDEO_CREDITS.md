# MatchShift Demo Video — Third-Party Credits

This file records the music, text-to-speech model, and training-data attribution for the final MatchShift hackathon demonstration video. The source and license information below was checked against the linked records; it is not legal advice. No creator or institution listed here endorses MatchShift.

## Final submission video

- **File:** `MatchShift-hackathon-v11-captioned.mp4` (kept outside Git)
- **SHA-256:** `7658EDCCF07B5472C86056FFD568029AC5103EFC9865BC34D8BE36CD428032DD`
- **Duration:** 107.72 seconds
- **Format:** 1920 × 1080, 25 fps, H.264 video and AAC stereo audio

The video burns in English translation only during its French narration. A full English [SRT](video/MatchShift-hackathon-Finalcut-English.srt) and [timestamped transcript](video/MatchShift-hackathon-Finalcut-English-Transcript.txt) are available separately.

## Music

- **Title:** “Chase”
- **Creator:** Adiutorium
- **Source:** [OpenGameArt](https://opengameart.org/content/chase-2)
- **License:** [Creative Commons CC0 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/)

The source page explicitly permits commercial and non-commercial use and says credit is not required. This credit is included for provenance.

## Kokoro narration

- **Model:** Kokoro-82M v1.0
- **Creator/project:** hexgrad
- **Source:** [hexgrad/Kokoro-82M](https://huggingface.co/hexgrad/Kokoro-82M)
- **License:** [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
- **Revision used:** `f3ff3571791e39611d31c381e3a41a3af07b4987`
- **Voices used in the final video:** `bf_emma` (British English) and `ff_siwis` (French); see the [official voice catalog](https://huggingface.co/hexgrad/Kokoro-82M/blob/main/VOICES.md).

Narration was generated locally from this model. No hosted or cloud text-to-speech service was used. This record makes no claim about ownership of generated output.

## French voice and SIWIS training-data attribution

`ff_siwis` is the Kokoro French voice used in the video. [Kokoro’s model documentation](https://huggingface.co/hexgrad/Kokoro-82M) identifies SIWIS audio as part of the model’s training data under CC BY 4.0. This describes training-data provenance; it does not describe the voice file as the SIWIS dataset or assign that dataset’s license directly to the voice file.

- **Dataset:** *The SIWIS French Speech Synthesis Database*
- **Authors:** Junichi Yamagishi; Pierre-Edouard Honnet; Philip Garner; Alexandros Lazaridis
- **Publisher:** University of Edinburgh, School of Informatics, Centre for Speech Technology Research
- **Source and DOI:** [10.7488/ds/1705](https://doi.org/10.7488/ds/1705); [University of Edinburgh record](https://datashare.ed.ac.uk/handle/10283/2353)
- **License:** [Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/), as stated in the [dataset README](https://datashare.ed.ac.uk/bitstreams/a15e775d-abd6-496f-815b-68d3a9229e90/download)
- **Changes/use:** SIWIS source recordings were not copied directly into the MatchShift video. New synthetic narration was generated locally with Kokoro-82M and `ff_siwis`.

For model-wide training provenance, the Kokoro card also lists **Koniwa `tnc`** audio: [Koniwa project/source](https://github.com/koniwa/koniwa), [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/). No Koniwa source recording was copied into the final video.

## MatchShift audio

The final video includes a separate goal/crowd replay segment using MatchShift's processed goal crowd recording: **ShangusBurger / Shane Vincent**, [Freesound sound 763816](https://freesound.org/people/ShangusBurger/sounds/763816/), under [CC0 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/). Credit is included for provenance, not represented as required. MatchShift also ships synthesized `crowd-chance.wav` and `crowd-murmur.wav` for the application. Their provenance and the goal clip's processing are documented in [THIRD_PARTY_ASSETS.md](THIRD_PARTY_ASSETS.md); their inclusion here does not claim they are separately audible in the final video mix.
