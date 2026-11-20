"""The built-in BGM: short seamless loops composed by rules and synthesized from plain waves, with no samples or model.

Each piece returns one loop as a stereo float array. The app loops it, so every sound that runs past the end wraps
onto the start (synth.Loop).
"""
import numpy as np

from synth import (SR, Loop, bass, bell, circular, clap, epiano, finish_loop, hat, highpass, kick, lowpass, midi, pad,
                   pulse, recorder, saw_pluck, snare, soft_square, string_pluck, triangle_lead, env)


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
    """Steady lo-fi in 41-tone equal temperament. The thirds and fifths are close to pure, so the chords sit still and
    sweet; the dominant chords take the harmonic seventh (7/4, lower and smoother than 12-tone's), and the melody leans
    on that seventh before it resolves. A phrase's first note slides up one step of 41, too small to hear as a slide.
    The melody is written out, not random, and there is no tape wobble or crackle, so nothing in it sounds unsure."""
    rng = np.random.default_rng(seed)
    edo = 41
    base = 261.63  # C4

    def hz(step):
        return base * 2 ** (step / edo)

    c_, d_, e_, f_, g_, a_, b_ = 0, 7, 13, 17, 24, 30, 37
    maj, minor, fifth, h7, min7, maj7, octave = 13, 11, 24, 33, 34, 37, 41
    # Two bars a chord: Fmaj7, Em7, Dm7, G7; then Fmaj7, E7, Am7, G7. Every 7 chord has the harmonic seventh.
    progression = [(f_, [0, maj, fifth, maj7]), (e_, [0, minor, fifth, min7]), (d_, [0, minor, fifth, min7]), (g_ - octave, [0, maj, fifth, h7]),
                   (f_, [0, maj, fifth, maj7]), (e_, [0, maj, fifth, h7]), (a_ - octave, [0, minor, fifth, min7]), (g_ - octave, [0, maj, fifth, h7])]
    # The melody for each chord as (beat in its two bars, step from C4, beats); the first chord of each half rests.
    hi = octave
    g7_seventh = g_ + h7 - octave  # The harmonic seventh of G, a little under F.
    e7_seventh = e_ + h7  # The harmonic seventh of E, a little under D.
    melody = [
        [],
        [(0.5, hi + e_, 1), (1.5, hi + g_, 0.5), (2, hi + a_, 1.5), (4.5, hi + g_, 1), (5.5, hi + e_, 2)],
        [(0.5, hi + d_, 1), (1.5, hi + f_, 1), (2.5, hi + a_, 1), (3.5, 2 * hi, 1.5), (5.5, hi + a_, 1.5)],
        [(0.5, hi + b_, 1), (1.5, hi + a_, 0.5), (2, hi + g_, 1.5), (4, hi + g7_seventh, 3)],
        [],
        [(0.5, hi + b_, 1), (1.5, hi + g_ + 2, 1), (2.5, hi + e_, 1), (3.5, e7_seventh, 2.5), (6, hi + e_, 1.5)],
        [(0.5, hi + a_, 1), (1.5, 2 * hi, 1), (2.5, hi + a_, 0.5), (3, hi + g_, 1), (4, hi + e_, 3)],
        [(0.5, hi + g_, 1), (1.5, hi + a_, 1), (2.5, hi + b_, 1.5), (4, hi + g_, 1), (5, hi + g7_seventh, 2.5)],
    ]
    bars = len(progression) * 2
    loop = Loop(78, bars * 4)
    beat = loop.beat

    def swing(pos):
        whole, frac = divmod(pos, 1.0)
        return whole + (0.58 if abs(frac - 0.5) < 1e-6 else frac)

    def lead(step, dur, slide):
        """A soft triangle; with `slide`, it starts one step of 41 low and settles within a few hundredths of a second."""
        n = int(dur * SR)
        t = np.arange(n) / SR
        bend = np.exp(-t / 0.03) if slide else 0.0
        f = hz(step - bend)
        tri = 2 / np.pi * np.arcsin(np.sin(2 * np.pi * np.cumsum(f) / SR))
        return lowpass(tri * env(n, 0.03, 0.6, 0.45, 0.15), 2600)

    keys, low, drums, mel = (loop.track() for _ in range(4))
    for bar in range(bars):
        chord = bar // 2
        root, tones = progression[chord]
        b0 = bar * 4
        hits = [(0, 0.9), (2.5, 0.55)] if bar % 2 == 0 else [(0.5, 0.6), (2, 0.7), (3.5, 0.4)]
        for pos, vel in hits:
            for i, tone in enumerate(tones):
                loop.add(keys, swing(b0 + pos), vel * epiano(hz(root + tone), beat * 2.2) * 0.2, i * 0.014 + rng.uniform(0, 0.008))
        loop.add(low, swing(b0), bass(hz(root - octave), beat * 2.2, 450, 1.5) * 0.5)
        loop.add(low, swing(b0 + 2.5), bass(hz(root - octave + (fifth if bar % 2 else 0)), beat * 1.3, 450, 1.5) * 0.38)
        for pos in [0, 1.5 if bar % 2 else 2.25]:
            loop.add(drums, swing(b0 + pos), kick(44, 65, 0.2) * 0.85)
        for pos in [1, 3]:
            loop.add(drums, swing(b0 + pos), snare(rng, 170) * 0.36, 0.012)
        for k in range(8):
            loop.add(drums, swing(b0 + k * 0.5), hat(rng, open_=(k == 7 and bar % 4 == 3)) * (0.12 if k % 2 == 0 else 0.075) * rng.uniform(0.9, 1.05))
        if bar % 2 == 0:
            for i, (pos, step, dur) in enumerate(melody[chord]):
                loop.add(mel, swing(b0 + pos), lead(step, beat * dur, slide=(i == 0)) * 0.17)

    mix = keys + low + drums + mel
    mix = mix + loop.room(keys + mel, 1.8, 2.8, 0.013, rng)
    mix = circular(lowpass, mix, 6200)
    return finish_loop(mix, 0.006, 1.2)
