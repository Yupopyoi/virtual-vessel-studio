from voicelab import prompt

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


def test_the_defaults_give_the_natural_preset():
    assert prompt.build({}) == NATURAL


def test_sliders_give_the_experiments_airy_soft():
    assert prompt.build({"softness": 0.85, "breathiness": 0.80, "energy": 0.25, "speed": 0.35}) == AIRY_SOFT


def test_a_slider_at_zero_leaves_its_sentence_out():
    assert "intonation" not in prompt.build({"expressiveness": 0})


def test_values_are_clamped():
    assert prompt.build({"youthful": 5}) == prompt.build({"youthful": 1})


def test_words_are_added_as_a_sentence():
    assert prompt.build({}, "  slightly husky ").endswith(" slightly husky.")
    assert prompt.build({}, "少し舌足らず。").endswith(" 少し舌足らず。")
    assert prompt.build({}, "   ") == NATURAL


def test_a_male_voice_swaps_the_person_and_pronouns_only():
    male = prompt.build({"youthful": 0.95}, "Her words stay.", "male")
    assert male.startswith("A youthful Japanese boy speaking casually. His voice")
    assert " She " not in male and " Her " not in male.replace("Her words stay.", "")
    assert male.endswith(" Her words stay.")
    assert prompt.build({}, voice="female") == NATURAL


def test_an_unknown_voice_is_refused():
    import pytest
    with pytest.raises(ValueError):
        prompt.build({}, voice="neutral")


def test_the_added_levels_are_their_own_sentences():
    # Each heard apart from its neighbour in the blind test of 2026-11-07.
    assert "deep and low" in prompt.build({"pitch": 0.05})
    assert "slightly low" in prompt.build({"pitch": 0.2})
    energies = [prompt._character(0.6, v) for v in (0.05, 0.2, 0.4, 0.6, 0.8, 0.95)]
    assert len(set(energies)) == 6
