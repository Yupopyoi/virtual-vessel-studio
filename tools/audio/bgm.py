"""The built-in BGM: short seamless loops composed by rules and synthesized from plain waves, with no samples or model.

Each piece returns one loop as a stereo float array. The app loops it, so every sound that runs past the end wraps
onto the start (synth.Loop).
"""
import numpy as np

from synth import (SR, bass, bell, brass, circular, clap, env, epiano, finish_loop, hat, highpass, kick, Loop,
                   lowpass, midi, pad, pulse, recorder, saw_pluck, snare, soft_square, string_pluck, triangle_lead,
                   woodblock, xylophone)


def cute_pop(seed=21):
    """Straight-timed cute pop on the royal-road progression, with a repeated motif and bells in the second half."""
    rng = np.random.default_rng(seed)
    loop = Loop(124, 16 * 4)
    prog = [
        ('F', [53, 57, 60, 64], 41), ('G', [55, 59, 62, 65], 43), ('Em', [52, 55, 59, 62], 40), ('Am', [57, 60, 64, 67], 45),
        ('F', [53, 57, 60, 64], 41), ('G', [55, 59, 62, 65], 43), ('C', [48, 52, 55, 60], 36), ('C', [48, 52, 55, 59], 36),
    ]
    tones_of = {'F': [65, 69, 72, 76], 'G': [67, 71, 74, 79], 'Em': [64, 67, 71, 76], 'Am': [69, 72, 76, 81], 'C': [72, 76, 79, 84]}
    motif_a = [(0, 0.5, 2), (0.5, 0.5, 1), (1, 1, 2), (2, 0.5, 3), (2.5, 0.5, 2), (3, 1, 1)]
    motif_b = [(0, 1, 1), (1, 0.5, 2), (1.5, 0.5, 3), (2, 1.5, 2), (3.5, 0.5, 0)]
    ending = [(0, 0.5, 2), (0.5, 0.5, 1), (1, 3, 0)]

    keys, lead, sparkle, low, drums, kicks = (loop.track() for _ in range(6))
    beat = loop.beat
    for bar in range(16):
        name, chord, root = prog[bar % 8]
        b0 = bar * 4
        second_half = bar >= 8
        for note in chord:
            loop.add(keys, b0, pad(midi(note), beat * 4) * 0.06)
        for k in range(8):
            loop.add(keys, b0 + k * 0.5, saw_pluck(midi(chord[k % 4] + 12), beat * 0.45) * 0.07)
            loop.add(low, b0 + k * 0.5, bass(midi(root + (12 if k % 2 else 0)), beat * 0.45) * 0.42)
            loop.add(drums, b0 + k * 0.5, hat(rng) * (0.12 if k % 2 else 0.07))
        for k in range(4):
            loop.add(drums, b0 + k, kick() * 0.85)
            loop.add(kicks, b0 + k, env(int(0.25 * SR), 0.001, 0.09))
        for k in (1, 3):
            loop.add(drums, b0 + k, clap(rng) * 0.35)
        tones = tones_of[name]
        if second_half:
            for k in range(16):
                loop.add(sparkle, b0 + k * 0.25, bell(midi(tones[[0, 1, 2, 3, 2, 1, 2, 3][k % 8]] + 12), beat * 0.9) * 0.05)
        shape = ending if bar % 8 == 7 else (motif_b if bar % 4 == 2 or bar % 8 == 6 else motif_a)
        for at, length, tone in shape:
            loop.add(lead, b0 + at, soft_square(midi(tones[tone]), beat * length * 0.92) * 0.16)
            if second_half:
                loop.add(lead, b0 + at, bell(midi(tones[tone] + 12), beat * 1.5) * 0.06)

    duck = 1 - 0.45 * np.clip(kicks, 0, 1)
    mix = keys * duck + low * (1 - 0.3 * np.clip(kicks, 0, 1)) + lead + sparkle + drums
    echo = np.roll(lead + sparkle, loop.at(0.75)) * 0.22 + np.roll(lead + sparkle, loop.at(1.5)) * 0.08
    mix += circular(lowpass, echo, 5000)
    return finish_loop(mix)


def lofi(seed=5):
    """Relaxed lo-fi: swung electric piano on Fmaj7-Em7-Dm7-Cmaj7, a sparse pentatonic melody, a little crackle."""
    rng = np.random.default_rng(seed)
    bars = 16
    loop = Loop(82, bars * 4)
    beat = loop.beat

    def swing(pos):
        whole, frac = divmod(pos, 1.0)
        return whole + (0.58 if abs(frac - 0.5) < 1e-6 else frac)

    chords = [[53, 57, 60, 64], [52, 55, 59, 62], [50, 53, 57, 60], [48, 52, 55, 59]]
    roots = [41, 40, 38, 36]
    scale = [60, 62, 65, 67, 69, 72, 74, 76]
    keys, low, drums, mel = (loop.track() for _ in range(4))
    for bar in range(bars):
        c = bar % 4
        b0 = bar * 4
        for pos, vel in [(0, 0.9), (2.5, 0.6)]:
            for i, note in enumerate(chords[c]):
                loop.add(keys, swing(b0 + pos), vel * epiano(midi(note), beat * 2.2) * 0.22, i * 0.012 + rng.uniform(0, 0.01))
        loop.add(low, swing(b0), bass(midi(roots[c]), beat * 2.2, 500, 1.6) * 0.5)
        loop.add(low, swing(b0 + 2.5), bass(midi(roots[c] + (7 if bar % 2 else 0)), beat * 1.3, 500, 1.6) * 0.4)
        for pos in [0, 1.5 if bar % 2 else 2.25]:
            loop.add(drums, swing(b0 + pos), kick(45, 70, 0.18) * 0.9)
        for pos in [1, 3]:
            loop.add(drums, swing(b0 + pos), snare(rng) * 0.45, 0.01)
        for k in range(8):
            loop.add(drums, swing(b0 + k * 0.5), hat(rng, open_=(k == 7 and bar % 4 == 3)) * (0.16 if k % 2 == 0 else 0.1) * rng.uniform(0.8, 1.1))
        # The melody rests in the first bars of each half, so the loop breathes.
        if bar % 8 >= 2:
            idx = int(rng.integers(2, 6))
            pos = 0.0
            while pos < 4:
                dur = float(rng.choice([0.5, 1.0, 1.5]))
                if rng.random() < (0.55 if bar % 8 < 4 else 0.75):
                    loop.add(mel, swing(b0 + pos), triangle_lead(midi(scale[idx]), beat * dur) * 0.18)
                idx = int(np.clip(idx + rng.choice([-2, -1, 1, 2]), 0, len(scale) - 1))
                pos += dur

    mix = keys + low + drums + mel
    mix = mix + loop.room(keys + mel, 1.6, 3.2, 0.012, rng)
    mix = circular(lowpass, mix, 7500)
    crackle = np.zeros(loop.length)
    pops = rng.integers(0, loop.length, size=bars * 25)
    crackle[pops] = rng.uniform(-1, 1, size=len(pops)) * 0.25
    mix += circular(highpass, crackle, 2000) + circular(highpass, rng.standard_normal(loop.length), 3000) * 0.0025
    return finish_loop(mix, 0.006, 1.2)


def recorder_waltz(seed=3):
    """A slow waltz: a recorder melody over a quiet plucked accompaniment and a soft hall."""
    rng = np.random.default_rng(seed)
    meter = 3
    loop = Loop(80, 16 * meter)
    beat = loop.beat
    n = {'C4': 60, 'D4': 62, 'E4': 64, 'F4': 65, 'G4': 67, 'A4': 69, 'B4': 71,
         'C5': 72, 'D5': 74, 'E5': 76, 'F5': 77, 'G5': 79, 'A5': 81, 'B5': 83, 'C6': 84, 'D6': 86}
    c, am, f, g = ('C', 48, ['C4', 'E4', 'G4']), ('Am', 45, ['C4', 'E4', 'A4']), ('F', 41, ['C4', 'F4', 'A4']), ('G', 43, ['D4', 'G4', 'B4'])
    g7, dm, em = ('G7', 43, ['D4', 'F4', 'B4']), ('Dm', 50, ['D4', 'F4', 'A4']), ('Em', 40, ['E4', 'G4', 'B4'])
    chords = [c, am, f, g, c, am, dm, g, f, g, em, am, f, g, g7, c]
    melody = [
        [('G5', 2), ('E5', 1)], [('A5', 1.5), ('G5', 0.5), ('E5', 1)], [('F5', 1), ('A5', 1), ('C6', 1)], [('B5', 2), ('G5', 1)],
        [('G5', 2), ('E5', 1)], [('A5', 1.5), ('B5', 0.5), ('C6', 1)], [('D6', 1), ('C6', 1), ('A5', 1)], [('B5', 3)],
        [('A5', 2), ('C6', 1)], [('B5', 1.5), ('A5', 0.5), ('G5', 1)], [('G5', 1), ('E5', 1), ('G5', 1)], [('A5', 3)],
        [('F5', 1), ('A5', 1), ('C6', 1)], [('D6', 2), ('B5', 1)], [('G5', 1), ('A5', 1), ('B5', 1)], [('C6', 3)],
    ]
    lead, accomp = loop.track(), loop.track()
    for bar in range(16):
        b0 = bar * meter
        pos = 0.0
        for note, length in melody[bar]:
            loop.add(lead, b0 + pos, recorder(midi(n[note]), beat * length * 0.93, rng) * 0.30)
            pos += length
        _, root, tones = chords[bar]
        loop.add(accomp, b0, string_pluck(midi(root), beat * 3.2, rng, 0.35) * 0.30)
        loop.add(accomp, b0 + 1, string_pluck(midi(n[tones[1]]), beat * 1.6, rng) * 0.16)
        loop.add(accomp, b0 + 1.5, string_pluck(midi(n[tones[2]]), beat * 1.4, rng) * 0.12)
        loop.add(accomp, b0 + 2, string_pluck(midi(n[tones[0]]), beat * 1.6, rng) * 0.14)
        loop.add(accomp, b0 + 2.5, string_pluck(midi(n[tones[2]]), beat * 1.0, rng) * 0.10)
        for tone in tones:
            loop.add(accomp, b0, pad(midi(n[tone]), beat * 3.0, 0.4, 2.0, 0.7, 0.4, 0.003) * 0.025)
    mix = lead + accomp * 0.55
    mix = mix + loop.room(mix, 2.2, 2.4, 0.010, rng, 3500)
    mix = circular(lowpass, mix, 9000)
    return finish_loop(mix, 0.005, 1.0)


def calm_talk(seed=11):
    """For talking over: slow electric piano and soft bells on Cmaj7-Am7-Fmaj7-G6, no drums but a light shaker."""
    rng = np.random.default_rng(seed)
    bars = 16
    loop = Loop(72, bars * 4)
    beat = loop.beat
    chords = [[48, 55, 59, 64], [45, 52, 55, 60], [41, 48, 52, 57], [43, 50, 52, 59]]
    tops = [[76, 79, 83], [72, 76, 79], [69, 72, 76], [71, 74, 76]]
    keys, low, shimmer, shake = (loop.track() for _ in range(4))
    for bar in range(bars):
        c = bar % 4
        b0 = bar * 4
        for i, note in enumerate(chords[c][1:]):
            loop.add(keys, b0, epiano(midi(note), beat * 4.2) * 0.16, i * 0.02)
            loop.add(keys, b0 + 2.5, epiano(midi(note), beat * 1.6) * 0.07, i * 0.015)
        loop.add(low, b0, pad(midi(chords[c][0] - 12), beat * 4, 0.08, 1.2, 0.5, 0.3, 0.002) * 0.22)
        # Bells answer every second bar, a few notes from the chord's top.
        if bar % 2 == 1:
            for k, tone in enumerate(rng.permutation(tops[c])[:2 + int(rng.integers(0, 2))]):
                loop.add(shimmer, b0 + 1 + k * 0.75, bell(midi(tone), beat * 3, 2.0, 1.2, 1.4) * 0.05)
        for k in range(8):
            loop.add(shake, b0 + k * 0.5, hat(rng) * (0.035 if k % 2 else 0.02))
    mix = keys + lowpass(low, 400) + shimmer + shake
    mix = mix + loop.room(keys + shimmer, 2.5, 2.0, 0.012, rng, 3500)
    mix = circular(lowpass, mix, 8000)
    return finish_loop(mix, 0.006, 1.0)


def denpa_41edo(seed=7):
    """Hyper chiptune pop in 41-tone equal temperament: near-pure thirds, harmonic-seventh dominants, a break on a
    neutral chord, and a chorus lifted 4 steps of 41, an interval 12-tone tuning lacks."""
    rng = np.random.default_rng(seed)
    edo = 41
    base = 261.63  # C4

    def hz(step):
        return base * 2 ** (step / edo)

    c_, d_, e_, f_, g_, a_ = 0, 7, 13, 17, 24, 30
    maj, minor, neutral, sept_min = 13, 11, 12, 9
    fifth, h7, octave = 24, 33, 41

    def chord(root, third, *more):
        return root, [0, third, fifth, *more]

    lift = 4
    verse = [chord(f_, maj), chord(g_ - octave, maj, h7), chord(e_, minor), chord(a_ - octave, minor),
             chord(f_, maj), chord(g_ - octave, maj, h7), chord(c_, maj), chord(c_, maj, h7)]
    brk = [chord(d_, neutral), chord(a_ - octave, sept_min, h7)]
    chorus = [chord(f_ + lift, maj), chord(g_ - octave + lift, maj, h7), chord(e_ + lift, minor), chord(a_ - octave + lift, minor),
              chord(f_ + lift, maj), chord(g_ - octave + lift, maj, h7), chord(c_ + lift, maj, h7), chord(c_ + lift, maj, h7, octave + maj)]
    song = [('verse', x) for x in verse] + [('break', x) for x in brk] + [('chorus', x) for x in chorus]
    loop = Loop(140, len(song) * 4)
    beat = loop.beat

    def lead(step, dur):
        n = int(dur * SR)
        return (pulse(hz(step), n, 0.25, 0.006) * 0.7 + pulse(hz(step + octave), n, 0.5) * 0.18) * env(n, 0.004, 0.18, 0.55, 0.02)

    def arp(step, dur):
        n = int(dur * SR)
        return pulse(hz(step), n, 0.125) * env(n, 0.002, 0.05)

    def low(step, dur):
        n = int(dur * SR)
        return lowpass(pulse(hz(step), n, 0.5), 900) * env(n, 0.003, 0.12, 0.4, 0.02)

    def laser(step):
        n = int(0.25 * SR)
        t = np.arange(n) / SR
        ph = 2 * np.pi * np.cumsum(hz(step) * 2 ** (-8 * t)) / SR
        return np.sign(np.sin(ph)) * 0.5 * env(n, 0.002, 0.08)

    rhythm_a = [(0, 0.5), (0.5, 0.25), (0.75, 0.25), (1, 0.5), (1.5, 0.5), (2, 0.75), (3, 0.5), (3.5, 0.5)]
    rhythm_b = [(0, 0.25), (0.25, 0.25), (0.5, 0.25), (0.75, 0.25), (1, 1), (2.5, 0.5), (3, 1)]
    shape_a = [2, 1, 2, 3, 2, 1, 0, 1]
    shape_b = [0, 1, 2, 3, 3, 2, 1]
    tracks = {name: loop.track() for name in ('lead', 'arp', 'bass', 'drums', 'fx', 'pad')}
    for bar, (part, (root, tones)) in enumerate(song):
        b0 = bar * 4
        steps = [root + octave + t for t in tones]
        for k in range(16):
            pick = steps[(k // 4) % len(steps)] if part == 'break' else steps[[0, 1, 2, 3, 2, 1, 2, 3][k % 8] % len(steps)]
            loop.add(tracks['arp'], b0 + k * 0.25, arp(pick + octave, beat * 0.22) * 0.06)
        for k in range(8):
            loop.add(tracks['bass'], b0 + k * 0.5, low(root - octave + (octave if k % 2 else 0), beat * 0.45) * 0.28)
        for k in range(4):
            if not (part == 'break' and bar % 2 == 0 and k >= 2):
                loop.add(tracks['drums'], b0 + k, kick(55, 120, 0.09) * 0.8)
        for k in (1, 3):
            loop.add(tracks['drums'], b0 + k, clap(rng) * 0.32)
        for k in range(16):
            loop.add(tracks['drums'], b0 + k * 0.25, hat(rng, open_=(k % 4 == 2)) * (0.08 if k % 2 else 0.05))
        if part == 'break':
            for k in range(8):
                loop.add(tracks['lead'], b0 + k * 0.5, lead(steps[1] + (octave if k % 4 == 3 else 0), beat * 0.22) * 0.13)
            loop.add(tracks['fx'], b0 + 3.5, laser(steps[0] + 2 * octave) * 0.08)
        else:
            rhythm, shape = (rhythm_b, shape_b) if bar % 4 == 2 else (rhythm_a, shape_a)
            for (at, length), index in zip(rhythm, shape):
                loop.add(tracks['lead'], b0 + at, lead(steps[index % len(steps)], beat * length * 0.9) * 0.14)
                if part == 'chorus':
                    n = int(beat * 1.2 * SR)
                    t = np.arange(n) / SR
                    f = hz(steps[index % len(steps)] + octave)
                    ring = np.sin(2 * np.pi * f * t + np.sin(2 * np.pi * f * 3.5 * t) * 2 * np.exp(-t * 10)) * env(n, 0.002, 0.4)
                    loop.add(tracks['fx'], b0 + at, ring * 0.05)
            if part == 'chorus' and bar % 4 == 3:
                loop.add(tracks['fx'], b0 + 3.75, laser(steps[0] + 2 * octave) * 0.07)
        n = int(4 * beat * SR)
        t = np.arange(n) / SR
        held = sum(np.sin(2 * np.pi * hz(root + tone) * t) for tone in tones) / len(tones)
        loop.add(tracks['pad'], b0, held * env(n, 0.05, 10.0, 1.0, 0.1) * 0.08)

    pump = np.ones(loop.length)
    beat_len = int(beat * SR)
    for k in range(len(song) * 4):
        s = k * beat_len
        seg = 1 - 0.5 * np.exp(-np.arange(min(beat_len, loop.length - s)) / SR / 0.06)
        pump[s:s + len(seg)] = seg
    mix = tracks['lead'] + tracks['bass'] + tracks['drums'] + tracks['fx'] + (tracks['arp'] + tracks['pad']) * pump
    mix += np.roll(tracks['lead'], loop.at(0.75)) * 0.18
    return finish_loop(mix, 0.004, 1.2)


def lofi_41edo(seed=13):
    """Bright, light lo-fi in 41-tone equal temperament, at 92 BPM in a major key. The thirds and fifths are close to
    pure, so the chords ring clean; they take an added ninth rather than a seventh, and the melody moves in short notes,
    so nothing rubs for long under talk. The tuning shows in the dominant chord's harmonic seventh (7/4), which the
    melody only passes through, and in a phrase's first note sliding up one step of 41."""
    rng = np.random.default_rng(seed)
    edo = 41
    base = 261.63  # C4

    def hz(step):
        return base * 2 ** (step / edo)

    c_, d_, e_, f_, g_, a_, b_ = 0, 7, 13, 17, 24, 30, 37
    maj, minor, fifth, h7, octave = 13, 11, 24, 33, 41
    ninth = octave + d_
    major, minor_add9, dominant = [0, maj, fifth, ninth], [0, minor, fifth, ninth], [0, maj, fifth, h7]
    # Two bars a chord: C, G, Am, F; then C, G, F, G7.
    progression = [(c_, major), (g_ - octave, major), (a_ - octave, minor_add9), (f_ - octave, major),
                   (c_, major), (g_ - octave, major), (f_ - octave, major), (g_ - octave, dominant)]
    hi = octave
    g7_seventh = hi + g_ + h7 - octave  # A little under F5.
    # The melody for each chord as (beat in its two bars, step from C4, beats).
    phrase_c = [(0.5, hi + e_, 0.5), (1, hi + g_, 0.5), (1.5, hi + a_, 0.5), (2, hi + g_, 1), (3, hi + e_, 1),
                (4.5, hi + d_, 0.5), (5, hi + e_, 0.5), (5.5, hi + g_, 1.5)]
    phrase_g = [(0.5, hi + d_, 0.5), (1, hi + d_, 0.5), (1.5, hi + e_, 0.5), (2, hi + d_, 1), (3, b_, 1),
                (4.5, hi + d_, 0.5), (5, hi + g_, 1), (6, hi + d_, 1.5)]
    melody = [
        phrase_c,
        phrase_g,
        [(0.5, hi + e_, 0.5), (1, hi + a_, 1), (2, hi + g_, 0.5), (2.5, hi + e_, 1), (4.5, hi, 0.5), (5, hi + d_, 0.5), (5.5, hi + e_, 1.5)],
        [(0.5, hi + f_, 0.5), (1, hi + a_, 0.5), (1.5, 2 * hi, 1), (2.5, hi + a_, 0.5), (3, hi + g_, 1), (4.5, hi + f_, 0.5), (5, hi + e_, 0.5), (5.5, hi + d_, 1.5)],
        phrase_c,
        phrase_g,
        [(0.5, hi + f_, 0.5), (1, hi + a_, 0.5), (1.5, 2 * hi, 1), (2.5, hi + a_, 0.5), (3, hi + g_, 1), (4.5, hi + f_, 0.5), (5, hi + g_, 0.5), (5.5, hi + a_, 1)],
        [(0.5, hi + b_, 0.5), (1, hi + a_, 0.5), (1.5, hi + g_, 1), (2.5, g7_seventh, 0.5), (3, hi + d_, 1), (4.5, hi + g_, 0.5), (5, hi + a_, 0.5), (5.5, hi + b_, 1.5)],
    ]
    bars = len(progression) * 2
    loop = Loop(92, bars * 4)
    beat = loop.beat

    def swing(pos):
        whole, frac = divmod(pos, 1.0)
        return whole + (0.56 if abs(frac - 0.5) < 1e-6 else frac)

    def lead(step, dur, slide):
        """A soft triangle; with `slide`, it starts one step of 41 low and settles within a few hundredths of a second."""
        n = int(dur * SR)
        t = np.arange(n) / SR
        bend = np.exp(-t / 0.03) if slide else 0.0
        f = hz(step - bend)
        tri = 2 / np.pi * np.arcsin(np.sin(2 * np.pi * np.cumsum(f) / SR))
        return lowpass(tri * env(n, 0.02, 0.4, 0.4, 0.08), 3200)

    keys, low, drums, mel = (loop.track() for _ in range(4))
    for bar in range(bars):
        chord = bar // 2
        root, tones = progression[chord]
        b0 = bar * 4
        hits = [(0, 0.85), (1.5, 0.5), (2.5, 0.6)] if bar % 2 == 0 else [(0.5, 0.55), (2, 0.65), (3.5, 0.45)]
        for pos, vel in hits:
            for i, tone in enumerate(tones):
                loop.add(keys, swing(b0 + pos), vel * epiano(hz(root + tone), beat * 1.4) * 0.18, i * 0.01 + rng.uniform(0, 0.006))
        loop.add(low, swing(b0), bass(hz(root - octave), beat * 1.6, 500, 1.5) * 0.5)
        loop.add(low, swing(b0 + 1.5), bass(hz(root - octave), beat * 0.6, 500, 1.5) * 0.3)
        loop.add(low, swing(b0 + 2.5), bass(hz(root - octave + (fifth if bar % 2 else 0)), beat * 1.2, 500, 1.5) * 0.38)
        for pos in [0, 1.5, 2.5] if bar % 2 else [0, 2.25]:
            loop.add(drums, swing(b0 + pos), kick(46, 70, 0.18) * 0.85)
        for pos in [1, 3]:
            loop.add(drums, swing(b0 + pos), snare(rng, 180) * 0.38, 0.01)
        for k in range(8):
            loop.add(drums, swing(b0 + k * 0.5), hat(rng, open_=(k == 7 and bar % 4 == 3)) * (0.13 if k % 2 == 0 else 0.085) * rng.uniform(0.9, 1.05))
        if bar % 2 == 0:
            for i, (pos, step, dur) in enumerate(melody[chord]):
                loop.add(mel, swing(b0 + pos), lead(step, beat * dur, slide=(i == 0)) * 0.17)

    mix = keys + low + drums + mel
    mix = mix + loop.room(keys + mel, 1.4, 3.2, 0.011, rng)
    mix = circular(lowpass, mix, 8500)
    return finish_loop(mix, 0.006, 1.2)


def toy_box(seed=17):
    """A toy box tipped out: a xylophone tune over a tuba-and-pizzicato march, a glockenspiel answering, woodblocks, a
    middle where a brass section takes the tune with a clarinet under it, and a last part with everyone at once, brass
    stabs and a slide whistle up into the start again. Bright C major in 12-tone tuning, so it stays cute."""
    rng = np.random.default_rng(seed)
    C, Am, F, G, Dm, Em, G7 = ([60, 64, 67], 36), ([57, 60, 64], 45), ([53, 57, 60], 41), ([55, 59, 62], 43), \
        ([53, 57, 62], 38), ([55, 59, 64], 40), ([53, 59, 62], 43)
    part_a = [C, Am, F, G, C, Am, Dm, G]
    part_b = [F, G, Em, Am, Dm, G, C, G7]
    bars = part_a + part_b + part_a
    loop = Loop(132, len(bars) * 4)
    beat = loop.beat

    # The xylophone tune of the A parts, a bar a line: (beat, note, beats).
    tune_a = [
        [(0, 76, .5), (.5, 79, .5), (1, 84, .5), (1.5, 79, .5), (2, 76, 1), (3, 72, .5), (3.5, 74, .5)],
        [(0, 76, .5), (.5, 72, .5), (1, 69, 1), (2, 72, .5), (2.5, 76, .5), (3, 81, 1)],
        [(0, 81, .5), (.5, 79, .5), (1, 77, .5), (1.5, 76, .5), (2, 77, 1), (3, 72, 1)],
        [(0, 74, .5), (.5, 76, .5), (1, 77, .5), (1.5, 79, .5), (2, 83, 1), (3, 79, 1)],
        [(0, 76, .5), (.5, 79, .5), (1, 84, .5), (1.5, 79, .5), (2, 76, 1), (3, 72, .5), (3.5, 74, .5)],
        [(0, 81, .5), (.5, 79, .5), (1, 76, .5), (1.5, 72, .5), (2, 69, 1), (3, 72, 1)],
        [(0, 74, .5), (.5, 77, .5), (1, 81, 1), (2, 77, .5), (2.5, 74, .5), (3, 74, 1)],
        [(0, 71, .5), (.5, 74, .5), (1, 79, .5), (1.5, 74, .5), (2, 71, 1), (3, 67, 1)],
    ]
    # The brass tune of the B part, a little fanfare.
    tune_b = [
        [(0, 72, 1), (1, 77, .5), (1.5, 77, .5), (2, 81, 1.5), (3.5, 79, .5)],
        [(0, 79, .5), (.5, 77, .5), (1, 74, 1), (2, 79, 1.5), (3.5, 77, .5)],
        [(0, 76, 1), (1, 79, .5), (1.5, 76, .5), (2, 71, 1), (3, 76, 1)],
        [(0, 72, .5), (.5, 76, .5), (1, 81, 1.5), (2.5, 79, .5), (3, 76, 1)],
        [(0, 77, 1), (1, 74, .5), (1.5, 77, .5), (2, 81, 1), (3, 77, 1)],
        [(0, 79, 1), (1, 83, .5), (1.5, 79, .5), (2, 74, 1), (3, 71, 1)],
        [(0, 72, .5), (.5, 76, .5), (1, 79, .5), (1.5, 84, 1.5), (3, 79, 1)],
        [(0, 77, .5), (.5, 76, .5), (1, 74, .5), (1.5, 71, .5), (2, 67, 2)],
    ]
    # The clarinet under the brass: two notes a bar from the chords.
    under_b = [(69, 72), (71, 74), (67, 71), (69, 72), (69, 65), (71, 67), (67, 64), (65, 62)]

    tuba, pizz, xylo, glock, horns, clar, drums, toys = (loop.track() for _ in range(8))
    for bar, (chord, root) in enumerate(bars):
        b0 = bar * 4
        part = 'a' if bar < 8 else 'b' if bar < 16 else 'a2'
        line = bar % 8

        # The march: tuba on 1 and 3 (root, then fifth), pizzicato chords on 2 and 4.
        loop.add(tuba, b0, brass(midi(root), beat * 0.8, bright=0.35) * 0.55)
        loop.add(tuba, b0 + 2, brass(midi(root + 7 if root < 43 else root - 5), beat * 0.8, bright=0.35) * 0.5)
        loop.add(tuba, b0, bass(midi(root), beat * 0.9, 400, 1.2) * 0.35)
        for pos in (1, 3):
            for i, note in enumerate(chord):
                loop.add(pizz, b0 + pos, string_pluck(midi(note), 0.35, rng, bright=0.7) * 0.22, i * 0.008)

        if part in ('a', 'a2'):
            for pos, note, dur in tune_a[line]:
                loop.add(xylo, b0 + pos, xylophone(midi(note), beat * dur + 0.25) * 0.42)
            # The glockenspiel answers at the end of every other bar.
            if line % 2 == 1:
                for k, note in enumerate(sorted(chord)[::-1] + [sorted(chord)[0]]):
                    loop.add(glock, b0 + 3 + k * 0.25, bell(midi(note + 24), 0.6, decay=0.35) * 0.12)
            for pos in (0.5, 1.5, 2.5, 3.5):
                loop.add(toys, b0 + pos, woodblock(rng, 1.0 if pos in (0.5, 2.5) else 1.25) * 0.16)
        if part == 'b':
            for pos, note, dur in tune_b[line]:
                # Two trumpets a third apart on the long notes, one on the short ones.
                loop.add(horns, b0 + pos, brass(midi(note), beat * dur * 0.95) * 0.3)
                if dur >= 1:
                    third = 3 if (note - root) % 12 in (4, 11) else 4
                    loop.add(horns, b0 + pos, brass(midi(note - third), beat * dur * 0.95, bright=0.8) * 0.18)
            for k, note in enumerate(under_b[line]):
                loop.add(clar, b0 + k * 2, soft_square(midi(note - 12), beat * 1.9, vibrato=0.002) * 0.17)
            for pos in (1, 3):
                loop.add(drums, b0 + pos, clap(rng) * 0.3)
            if line in (0, 4):
                loop.add(glock, b0, bell(midi(84), 1.0, ratio=4.2, decay=0.5) * 0.08)
        if part == 'a2':
            # Everyone at once: brass stabs on the off-beats answer the xylophone.
            for pos in (1.5, 3.5) if line % 2 == 0 else (0.5, 2.5):
                for i, note in enumerate(chord):
                    loop.add(horns, b0 + pos, brass(midi(note), beat * 0.3) * 0.13, i * 0.004)
            loop.add(clar, b0, soft_square(midi(chord[0] - 12), beat * 3.8, vibrato=0.002) * 0.08)

        # Drums: a light march, a roll into each new part.
        loop.add(drums, b0, kick(55, 80, 0.12) * 0.7)
        loop.add(drums, b0 + 2, kick(55, 80, 0.12) * 0.55)
        for pos in (1, 3):
            loop.add(drums, b0 + pos, snare(rng, 220) * 0.28)
        for k in range(8):
            loop.add(drums, b0 + k * 0.5, hat(rng) * (0.09 if k % 2 == 0 else 0.06))
        if line == 7:
            for k in range(8):
                loop.add(drums, b0 + 2 + k * 0.25, snare(rng, 230) * (0.12 + 0.025 * k))

    # A slide whistle up into the start again.
    n = int(beat * 1.8 * SR)
    t = np.arange(n) / SR
    glide = midi(72) * 2 ** (1.0 * (t / t[-1]) ** 1.5)
    whistle = np.sin(2 * np.pi * np.cumsum(glide * (1 + 0.006 * np.sin(2 * np.pi * 7 * t))) / SR) * env(n, 0.04, 10.0, 1.0, 0.08)
    loop.add(toys, len(bars) * 4 - 2, whistle * 0.12)

    mix = tuba + pizz + xylo + glock + horns + clar + drums + toys
    mix = mix + loop.room(xylo + glock + horns + clar + pizz, 1.2, 3.8, 0.012, rng, 5000)
    return finish_loop(mix, 0.005, 1.15)
