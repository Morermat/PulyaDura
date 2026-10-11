"""
Генератор MIDI-набросков для FL Studio. Без зависимостей: python make_midi.py
Всё сочинено с нуля под игру: ре минор, 100 BPM, 8 тактов, гармония Dm–B♭–F–C
по два такта (как у музыки-заглушки в MusicDirector.cs), поэтому петли совпадут с битом игры.
Файлы появляются в папке midi/. Перетащите .mid в Channel Rack или Piano Roll FL Studio.
"""
import os
import struct

PPQ = 480
BPM = 100
BAR = PPQ * 4
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "midi")

D, F, A, Bb, C, E, G = 62, 65, 69, 70, 72, 64, 67
# Аккорды по два такта: (бас, голоса пэда)
CHORDS = [
    (38, [50, 53, 57, 62]),  # Dm
    (34, [50, 53, 58, 62]),  # B♭ (с D сверху — мягче переход)
    (41, [53, 57, 60, 65]),  # F
    (36, [52, 55, 60, 64]),  # C
]


def vlq(n):
    out = [n & 0x7F]
    n >>= 7
    while n:
        out.insert(0, (n & 0x7F) | 0x80)
        n >>= 7
    return bytes(out)


def track(name, notes, channel=0, program=None, tempo=False):
    """notes: список (старт_в_тиках, длина_в_тиках, нота, громкость)."""
    ev = []
    if tempo:
        ev.append((0, b"\xFF\x51\x03" + struct.pack(">I", int(60_000_000 / BPM))[1:]))
        ev.append((0, b"\xFF\x58\x04\x04\x02\x18\x08"))
    nm = name.encode("utf-8")
    ev.append((0, b"\xFF\x03" + vlq(len(nm)) + nm))
    if program is not None:
        ev.append((0, bytes([0xC0 | channel, program])))
    for start, length, note, vel in notes:
        ev.append((start, bytes([0x90 | channel, note, vel])))
        ev.append((start + length, bytes([0x80 | channel, note, 0])))
    ev.sort(key=lambda e: (e[0], e[1][0] & 0xF0 == 0x90))  # note-off раньше note-on в один тик
    data, last = b"", 0
    for t, msg in ev:
        data += vlq(t - last) + msg
        last = t
    data += b"\x00\xFF\x2F\x00"
    return b"MTrk" + struct.pack(">I", len(data)) + data


def save(filename, tracks):
    os.makedirs(OUT, exist_ok=True)
    head = b"MThd" + struct.pack(">IHHH", 6, 1, len(tracks), PPQ)
    with open(os.path.join(OUT, filename), "wb") as f:
        f.write(head + b"".join(tracks))
    print("midi/" + filename)


def calm_pad():
    notes = []
    for i, (bass, voices) in enumerate(CHORDS):
        start = i * 2 * BAR
        for k, n in enumerate(voices):
            notes.append((start + k * 40, 2 * BAR - 60 - k * 40, n, 58 - k * 4))  # лёгкое «перебирание» при атаке
        notes.append((start, 2 * BAR - 30, bass, 64))
    return notes


def calm_arp():
    """Редкие колокольчики: восьмые с паузами, каждые два такта рисунок чуть меняется."""
    pattern = [0, 2, 1, 3, None, 2, None, 1]
    notes = []
    for i, (_, voices) in enumerate(CHORDS):
        for bar in range(2):
            for step, idx in enumerate(pattern):
                if idx is None or (bar == 1 and step in (5, 6)):
                    continue
                t = (i * 2 + bar) * BAR + step * PPQ // 2
                notes.append((t, PPQ // 2 - 20, voices[idx] + 12, 52 + (step % 3) * 6))
    return notes


def combat_bass():
    """Пульсирующий бас восьмыми, с октавным подскоком в конце каждого такта."""
    notes = []
    for i, (bass, _) in enumerate(CHORDS):
        for bar in range(2):
            for step in range(8):
                t = (i * 2 + bar) * BAR + step * PPQ // 2
                n = bass + (12 if step in (3, 7) else 0)
                notes.append((t, PPQ // 2 - 40, n, 100 if step % 2 == 0 else 78))
    return notes


def combat_drums():
    """GM-ударные (канал 10): бочка на каждую долю, рабочий на 2 и 4, хэт восьмыми, открытый хэт на «и» четвёртой."""
    KICK, SNARE, HAT, OPEN, CLAP = 36, 38, 42, 46, 39
    notes = []
    for bar in range(8):
        b = bar * BAR
        for beat in range(4):
            notes.append((b + beat * PPQ, 100, KICK, 112))
            if beat in (1, 3):
                notes.append((b + beat * PPQ, 100, SNARE, 100))
                notes.append((b + beat * PPQ, 100, CLAP, 60))
        for step in range(8):
            notes.append((b + step * PPQ // 2, 60, OPEN if step == 7 else HAT, 70 if step % 2 else 90))
        if bar % 2 == 1:  # сбивка в конце фразы
            for k in range(4):
                notes.append((b + 3 * PPQ + k * PPQ // 4, 60, SNARE, 60 + k * 12))
    return notes


def combat_lead():
    """Короткий мотив-«рикошет»: фраза отскакивает вверх и падает, отвечает себе через два такта."""
    phrase = [(0, D + 12, 2), (2, F + 12, 1), (3, A + 12, 1), (4, D + 24, 2), (7, C + 12, 1)]
    answer = [(0, Bb + 12, 2), (2, A + 12, 1), (3, F + 12, 1), (4, E + 12, 3)]
    notes = []
    for rep, motif in enumerate([phrase, answer, phrase, answer]):
        base = rep * 2 * BAR
        for step, n, length in motif:
            notes.append((base + step * PPQ // 2, length * PPQ // 2 - 30, n, 92))
    return notes


def stinger_clear():
    """«Комната чиста»: восходящее арпеджио ре мажора (светлый финал минорной пьесы) за два такта."""
    seq = [D, F + 1, A, D + 12, F + 13, A + 12]
    notes = [(k * PPQ // 3, 2 * BAR - k * PPQ // 3, n, 70 + k * 6) for k, n in enumerate(seq)]
    notes.append((0, 2 * BAR, 38, 90))
    return notes


def stinger_death():
    """Смерть: нисходящий хроматический спуск и низкий удар."""
    seq = [A + 12, G + 12 + 1, G + 12, F + 12 + 1, F + 12, E + 12, D + 12]
    notes = [(k * PPQ // 4, PPQ // 4 + 40, n, 96 - k * 6) for k, n in enumerate(seq)]
    notes.append((len(seq) * PPQ // 4, BAR, 26, 110))
    return notes


def bounce_scale():
    """Ноты отскоков (ProjectileSystem.BounceScale от D5): по одной ноте на такт — удобно резать на сэмплы."""
    scale = [0, 3, 5, 7, 10, 12, 15, 17, 19]
    return [(k * PPQ * 2, PPQ, 74 + s, 100) for k, s in enumerate(scale)]


if __name__ == "__main__":
    save("calm_pad.mid", [track("Calm pad", calm_pad(), program=89, tempo=True)])
    save("calm_arp.mid", [track("Calm bells", calm_arp(), program=11, tempo=True)])
    save("combat_bass.mid", [track("Combat bass", combat_bass(), program=38, tempo=True)])
    save("combat_drums.mid", [track("Combat drums", combat_drums(), channel=9, tempo=True)])
    save("combat_lead.mid", [track("Combat lead", combat_lead(), program=81, tempo=True)])
    save("full_sketch.mid", [
        track("Pad", calm_pad(), channel=0, program=89, tempo=True),
        track("Bells", calm_arp(), channel=1, program=11),
        track("Bass", combat_bass(), channel=2, program=38),
        track("Lead", combat_lead(), channel=3, program=81),
        track("Drums", combat_drums(), channel=9),
    ])
    save("stinger_clear.mid", [track("Room clear", stinger_clear(), program=11, tempo=True)])
    save("stinger_death.mid", [track("Death", stinger_death(), program=89, tempo=True)])
    save("bounce_scale.mid", [track("Bounce xylophone", bounce_scale(), program=13, tempo=True)])
