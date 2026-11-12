"""Prepare optional build-specific inventory thumbnails from the local Steam installation."""

import os
from pathlib import Path
import tempfile
from collections import defaultdict

from PIL import Image


def render_icon(sprite):
    data = sprite.m_RD
    if sprite.m_SpriteAtlas or sprite.m_AtlasTags or data.alphaTexture or data.settingsRaw & 1:
        return None
    texture = data.texture.deref_parse_as_object()
    width, height = texture.m_Width, texture.m_Height
    # Standalone RGBA32 sprites need no atlas mapping, separate alpha, or native decoder.
    if texture.m_TextureFormat != 4 or not (0 < width <= 1024 and 0 < height <= 1024):
        return None
    crop = data.textureRect
    rect = sprite.m_Rect
    if (crop.x, crop.y, crop.width, crop.height) != (0, 0, width, height) or (
        rect.width, rect.height
    ) != (width, height):
        return None
    raw = texture.get_image_data()
    if len(raw) < width * height * 4:
        return None
    image = Image.frombytes("RGBA", (width, height), raw).transpose(Image.Transpose.FLIP_TOP_BOTTOM)
    return image if image.getchannel("A").getbbox() else None


def write_icons(game, probe):
    game = Path(os.path.abspath(game))
    manifest = game.parents[1] / "appmanifest_1211600.acf"
    build = probe["steam_build"](manifest, game)
    directory = Path(os.environ["LOCALAPPDATA"]) / "ZEDZONE.Companion"
    destination = directory / f"icons-v1-{build}"
    if destination.is_dir():
        return
    data = game / "ZEDZONE_Data"
    sources = [manifest, game / "GameAssembly.dll", data / "globalgamemanagers",
               data / "globalgamemanagers.assets", data / "resources.assets",
               data / "il2cpp_data" / "Metadata" / "global-metadata.dat"]
    sources.extend(data.glob("*.resS"))
    before = [(file.stat().st_size, file.stat().st_mtime_ns) for file in sources]
    environment, resources = probe["read_resources"](game)
    items = probe["read_item_assets"](game, resources)
    candidates = defaultdict(dict)
    for path, pointer in resources:
        name = path.rsplit("/", 1)[-1]
        if not path.startswith("sprites/items/") or not name.isascii() or not name.isdecimal():
            continue
        item_id = int(name)
        if item_id not in items:
            continue
        sprite = pointer.deref()
        if sprite.type.name == "Sprite":
            candidates[item_id][(sprite.assets_file.name, sprite.path_id)] = sprite

    directory.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="icons-", dir=directory) as temporary:
        staging = Path(temporary) / "images"
        staging.mkdir()
        count = 0
        for item_id, choices in candidates.items():
            if len(choices) != 1:
                continue
            try:
                sprite = next(iter(choices.values())).parse_as_object()
                image = render_icon(sprite) if sprite.m_Name == str(item_id) else None
                if image is not None:
                    image.save(staging / f"{item_id}.png")
                    count += 1
            except (ValueError, OSError, TypeError, AttributeError, KeyError, RuntimeError):
                continue
        after = [(file.stat().st_size, file.stat().st_mtime_ns) for file in sources]
        if not count or before != after or build != probe["steam_build"](manifest, game):
            raise ValueError("No supported icons or the Steam installation changed during extraction")
        try:
            staging.rename(destination)
        except OSError:
            if not destination.is_dir():
                raise
        print(f"{build}: {count} icons, {sum(len(choices) > 1 for choices in candidates.values())} ambiguous IDs")
