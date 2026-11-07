import pytest

import generate
from voicelab import protocol


def test_candidates_need_no_reference():
    assert generate.reference_of({}) is None
    assert generate.reference_of({"reference": ""}) is None


def test_a_reference_must_be_a_file_that_exists(tmp_path):
    wav = tmp_path / "picked.wav"
    wav.write_bytes(b"RIFF")

    assert generate.reference_of({"reference": str(wav)}) == str(wav)
    with pytest.raises(FileNotFoundError):
        generate.reference_of({"reference": str(tmp_path / "missing.wav")})
    with pytest.raises(protocol.BadRequest):
        generate.reference_of({"reference": 3})


def test_an_unknown_voice_is_refused_before_the_model_loads(tmp_path):
    request = {"count": 1, "firstSeed": 1, "output": str(tmp_path), "voice": "neutral", "model": "unused"}
    with pytest.raises(protocol.BadRequest):
        generate.candidates(request, None)
