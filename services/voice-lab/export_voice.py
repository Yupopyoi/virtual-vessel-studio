"""Turns trained weights into a voice the runtime loads, without the Unity Editor (Voice Lab detailed design 8.4, 8.5).

run: {"weights": voice.pth, "rvc": RVC's folder, "template": .sentis, "manifest": .json, "frames": 100, "returned": 21,
      "speech": folder of the training speech, "output": folder}
     -> output/voice.sentis, voice.spk, voice.json (with the voice's median pitch, for matching the user's pitch).
"""
import json
import os
import sys
import tempfile

# Run with python -I, which leaves this folder off the path: the voicelab package beside it is added by hand.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import numpy as np
import soundfile as sf

from voicelab import pitch, protocol

SPEECH_SAMPLES = 8


def median_pitch(folder):
    """The median pitch over a spread of the training speech, in whole Hz."""
    names = sorted(name for name in os.listdir(folder) if name.lower().endswith(".wav"))
    if not names:
        raise FileNotFoundError(f"No WAV files in {folder}.")
    chosen = names[:: max(1, len(names) // SPEECH_SAMPLES)][:SPEECH_SAMPLES]
    tracks = []
    for name in chosen:
        samples, rate = sf.read(os.path.join(folder, name), always_2d=True)
        tracks.append(pitch.track(samples.mean(axis=1), rate))
    stats = pitch.summary(np.concatenate(tracks))
    if stats is None:
        raise RuntimeError("The training speech has no voiced frames.")
    return round(stats[0])


def run(request, reporter):
    from voicelab.generator import export
    from voicelab.weights import apply

    weights = protocol.require(request, "weights", str)
    rvc_root = os.path.abspath(protocol.require(request, "rvc", str))
    template = protocol.require(request, "template", str)
    manifest = protocol.require(request, "manifest", str)
    speech = protocol.require(request, "speech", str)
    output = protocol.require(request, "output", str)
    frames = request.get("frames", 100)
    returned = request.get("returned", 21)
    for path in (weights, template, manifest, speech):
        if not os.path.exists(path):
            raise FileNotFoundError(path)

    os.makedirs(output, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output) as scratch:
        reporter.progress("export", 0, 3)
        onnx_path = os.path.join(scratch, "voice.onnx")
        export(weights, frames, returned, onnx_path, rvc_root)
        reporter.progress("export", 1, 3)
        sentis = os.path.join(output, "voice.sentis")
        with protocol.atomic_path(sentis) as temporary:
            apply(template, onnx_path, manifest, temporary)
        with protocol.atomic_path(os.path.join(output, "voice.spk")) as temporary:
            with open(onnx_path + ".spk", "rb") as source, open(temporary, "wb") as target:
                target.write(source.read())
    reporter.progress("export", 2, 3)
    info = {"schemaVersion": 1, "frames": frames, "returned": returned, "medianPitchHz": median_pitch(speech)}
    with protocol.atomic_path(os.path.join(output, "voice.json")) as temporary:
        with open(temporary, "w", encoding="utf-8") as file:
            json.dump(info, file, indent=2)
    reporter.progress("export", 3, 3)
    reporter.result(output=output, **info)


if __name__ == "__main__":
    sys.exit(protocol.main({"run": run}))
