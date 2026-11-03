"""Makes a voice's .sentis without the Unity Editor.

build:  from the template and one voice converted by the Editor, works out where each weight sits.
apply:  writes another voice's ONNX weights into the template at those places.
The Editor is needed only once, by developers, to make the template.
"""
import json, sys
import numpy as np, onnx
from onnx import numpy_helper


def weights(path):
    return {i.name: numpy_helper.to_array(i) for i in onnx.load(path).graph.initializer}


def every(hay, needle):
    at, found = hay.find(needle), []
    while at >= 0:
        found.append(at)
        at = hay.find(needle, at + 1)
    return found


def build(template_onnx, voice_onnx, template_sentis, voice_sentis, out):
    t, v = weights(template_onnx), weights(voice_onnx)
    tmpl = open(template_sentis, "rb").read()
    real = open(voice_sentis, "rb").read()
    assert len(tmpl) == len(real)
    entries, unused = [], []
    for k in t:
        a, b = t[k].tobytes(), v[k].tobytes()
        if a == b:
            continue
        # A place counts only if the Editor's file really holds this voice's bytes there: a tensor of
        # a few bytes also matches by chance elsewhere.
        places = [at for at in every(tmpl, a) if real[at:at + len(b)] == b]
        if places:
            entries.extend({"name": k, "at": at, "kind": "whole"} for at in places)
            continue
        # A relative embedding: transposed, and padded to the window, one column of [channels, time] apart.
        if t[k].ndim == 3:
            m = t[k][0]
            found = []
            for c in range(m.shape[1]):
                found.append([at for at in every(tmpl, m[:, c].tobytes()) if real[at:at + m.shape[0] * 4] == v[k][0][:, c].tobytes()])
            if all(len(f) == 1 for f in found):
                entries.append({"name": k, "at": found[0][0], "kind": "columns", "stride": found[1][0] - found[0][0]})
                continue
        unused.append(k)
    json.dump({"size": len(tmpl), "entries": entries}, open(out, "w"), indent=0)
    print("placed", len(entries), "of", len([k for k in t if t[k].tobytes() != v[k].tobytes()]), "not in the file:", unused)


def apply(template_sentis, voice_onnx, manifest, out):
    v = weights(voice_onnx)
    m = json.load(open(manifest))
    data = bytearray(open(template_sentis, "rb").read())
    assert len(data) == m["size"]
    for e in m["entries"]:
        w = v[e["name"]]
        if e["kind"] == "whole":
            b = w.tobytes()
            data[e["at"]:e["at"] + len(b)] = b
        else:
            n = w[0]
            for c in range(n.shape[1]):
                at = e["at"] + c * e["stride"]
                data[at:at + n.shape[0] * 4] = n[:, c].tobytes()
    open(out, "wb").write(data)


if __name__ == "__main__":
    {"build": build, "apply": apply}[sys.argv[1]](*sys.argv[2:])
