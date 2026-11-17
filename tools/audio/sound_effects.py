"""The built-in sound effects: short one-shots synthesized from plain waves and noise, with no samples."""
import numpy as np

from synth import SR, bell, band, cymbal, env, finish_effect, highpass, kick, lowpass, midi, pulse, snare


def _place(total_seconds, *parts):
    """Mixes (start seconds, sound) parts into one buffer of the given length."""
    out = np.zeros(int(total_seconds * SR))
    for start, sound in parts:
        s = int(start * SR)
        k = min(len(sound), len(out) - s)
        out[s:s + k] += sound[:k]
    return out


def chime():
    """A two-tone door chime: the high note, then the low one ringing on."""
    return finish_effect(_place(1.8, (0.0, bell(midi(76), 1.0, 2.0, 1.0, 0.6)), (0.32, bell(midi(72), 1.45, 2.0, 1.0, 0.8))))


def correct():
    """A bright rising arpeggio that lands on a held top note."""
    notes = [72, 76, 79, 84]
    parts = [(k * 0.07, bell(midi(n + 12), 0.5 if k < 3 else 1.0, 3.0, 1.5, 0.3 if k < 3 else 0.5)) for k, n in enumerate(notes)]
    return finish_effect(_place(1.3, *parts))


def wrong():
    """Two low buzzes, the second longer."""
    def buzz(dur):
        n = int(dur * SR)
        return lowpass(pulse(midi(41), n, 0.5) + pulse(midi(41) * 1.012, n, 0.5), 1800) * env(n, 0.005, 10, 1.0, 0.03)
    return finish_effect(_place(0.9, (0.0, buzz(0.22)), (0.3, buzz(0.55))) * 0.6)


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


def drumroll(rng):
    """A snare roll that grows for two seconds and ends on a cymbal with a kick under it."""
    hits = []
    t = 0.0
    while t < 2.0:
        hits.append((t, snare(rng, 220) * (0.25 + 0.35 * t / 2.0)))
        t += 0.045
    return finish_effect(_place(4.0, *hits, (2.05, kick(48, 80, 0.2) * 0.9), (2.05, cymbal(rng, 1.9) * 0.7)) * 0.8)


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


def rimshot(rng):
    """The comedy sting: two snare hits and a cymbal (ba-dum-tss)."""
    return finish_effect(_place(1.6, (0.0, snare(rng, 240) * 0.7), (0.16, snare(rng, 200) * 0.6), (0.16, kick() * 0.7),
                                (0.38, cymbal(rng, 1.1) * 0.6)))


def coin():
    """A game coin: a short square note, then a higher one held."""
    def note(m, dur):
        n = int(dur * SR)
        return pulse(midi(m), n, 0.5) * env(n, 0.002, 10, 1.0, 0.04) * 0.4
    return finish_effect(_place(0.6, (0.0, note(83, 0.07)), (0.07, note(88, 0.4) * env(int(0.4 * SR), 0.001, 0.18))))


def fanfare(rng):
    """A short cheerful fanfare for the start of a stream: a pickup run to a held major chord with a cymbal."""
    def brass(m, dur):
        n = int(dur * SR)
        t = np.arange(n) / SR
        vib = 1 + 0.004 * np.sin(2 * np.pi * 5.5 * t) * np.clip((t - 0.15) / 0.2, 0, 1)
        ph = 2 * np.pi * midi(m) * np.cumsum(vib) / SR
        tone = sum(np.sin(k * ph) / k ** 1.1 for k in range(1, 9))
        return lowpass(tone, 3500) * env(n, 0.03, 10, 1.0, 0.15) * 0.25
    run = [(0.0, brass(67, 0.12)), (0.14, brass(67, 0.12)), (0.28, brass(67, 0.12)), (0.42, brass(72, 0.3))]
    held = [(0.75, brass(m, 1.2)) for m in (60, 64, 67, 72)]
    sparkle_top = [(0.75 + k * 0.06, bell(midi(m), 0.9, 3.5, 1.0, 0.4) * 0.25) for k, m in enumerate((84, 88, 91, 96))]
    return finish_effect(_place(2.6, *run, *held, *sparkle_top, (0.75, cymbal(rng, 1.6) * 0.35), (0.75, kick() * 0.6)))


def applause(rng):
    """A short burst of clapping: many hand claps at random moments, swelling and fading."""
    n = int(3.0 * SR)
    out = np.zeros(n)
    count = 260
    times = np.sort(rng.uniform(0, 2.7, count))
    for t0 in times:
        m = int(0.03 * SR)
        clap_ = band(rng.standard_normal(m), 900 + rng.uniform(0, 1500), 5000) * env(m, 0.0005, 0.008)
        s = int(t0 * SR)
        out[s:s + m] += clap_[:max(0, min(m, n - s))] * rng.uniform(0.3, 1.0)
    swell = np.clip(np.linspace(0, 1, n) / 0.15, 0, 1) * np.clip((1 - np.linspace(0, 1, n)) / 0.4, 0, 1)
    return finish_effect(highpass(out, 300) * swell * 0.5)
