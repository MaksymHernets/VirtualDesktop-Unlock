"""Package the edited Streamer-derived master artwork as PNG and Windows ICO."""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[1]
for name in ("icon", "icon-stopped", "icon-unloaded"):
    with Image.open(root / f"assets/{name}-master.png") as source:
        image = source.convert("RGBA")
        if image.width != image.height:
            raise ValueError("Icon master must be square")
        image = image.resize((512, 512), Image.Resampling.LANCZOS)
        image.save(root / f"assets/{name}.png")
        image.save(root / f"assets/{name}.ico", sizes=[(16,16), (24,24), (32,32), (48,48), (64,64), (128,128), (256,256)])
    print(f"Packaged assets/{name}.png and assets/{name}.ico")
