"""Renders the built-in BGM and sound effects (BGM and SE detailed design 6.3) into StreamingAssets, with their list.

python -I tools/audio/build_builtin_audio.py

Needs numpy and scipy (tools/audio/requirements.txt). The output is fixed by the seeds in bgm.py and
sound_effects.py, so running it again gives the same files. The files are WAV, not MP3: an MP3 starts and ends with
the encoder's padding, which Media Foundation does not remove, so a loop would have a gap at its seam. They are mono,
as the pieces' only width was a few milliseconds of delay, and that halves their size.
"""
import json
import os
import sys
import uuid
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import bgm  # noqa: E402
import sound_effects as se  # noqa: E402
from synth import SR  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(HERE))
OUTPUT = os.path.join(ROOT, 'unity', 'VirtualVesselStudio', 'Assets', 'StreamingAssets', 'Audio', 'BuiltIn')
MANIFEST = 'built-in-audio.json'
SCHEMA_VERSION = 1

# BGM is levelled by loudness, so the pieces sit at the same volume; sound effects by peak, as they are short.
BGM_RMS = 10 ** (-19 / 20)
BGM_PEAK = 10 ** (-1 / 20)
EFFECT_PEAK = 10 ** (-3 / 20)

BGM = [
    ('cute-pop', bgm.cute_pop, 'きらきらポップ', 'Sparkly Pop'),
    ('mellow-lofi', bgm.lofi, 'まったりローファイ', 'Mellow Lo-fi'),
    ('recorder-waltz', bgm.recorder_waltz, 'リコーダーのワルツ', 'Recorder Waltz'),
    ('chat-time', bgm.calm_talk, 'おしゃべりタイム', 'Chat Time'),
    ('denpa-41', bgm.denpa_41edo, 'でんぱ41（41平均律）', 'Denpa 41 (41-EDO)'),
]

EFFECTS = [
    ('chime', lambda rng: se.chime(), 'ピンポーン', 'Chime'),
    ('correct', lambda rng: se.correct(), '正解', 'Correct'),
    ('wrong', lambda rng: se.wrong(), 'ブブー', 'Wrong'),
    ('pop', lambda rng: se.pop(), 'ポン', 'Pop'),
    ('sparkle', se.sparkle, 'キラキラ', 'Sparkle'),
    ('drumroll', se.drumroll, 'ドラムロール', 'Drumroll'),
    ('whoosh', se.whoosh, 'シュッ', 'Whoosh'),
    ('laser', lambda rng: se.laser(), 'ピュン', 'Laser'),
    ('boing', lambda rng: se.boing(), 'ボヨン', 'Boing'),
    ('rimshot', se.rimshot, 'ツッコミ', 'Rimshot'),
    ('coin', lambda rng: se.coin(), 'コイン', 'Coin'),
    ('fanfare', se.fanfare, 'ファンファーレ', 'Fanfare'),
    ('applause', se.applause, '拍手', 'Applause'),
]


def level_bgm(stereo):
    """Mono at the target loudness, the peaks held under BGM_PEAK by a soft limit. Pure."""
    mono = stereo.mean(axis=1)
    mono = mono * (BGM_RMS / np.sqrt(np.mean(mono ** 2)))
    return np.tanh(mono / BGM_PEAK) * BGM_PEAK


def level_effect(stereo):
    mono = stereo.mean(axis=1)
    return mono * (EFFECT_PEAK / np.max(np.abs(mono)))


def write_wav(path, mono):
    pcm = np.clip(np.round(mono * 32767), -32768, 32767).astype('<i2')
    with wave.open(path, 'wb') as file:
        file.setnchannels(1)
        file.setsampwidth(2)
        file.setframerate(SR)
        file.writeframes(pcm.tobytes())


FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

FILE_META = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def ensure_meta(path, folder):
    """Writes Unity's .meta beside a new file or folder, and keeps an existing one so its GUID stays the same."""
    meta = os.path.normpath(path) + '.meta'
    if not os.path.exists(meta):
        with open(meta, 'w', encoding='utf-8', newline='\n') as file:
            file.write((FOLDER_META if folder else FILE_META).format(guid=uuid.uuid4().hex))


def main():
    clips = []
    for folder, kind, items, level in (('bgm', 'bgm', BGM, level_bgm), ('se', 'soundEffect', EFFECTS, level_effect)):
        os.makedirs(os.path.join(OUTPUT, folder), exist_ok=True)
        for index, (clip_id, render, ja, en) in enumerate(items):
            rng = np.random.default_rng(1000 + index)
            audio = render() if kind == 'bgm' else render(rng)
            mono = level(audio)
            relative = f'{folder}/{clip_id}.wav'
            write_wav(os.path.join(OUTPUT, folder, f'{clip_id}.wav'), mono)
            ensure_meta(os.path.join(OUTPUT, folder, f'{clip_id}.wav'), False)
            clips.append({'id': f'{folder}-{clip_id}', 'kind': kind, 'file': relative, 'names': {'ja': ja, 'en': en}})
            print(f'{relative}: {len(mono) / SR:.1f} s, peak {20 * np.log10(np.max(np.abs(mono))):.1f} dBFS')
    with open(os.path.join(OUTPUT, MANIFEST), 'w', encoding='utf-8', newline='\n') as file:
        json.dump({'schemaVersion': SCHEMA_VERSION, 'clips': clips}, file, ensure_ascii=False, indent=2)
        file.write('\n')
    ensure_meta(os.path.join(OUTPUT, MANIFEST), False)
    for folder in (os.path.join(OUTPUT, 'bgm'), os.path.join(OUTPUT, 'se'), OUTPUT, os.path.dirname(OUTPUT), os.path.dirname(os.path.dirname(OUTPUT))):
        ensure_meta(folder, True)
    print('wrote', len(clips), 'clips to', OUTPUT)


if __name__ == '__main__':
    main()
