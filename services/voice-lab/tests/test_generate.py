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
