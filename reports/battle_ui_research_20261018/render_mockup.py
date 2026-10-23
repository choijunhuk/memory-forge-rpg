#!/usr/bin/env python3
"""Draws the proposed battle-screen layout (README.md) as a 1600x900 mock-up from the game's own art.

Layout study only: zones, sizes and information placement. Final frames/icons come from Codex (art/requests/).
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'client/MemoryForgeUnity/Assets/Resources/Art'
FONT = str(ROOT / 'client/MemoryForgeUnity/Assets/Resources/Fonts/IBMPlexSansKR-Regular.ttf')
W, H = 1600, 900
BRASS, TEAL, INK, DIM, RED = (226, 180, 95), (98, 214, 198), (233, 238, 248), (152, 165, 192), (255, 111, 111)


def font(size):
    return ImageFont.truetype(FONT, size)


def paste_fit(img, path, box, flip=False):
    art = Image.open(path).convert('RGBA')
    if flip:
        art = art.transpose(Image.FLIP_LEFT_RIGHT)
    x0, y0, x1, y1 = box
    art.thumbnail((x1 - x0, y1 - y0), Image.LANCZOS)
    img.alpha_composite(art, (x0 + (x1 - x0 - art.width) // 2, y1 - art.height))


def pill(d, box, fill, outline=None, r=None):
    d.rounded_rectangle(box, radius=r or (box[3] - box[1]) // 2, fill=fill, outline=outline, width=2)


def text(d, xy, s, size, color=INK, anchor='la', stroke=0):
    d.text(xy, s, font=font(size), fill=color, anchor=anchor, stroke_width=stroke, stroke_fill=(10, 12, 20))


def intent(d, cx, top, value, ghost=None):
    # this turn: big icon disc + number; next turn: smaller faded disc beside it
    d.ellipse((cx - 30, top, cx + 30, top + 60), fill=(150, 40, 40), outline=(255, 200, 200), width=3)
    d.polygon([(cx - 12, top + 44), (cx + 14, top + 14), (cx + 18, top + 18), (cx - 8, top + 48)], fill=INK)  # sword glyph
    text(d, (cx + 34, top + 30), value, 30, INK, 'lm', 3)
    if ghost:
        g = cx + 92
        d.ellipse((g - 18, top + 14, g + 18, top + 50), fill=(70, 60, 80), outline=(160, 150, 170), width=2)
        text(d, (g + 22, top + 32), ghost, 18, DIM, 'lm', 2)


def hp(d, cx, y, w, cur, mx, block=0):
    d.rounded_rectangle((cx - w // 2, y, cx + w // 2, y + 14), 7, fill=(40, 20, 24))
    d.rounded_rectangle((cx - w // 2, y, cx - w // 2 + int(w * cur / mx), y + 14), 7, fill=(220, 70, 70))
    text(d, (cx, y + 7), f'{cur}/{mx}', 13, INK, 'mm', 2)
    if block:
        d.ellipse((cx - w // 2 - 26, y - 6, cx - w // 2 + 2, y + 22), fill=(70, 120, 200), outline=INK, width=2)
        text(d, (cx - w // 2 - 12, y + 8), str(block), 14, INK, 'mm', 2)


def main():
    img = Image.open(ART / 'Backgrounds/bg_zone1_battle.png').convert('RGBA').resize((W, H), Image.LANCZOS)
    img.alpha_composite(Image.new('RGBA', (W, H), (8, 10, 20, 70)))
    d = ImageDraw.Draw(img)

    # top bar (12%)
    d.rectangle((0, 0, W, 64), fill=(10, 14, 26, 230))
    text(d, (24, 32), '1구역 · 3층', 22, BRASS, 'lm')
    for i, s in enumerate(['동전 174', '유물 2']):
        pill(d, (220 + i * 150, 16, 350 + i * 150, 48), (30, 38, 60))
        text(d, (285 + i * 150, 32), s, 18, INK, 'mm')
    for i, s in enumerate(['덱', '기록', '설정']):
        d.ellipse((W - 200 + i * 60, 12, W - 160 + i * 60, 52), fill=(30, 38, 60), outline=DIM, width=2)
        text(d, (W - 180 + i * 60, 32), s, 13, INK, 'mm')

    # reservation rail (NOW | NEXT | AFTER) top centre
    rx, ry = 470, 82
    for i, (label, chip) in enumerate([('지금', None), ('다음', '예약 12'), ('다다음', '예약 8')]):
        x = rx + i * 230
        d.rounded_rectangle((x, ry, x + 210, ry + 58), 12, fill=(14, 20, 36, 200), outline=TEAL if i else BRASS, width=2)
        text(d, (x + 14, ry + 29), label, 18, TEAL if i else BRASS, 'lm')
        if chip:
            pill(d, (x + 92, ry + 12, x + 196, ry + 46), (40, 30, 70), (190, 150, 255))
            text(d, (x + 144, ry + 29), chip, 16, INK, 'mm')
    text(d, (rx + 690, ry + 29), '적 다음 턴: 공격 14 · 공격 6', 15, DIM, 'lm')

    # stage: party left on a diagonal, enemies right; info sits on the bodies
    floor = 600
    for i, cid in enumerate(['sera', 'harin', 'nia']):
        cx, top = 200 + i * 150, floor - 260 + i * 40
        paste_fit(img, ART / f'Characters/char_{cid}_sd.png', (cx - 110, top, cx + 110, top + 230))
        hp(d, cx, top + 236, 140, [64, 82, 50][i], [82, 95, 66][i], [0, 9, 0][i])
    enemies = [('enemy_e_gardener', 1060, 300, '25', '14'), ('enemy_e_seed', 1270, 330, '2×2', '방2'),
               ('enemy_e_seed', 1460, 330, '6', None)]
    for name, cx, top, val, ghost in enemies:
        paste_fit(img, ART / f'Enemies/{name}.png', (cx - 100, top, cx + 100, top + 230), flip=True)
        intent(d, cx - 30, top - 70, val, ghost)
        hp(d, cx, top + 236, 130, 40, 55)
        for k in range(3):  # status icons under the bar
            d.ellipse((cx - 50 + k * 34, top + 260, cx - 24 + k * 34, top + 286), fill=(60, 50, 90), outline=DIM, width=1)

    # bottom 25%: AP gem, party portraits with ult rings, fanned hand, end-turn dial, pile badges
    d.rectangle((0, 690, W, H), fill=(8, 10, 20, 150))
    d.regular_polygon((110, 790, 62), 6, rotation=30, fill=(200, 150, 60), outline=(255, 230, 160))
    text(d, (110, 790), '3/4', 34, (30, 20, 10), 'mm')
    text(d, (110, 864), '행동력', 15, BRASS, 'mm')
    for i, cid in enumerate(['sera', 'harin', 'nia']):
        cx = 230 + i * 78
        d.ellipse((cx - 32, 752, cx + 32, 816), outline=BRASS if i == 0 else (90, 100, 130), width=5)
        port = Image.open(ART / f'Characters/char_{cid}_sd.png').convert('RGBA').crop((300, 120, 724, 544)).resize((54, 54))
        mask = Image.new('L', (54, 54), 0); ImageDraw.Draw(mask).ellipse((0, 0, 54, 54), fill=255)
        img.paste(port, (cx - 27, 757), mask)
        text(d, (cx, 836), ['필살 준비', '62%', '30%'][i], 13, BRASS if i == 0 else DIM, 'mm')
    hand = [('반격 태세', '1'), ('유리탄', '1'), ('구조 분석', '1'), ('미세 조정', '0'), ('방패 강타', '1')]
    for i, (name, cost) in enumerate(hand):
        angle = (i - 2) * -6
        lifted = i == 1
        card = Image.new('RGBA', (170, 240), (0, 0, 0, 0))
        cd = ImageDraw.Draw(card)
        cd.rounded_rectangle((0, 0, 169, 239), 14, fill=(24, 30, 52), outline=TEAL if lifted else (150, 120, 200), width=4)
        cd.rounded_rectangle((10, 34, 160, 130), 8, fill=(60, 70, 110))
        cd.ellipse((-8, -8, 44, 44), fill=(200, 150, 60), outline=(255, 230, 160), width=2)
        cd.text((18, 18), cost, font=font(26), fill=(30, 20, 10), anchor='mm')
        cd.text((85, 152), name, font=font(20), fill=INK, anchor='mm')
        cd.text((85, 192), '피해 8', font=font(22), fill=INK, anchor='mm')
        card = card.rotate(angle, expand=True, resample=Image.BICUBIC)
        x = 560 + i * 118 - card.width // 2 + 85
        y = (612 if lifted else 668) + abs(i - 2) * 10
        img.alpha_composite(card, (x, y))
    d.ellipse((W - 190, 720, W - 50, 860), fill=(170, 120, 50), outline=(255, 230, 160), width=4)
    text(d, (W - 120, 790), '턴 종료', 24, (30, 20, 10), 'mm')
    for label, x in [('뽑을 17', 30), ('버림 4', W - 260)]:
        pill(d, (x, 868, x + 110, 896), (30, 38, 60))
        text(d, (x + 55, 882), label, 15, INK, 'mm')

    # drag arrow from the lifted card to the target (play now) and notes
    d.line([(720, 650), (900, 520), (1080, 380)], fill=(255, 120, 120), width=6, joint='curve')
    d.polygon([(1080, 380), (1052, 386), (1068, 408)], fill=(255, 120, 120))
    text(d, (760, 600), '끌어서 적에게 놓으면 지금 사용 · 레일 칸에 놓으면 예약', 16, INK, 'lm', 3)
    out = Path(__file__).with_name('mockup_battle.png')
    img.convert('RGB').save(out, quality=92)
    print(out)


if __name__ == '__main__':
    main()
