"""Generate MatchShift's Evidence Replay crowd-atmosphere clips.

Original audio, rendered offline from seeded noise - no recordings, samples, chants or third-party material - so the
clips are MatchShift's own work and safe to ship. The browser only plays these finished files; it synthesises nothing.

    python tools/crowd-audio/generate_crowd_audio.py

writes src/MatchShift.Web/public/audio/crowd-{goal,chance,murmur}.wav (mono, 22.05 kHz, 16-bit). The same seed always
produces the same files. Design rules:
  - generic crowd excitement only: broadband, vowel-like noise "voices" and soft applause;
  - no tones, whistles or anything that could pass for a referee's whistle, a final whistle or a chant;
  - every clip starts and ends in silence (fade-in and fade-out), so it never clicks or cuts off.
Requires numpy.
"""
import os
import wave

import numpy as np

RATE = 22050
OUT = os.path.join(os.path.dirname(__file__), '..', '..', 'src', 'MatchShift.Web', 'public', 'audio')


def band_noise(rng, seconds, low, high):
    """White noise filtered to a soft band [low, high] Hz (FFT shaping with gentle skirts)."""
    n = int(seconds * RATE)
    spectrum = np.fft.rfft(rng.standard_normal(n))
    f = np.fft.rfftfreq(n, 1 / RATE)
    centre, width = (low + high) / 2, (high - low) / 2
    shape = np.exp(-0.5 * ((f - centre) / max(width, 1)) ** 4)
    out = np.fft.irfft(spectrum * shape, n)
    return out / (np.abs(out).max() + 1e-9)


def vowel(rng, seconds, formants):
    """Noise shaped by a few broad formant bands - reads as many distant voices on one vowel, never as a tone."""
    n = int(seconds * RATE)
    spectrum = np.fft.rfft(rng.standard_normal(n))
    f = np.fft.rfftfreq(n, 1 / RATE)
    shape = sum(gain * np.exp(-0.5 * ((f - centre) / width) ** 2) for centre, width, gain in formants)
    shape *= 1 - np.exp(-f / 120)            # no rumble below ~100 Hz
    out = np.fft.irfft(spectrum * shape, n)
    return out / (np.abs(out).max() + 1e-9)


def envelope(n, attack, hold, release, start=0.0):
    """0 → 1 over `attack` s (after `start`), hold, then a smooth release to 0 within `release` s."""
    t = np.arange(n) / RATE - start
    env = np.clip(t / attack, 0, 1)
    tail = np.clip((t - attack - hold) / release, 0, 1)
    return env * (1 - (3 * tail ** 2 - 2 * tail ** 3))


def slow_wobble(rng, n, rate_hz, depth):
    """A slow random amplitude movement (crowds swell unevenly)."""
    points = int(n / RATE * rate_hz) + 3
    knots = 1 + depth * rng.uniform(-1, 1, points)
    return np.interp(np.linspace(0, points - 1, n), np.arange(points), knots)


def applause(rng, seconds, density, start, attack, hold, release):
    """Soft claps: short, band-limited noise bursts at random times, thinning out over the release."""
    n = int(seconds * RATE)
    out = np.zeros(n)
    clap = band_noise(rng, 0.03, 900, 4200) * np.exp(-np.arange(int(0.03 * RATE)) / (0.006 * RATE))
    env = envelope(n, attack, hold, release, start)
    count = int(density * seconds)
    for at in np.sort(rng.uniform(start, seconds - 0.05, count)):
        i = int(at * RATE)
        if rng.uniform() > env[i]:
            continue
        seg = clap[: min(len(clap), n - i)]
        out[i:i + len(seg)] += seg * rng.uniform(0.4, 1.0)
    return out / (np.abs(out).max() + 1e-9)


def fade(x, fade_in=0.04, fade_out=0.12):
    n = len(x)
    x[: int(fade_in * RATE)] *= np.linspace(0, 1, int(fade_in * RATE))
    x[n - int(fade_out * RATE):] *= np.linspace(1, 0, int(fade_out * RATE))
    return x


def normalise(x, peak_db):
    return x / (np.abs(x).max() + 1e-9) * 10 ** (peak_db / 20)


def goal(rng):
    """About 2.4 s: the crowd erupts in staggered groups ("ah!"), with applause; settles by the end."""
    seconds = 2.4
    n = int(seconds * RATE)
    roar = np.zeros(n)
    # Groups of voices join at slightly different moments (like the visual cohorts) and fall back at different times.
    for group in range(10):
        start = rng.uniform(0.0, 0.3)
        formants = [(rng.uniform(600, 800), 220, 1.0), (rng.uniform(1100, 1350), 300, 0.55), (rng.uniform(2400, 2800), 500, 0.18)]
        voice = vowel(rng, seconds, formants)
        env = envelope(n, rng.uniform(0.18, 0.32), rng.uniform(0.7, 1.1), rng.uniform(0.6, 0.9), start)
        roar += voice * env * slow_wobble(rng, n, 6, 0.25) * rng.uniform(0.6, 1.0)
    body = band_noise(rng, seconds, 150, 450) * envelope(n, 0.3, 0.9, 0.9) * 0.35
    claps = applause(rng, seconds, 900, 0.35, 0.4, 0.8, 0.8) * 0.28
    return fade(normalise(roar / 10 + body + claps, -3))


def chance(rng):
    """About 1.25 s: a smaller "ooh" swell - fewer voices, rounder vowel, no applause."""
    seconds = 1.25
    n = int(seconds * RATE)
    swell = np.zeros(n)
    for group in range(5):
        start = rng.uniform(0.0, 0.15)
        formants = [(rng.uniform(330, 420), 120, 1.0), (rng.uniform(780, 900), 180, 0.45)]
        swell += vowel(rng, seconds, formants) * envelope(n, rng.uniform(0.25, 0.35), 0.25, rng.uniform(0.45, 0.6), start) * rng.uniform(0.6, 1.0)
    return fade(normalise(swell * slow_wobble(rng, n, 4, 0.15), -6))


def murmur(rng):
    """About 0.8 s: a low, soft stir for a won-back ball."""
    seconds = 0.8
    n = int(seconds * RATE)
    stir = band_noise(rng, seconds, 220, 650) * envelope(n, 0.2, 0.15, 0.4) * slow_wobble(rng, n, 5, 0.3)
    return fade(normalise(stir, -10))


def write(name, samples):
    path = os.path.join(OUT, name)
    with wave.open(path, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((np.clip(samples, -1, 1) * 32767).astype('<i2').tobytes())
    print(f'{name}: {len(samples) / RATE:.2f} s, {os.path.getsize(path) // 1024} KB')


if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    write('crowd-goal.wav', goal(np.random.default_rng(3501)))
    write('crowd-chance.wav', chance(np.random.default_rng(3502)))
    write('crowd-murmur.wav', murmur(np.random.default_rng(3503)))
