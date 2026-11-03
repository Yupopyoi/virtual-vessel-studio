"""Trains an RVC voice on training speech (Voice Lab detailed design 8.3.3, 8.5).

run: {"rvc": RVC's folder, "data": folder of WAVs, "work": folder, "epochs": n (150)} -> work/voice.pth.
"""
import os
import shutil
import subprocess
import sys
import uuid

# Run with python -I, which leaves this folder off the path: the voicelab package beside it is added by hand.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from voicelab import protocol, rvc

# Only the parselmouth stand-in: the scripts beside this file would shadow RVC's own "train" package.
STUBS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "stubs")


def _run(script, root, env, reporter, stage, epochs):
    """Runs one of RVC's scripts, copying its output to the log; the training's epochs become progress."""
    process = subprocess.Popen([sys.executable, *script], cwd=root, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                               encoding="utf-8", errors="replace")
    out_of_memory = False
    for text in process.stdout:
        sys.stderr.write(text)
        out_of_memory = out_of_memory or bool(rvc.OUT_OF_MEMORY.search(text))
        epoch = rvc.epoch_of(text) if stage == "train" else None
        if epoch is not None:
            reporter.progress("train", min(epoch, epochs), epochs)
    code = process.wait()
    if out_of_memory:
        raise MemoryError(f"RVC ran out of GPU memory in {stage}.")
    if code != 0:
        raise RuntimeError(f"RVC's {script[0]} exited with {code}.")


def run(request, reporter):
    root = os.path.abspath(protocol.require(request, "rvc", str))
    data = os.path.abspath(protocol.require(request, "data", str))
    work = os.path.abspath(protocol.require(request, "work", str))
    epochs = request.get("epochs", 150)
    if not isinstance(epochs, int) or not 1 <= epochs <= 1000:
        raise protocol.BadRequest("'epochs' must be 1 to 1000.")
    for path in (os.path.join(root, "train", "train.py"), os.path.join(root, rvc.PRETRAINED_G), data):
        if not os.path.exists(path):
            raise FileNotFoundError(path)

    # A cancelled run is stopped before it can clean up; only one training runs at a time, so what is left is stale.
    for kind in ("logs", os.path.join("assets", "weights")):
        for stale in rvc.leftovers(os.listdir(os.path.join(root, kind))):
            path = os.path.join(root, kind, stale)
            shutil.rmtree(path, ignore_errors=True) if os.path.isdir(path) else os.remove(path)

    name = rvc.PREFIX + uuid.uuid4().hex[:12]
    folder = os.path.join(root, "logs", name)
    os.makedirs(folder)
    env = rvc.environment(os.environ, root, STUBS)
    weights = os.path.join(root, "assets", "weights", name + ".pth")
    try:
        stages = rvc.steps(data, folder, name, epochs, max(1, min(8, os.cpu_count() or 1)))
        for done, (stage, script) in enumerate(stages):
            if stage == "train":
                listings = {d: os.listdir(os.path.join(folder, d)) for d in ("0_gt_wavs", "3_feature768", "2a_f0", "2b-f0nsf")}
                slices = rvc.complete_slices(listings)
                if not slices:
                    raise RuntimeError("Preprocessing left no complete slices to train on.")
                with open(os.path.join(folder, "filelist.txt"), "w", encoding="utf-8") as file:
                    file.write("\n".join(rvc.filelist(folder, root, slices)))
                shutil.copyfile(os.path.join(root, rvc.CONFIG), os.path.join(folder, "config.json"))
                reporter.progress("train", 0, epochs)
            else:
                reporter.progress(stage, 0, 1)
            _run(script, root, env, reporter, stage, epochs)
        os.makedirs(work, exist_ok=True)
        target = os.path.join(work, "voice.pth")
        with protocol.atomic_path(target) as temporary:
            shutil.copyfile(weights, temporary)
        reporter.result(weights=target, epochs=epochs, slices=len(slices))
    finally:
        # RVC's working files and its copy of the weights are not ours to keep in RVC's folder.
        shutil.rmtree(folder, ignore_errors=True)
        if os.path.exists(weights):
            os.remove(weights)


if __name__ == "__main__":
    sys.exit(protocol.main({"run": run}))
