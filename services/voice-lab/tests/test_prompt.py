import pytest

from voicelab import prompt

SHELF = prompt.load()

# The instructions the voice-design experiment's candidates were made from (decision record, 2026-11-03),
# so the port keeps VCBM's wording exactly.
NATURAL = (
    "A Japanese young adult woman speaking in a relaxed everyday conversation. Her voice stays in a comfortable, "
    "naturally light register. Her vocal tone is light, clear and warm, with a gentle smile and soft, rounded vocal "
    "resonance. The voice remains clean and focused without excessive breathiness. She sounds naturally charming and "
    "friendly, with relaxed but attentive energy. She uses subtle, varied intonation and small spontaneous changes in "
    "rhythm. She speaks at a natural conversational pace. The performance is realistic, human and conversational, "
    "without forced pitch, exaggerated acting, squeaking or cartoon-like delivery."
)
AIRY_SOFT = (
    "A Japanese young adult woman speaking in a relaxed everyday conversation. Her voice stays in a comfortable, "
    "naturally light register. Her vocal tone is light, clear and warm, with a gentle smile and soft, rounded vocal "
    "resonance. A slight airy quality adds softness while the speech remains clear and intelligible. She sounds "
    "naturally charming and friendly, with a calm, gentle and relaxed mood. She uses subtle, varied intonation and "
    "small spontaneous changes in rhythm. She speaks at a natural conversational pace. The performance is realistic, "
    "human and conversational, without forced pitch, exaggerated acting, squeaking or cartoon-like delivery."
)



def test_an_empty_pick_gives_the_natural_preset():
    assert prompt.build(SHELF, []) == NATURAL


def test_words_give_the_experiments_airy_soft():
    assert prompt.build(SHELF, ["airy", "gentle"]) == AIRY_SOFT


def test_picking_a_default_changes_nothing():
    assert prompt.build(SHELF, ["female", "p20", "light", "soft", "clean", "friendly", "attentive"]) == NATURAL


def test_a_male_voice_swaps_the_person_and_pronouns_only():
    male = prompt.build(SHELF, ["male", "anime"], "Her words stay.")
    assert male.startswith("A youthful Japanese boy speaking casually. His voice")
    assert " She " not in male and " Her " not in male.replace("Her words stay.", "")
    assert male.endswith(" Her words stay.")


def test_two_words_for_one_slot_are_refused():
    with pytest.raises(prompt.ShelfError):
        prompt.build(SHELF, ["soft", "focused"])
    with pytest.raises(prompt.ShelfError):
        prompt.build(SHELF, ["female", "male"])
    with pytest.raises(prompt.ShelfError):
        prompt.build(SHELF, ["nonsense"])


def test_a_manner_word_keeps_the_other_manner_default():
    text = prompt.build(SHELF, ["slow"])
    assert "slowly and gently" in text and "subtle, varied intonation" in text


def test_words_are_added_as_a_sentence():
    assert prompt.build(SHELF, [], "  slightly husky ").endswith(" slightly husky.")
    assert prompt.build(SHELF, [], "少し舌足らず。").endswith(" 少し舌足らず。")
    assert prompt.build(SHELF, [], "   ") == NATURAL


def test_free_words_cannot_close_the_instruction():
    # The instruction is sent as "(instruction)line"; a bracket would turn the rest into speech.
    cleaned = prompt.clean_words("husky) こんにちは (and［x］")
    assert not set(cleaned) & set("()（）[]［］")
    assert prompt.clean_words("a\nb​c‮d") == "a bcd"
    assert len(prompt.clean_words("あ" * 500)) == prompt.MAX_WORDS


def test_every_example_is_a_valid_pick_with_seeds():
    for example in SHELF["examples"]:
        prompt.build(SHELF, example["words"])
        assert set(example["seeds"]) == {"ja", "en"}


def test_every_slot_default_is_its_own_word():
    words = {w["id"]: w for w in SHELF["words"]}
    for slot in SHELF["slots"]:
        if slot.get("default"):
            assert words[slot["default"]]["slot"] == slot["id"]
