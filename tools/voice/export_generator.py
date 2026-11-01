"""Exports the RVC v2 generator for streaming: a fixed window, and only its tail reaches the decoder,
the way upstream's realtime infer() does it, so the costly vocoder runs on one block, not the window."""
import os, sys
from pathlib import Path
import torch
# The RVC checkout is outside the repository; run from its folder.
ROOT = Path.cwd(); sys.path.insert(0, str(ROOT))
from infer.module.models import SynthesizerTrnMs768NSFsid

class Tail(torch.nn.Module):
    def __init__(self, g, skip, ret):
        super().__init__(); self.g, self.skip, self.ret = g, skip, ret
    def forward(self, phone, pitch, pitchf, rnd, spk):
        # The speaker comes in as an input: fixed, it would be folded into the weights it conditions
        # when Unity imports the model, and those could not be swapped for another voice's.
        g = self.g; n = phone.shape[1]
        lengths = torch.tensor([n], dtype=torch.int64)
        e = spk
        head = self.skip; flow_head = max(head - 24, 0); dec_head = head - flow_head
        m_p, logs_p, mask = g.enc_p(phone, pitch, lengths, flow_head)
        z_p = (m_p + torch.exp(logs_p) * rnd * 0.66666) * mask
        z = g.flow(z_p, mask, g=e, reverse=True)
        z = z[:, :, dec_head:dec_head + self.ret]
        return g.dec(z, pitchf[:, head:head + self.ret], g=e)

pth, frames, ret, out = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4]
ck = torch.load(pth, map_location="cpu", weights_only=False)
cfg = ck["config"]; cfg[-3] = ck["weight"]["emb_g.weight"].shape[0]
g = SynthesizerTrnMs768NSFsid(*cfg, is_half=False)
del g.enc_q
print(g.load_state_dict(ck["weight"], strict=False))
g.eval().float(); g.remove_weight_norm() if hasattr(g, "remove_weight_norm") else None
w = Tail(g, frames - ret, ret).eval()
d = (torch.randn(1, frames, 768), torch.randint(1, 255, (1, frames)), torch.full((1, frames), 220.0), torch.randn(1, 192, frames - max(frames - ret - 24, 0)), g.emb_g(torch.zeros(1, dtype=torch.int64)).unsqueeze(-1).detach())
with torch.no_grad():
    print("audio", w(*d).shape)
torch.onnx.export(w, d, os.path.abspath(out), input_names=["phone", "pitch", "pitchf", "rnd", "spk"], output_names=["audio"], opset_version=17, do_constant_folding=True, dynamo=False)
d[-1].numpy().tofile(out + ".spk")
print("OK", out, ck.get("sr"), ck.get("version"), ck.get("f0"))
