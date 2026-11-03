"""Moving a voice's timbre with WORLD (Voice Lab detailed design 8.3.2, 8.5).

WORLD splits a voice into pitch, the spectral envelope, and aperiodicity; each knob moves one part, so the
voice stays the same person. The same settings give the same result, for previews and for training speech.
"""
from dataclasses import dataclass

import numpy as np

FRAME_MS = 5.0
STEP_LIMIT = 6
AGE_STEP = 0.025          # Formant ratio per age step: +4 is x1.10, the edit the user heard as the same person.
BRIGHT_DB_PER_OCTAVE = 1.5
BRIGHT_PIVOT_HZ = 1000.0
BREATH_STEPS_PER_DOUBLING = 5


@dataclass(frozen=True)
class TimbreSettings:
    """Steps of -6 to +6; 0 changes nothing."""
    pitch: float = 0.0
    age: float = 0.0
    brightness: float = 0.0
    breathiness: float = 0.0

    @staticmethod
    def from_request(values):
        """Settings from a request's object, each step clamped. Pure."""
        def step(key):
            return float(min(STEP_LIMIT, max(-STEP_LIMIT, float(values.get(key, 0.0)))))
        return TimbreSettings(step("pitch"), step("age"), step("brightness"), step("breathiness"))

    @property
    def unchanged(self):
        return self == TimbreSettings()


def formant_ratio(age):
    """How far the envelope is stretched along frequency; above 1 is a shorter vocal tract, younger. Pure."""
    return 1.0 + AGE_STEP * age


def breath_exponent(breathiness):
    """The power voiced aperiodicity is raised to; below 1 adds breath, above 1 removes it. Pure."""
    return 2.0 ** (-breathiness / BREATH_STEPS_PER_DOUBLING)


def tilt_gains(bins, sample_rate, brightness):
    """Power gains per envelope bin: a slope in dB per octave about the pivot; the lowest bin is held at 50 Hz
    so the slope does not run away at 0 Hz. Pure."""
    hz = np.maximum(np.linspace(0.0, sample_rate / 2.0, bins), 50.0)
    db = BRIGHT_DB_PER_OCTAVE * brightness * np.log2(hz / BRIGHT_PIVOT_HZ)
    return 10.0 ** (db / 10.0)


def edit(f0, envelope, aperiodicity, sample_rate, settings):
    """The WORLD parts moved by the settings, as new arrays. Pure."""
    voiced = f0 > 0
    out_f0 = np.where(voiced, f0 * 2.0 ** (settings.pitch / 12.0), 0.0)
    bins = envelope.shape[1]
    ratio = formant_ratio(settings.age)
    out_env = envelope
    if ratio != 1.0:
        # A bin at frequency f takes the envelope from f / ratio, so the formants move up by the ratio.
        source = np.arange(bins) / ratio
        out_env = np.array([np.interp(source, np.arange(bins), frame) for frame in envelope])
    if settings.brightness:
        out_env = out_env * tilt_gains(bins, sample_rate, settings.brightness)
    out_ap = aperiodicity.copy()
    if settings.breathiness:
        out_ap[voiced] = np.clip(aperiodicity[voiced], 1e-12, 1.0) ** breath_exponent(settings.breathiness)
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
