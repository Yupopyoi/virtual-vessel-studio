import numpy as np
import pytest

from voicelab.timbre import TimbreSettings, breath_exponent, edit, formant_ratio, tilt_gains

RATE = 48000


def parts(frames=20, bins=1025):
    f0 = np.where(np.arange(frames) % 5 == 0, 0.0, 200.0)
    # One formant-like peak at bin 100, on a falling floor.
    envelope = np.tile(np.exp(-((np.arange(bins) - 100.0) ** 2) / 200.0) + 1.0 / (1.0 + np.arange(bins)), (frames, 1))
    aperiodicity = np.full((frames, bins), 0.25)
    return f0, envelope, aperiodicity


def test_no_change_gives_the_same_parts():
    f0, env, ap = parts()
    out = edit(f0, env, ap, RATE, TimbreSettings())

    for a, b in zip(out, (f0, env, ap)):
        np.testing.assert_array_equal(a, b)


def test_pitch_moves_voiced_frames_by_semitones():
    f0, env, ap = parts()
    out_f0, _, _ = edit(f0, env, ap, RATE, TimbreSettings(pitch=12))

    np.testing.assert_allclose(out_f0[f0 > 0], 400.0)
    assert (out_f0[f0 == 0] == 0).all()


def test_age_moves_the_formants_up_by_the_ratio():
    f0, env, ap = parts()
    _, out_env, _ = edit(f0, env, ap, RATE, TimbreSettings(age=4))

    assert formant_ratio(4) == pytest.approx(1.10)
    assert int(np.argmax(out_env[0])) == 110


def test_brightness_raises_highs_and_holds_the_pivot():
    gains = tilt_gains(1025, RATE, 2)
    pivot = int(round(1000.0 / (RATE / 2) * 1024))

    assert gains[pivot] == pytest.approx(1.0, abs=0.01)
    assert gains[-1] > 1.0 > gains[10]


def test_breathiness_adds_breath_to_voiced_frames_only():
    f0, env, ap = parts()
    _, _, more = edit(f0, env, ap, RATE, TimbreSettings(breathiness=5))
    _, _, less = edit(f0, env, ap, RATE, TimbreSettings(breathiness=-5))

    assert breath_exponent(5) == pytest.approx(0.5)
    assert (more[f0 > 0] > 0.25).all() and (less[f0 > 0] < 0.25).all()
    assert (more[f0 == 0] == 0.25).all()


def test_the_edit_does_not_change_its_inputs():
    f0, env, ap = parts()
    copies = [a.copy() for a in (f0, env, ap)]
    edit(f0, env, ap, RATE, TimbreSettings(pitch=3, age=2, brightness=1, breathiness=-2))

    for a, b in zip((f0, env, ap), copies):
        np.testing.assert_array_equal(a, b)


def test_request_values_are_clamped_and_default_to_zero():
    settings = TimbreSettings.from_request({"pitch": 20, "age": -9, "brightness": "1.5"})

    assert settings == TimbreSettings(pitch=6, age=-6, brightness=1.5, breathiness=0)
    assert TimbreSettings.from_request({}).unchanged
