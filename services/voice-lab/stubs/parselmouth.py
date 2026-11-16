"""Stands in for praat-parselmouth (GPLv3), which is left out of the training environment.

RVC imports it at the top of its files, but only its "pm" pitch method calls it, and this project uses
RMVPE. Any use stops here, so a path that really needs it fails instead of giving wrong pitch.
"""


def __getattr__(name):
    raise RuntimeError(f"parselmouth.{name} was used; this environment leaves it out.")
