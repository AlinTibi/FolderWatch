"""Render the editable SVG's simple shapes into a multi-resolution Windows icon.
Development dependency: Pillow. Run from any directory: python scripts/render-icon.py
"""
from pathlib import Path
import xml.etree.ElementTree as ET
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent.parent
assets = root / "src/FolderWatch.App/Assets"
svg = ET.parse(assets / "folderwatch.svg").getroot()
scale = 16
image = Image.new("RGBA", (64 * scale, 64 * scale), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
for shape in svg:
    tag = shape.tag.split("}")[-1]
    a = shape.attrib
    if tag in ("polygon", "polyline"):
        points = [tuple(float(v) * scale for v in point.split(",")) for point in a["points"].split()]
        if tag == "polygon":
            draw.polygon(points, fill=a["fill"])
        else:
            width = round(float(a["stroke-width"]) * scale)
            draw.line(points, fill=a["stroke"], width=width, joint="curve")
            radius = width / 2
            for x, y in points:
                draw.ellipse((x-radius, y-radius, x+radius, y+radius), fill=a["stroke"])
    elif tag == "rect":
        x, y, w, h = [float(a[k]) * scale for k in ("x", "y", "width", "height")]
        draw.rounded_rectangle((x, y, x+w, y+h), radius=float(a["rx"])*scale, fill=a["fill"])
image.resize((256, 256), Image.Resampling.LANCZOS).save(assets / "folderwatch.ico", sizes=[(s,s) for s in (16,20,24,32,40,48,64,128,256)])
