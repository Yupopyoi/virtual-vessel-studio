"""Exports the generator with the steps between the models inside it, so a block needs one trip to the GPU:
HuBERT's raw features are doubled to 100 Hz, and RMVPE's salience becomes pitch, in the graph.

Inputs: feats [1, H, 768] (HuBERT's output), salience [1, 32, 360] (RMVPE's), history [1, W - 28] (Hz, the pitch
of earlier blocks, untransposed), last [1] (the last voiced Hz, for a start that is unvoiced), factor [1] (the
transposition as a ratio), rnd, spk. Output: audio, and f0new [1, 28] (the pitch it used, for the next history).
"""
import os, sys
from pathlib import Path
import torch
ROOT = Path.cwd()
sys.path.insert(0, str(ROOT))
from infer.module.models import SynthesizerTrnMs768NSFsid

HEAD, TAIL, BINS, THRESHOLD = 3, 1, 360, 0.03


class Fused(torch.nn.Module):
    def __init__(self, g, frames, ret, pitch_frames=32):
        super().__init__()
        self.g, self.frames, self.ret, self.pitch_frames = g, frames, ret, pitch_frames
        self.register_buffer("cents", 20.0 * torch.arange(BINS, dtype=torch.float32) + 1997.3794084376191)

    def f0(self, salience, last):
        # As RvcDsp.DecodeF0: the weighted cents of the 9 bins around the peak; quiet frames are unvoiced.
        s = salience[0]
        peak = torch.argmax(s, dim=1)
        padded = torch.nn.functional.pad(s, (4, 4))
        cents = torch.nn.functional.pad(self.cents, (4, 4))
        offsets = torch.arange(9)
        index = peak[:, None] + offsets[None, :]
        w = torch.gather(padded, 1, index)
        c = cents[index]
        value = (w * c).sum(1) / torch.maximum(w.sum(1), torch.tensor(1e-9))
        hz = 10.0 * torch.pow(2.0, value / 1200.0)
        voiced = s.max(dim=1).values > THRESHOLD
        hz = torch.where(voiced, hz, torch.zeros_like(hz))
        hz = hz[HEAD:self.pitch_frames - TAIL]
        # Unvoiced frames keep the last voiced pitch, so gaps do not silence the vocoder (RvcDsp.HoldUnvoiced).
        held = []
        previous = last[0]
        for i in range(hz.shape[0]):
            previous = torch.where(hz[i] > 0, hz[i], previous)
            held.append(previous)
        return torch.stack(held)[None]

    def forward(self, feats, salience, history, last, factor, rnd, spk):
        g = self.g
        n = self.frames
        h = feats.shape[1]
        # A constant: Inference Engine's Clip needs both bounds, and the sizes are fixed anyway.
        index = torch.tensor([min(t // 2, h - 1) for t in range(n)], dtype=torch.int64)
        phone = feats[:, index]
        f0new = self.f0(salience, last)
        f0 = torch.cat((history, f0new), 1) * factor
        mel = 1127.0 * torch.log(1.0 + f0 / 700.0)
        low = 1127.0 * torch.log(torch.tensor(1.0 + 50.0 / 700.0))
        high = 1127.0 * torch.log(torch.tensor(1.0 + 1100.0 / 700.0))
        coarse = torch.where(f0 > 0, (mel - low) * 254.0 / (high - low) + 1.0, torch.ones_like(f0))
        pitch = torch.clamp(torch.round(coarse), 1, 255).long()
        lengths = torch.tensor([n], dtype=torch.int64)
        e = spk
        head = n - self.ret
        flow_head = max(head - 24, 0)
        dec_head = head - flow_head
        m_p, logs_p, mask = g.enc_p(phone, pitch, lengths, flow_head)
        z_p = (m_p + torch.exp(logs_p) * rnd * 0.66666) * mask
        z = g.flow(z_p, mask, g=e, reverse=True)
        z = z[:, :, dec_head:dec_head + self.ret]
        audio = g.dec(z, f0[:, head:head + self.ret], g=e)
        return audio, f0new


pth, frames, ret, out = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4]
ck = torch.load(pth, map_location="cpu", weights_only=False)
cfg = ck["config"]
cfg[-3] = ck["weight"]["emb_g.weight"].shape[0]
g = SynthesizerTrnMs768NSFsid(*cfg, is_half=False)
del g.enc_q
print(g.load_state_dict(ck["weight"], strict=False))
g.eval().float()
w = Fused(g, frames, ret).eval()
hubert_frames = (frames * 160 - 400) // 320 + 1
new = 32 - HEAD - TAIL
d = (torch.randn(1, hubert_frames, 768), torch.rand(1, 32, BINS), torch.full((1, frames - new), 200.0), torch.tensor([200.0]), torch.tensor([1.0]),
     torch.randn(1, 192, frames - max(frames - ret - 24, 0)), g.emb_g(torch.zeros(1, dtype=torch.int64)).unsqueeze(-1).detach())
with torch.no_grad():
    a, f = w(*d)
    print("audio", a.shape, "f0new", f.shape, "hubert frames", hubert_frames)
torch.onnx.export(w, d, os.path.abspath(out), input_names=["feats", "salience", "history", "last", "factor", "rnd", "spk"], output_names=["audio", "f0new"], opset_version=17, do_constant_folding=True, dynamo=False)
d[6].numpy().tofile(os.path.abspath(out) + ".spk")
print("OK", out)
