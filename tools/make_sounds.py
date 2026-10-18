#!/usr/bin/env python3
"""Synthesises AVAPuzzleBobble's sound effects and tunes into ../src/AVAPuzzleBobble/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle, sine and noise voices. The title
plays the opening strain of The Blue Danube (Johann Strauss II, 1866); the tune in play is a new composition. The two
calls are spoken by the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAPuzzleBobble", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    write("Fire.wav", tone(0.12, 700, 1400, "square", attack=0.002, release=0.6, gain=0.5), 0.14)
    write("Bounce.wav", tone(0.04, 1800, 1500, "tri", attack=0.001, release=1.0), 0.12)
    write("Stick.wav", tone(0.06, 500, 300, "sine", attack=0.001, release=1.0), 0.2)
    write("Pop.wav", cat(*[mix(tone(0.05, 1200 + k * 300, 400, "sine", attack=0.001, release=1.0), tone(0.03, 3000, 1000, "noise", gain=0.4)) for k in range(3)]), 0.3)
    write("Drop.wav", cat(*[tone(0.05, hz(n), kind="tri", release=0.4) for n in ["C7", "A6", "F6", "D6", "B5", "G5"]]), 0.28)
    write("Ceiling.wav", mix(tone(0.4, 90, 60, "saw", attack=0.01, release=0.6), tone(0.3, 400, 120, "noise", gain=0.5)), 0.34)
    write("Lose.wav", cat(*[tone(0.08, 600 - k * 50, kind="square", release=0.4, gain=0.6) for k in range(8)]), 0.3)
    write("Clear.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6", "C7", "E7", "G7"]], tone(0.35, hz("C8"), kind="pulse", vibrato=0.02, release=0.6)), 0.26)
    write("Hurry.wav", cat(*[tone(0.1, f, kind="square", release=0.3) for f in [1320, 990, 1320, 990]]), 0.22)
    write("Tick.wav", tone(0.025, 1800, 1200, "square", attack=0.001, release=1.0, gain=0.5), 0.12)
    for name, words, pitch in [("SayGo", "R EH1 D IY , G OW1 !", 200), ("SayClear", "K L IY1 R !", 210)]:
        write(name + ".wav", mix(say(words, rate=RATE, pitch=pitch, speed=1.05, size=0.85), [s * 0.3 for s in say(words, rate=RATE, pitch=pitch * 1.5, speed=1.05, size=0.85, seed=11)]), 0.5)

    # In play: a bouncy tune of its own.
    e = 0.5
    lead = [("C5", e), ("E5", e), ("G5", e), ("E5", e), ("A5", 1), ("G5", 1), ("F5", e), ("A5", e), ("C6", e), ("A5", e), ("G5", 2),
            ("E5", e), ("G5", e), ("C6", e), ("B5", e), ("A5", e), ("G5", e), ("F5", e), ("E5", e), ("D5", e), ("F5", e), ("E5", e), ("D5", e), ("C5", 2)]
    bass = [("C3", 1), ("G3", 1)] * 2 + [("F3", 1), ("C3", 1), ("C3", 1), ("G3", 1)] + [("C3", 1), ("A2", 1), ("F2", 1), ("G2", 1), ("G2", 1), ("G2", 1), ("C3", 2)]
    write("Music.wav", cat(tune(144, lead, bass, kind="pulse"), tune(144, [(n[:-1] + str(int(n[-1]) + 1), b) for n, b in lead], bass, kind="square")), 0.26)
    # The title: The Blue Danube (Johann Strauss II, 1866), the waltz's first strain.
    q = 1
    dan = [("-", 2), ("D4", q), ("D4", q), ("F#4", q), ("A4", q), ("A4", 3), ("A5", q), ("-", q), ("A5", q), ("F#5", q), ("-", q), ("F#5", q),
           ("D4", q), ("D4", q), ("F#4", q), ("A4", q), ("A4", 3), ("A5", q), ("-", q), ("A5", q), ("G5", q), ("-", q), ("G5", q),
           ("C#4", q), ("C#4", q), ("E4", q), ("B4", q), ("B4", 3), ("B5", q), ("-", q), ("B5", q), ("G5", q), ("-", q), ("G5", q),
           ("C#4", q), ("C#4", q), ("E4", q), ("B4", q), ("B4", 3), ("B5", q), ("-", q), ("B5", q), ("F#5", q), ("-", q), ("F#5", q),
           ("D4", q), ("D4", q), ("F#4", q), ("A4", q), ("D5", 3), ("D6", q), ("-", q), ("D6", q), ("A5", q), ("-", q), ("A5", q), ("D5", 6)]
    waltz = []
    for chord in ["D3", "D3", "D3", "D3", "A2", "A2", "A2", "A2", "A2", "A2", "D3", "D3", "D3", "D3", "D3"]:
        waltz += [(chord, 1), (chord[0] + "#" + chord[1] if False else chord[:-1] + str(int(chord[-1]) + 1), 1), (chord[:-1] + str(int(chord[-1]) + 1), 1)]
    write("TitleTune.wav", cat(tune(170, dan, waltz, kind="tri"), silence(1.0)), 0.34)
    write("Victory.wav", tune(150, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1.5), ("B5", 0.5), ("C6", 0.5), ("D6", 0.5), ("E6", 3)],
                              [("C3", 1), ("G2", 1), ("A2", 1), ("F2", 1), ("G2", 1), ("C3", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(70, [("E5", 1), ("C5", 1), ("A4", 1), ("E4", 1), ("A4", 3)], [("A2", 2), ("F2", 2), ("A1", 3)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
