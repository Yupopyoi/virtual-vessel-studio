"""Makes a voice's .sentis without the Unity Editor: build | apply (developer tool; the code is services/voice-lab/voicelab/weights.py, shared with Voice Lab)."""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "services", "voice-lab"))

from voicelab.weights import apply, build

if __name__ == "__main__":
    {"build": build, "apply": apply}[sys.argv[1]](*sys.argv[2:])
