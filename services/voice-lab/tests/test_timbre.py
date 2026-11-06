import numpy as np
import pytest

from voicelab.timbre import TimbreSettings, breath_exponent, brightness_db, edit, formant_ratio, softness_db

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


def hz(bins=1025):
    return np.linspace(0.0, RATE / 2.0, bins)


def bin_of(frequency, bins=1025):
    return int(round(frequency / (RATE / 2.0) * (bins - 1)))


def test_brightness_moves_2_to_5_khz_and_leaves_the_rest():
    db = brightness_db(hz(), 6)

    assert db[bin_of(3000)] == pytest.approx(8.0)
    assert db[bin_of(500)] == 0.0
    # The old slope ran on into the highs and was heard as hiss: nothing above 7 kHz moves.
    assert (db[hz() >= 7000] == 0.0).all()
    assert brightness_db(hz(), -3)[bin_of(3000)] == pytest.approx(-4.0)


def test_raising_keeps_the_noise_at_its_level():
    f0, env, ap = parts()
    _, out_env, out_ap = edit(f0, env, ap, RATE, TimbreSettings(brightness=6))
    band = bin_of(3000)

    voiced = f0 > 0
    assert out_env[0, band] == pytest.approx(env[0, band] * 10 ** 0.8)
    # Noise power is aperiodicity squared times the envelope: the same as before where voiced.
    np.testing.assert_allclose((out_ap[voiced, band] ** 2) * out_env[voiced, band], (ap[voiced, band] ** 2) * env[voiced, band])
    assert (out_ap[~voiced] == 0.25).all()


def test_softness_raises_the_first_harmonic_and_lowers_the_highs():
    at = np.array([200.0, 400.0, 6000.0])
    db = softness_db(at, np.array([200.0, 0.0]), 6)

    assert db[0, 0] == pytest.approx(6.0)
    assert db[0, 1] == pytest.approx(0.0, abs=0.01), "The second harmonic stays."
    assert db[0, 2] == pytest.approx(-3.0)
    assert db[1, 0] == 0.0, "No first harmonic in an unvoiced frame."


def test_softness_follows_the_pitch_after_the_pitch_knob():
    f0, env, ap = parts()
    _, out_env, _ = edit(f0, env, ap, RATE, TimbreSettings(pitch=12, softness=6))

    voiced = int(np.flatnonzero(f0 > 0)[0])
    expected_db = softness_db(hz(), np.array([400.0]), 6)[0]
    np.testing.assert_allclose(out_env[voiced], env[voiced] * 10 ** (expected_db / 10))
    assert expected_db[bin_of(400)] > 5.5, "The first harmonic is at the moved pitch, 400 Hz."


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
    edit(f0, env, ap, RATE, TimbreSettings(pitch=3, age=2, brightness=1, breathiness=-2, softness=2))

    for a, b in zip((f0, env, ap), copies):
        np.testing.assert_array_equal(a, b)


def test_request_values_are_clamped_and_default_to_zero():
    settings = TimbreSettings.from_request({"pitch": 20, "age": -9, "brightness": "1.5", "softness": -3})

    assert settings == TimbreSettings(pitch=6, age=-6, brightness=1.5, breathiness=0, softness=0)
    assert TimbreSettings.from_request({"softness": 9}).softness == 6
    assert TimbreSettings.from_request({}).unchanged
