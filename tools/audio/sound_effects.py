"""The built-in sound effects: short one-shots synthesized from plain waves and noise, with no samples."""
import numpy as np

from synth import SR, bell, band, env, finish_effect, highpass, kick, lowpass, midi, pulse, snare


def _place(total_seconds, *parts):
    """Mixes (start seconds, sound) parts into one buffer of the given length."""
    out = np.zeros(int(total_seconds * SR))
    for start, sound in parts:
        s = int(start * SR)
        k = min(len(sound), len(out) - s)
        out[s:s + k] += sound[:k]
    return out


def chime():
    """A two-tone door chime: the high note, then the low one ringing on. A fifth higher than a house's, so it rings
    out brightly over a stream (the user's request, 2026-11-20)."""
    return finish_effect(_place(1.8, (0.0, bell(midi(83), 1.0, 2.0, 1.0, 0.6)), (0.32, bell(midi(79), 1.45, 2.0, 1.0, 0.8))))


def correct():
    """A bright rising arpeggio that lands on a held top note."""
    notes = [72, 76, 79, 84]
    parts = [(k * 0.07, bell(midi(n + 12), 0.5 if k < 3 else 1.0, 3.0, 1.5, 0.3 if k < 3 else 0.5)) for k, n in enumerate(notes)]
    return finish_effect(_place(1.3, *parts))


def wrong():
    """Two buzzes, the second held: a quiz show's playful miss rather than an alarm, so brighter than a plain low
    buzz, low in pitch, and the second drawn out (the user's requests, 2026-11-20)."""
    def buzz(dur):
        n = int(dur * SR)
        tone = pulse(midi(43), n, 0.35) + pulse(midi(43) * 1.01, n, 0.35) + 0.5 * pulse(midi(50), n, 0.35)
        return lowpass(tone, 4200) * env(n, 0.005, 10, 1.0, 0.08)
    return finish_effect(_place(1.5, (0.0, buzz(0.2)), (0.28, buzz(1.1))) * 0.45)


def pop():
    """A small bubble pop: a sine dropping fast in pitch."""
    n = int(0.12 * SR)
    t = np.arange(n) / SR
    f = 300 + 900 * np.exp(-t * 45)
    return finish_effect(np.sin(2 * np.pi * np.cumsum(f) / SR) * env(n, 0.001, 0.035))


def sparkle(rng):
    """A quick run of high bells going up, with a shimmer of noise."""
    notes = [84, 88, 91, 96, 100, 103]
    parts = [(k * 0.045, bell(midi(n), 0.8, 3.5, 1.0, 0.25) * (0.6 + 0.08 * k)) for k, n in enumerate(notes)]
    n = int(0.6 * SR)
    shimmer = band(rng.standard_normal(n), 7000, 14000) * env(n, 0.05, 0.15) * 0.15
    return finish_effect(_place(1.2, *parts, (0.0, shimmer)))


def crash(rng, seconds=3.2):
    """A crash cymbal: a bright wash of noise over clangy inharmonic partials, with a hard first splash."""
    n = int(seconds * SR)
    t = np.arange(n) / SR
    wash = highpass(rng.standard_normal(n), 3000, 4) * env(n, 0.001, seconds / 3.0)
    splash = highpass(rng.standard_normal(n), 6000, 2) * env(n, 0.0005, 0.12)
    partials = sum(np.sign(np.sin(2 * np.pi * f * t + rng.uniform(0, 6.3))) for f in (342, 517, 788, 1213, 1587, 2437, 3311))
    clang = highpass(partials, 1500) * env(n, 0.001, seconds / 4.0) * 0.06
    return wash + splash * 0.8 + clang


def drumroll(rng):
    """A snare roll that grows for two seconds and bursts into a big crash: two cymbals a moment apart, a kick and a
    snare under them (the user asked for a showier ending, 2026-11-20)."""
    hits = []
    t = 0.0
    while t < 2.0:
        hits.append((t, snare(rng, 220) * (0.2 + 0.25 * t / 2.0)))
        t += 0.045
    ending = [(2.05, kick(45, 90, 0.3) * 1.1), (2.05, snare(rng, 200) * 0.8), (2.05, crash(rng, 3.2) * 1.0), (2.07, crash(rng, 2.8) * 0.7)]
    return finish_effect(_place(5.0, *hits, *ending) * 0.8)


def whoosh(rng):
    """Air rushing past: noise through a band that sweeps up and back down."""
    n = int(0.6 * SR)
    noise = rng.standard_normal(n)
    out = np.zeros(n)
    chunk = int(0.01 * SR)
    for s in range(0, n, chunk):
        x = s / n
        centre = 400 + 3600 * np.sin(np.pi * x) ** 2
        piece = band(noise[max(0, s - 2048):s + chunk], centre * 0.7, centre * 1.4)
        out[s:s + chunk] = piece[-min(chunk, n - s):]
    return finish_effect(out * np.sin(np.pi * np.linspace(0, 1, n)) ** 1.5 * 1.4)


def laser():
    """A retro 'pew': a square falling two octaves fast."""
    n = int(0.3 * SR)
    t = np.arange(n) / SR
    ph = 2 * np.pi * np.cumsum(midi(96) * 2 ** (-7 * t)) / SR
    return finish_effect(lowpass(np.sign(np.sin(ph)), 7000) * 0.4 * env(n, 0.002, 0.1))


def boing():
    """A spring: a low sine whose pitch wobbles and settles."""
    n = int(0.7 * SR)
    t = np.arange(n) / SR
    f = 180 * (1 + 0.45 * np.sin(2 * np.pi * 11 * t) * np.exp(-t * 4)) * (1 + 0.6 * np.exp(-t * 12))
    tone = np.sin(2 * np.pi * np.cumsum(f) / SR)
    return finish_effect(np.tanh(tone * 1.5) * env(n, 0.002, 0.25) * 0.8)


def coin():
    """A game coin: a short square note, then a higher one held."""
    def note(m, dur):
        n = int(dur * SR)
        return pulse(midi(m), n, 0.5) * env(n, 0.002, 10, 1.0, 0.04) * 0.4
    return finish_effect(_place(0.6, (0.0, note(83, 0.07)), (0.07, note(88, 0.4) * env(int(0.4 * SR), 0.001, 0.18))))


def applause(rng):
    """A big room clapping: over a thousand claps from people of many hand sizes, on a wash of the crowd, swelling and
    fading (the user asked for more people, 2026-11-20)."""
    seconds = 3.5
    n = int(seconds * SR)
    out = np.zeros(n)
    count = 1400
    times = np.sort(rng.uniform(0, seconds - 0.3, count))
    m = int(0.03 * SR)
    for t0 in times:
        low = rng.uniform(600, 2200)
        clap_ = band(rng.standard_normal(m), low, low * rng.uniform(2.5, 5.0)) * env(m, 0.0005, rng.uniform(0.005, 0.012))
        s = int(t0 * SR)
        out[s:s + m] += clap_[:max(0, min(m, n - s))] * rng.uniform(0.15, 0.6)
    crowd = band(rng.standard_normal(n), 500, 6000) * 0.12
    x = np.linspace(0, 1, n)
    swell = np.clip(x / 0.12, 0, 1) * np.clip((1 - x) / 0.35, 0, 1)
    return finish_effect(highpass(out + crowd, 300) * swell * 0.4)
