"""Exports a trained voice's fused generator. Run from RVC's folder: export_generator.py voice.pth frames ret out.onnx (developer tool; the code is services/voice-lab/voicelab/generator.py, shared with Voice Lab)."""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "services", "voice-lab"))

from voicelab.generator import export

export(sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4], os.getcwd())
