import pytest

import generate
from voicelab import prompt, protocol


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


def test_a_bad_shelf_is_refused_before_the_model_loads(tmp_path):
    request = {"count": 1, "firstSeed": 1, "output": str(tmp_path), "shelf": ["soft", "focused"], "model": "unused"}
    with pytest.raises(protocol.BadRequest):
        generate.candidates(request, None)
    with pytest.raises(protocol.BadRequest):
        generate.instruction_of({"shelf": "soft"})


def test_the_shelf_and_words_make_the_instruction():
    assert generate.instruction_of({"shelf": ["male"], "words": "husky)"}).endswith(" husky.")


def test_samples_are_the_examples_the_base_and_each_word_that_is_not_a_default():
    shelf = prompt.load()
    plan = generate.sample_plan(shelf, ["ja"])
    names = [name for _, name, _, _ in plan]
    assert names[:len(shelf["examples"])] == [f"example_{e['id']}.wav" for e in shelf["examples"]]
    assert "base.wav" in names and "word_male.wav" in names and "word_soft.wav" not in names
    assert {seed for _, name, _, seed in plan if name.startswith("word_")} == {shelf["sampleSeed"]}
    with pytest.raises(protocol.BadRequest):
        generate.sample_plan(shelf, ["xx"])
