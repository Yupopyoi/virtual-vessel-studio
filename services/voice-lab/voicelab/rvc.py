"""RVC v2 40k training as RVC's WebUI runs it, from outside RVC (system design 13.3: RVC is not changed).

RVC's scripts run from its folder and keep their working files in logs/<name> there; train.py removes that
folder once the weights are copied out.
"""
import os
import random
import re

SAMPLE_RATE = "40k"
VERSION = "v2"
BATCH = 8
# RVC's own settings: a v2 model at 40 kHz trains with configs/v1/40k.json.
CONFIG = os.path.join("configs", "v1", "40k.json")
PRETRAINED_G = "assets/pretrained_v2/f0G40k.pth"
PRETRAINED_D = "assets/pretrained_v2/f0D40k.pth"
# "====> Epoch: 3 [time] | (elapsed)", with "Epoch" in the log's language.
EPOCH_LINE = re.compile(r"====>\s*[^:：\d]*[:：]\s*(\d+)")
OUT_OF_MEMORY = re.compile(r"CUDA out of memory|OutOfMemoryError", re.IGNORECASE)


def steps(data, experiment_folder, name, epochs, cpus):
    """The scripts to run, in order, as (stage, script and arguments). Pure."""
    return [
        ("preprocess", ["train/preprocess.py", data, "40000", str(cpus), experiment_folder, "True", "3.7"]),
        ("pitch", ["train/dataset/extract_f0.py", "cuda", "1", "0", "0", experiment_folder, "True"]),
        ("features", ["train/dataset/extract_hubert_feature.py", "cuda:0", "1", "0", "0", experiment_folder, VERSION, "True"]),
        ("train", ["train/train.py", "-e", name, "-sr", SAMPLE_RATE, "-f0", "1", "-bs", str(BATCH), "-g", "0",
                   "-te", str(epochs), "-se", str(epochs), "-pg", PRETRAINED_G, "-pd", PRETRAINED_D,
                   "-l", "1", "-c", "0", "-sw", "0", "-v", VERSION]),
    ]


def filelist(experiment_folder, rvc_root, slices, shuffle_seed=0):
    """RVC's training list: every slice with all four features, plus two silent slices, shuffled. Pure."""
    e = experiment_folder.replace("\\", "/")
    r = rvc_root.replace("\\", "/")
    lines = [f"{e}/0_gt_wavs/{n}.wav|{e}/3_feature768/{n}.npy|{e}/2a_f0/{n}.wav.npy|{e}/2b-f0nsf/{n}.wav.npy|0" for n in sorted(slices)]
    mute = f"{r}/logs/mute/0_gt_wavs/mute40k.wav|{r}/logs/mute/3_feature768/mute.npy|{r}/logs/mute/2a_f0/mute.wav.npy|{r}/logs/mute/2b-f0nsf/mute.wav.npy|0"
    lines += [mute, mute]
    random.Random(shuffle_seed).shuffle(lines)
    return lines


def complete_slices(listings):
    """The slice names present in every feature folder, from {folder: file names}. Pure."""
    sets = [{name.split(".")[0] for name in names} for names in listings.values()]
    return set.intersection(*sets) if sets else set()


def epoch_of(line):
    """The finished epoch a log line reports, or None. Pure."""
    match = EPOCH_LINE.search(line)
    return int(match.group(1)) if match else None


PREFIX = "vvs-lab-"


def leftovers(names):
    """Earlier runs' folders or weights in RVC's folder: only names this project gives. Pure."""
    return sorted(name for name in names if name.startswith(PREFIX))


def environment(base, rvc_root, stub_folder):
    """The variables RVC's scripts run with: its folder and the parselmouth stand-in on the path. Pure.

    PYTHONSAFEPATH keeps a script's own folder off the path: train/preprocess.py would otherwise import
    train/train.py as the package "train"."""
    env = dict(base)
    env.update(PYTHONPATH=os.pathsep.join([rvc_root, stub_folder]), PYTHONSAFEPATH="1", PYTHONUTF8="1", PYTHONIOENCODING="utf-8", PYTHONNOUSERSITE="1")
    env.pop("PYTHONHOME", None)
    return env
