"""Small original vector UI marks with deterministic PNG exports; no external services."""
from pathlib import Path
import json
from PIL import Image,ImageDraw
R=Path(__file__).resolve().parent;P=R/'assets/ui';P.mkdir(exist_ok=True)
# 40-unit viewbox, paths match the map and attribute words in the specification.
paths={
'battle':'M8 8L32 32M32 8L8 32',
'elite':'M8 16L32 36M32 16L8 36M6 13L3 3L20 8L37 3L34 13Z',
'boss':'M8 29L10 16Q10 5 20 5Q30 5 30 16L33 29ZM17 34L23 34M20 2L20 5',
'rest':'M6 8H29V31H6ZM29 11H33Q39 20 29 26M4 35H32',
'forge':'M20 2V8M20 32V38M2 20H8M32 20H38M7 7L11 11M29 29L33 33M7 33L11 29M29 11L33 7M20 8A12 12 0 1 1 19.99 8ZM20 15A5 5 0 1 1 19.99 15Z',
'shop':'M20 4A16 16 0 1 1 19.99 4ZM20 10V30M15 13H24M15 27H24',
'event':'M11 12Q11 4 20 4Q30 4 29 13Q28 18 20 21V26M20 33V35',
'archive':'M20 9Q13 4 3 6V32Q13 30 20 35Q27 30 37 32V6Q27 4 20 9ZM20 9V35',
'volt':'M23 2L8 23H19L16 38L33 16H22Z',
'flame':'M21 2Q22 12 30 18Q37 28 29 35Q20 41 11 33Q3 25 13 14L15 23Q21 16 21 2Z',
'frost':'M20 3V37M5 11L35 29M5 29L35 11M16 7L20 11L24 7M16 33L20 29L24 33M6 16L12 16L12 10M28 30V24H34M6 24H12V30M28 10V16H34',
'void':'M26 5A16 16 0 1 0 26 35A13 13 0 1 1 26 5ZM8 20L32 20M20 8L20 32'
}
colors={'volt':'#ffd84d','flame':'#ff7a3d','frost':'#7cc8ff','void':'#b68cff'}
for k,p in paths.items():
 group='attribute' if k in colors else 'node';co=colors.get(k,'#e9eef8')
 (P/f'ico_{group}_{k}.svg').write_text(f'<svg xmlns="http://www.w3.org/2000/svg" width="128" height="128" viewBox="0 0 40 40"><path d="{p}" fill="none" stroke="{co}" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/></svg>\n')
# PNGs rendered with local Quick Look-free, dependency-free CoreGraphics via SVG is not available.
# The stroke geometry below is a deliberately equivalent raster companion, not generated art.
def line(d,pts,co,width=10):d.line([(round(x*4),round(y*4)) for x,y in pts],fill=co,width=width,joint='curve')
for k in paths:
 a=Image.new('RGBA',(160,160));d=ImageDraw.Draw(a);co=colors.get(k,'#e9eef8')
 if k in ['battle','elite']:
  line(d,[(8,8),(32,32)],co);line(d,[(32,8),(8,32)],co)
  if k=='elite':line(d,[(6,13),(3,3),(20,8),(37,3),(34,13),(6,13)],co)
 elif k=='boss':
  d.arc((40,20,120,108),180,360,fill=co,width=10);line(d,[(10,16),(8,29),(33,29),(30,16)],co);line(d,[(17,34),(23,34)],co)
 elif k=='rest':
  d.rectangle((24,32,116,124),outline=co,width=10);d.arc((100,44,152,108),-90,90,fill=co,width=10);line(d,[(4,35),(32,35)],co)
 elif k in ['forge','shop','void']:
  d.ellipse((16,16,144,144),outline=co,width=10)
  if k=='forge':
   d.ellipse((60,60,100,100),outline=co,width=10)
   import math
   for ang in range(0,360,45):
    t=math.radians(ang);line(d,[(20+13*math.cos(t),20+13*math.sin(t)),(20+19*math.cos(t),20+19*math.sin(t))],co)
  elif k=='shop':line(d,[(20,10),(20,30)],co)
  else:line(d,[(8,20),(32,20)],co);line(d,[(20,8),(20,32)],co)
 elif k=='event':
  d.arc((40,16,120,88),180,470,fill=co,width=10);line(d,[(27,20),(20,24),(20,27)],co);d.ellipse((74,128,86,140),fill=co)
 elif k=='archive':
  line(d,[(20,9),(3,6),(3,32),(20,35),(37,32),(37,6),(20,9),(20,35)],co)
 elif k=='volt':line(d,[(23,2),(8,23),(19,23),(16,38),(33,16),(22,16),(23,2)],co)
 elif k=='flame':line(d,[(21,2),(22,12),(30,18),(33,28),(29,35),(20,38),(11,33),(7,25),(13,14),(15,23),(20,16),(21,2)],co)
 elif k=='frost':
  line(d,[(20,3),(20,37)],co);line(d,[(5,11),(35,29)],co);line(d,[(5,29),(35,11)],co)
 a.resize((128,128),Image.Resampling.LANCZOS).save(P/f'ico_{"attribute" if k in colors else "node"}_{k}.png')
print('12 SVG originals +12 transparent128px PNG companions')
