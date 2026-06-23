#!/usr/bin/env python3
"""Fog-of-war demo: same overlay, explored-only vs map-wide. Decides the open lock by EYE."""
import sys, json
import overlay_style_mock as om

def main():
    grid=sys.argv[1]; named=sys.argv[2]; out=sys.argv[3] if len(sys.argv)>3 else "fog_demo.png"
    g=om.load_grid(grid); size=g["size"]
    from PIL import Image, ImageDraw
    # explored = a plausible mid-game reveal: a disc around a spawn-ish point + a travel corridor
    cx, cy = size*0.54, size*0.46   # offset from center, like a real playthrough
    rad = size*0.20
    panels=[
        ("borders", None, None, "Map-wide (all regions visible)"),
        ("borders", (cx,cy), rad, "Fog-respecting (explored only)"),
        ("parchment", (cx,cy), rad, "Parchment + fog (explored only)"),
    ]
    rendered=[(om.render_panel(g,m,fc,fr), label) for m,fc,fr,label in panels]
    pw=rendered[0][0].size[0]; pad=24; titleh=70; lblh=42
    cw=pw+pad; cols=3
    canvas=Image.new("RGB",(cw*cols+pad, pw+lblh+titleh),(12,13,18))
    d=ImageDraw.Draw(canvas)
    ft=om.font(30,bold=True); fl=om.font(19,bold=True); fs=om.font(15)
    d.text((pad,16),"NIFLHEIM · fog-of-war — the open lock, by eye",font=ft,fill=(238,240,246))
    d.text((pad,50),"left = everything revealed at game start (cluttered, spoils the world) · right = regions appear as you explore (clean, native-feeling)",
           font=fs,fill=(168,172,186))
    for i,(im,label) in enumerate(rendered):
        x=pad+i*cw; y=titleh
        canvas.paste(im,(x,y))
        d.text((x+6,y+pw+8),label,font=fl,fill=(228,230,238))
    canvas.save(out)
    print(f"saved {out} ({canvas.size[0]}x{canvas.size[1]})")

if __name__=="__main__":
    main()
