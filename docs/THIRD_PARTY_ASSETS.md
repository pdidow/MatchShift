# Third-party assets

Provenance of third-party media shipped with MatchShift. CC0 does not require attribution; it is recorded here anyway
for contest and IP clarity.

## Crowd goal audio

| | |
|---|---|
| File in MatchShift | `src/MatchShift.Web/public/audio/crowd-goal.wav` |
| Source | ShangusBurger / Shane Vincent — Freesound sound 763816 |
| Original title | CRWDApls_Loud Applause, Cheering, Whistling, Barking_ShaneVincent_GSC24_XY-AKG214-96k.wav |
| License | Creative Commons CC0 1.0 (public domain dedication) — https://creativecommons.org/publicdomain/zero/1.0/ |
| Source URL | https://freesound.org/people/ShangusBurger/sounds/763816/ |
| License checked | 2026-10-21, on the live Freesound page ("Creative Commons 0") |
| Original file | WAV, stereo, 96 kHz, 24-bit, 20.375 s |
| Use in MatchShift | Trimmed/normalized approximately 2.8-second crowd goal reaction for the Evidence Replay |

**This is third-party CC0 material. MatchShift does not claim authorship of the original recording.**

Processing applied (nothing else):

- trimmed to 0.90–3.70 s of the original: the crowd erupting into cheering, applause and supporter whistling, before
  the barking later in the recording;
- resampled from 96 kHz to 44.1 kHz and exported as 16-bit stereo PCM WAV for browsers;
- 40 ms fade-in and 0.5 s fade-out (raised cosine);
- normalized to −18 dBFS RMS (peaks about −4 dBFS).

No synthesis, added noise, pitch-shifting or other editing.

## Other crowd clips

`crowd-chance.wav` and `crowd-murmur.wav` in the same folder are not third-party material: they are synthesized for
MatchShift (see `src/MatchShift.Web/public/audio/README.txt`).
