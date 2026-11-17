"""Building blocks for the built-in BGM and sound effects: envelopes, filters, and instruments from plain waves.

Every function here is pure apart from the random generator passed in, so a fixed seed renders the same audio.
"""
import numpy as np
from scipy.signal import butter, fftconvolve, sosfilt

SR = 48000


def midi(note):
    return 440.0 * 2 ** ((note - 69) / 12)


def env(n, attack, decay, sustain=0.0, release=0.03):
    t = np.arange(n) / SR
    a = np.clip(t / max(attack, 1e-4), 0, 1)
    d = sustain + (1 - sustain) * np.exp(-t / max(decay, 1e-4))
    r = np.clip((n / SR - t) / max(release, 1e-4), 0, 1)
    return a * d * r


def lowpass(x, hz, order=2):
    return sosfilt(butter(order, hz, 'low', fs=SR, output='sos'), x)


def highpass(x, hz, order=2):
    return sosfilt(butter(order, hz, 'high', fs=SR, output='sos'), x)


def band(x, lo, hi, order=2):
    return sosfilt(butter(order, [lo, min(hi, SR / 2 - 100)], 'band', fs=SR, output='sos'), x)


def circular(f, x, *args):
    """Filters a loop over three copies and keeps the middle, so its end runs on into its start without a click."""
    n = len(x)
    return f(np.tile(x, 3), *args)[n:2 * n]


class Loop:
    """A seamless loop of a fixed length in beats: a sound placed near the end wraps onto the start."""

    def __init__(self, bpm, beats):
        self.beat = 60.0 / bpm
        self.length = int(round(SR * self.beat * beats))

    def track(self):
        return np.zeros(self.length)

    def at(self, beats):
        return int(round(beats * self.beat * SR))

    def add(self, track, beats, sound, offset_seconds=0.0):
        s = (self.at(beats) + int(offset_seconds * SR)) % self.length
        end = s + len(sound)
        if end <= self.length:
            track[s:end] += sound
        else:
            cut = self.length - s
            track[s:] += sound[:cut]
            track[:end - self.length] += sound[cut:]

    def room(self, x, seconds, decay, level, rng, lowpass_hz=4000):
        """A small reverb from a decaying noise tail, its tail wrapped onto the start."""
        n = int(seconds * SR)
        ir = lowpass(rng.standard_normal(n), lowpass_hz) * np.exp(-np.arange(n) / SR * decay) * level
        wet = fftconvolve(x, ir)
        out = wet[:self.length].copy()
        tail = wet[self.length:]
        while len(tail):
            k = min(len(tail), self.length)
            out[:k] += tail[:k]
            tail = tail[k:]
        return out


# Instruments. Each returns a mono note of the given length in seconds.

def bell(freq, dur, ratio=3.5, index=2.2, decay=0.7):
    """Glockenspiel-like FM: an inharmonic ratio, a bright strike, a long ring."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    mod = np.sin(2 * np.pi * freq * ratio * t) * index * np.exp(-t * 9)
    return np.sin(2 * np.pi * freq * t + mod) * env(n, 0.002, decay)


def soft_square(freq, dur, vibrato=0.003):
    """A round square: a few odd harmonics only."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    vib = 1 + vibrato * np.sin(2 * np.pi * 6 * t) * np.clip((t - 0.12) / 0.2, 0, 1)
    ph = 2 * np.pi * freq * np.cumsum(vib) / SR
    tone = np.sin(ph) + np.sin(3 * ph) / 3 + np.sin(5 * ph) / 6 + np.sin(7 * ph) / 12
    return tone * env(n, 0.008, 0.35, 0.55, 0.04)


def pulse(freq, n, duty, vibrato=0.0):
    """A band-limited pulse wave, the chiptune sound; the duty sets how thin it is."""
    t = np.arange(n) / SR
    vib = 1 + vibrato * np.sin(2 * np.pi * 7 * t) * np.clip((t - 0.06) / 0.1, 0, 1)
    ph = 2 * np.pi * freq * np.cumsum(vib) / SR
    out = np.zeros(n)
    k = 1
    while k * freq < 9000 and k < 40:
        out += np.sin(np.pi * k * duty) / k * np.cos(k * ph - np.pi * k * duty)
        k += 1
    return out * 1.2


def saw_pluck(freq, dur, cutoff=2800):
    n = int(dur * SR)
    t = np.arange(n) / SR
    saw = 2 * ((freq * t) % 1.0) - 1
    return lowpass(saw * env(n, 0.002, 0.09), cutoff)


def string_pluck(freq, dur, rng, bright=0.5):
    """Karplus-Strong: noise through a short delay loop that loses its highs, as a plucked string does."""
    n = int(dur * SR)
    period = max(2, int(round(SR / freq)))
    buf = lowpass(rng.uniform(-1, 1, period), 1500 + 6000 * bright)
    out = np.empty(n)
    for i in range(n):
        out[i] = buf[i % period]
        buf[i % period] = 0.996 * 0.5 * (out[i] + buf[(i + 1) % period])
    return out * env(n, 0.001, 10.0, 1.0, 0.1)


def pad(freq, dur, attack=0.05, decay=0.6, sustain=0.6, release=0.08, detune=0.004):
    n = int(dur * SR)
    t = np.arange(n) / SR
    tone = sum(np.sin(2 * np.pi * freq * (1 + d) * t) for d in (-detune, 0.0, detune)) / 3
    tone += 0.3 * np.sin(2 * np.pi * freq * 2 * t)
    return tone * env(n, attack, decay, sustain, release)


def epiano(freq, dur):
    n = int(dur * SR)
    t = np.arange(n) / SR
    mod = np.sin(2 * np.pi * freq * 2 * t) * 1.2 * np.exp(-t * 6)
    tone = np.sin(2 * np.pi * freq * t + mod) + 0.25 * np.sin(2 * np.pi * freq * 2 * t)
    trem = 1 + 0.08 * np.sin(2 * np.pi * 4.5 * t)
    return tone * trem * env(n, 0.006, 1.4, 0.15, 0.3)


def recorder(freq, dur, rng):
    """A soprano recorder: nearly pure, breath around the note, and a chiff as the tongue releases the air."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    vib = 1 + 0.0045 * np.sin(2 * np.pi * 5.0 * t) * np.clip((t - 0.25) / 0.35, 0, 1)
    ph = 2 * np.pi * freq * np.cumsum(vib) / SR
    tone = np.sin(ph) + 0.10 * np.sin(2 * ph) + 0.07 * np.sin(3 * ph) + 0.02 * np.sin(4 * ph)
    shape = env(n, 0.035, 10.0, 1.0, 0.07)
    breath = band(rng.standard_normal(n), freq * 0.8, freq * 4) * 0.05
    chiff = band(rng.standard_normal(n), freq * 2.5, freq * 6) * env(n, 0.002, 0.025) * 0.35
    return (tone + breath) * shape + chiff


def triangle_lead(freq, dur):
    n = int(dur * SR)
    t = np.arange(n) / SR
    vib = 1 + 0.004 * np.sin(2 * np.pi * 5.2 * t) * np.clip(t / 0.25, 0, 1)
    phase = 2 * np.pi * freq * np.cumsum(vib) / SR
    tri = 2 / np.pi * np.arcsin(np.sin(phase))
    return lowpass(tri * env(n, 0.02, 0.5, 0.4, 0.12), 3000)


def bass(freq, dur, cutoff=900, drive=1.4):
    n = int(dur * SR)
    t = np.arange(n) / SR
    tone = np.sin(2 * np.pi * freq * t) + 0.35 * np.sin(2 * np.pi * freq * 2 * t)
    return lowpass(np.tanh(drive * tone) * env(n, 0.004, 0.18, 0.35, 0.03), cutoff)


# Drums.

def kick(low=50, sweep=90, decay=0.14):
    n = int(0.3 * SR)
    t = np.arange(n) / SR
    f = low + sweep * np.exp(-t * 35)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * env(n, 0.001, decay)


def clap(rng):
    n = int(0.22 * SR)
    noise = highpass(lowpass(rng.standard_normal(n), 5000), 900)
    shape = np.zeros(n)
    for k, delay in enumerate([0.0, 0.009, 0.018]):  # a clap is a few hands a moment apart
        s = int(delay * SR)
        shape[s:] += env(n - s, 0.001, 0.012 if k < 2 else 0.07)
    return noise * shape


def snare(rng, tone_hz=190):
    n = int(0.25 * SR)
    t = np.arange(n) / SR
    noise = lowpass(highpass(rng.standard_normal(n), 1200), 6000) * env(n, 0.001, 0.08)
    body = np.sin(2 * np.pi * tone_hz * t) * env(n, 0.001, 0.05)
    return 0.6 * noise + 0.5 * body


def hat(rng, open_=False):
    n = int((0.15 if open_ else 0.04) * SR)
    return highpass(rng.standard_normal(n), 8000, 4) * env(n, 0.001, 0.05 if open_ else 0.012)


def cymbal(rng, seconds=1.6):
    n = int(seconds * SR)
    return highpass(rng.standard_normal(n), 5000, 4) * env(n, 0.001, seconds / 3.5)


# Finishing.

def finish_loop(mono, delay_seconds=0.004, drive=1.1):
    """Soft-clips the loop and widens it a little; the right channel is the left a few ms late, wrapped round."""
    mono = np.tanh(mono * drive)
    return np.stack([mono, np.roll(mono, int(delay_seconds * SR))], axis=1)


def finish_effect(mono, delay_seconds=0.003):
    """A one-shot: a short fade at the end so nothing clicks, then the same small width."""
    mono = mono.copy()
    fade = min(len(mono), int(0.01 * SR))
    mono[-fade:] *= np.linspace(1, 0, fade)
    right = np.concatenate([np.zeros(int(delay_seconds * SR)), mono])[:len(mono)]
    return np.stack([mono, right], axis=1)
