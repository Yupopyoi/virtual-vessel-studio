"""Makes speech with VoxCPM2 (Voice Lab detailed design 8.3.1, 8.3.3, 8.5).

candidates: {"shelf": [word id], "words": str, "count": n, "firstSeed": n, "model": folder, "output": folder,
             "reference": wav (optional)}
            -> a voice per seed reading the same line, with its pitch, to compare by ear and by number. The
               shelf's words (voicelab/shelf.json) and the user's free words make the instruction. With a
               reference, the voices are made near it: new candidates around one the user picked (8.3.1).
samples:    {"languages": ["ja", ...], "model": folder, "output": folder}
            -> STEP 1's samples, made on the user's PC when Voice Lab is set up: each example of the shelf with
               its seed, the base (no words picked), and each word added to the base alone, all with one seed so
               only the word differs. <output>/<lang>/example_<id>.wav, base.wav, word_<id>.wav. Files already
               made are kept, so a stopped run continues.
speech:     {"reference": wav, "firstSeed": n, "model": folder, "output": folder}
            -> training speech: the reference's voice reading this project's texts. Files already made are kept,
               so a stopped run continues.
"""
import os
import sys

# Run with python -I, which leaves this folder off the path: the voicelab package beside it is added by hand.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import numpy as np
import soundfile as sf

from voicelab import corpus, pitch, prompt, protocol

CANDIDATE_LINE = "今日はとてもいい天気ですね。少し散歩に出かけてみようと思います。みなさんは、休みの日は何をして過ごしていますか？"
# The line the samples read in each language the screen can show.
SAMPLE_LINES = {
    "ja": CANDIDATE_LINE,
    "en": "It's such a nice day today. I think I'll go out for a little walk. What do you all like to do on your days off?",
}
CFG = 2.0
STEPS = 10


def _load(model):
    if not os.path.isdir(model):
        raise FileNotFoundError(f"The VoxCPM2 model folder {model} does not exist.")
    from voxcpm import VoxCPM
    return VoxCPM.from_pretrained(hf_model_id=model, load_denoiser=False, local_files_only=True)


def _seeded(seed):
    import torch
    torch.manual_seed(seed)
    np.random.seed(seed)


def _versions():
    """What a seed's voice depends on: the same seed gives another voice under other versions."""
    import torch
    from importlib.metadata import version
    return {"torch": torch.__version__, "voxcpm": version("voxcpm"), "numpy": np.__version__}


def _save(path, wav, rate):
    with protocol.atomic_path(path) as temporary:
        sf.write(temporary, np.asarray(wav, dtype=np.float32).reshape(-1), rate, format="WAV")


def candidates(request, reporter):
    count = protocol.require(request, "count", int)
    first = protocol.require(request, "firstSeed", int)
    output = protocol.require(request, "output", str)
    if not 1 <= count <= 24:
        raise protocol.BadRequest("'count' must be 1 to 24.")
    instruction = instruction_of(request)
    reference = reference_of(request)
    reporter.progress("load", 0, 1)
    model = _load(protocol.require(request, "model", str))
    rate = model.tts_model.sample_rate
    made = []
    for i in range(count):
        reporter.progress("candidates", i, count)
        seed = first + i
        _seeded(seed)
        wav = model.generate(text=f"({instruction}){CANDIDATE_LINE}", cfg_value=CFG, inference_timesteps=STEPS, normalize=False,
                             **({"reference_wav_path": reference} if reference else {}))
        path = os.path.join(output, f"candidate_{seed}.wav")
        _save(path, wav, rate)
        made.append({"seed": seed, "file": path, **pitch.describe(np.asarray(wav, dtype=np.float32).reshape(-1), rate)})
    reporter.progress("candidates", count, count)
    reporter.result(instruction=instruction, reference=reference, candidates=made, versions=_versions())


def instruction_of(request):
    """The instruction for the request's shelf words and free words, checked before the model loads. Pure but
    for reading shelf.json."""
    picked = request.get("shelf", [])
    words = request.get("words", "")
    if not isinstance(picked, list) or not all(isinstance(word, str) for word in picked):
        raise protocol.BadRequest("'shelf' must be a list of word ids.")
    if not isinstance(words, str):
        raise protocol.BadRequest("'words' must be text.")
    try:
        return prompt.build(prompt.load(), picked, words)
    except prompt.ShelfError as error:
        raise protocol.BadRequest(str(error)) from error


def reference_of(request):
    """The optional reference to make candidates near, checked before the model loads; None without one."""
    reference = request.get("reference")
    if reference is None or reference == "":
        return None
    if not isinstance(reference, str):
        raise protocol.BadRequest("'reference' must be a path.")
    if not os.path.isfile(reference):
        raise FileNotFoundError(f"The reference {reference} does not exist.")
    return reference


def sample_plan(shelf, languages):
    """Every sample to make: (language, file name, picked words, seed), in order. Pure."""
    defaults = {slot.get("default") for slot in shelf["slots"]}
    plan = []
    for lang in languages:
        if lang not in SAMPLE_LINES:
            raise protocol.BadRequest(f"No sample line for language '{lang}'.")
        for example in shelf["examples"]:
            plan.append((lang, f"example_{example['id']}.wav", example["words"], example["seeds"][lang]))
        plan.append((lang, "base.wav", [], shelf["sampleSeed"]))
        # A default word adds nothing to the base, so its sample is the base.
        for word in shelf["words"]:
            if word["id"] not in defaults:
                plan.append((lang, f"word_{word['id']}.wav", [word["id"]], shelf["sampleSeed"]))
    return plan


def samples(request, reporter):
    languages = protocol.require(request, "languages", list)
    output = protocol.require(request, "output", str)
    shelf = prompt.load()
    plan = sample_plan(shelf, languages)
    reporter.progress("load", 0, 1)
    model = _load(protocol.require(request, "model", str))
    rate = model.tts_model.sample_rate
    for done, (lang, name, picked, seed) in enumerate(plan):
        reporter.progress("samples", done, len(plan))
        path = os.path.join(output, lang, name)
        if not os.path.exists(path):
            _seeded(seed)
            wav = model.generate(text=f"({prompt.build(shelf, picked)}){SAMPLE_LINES[lang]}", cfg_value=CFG, inference_timesteps=STEPS, normalize=False)
            _save(path, wav, rate)
    reporter.progress("samples", len(plan), len(plan))
    reporter.result(output=output, count=len(plan), versions=_versions())


def speech(request, reporter):
    reference = protocol.require(request, "reference", str)
    output = protocol.require(request, "output", str)
    first = request.get("firstSeed", 5000)
    if not os.path.isfile(reference):
        raise FileNotFoundError(f"The reference {reference} does not exist.")
    reporter.progress("load", 0, 1)
    model = _load(protocol.require(request, "model", str))
    rate = model.tts_model.sample_rate
    lines = corpus.lines()
    files, seconds = [], 0.0
    for done, (index, text) in enumerate(lines):
        reporter.progress("speech", done, len(lines))
        path = os.path.join(output, f"{index:03d}.wav")
        if not os.path.exists(path):
            _seeded(first + index)
            # No instruction: the reference's voice is copied as it is, and the timbre is edited afterwards.
            wav = model.generate(text=text, reference_wav_path=reference, cfg_value=CFG, inference_timesteps=STEPS, normalize=False,
                                 retry_badcase=True, retry_badcase_max_times=3, retry_badcase_ratio_threshold=6.0)
            _save(path, wav, rate)
        files.append(os.path.basename(path))
        seconds += sf.info(path).duration
    reporter.progress("speech", len(lines), len(lines))
    reporter.result(output=output, files=files, seconds=round(seconds, 1), versions=_versions())


if __name__ == "__main__":
    sys.exit(protocol.main({"candidates": candidates, "samples": samples, "speech": speech}))
