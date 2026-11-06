"""Moving a voice's timbre with WORLD (Voice Lab detailed design 8.3.2, 8.5).

WORLD splits a voice into pitch, the spectral envelope, and aperiodicity; each knob moves one part, so the
voice stays the same person. The same settings give the same result, for previews and for training speech.
The sizes and bands come from listening tests (decision record, 2026-11-06 "Shaping the timbre").
"""
from dataclasses import dataclass

import numpy as np

FRAME_MS = 5.0
STEP_LIMIT = 6
AGE_STEP = 0.025          # Formant ratio per age step: +4 is x1.10, the edit the user heard as the same person.
BREATH_STEPS_PER_DOUBLING = 5

# Brightness raises or lowers 2-5 kHz, where a voice sounds bright; +8 dB was heard as brighter and natural.
# The old slope about 1 kHz ran on to 24 kHz and was heard as hiss, so the band ends by 7 kHz.
BRIGHT_DB_PER_STEP = 8.0 / 6.0
BRIGHT_BAND_HZ = (1400.0, 2000.0, 5000.0, 7000.0)   # easing in, flat from, flat to, easing out by

# Softness raises the first harmonic and takes the edge off above 4 kHz. Tilting every harmonic instead
# sounded nasal, and the pressed side harsh, so there is only the soft side.
SOFT_H1_DB_PER_STEP = 1.0
SOFT_EDGE_DB_PER_STEP = -0.5
SOFT_EDGE_HZ = (3000.0, 4000.0)                     # easing in, full from


@dataclass(frozen=True)
class TimbreSettings:
    """Steps of -6 to +6 (softness 0 to +6); 0 changes nothing."""
    pitch: float = 0.0
    age: float = 0.0
    brightness: float = 0.0
    breathiness: float = 0.0
    softness: float = 0.0

    @staticmethod
    def from_request(values):
        """Settings from a request's object, each step clamped. Pure."""
        def step(key, low=-STEP_LIMIT):
            return float(min(STEP_LIMIT, max(low, float(values.get(key, 0.0)))))
        return TimbreSettings(step("pitch"), step("age"), step("brightness"), step("breathiness"), step("softness", 0.0))

    @property
    def unchanged(self):
        return self == TimbreSettings()


def formant_ratio(age):
    """How far the envelope is stretched along frequency; above 1 is a shorter vocal tract, younger. Pure."""
    return 1.0 + AGE_STEP * age


def breath_exponent(breathiness):
    """The power voiced aperiodicity is raised to; below 1 adds breath, above 1 removes it. Pure."""
    return 2.0 ** (-breathiness / BREATH_STEPS_PER_DOUBLING)


def _ease(x, start, end):
    """0 below start, 1 above end, a raised cosine between. Pure."""
    t = np.clip((x - start) / (end - start), 0.0, 1.0)
    return 0.5 - 0.5 * np.cos(np.pi * t)


def brightness_db(hz, brightness):
    """dB per bin: BRIGHT_DB_PER_STEP per step over 2-5 kHz, easing in and out in log-frequency. Pure."""
    rise_from, flat_from, flat_to, fall_by = np.log2(BRIGHT_BAND_HZ)
    x = np.log2(np.maximum(hz, 1.0))
    weight = _ease(x, rise_from, flat_from) * (1.0 - _ease(x, flat_to, fall_by))
    return BRIGHT_DB_PER_STEP * brightness * weight


def softness_db(hz, f0, softness):
    """dB per bin for every frame (frames x bins): the first harmonic raised, following each frame's pitch
    (half a harmonic wide each side), and the highs above 4 kHz lowered. Unvoiced frames get the highs only. Pure."""
    pitch = np.asarray(f0, dtype=float)[:, None]
    distance = np.abs(hz[None, :] - pitch) / np.maximum(0.5 * pitch, 1e-9)
    first = np.where((pitch > 0) & (distance < 1.0), 0.5 * (1.0 + np.cos(np.pi * np.minimum(distance, 1.0))), 0.0)
    log_hz = np.log2(np.maximum(hz, 1.0))
    edge = _ease(log_hz, *np.log2(SOFT_EDGE_HZ))[None, :]
    return softness * (SOFT_H1_DB_PER_STEP * first + SOFT_EDGE_DB_PER_STEP * edge)


def edit(f0, envelope, aperiodicity, sample_rate, settings):
    """The WORLD parts moved by the settings, as new arrays. Pure."""
    voiced = f0 > 0
    out_f0 = np.where(voiced, f0 * 2.0 ** (settings.pitch / 12.0), 0.0)
    bins = envelope.shape[1]
    hz = np.linspace(0.0, sample_rate / 2.0, bins)
    ratio = formant_ratio(settings.age)
    out_env = envelope
    if ratio != 1.0:
        # A bin at frequency f takes the envelope from f / ratio, so the formants move up by the ratio.
        source = np.arange(bins) / ratio
        out_env = np.array([np.interp(source, np.arange(bins), frame) for frame in envelope])
    gain_db = np.zeros_like(envelope)
    if settings.brightness:
        gain_db = gain_db + brightness_db(hz, settings.brightness)[None, :]
    if settings.softness:
        gain_db = gain_db + softness_db(hz, out_f0, settings.softness)
    out_ap = aperiodicity.copy()
    if gain_db.any():
        out_env = out_env * 10.0 ** (gain_db / 10.0)
        # Where the envelope was raised, the noise is kept at its level so only the harmonics grow: raising
        # breath and noise with them was heard as hoarseness. WORLD mixes noise with power proportional to
        # aperiodicity squared times the envelope, so aperiodicity is divided by the root of the power gain.
        raised = 10.0 ** (-np.maximum(gain_db, 0.0) / 20.0)
        out_ap[voiced] = np.clip(aperiodicity[voiced] * raised[voiced], 1e-12, 1.0)
    if settings.breathiness:
        out_ap[voiced] = np.clip(out_ap[voiced], 1e-12, 1.0) ** breath_exponent(settings.breathiness)
    return out_f0, out_env, out_ap


def analyze(samples, sample_rate):
    """WORLD's parts of mono samples: f0 (Harvest), the envelope (CheapTrick), aperiodicity (D4C)."""
    import pyworld
    x = np.ascontiguousarray(samples, dtype=np.float64)
    f0, times = pyworld.harvest(x, sample_rate, f0_floor=70.0, f0_ceil=900.0, frame_period=FRAME_MS)
    return f0, pyworld.cheaptrick(x, f0, times, sample_rate), pyworld.d4c(x, f0, times, sample_rate)


def synthesize(f0, envelope, aperiodicity, sample_rate):
    """Samples from WORLD's parts, kept below full scale."""
    import pyworld
    y = pyworld.synthesize(f0, envelope, aperiodicity, sample_rate, frame_period=FRAME_MS)
    return (y / max(1.0, float(np.abs(y).max()) / 0.95)).astype(np.float32)


def apply(samples, sample_rate, settings):
    """The samples with the settings applied; unchanged settings give the samples back as they are."""
    if settings.unchanged:
        return np.asarray(samples, dtype=np.float32)
    return synthesize(*edit(*analyze(samples, sample_rate), sample_rate, settings), sample_rate)
