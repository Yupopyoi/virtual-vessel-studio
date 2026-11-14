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


def test_a_written_prompt_replaces_the_shelf_and_is_cleaned():
    assert generate.instruction_of({"shelf": ["male"], "words": "husky", "prompt": "A calm (woman)\nspeaking."}) == "A calm woman speaking."


def test_a_written_prompt_is_cut_at_500_characters_and_cannot_be_empty():
    assert len(generate.instruction_of({"prompt": "a" * 600})) == prompt.MAX_PROMPT
    with pytest.raises(protocol.BadRequest):
        generate.instruction_of({"prompt": "()\n"})
    with pytest.raises(protocol.BadRequest):
        generate.instruction_of({"prompt": 3})


def test_samples_are_the_examples_the_base_and_each_word_that_is_not_a_default():
    shelf = prompt.load()
    plan = generate.sample_plan(shelf, ["ja"])
    names = [name for _, name, _, _ in plan]
    assert names[:len(shelf["examples"])] == [f"example_{e['id']}.wav" for e in shelf["examples"]]
    assert "base.wav" in names and "word_male.wav" in names and "word_soft.wav" not in names
    assert {seed for _, name, _, seed in plan if name.startswith("word_")} == {shelf["sampleSeed"]}
    with pytest.raises(protocol.BadRequest):
        generate.sample_plan(shelf, ["xx"])


def test_speech_can_be_limited_to_a_few_lines_for_checking_the_screens():
    lines = [(0, "a"), (1, "b"), (2, "c")]
    assert generate.limited(lines, None) == lines
    assert generate.limited(lines, 2) == lines[:2]
    assert generate.limited(lines, 10) == lines
    for bad in (0, -1, "2", 1.5, True):
        with pytest.raises(protocol.BadRequest):
            generate.limited(lines, bad)


def test_candidates_read_the_shelfs_japanese_line_unless_the_user_wrote_one():
    shelf = prompt.load()
    assert generate.line_of({}, shelf) == shelf["lines"]["ja"]
    assert generate.line_of({"line": ""}, shelf) == shelf["lines"]["ja"]
    assert generate.line_of({"line": "おはよう。"}, shelf) == "おはよう。"


def test_a_written_line_is_cleaned_so_it_cannot_become_an_instruction():
    shelf = prompt.load()
    assert generate.line_of({"line": "(calm)\nこんにちは"}, shelf) == "calm こんにちは"
    assert len(generate.line_of({"line": "あ" * 300}, shelf)) == generate.MAX_LINE
    with pytest.raises(protocol.BadRequest):
        generate.line_of({"line": "（）\n"}, shelf)
    with pytest.raises(protocol.BadRequest):
        generate.line_of({"line": 3}, shelf)


def test_a_seed_outside_what_unity_holds_is_refused_before_the_model_loads(tmp_path):
    request = {"count": 2, "output": str(tmp_path), "shelf": [], "model": "unused"}
    for seed in (-1, generate.MAX_SEED - 1, True):
        with pytest.raises(protocol.BadRequest):
            generate.candidates({**request, "firstSeed": seed}, None)


def test_every_sample_language_has_a_line():
    shelf = prompt.load()
    plan = generate.sample_plan(shelf, ["ja", "en"])
    assert {lang for lang, *_ in plan} == {"ja", "en"}
    with pytest.raises(protocol.BadRequest):
        generate.sample_plan(shelf, ["fr"])
