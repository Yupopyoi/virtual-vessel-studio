import numpy as np
import pytest

from voicelab import corpus, pitch


def test_the_summary_skips_unvoiced_frames():
    f0 = np.array([0.0, np.nan, 100.0, 200.0, 300.0])

    median, low, high = pitch.summary(f0)

    assert median == 200.0
    assert low == pytest.approx(110.0)
    assert high == pytest.approx(290.0)


def test_nothing_voiced_has_no_summary():
    assert pitch.summary([0.0, np.nan]) is None


def test_a_tone_is_tracked_at_its_pitch():
    rate = 16000
    t = np.arange(rate) / rate
    stats = pitch.describe(0.3 * np.sin(2 * np.pi * 220.0 * t), rate)

    assert abs(stats["medianHz"] - 220) <= 2


def test_every_text_is_read_twice_in_order():
    lines = corpus.lines()

    assert [i for i, _ in lines] == list(range(len(corpus.TEXTS) * 2))
    assert [text for _, text in lines[:2]] == [corpus.TEXTS[0]] * 2


def test_the_texts_carry_the_five_vowels_and_held_sounds():
    text = "".join(corpus.TEXTS)

    for vowel in "あいうえお":
        assert vowel in text
    assert "ー" in text
