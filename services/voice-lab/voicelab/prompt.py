"""The instruction VoxCPM2 gets for a voice, built from the words picked on STEP 1's shelf (Voice Lab detailed
design 8.3.1).

The shelf is voicelab/shelf.json, read by Unity too. Each word fills one slot of the instruction; a slot with
no word takes its default word, or its default sentence. The start (start_picks, what the screen starts with)
gives the instruction VCBM's VoicePromptBuilder was tuned with, but for the person, which says the age the
screen shows (the user's decision, 2026-11-07). The character slot has a start word but no default, so the user
can take the character out (the user's decision, 2026-11-14). The other sentences
are VCBM's, or were heard apart from their neighbours in a blind A/B test (decision record, 2026-11-07). They
are written for a female voice; the male word swaps the person and the pronouns.
"""

import json
import os
import re
import unicodedata

SHELF_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "shelf.json")
MAX_WORDS = 200

# A prompt the user writes whole, in place of the shelf (the user's decision, 2026-11-11).
MAX_PROMPT = 500

_FEMALE = {"{woman}": "woman", "{girl}": "girl", "{Her}": "Her", "{her}": "her", "{She}": "She"}
_MALE = {"{woman}": "man", "{girl}": "boy", "{Her}": "His", "{her}": "his", "{She}": "He"}

# The instruction is sent in brackets before the line read aloud, so a bracket in the user's words would end
# it early and turn the rest into speech. Line breaks, controls, and invisible or direction-changing characters
# are removed too.
_BRACKETS = set("()（）[]［］{}｛｝<>＜＞")


class ShelfError(ValueError):
    """A pick the shelf does not allow: an unknown word, or two words for one slot."""


def load(path=SHELF_PATH):
    with open(path, encoding="utf-8") as file:
        return json.load(file)


def clean_words(text, limit=MAX_WORDS):
    """The user's free words as they are sent: no brackets, controls, or invisible characters, spaces folded,
    at most `limit` characters. Pure."""
    kept = []
    for ch in text or "":
        if ch in _BRACKETS:
            continue
        category = unicodedata.category(ch)
        if category == "Cf":
            continue  # zero-width and direction-changing characters
        kept.append(" " if category in ("Cc", "Zl", "Zp") else ch)
    return re.sub(r"\s+", " ", "".join(kept)).strip()[:limit].strip()


def written(text):
    """A prompt the user wrote whole, as it is sent: cleaned as free words are, so it cannot end the instruction
    early, at most MAX_PROMPT characters. Empty when nothing is left. Pure."""
    return clean_words(text, MAX_PROMPT)


def _sentence(text):
    if not text:
        return ""
    return text if text[-1] in ".!?。！？" else text + "."


def start_picks(shelf):
    """The words the screen starts with and the samples' base is made with: each slot's "start" word. A slot's
    default needs no pick, as it is used anyway. Pure."""
    return [slot["start"] for slot in shelf["slots"] if slot.get("start")]


def in_base(shelf):
    """The words already in the base: each slot's default and start word, whose own sample is the base. Pure."""
    return {slot.get("default") or slot.get("start") for slot in shelf["slots"]} - {None}


def resolve(shelf, picked):
    """The word for every slot: the picked one, else the slot's default. Pure."""
    words = {word["id"]: word for word in shelf["words"]}
    chosen = {}
    for word_id in picked:
        if word_id not in words:
            raise ShelfError(f"Unknown word '{word_id}'.")
        slot = words[word_id]["slot"]
        if slot in chosen and chosen[slot] != word_id:
            raise ShelfError(f"'{chosen[slot]}' and '{word_id}' are both for {slot}.")
        chosen[slot] = word_id
    return {slot["id"]: chosen.get(slot["id"], slot.get("default")) for slot in shelf["slots"]}


def build(shelf, picked, words=""):
    """The instruction for the picked word ids and the user's free words. Pure."""
    by_id = {word["id"]: word for word in shelf["words"]}
    resolved = resolve(shelf, picked)
    pronouns = _MALE if resolved.get("voice") == "male" else _FEMALE
    text = ""
    for slot in shelf["slots"]:
        word = resolved[slot["id"]]
        sentence = by_id[word].get("sentence", "") if word else slot.get("defaultSentence", "")
        if not sentence:
            continue
        # The energy continues the character's sentence.
        text += sentence if sentence.startswith(",") or not text else " " + sentence
    for key, value in pronouns.items():
        text = text.replace(key, value)
    extra = _sentence(clean_words(words))
    return text + (" " + extra if extra else "")
