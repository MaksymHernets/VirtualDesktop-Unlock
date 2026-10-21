"""Render the project's original geometric icon into PNG and multi-size Windows ICO."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
scale = 4
image = Image.new("RGBA", (512 * scale, 512 * scale))
draw = ImageDraw.Draw(image)
navy, panel, white, mint = "#101a29", "#1c3044", "#e7f2fa", "#6cdabc"
def box(rect): return tuple(round(v * scale) for v in rect)
def rounded(rect, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(box(rect), radius * scale, fill, outline, width * scale)
def line(points, color, width):
    draw.line([(x * scale, y * scale) for x, y in points], color, width * scale, joint="curve")
    r = width / 2
    for x, y in (points[0], points[-1]): draw.ellipse(box((x-r,y-r,x+r,y+r)), fill=color)
rounded((8,8,504,504),112,navy)
rounded((58,90,418,352),44,white)
rounded((78,110,398,332),24,panel)
line([(238,344),(238,396)],white,20)
line([(176,400),(292,400)],white,20)
rounded((127,167,346,281),34,mint)
draw.polygon([(x*scale,y*scale) for x,y in [(190,281),(215,258),(258,258),(283,281)]],fill=panel)
rounded((155,199,212,241),16,navy)
rounded((261,199,318,241),16,navy)
draw.ellipse(box((287,273,469,455)),fill=navy)
# Open shackle: deliberately stop the right leg above the body.
draw.arc(box((336.5,274.5,423.5,353)),180,360,fill=mint,width=19*scale)
line([(346,314),(346,345)],mint,19)
draw.ellipse(box((404.5,309.5,423.5,328.5)),fill=mint)
rounded((328,337,428,423),21,mint)
line([(378,366),(378,394)],navy,13)
image = image.resize((512,512),Image.Resampling.LANCZOS)
image.save(root / "assets/icon.png")
image.save(root / "assets/icon.ico",sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
print("Generated assets/icon.png and assets/icon.ico")
