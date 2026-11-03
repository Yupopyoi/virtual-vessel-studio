"""Moves a voice's timbre (Voice Lab detailed design 8.3.2, 8.5).

preview: {"source": wav, "timbre": {...}, "output": wav} -> the edited audio and its pitch.
batch:   {"source": folder, "timbre": {...}, "output": folder} -> every WAV in the folder, edited the same way.
"""
import os
import sys

# Run with python -I, which leaves this folder off the path: the voicelab package beside it is added by hand.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import soundfile as sf

from voicelab import pitch, protocol
from voicelab.timbre import TimbreSettings, apply


def _edit_file(source, target, settings):
    samples, rate = sf.read(source, always_2d=True)
    edited = apply(samples.mean(axis=1), rate, settings)
    with protocol.atomic_path(target) as temporary:
        sf.write(temporary, edited, rate, format="WAV")
    return edited, rate


def preview(request, reporter):
    source = protocol.require(request, "source", str)
    output = protocol.require(request, "output", str)
    settings = TimbreSettings.from_request(protocol.require(request, "timbre", dict))
    edited, rate = _edit_file(source, output, settings)
    reporter.result(output=output, **pitch.describe(edited, rate))


def batch(request, reporter):
    source = protocol.require(request, "source", str)
    output = protocol.require(request, "output", str)
    settings = TimbreSettings.from_request(protocol.require(request, "timbre", dict))
    names = sorted(name for name in os.listdir(source) if name.lower().endswith(".wav"))
    if not names:
        raise FileNotFoundError(f"No WAV files in {source}.")
    for done, name in enumerate(names):
        reporter.progress("timbre", done, len(names))
        _edit_file(os.path.join(source, name), os.path.join(output, name), settings)
    reporter.progress("timbre", len(names), len(names))
    reporter.result(output=output, files=names)


if __name__ == "__main__":
    sys.exit(protocol.main({"preview": preview, "batch": batch}))
