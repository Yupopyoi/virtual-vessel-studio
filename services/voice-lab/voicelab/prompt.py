"""The instruction VoxCPM2 gets for a voice's mood (Voice Lab detailed design 8.3.1).

The sentences and thresholds are VCBM's VoicePromptBuilder, kept as they were: they were tuned there by listening.
Added later, each heard apart from its neighbour in a blind A/B test (decision record, 2026-11-07): a low pitch,
the sleepy, upbeat, and excited energy, and the male voice. A slider at exactly 0 leaves its sentence out.
"""

import re

DEFAULTS = {
    "youthful": 0.55, "pitch": 0.55, "brightness": 0.65, "softness": 0.70, "breathiness": 0.25,
    "energy": 0.40, "cuteness": 0.60, "expressiveness": 0.55, "speed": 0.50, "naturalness": 0.90,
}

VOICES = ("female", "male")

# The sentences are written for a female voice; a male one swaps the person and the pronouns. A neutral
# subject was tried and gave a female or a male voice by seed, not a neutral one.
_MALE = ((r"\bwoman\b", "man"), (r"\bgirl\b", "boy"), (r"\bHer\b", "His"), (r"\bher\b", "his"), (r"\bShe\b", "He"))


def _speaker(v):
    if v == 0:
        return ""
    if v >= 0.90:
        return "A youthful Japanese girl speaking casually. Her voice is squeaky cartoon-character voice"
    if v >= 0.80:
        return "A youthful Japanese girl speaking casually in a relaxed everyday conversation."
    if v >= 0.40:
        return "A Japanese young adult woman speaking in a relaxed everyday conversation."
    return "A mature Japanese woman speaking calmly in a relaxed everyday conversation."


def _pitch(v):
    if v == 0:
        return ""
    if v >= 0.92:
        return "Her voice is clearly in the high register, with a light, youthful tone that gives the impression of being childlike."
    if v >= 0.82:
        return "Her voice has a distinctly high, light and youthful register that remains comfortable and effortless."
    if v >= 0.64:
        return "Her voice has a naturally elevated, light register without sounding strained."
    if v <= 0.10:
        return "Her voice is deep and low, with a calm, steady register."
    if v <= 0.28:
        return "Her voice has a calm, slightly low and stable register."
    return "Her voice stays in a comfortable, naturally light register."


def _tone(brightness, softness):
    if brightness == 0 and softness == 0:
        return ""
    if brightness >= 0.68:
        tone = "Her vocal tone is light, clear, fresh and bright"
    elif brightness <= 0.32:
        tone = "Her vocal tone is clear, warm, calm and mellow"
    else:
        tone = "Her vocal tone is light, clear and warm"
    if softness >= 0.68:
        return tone + ", with a gentle smile and soft, rounded vocal resonance."
    if softness <= 0.32:
        return tone + ", with focused resonance and crisp articulation."
    return tone + ", with a gentle smile and balanced vocal resonance."


def _breathiness(v):
    if v == 0:
        return ""
    if v >= 0.72:
        return "A slight airy quality adds softness while the speech remains clear and intelligible."
    if v <= 0.30:
        return "The voice remains clean and focused without excessive breathiness."
    return "A very subtle airy texture keeps the voice soft without reducing clarity."


def _character(cuteness, energy):
    if cuteness == 0 and energy == 0:
        return ""
    if cuteness >= 0.78:
        character = "She sounds naturally charming, sweet and slightly playful"
    elif cuteness >= 0.45:
        character = "She sounds naturally charming and friendly"
    else:
        character = "She sounds composed and understated"
    if energy >= 0.90:
        return character + ", with very energetic, bubbly and excited energy."
    if energy >= 0.72:
        return character + ", with cheerful and pleasantly lively energy."
    if energy >= 0.55:
        return character + ", with warm and upbeat energy."
    if energy <= 0.10:
        return character + ", with a quiet, subdued and sleepy mood."
    if energy <= 0.30:
        return character + ", with a calm, gentle and relaxed mood."
    return character + ", with relaxed but attentive energy."


def _intonation(v):
    if v == 0:
        return ""
    if v >= 0.72:
        return "She uses lively but natural variations in rhythm and intonation."
    if v <= 0.30:
        return "She uses restrained, calm and stable intonation."
    return "She uses subtle, varied intonation and small spontaneous changes in rhythm."


def _speed(v):
    if v == 0:
        return ""
    if v >= 0.72:
        return "She speaks at a slightly quick but comfortable conversational pace."
    if v <= 0.30:
        return "She speaks slowly and gently with natural pauses."
    return "She speaks at a natural conversational pace."


def _naturalness(v):
    if v == 0:
        return ""
    if v >= 0.82:
        return "The performance is realistic, human and conversational, without forced pitch, exaggerated acting, squeaking or cartoon-like delivery."
    if v >= 0.52:
        return "The performance is natural and conversational without excessive acting."
    return "The performance may be slightly stylized while remaining clear and coherent."


def _sentence(text):
    text = (text or "").strip()
    if not text:
        return ""
    return text if text[-1] in ".!?。！？" else text + "."


def _voiced(text, voice):
    if voice == "male":
        for female, male in _MALE:
            text = re.sub(female, male, text)
    return text


def build(sliders, words="", voice="female"):
    """The instruction for slider values from 0 to 1 (missing ones take DEFAULTS), optional words, and a voice
    from VOICES. Pure."""
    if voice not in VOICES:
        raise ValueError(f"voice must be one of {VOICES}.")
    s = {key: min(1.0, max(0.0, float(sliders.get(key, value)))) for key, value in DEFAULTS.items()}
    parts = [
        _speaker(s["youthful"]), _pitch(s["pitch"]), _tone(s["brightness"], s["softness"]), _breathiness(s["breathiness"]),
        _character(s["cuteness"], s["energy"]), _intonation(s["expressiveness"]), _speed(s["speed"]),
        _naturalness(s["naturalness"]),
    ]
    # The user's words are theirs, so they are not changed for the voice.
    return " ".join(part for part in [_voiced(" ".join(part for part in parts if part), voice), _sentence(words)] if part)
