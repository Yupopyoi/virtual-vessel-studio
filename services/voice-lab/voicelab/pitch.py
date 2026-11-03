"""A voice's pitch as the screens show it (Voice Lab detailed design 8.3.1): the median and the 5 to 95% range.

The median rather than the mean: a cracked note or a misread octave pulls a mean far more.
"""
import numpy as np

TRACK_RATE = 16000


def summary(f0):
    """(median, 5%, 95%) in Hz over the voiced frames (f0 > 0, not NaN); None when nothing is voiced. Pure."""
    f0 = np.asarray(f0, dtype=np.float64)
    voiced = f0[np.isfinite(f0) & (f0 > 0)]
    if voiced.size == 0:
        return None
    return float(np.median(voiced)), float(np.percentile(voiced, 5)), float(np.percentile(voiced, 95))


def track(samples, sample_rate):
    """f0 per 10 ms with pYIN, 0 where unvoiced."""
    import librosa
    y = librosa.resample(np.asarray(samples, dtype=np.float32), orig_sr=sample_rate, target_sr=TRACK_RATE)
    f0, voiced, _ = librosa.pyin(y, fmin=60, fmax=1000, sr=TRACK_RATE, frame_length=1024, hop_length=160)
    return np.where(voiced, f0, 0.0)


def describe(samples, sample_rate):
    """The summary as a request result's fields, rounded to whole Hz; empty when nothing is voiced."""
    stats = summary(track(samples, sample_rate))
    if stats is None:
        return {}
    median, low, high = stats
    return {"medianHz": round(median), "lowHz": round(low), "highHz": round(high)}
