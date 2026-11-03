import sys
from pathlib import Path

# The entry scripts and the voicelab package sit in services/voice-lab, beside this folder.
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
