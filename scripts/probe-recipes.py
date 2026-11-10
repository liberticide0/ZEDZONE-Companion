"""Inspect installed recipes or prepare a private, build-specific Companion cache."""

import argparse
from collections import Counter
import csv
import json
import os
from pathlib import Path
import re
import tempfile

import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator


def read_resources(game: Path):
    data = game / "ZEDZONE_Data"
    index_path = data / "globalgamemanagers"
    environment = UnityPy.load(
        str(index_path),
        str(data / "globalgamemanagers.assets"),
        str(data / "resources.assets"),
    )
    index = next(asset for asset in environment.files.values() if asset.name == index_path.name)
    generator = TypeTreeGenerator(str(index.unity_version), "AssetStudio")
    generator.load_il2cpp(
        (game / "GameAssembly.dll").read_bytes(),
        (data / "il2cpp_data" / "Metadata" / "global-metadata.dat").read_bytes(),
    )
    environment.typetree_generator = generator
    manager = next(obj for obj in index.objects.values() if obj.type.name == "ResourceManager")

    return environment, manager.read().m_Container


def read_item_assets(game: Path, resources=None) -> dict:
    if resources is None:
        environment, resources = read_resources(game)
    items = {}
    for resource_path, pointer in resources:
        directory, _, resource_id = resource_path.rpartition("/")
        if directory != "properties/item" or not resource_id.isascii() or not resource_id.isdecimal():
            continue
        item_id = int(resource_id)
        item = pointer.deref().parse_as_dict()
        if item["itemId"] != item_id or item_id in items:
            raise ValueError(f"Invalid or duplicate item ID at {resource_path}")
        items[item_id] = item

    if not items:
        raise ValueError("No numeric item assets found")
    return items


def steam_build(manifest: Path, game: Path) -> str:
    text = manifest.read_text(encoding="utf-8-sig")
    app = re.search(r'^\s*"appid"\s+"1211600"\s*$', text, re.MULTILINE)
    folder = re.search(r'^\s*"installdir"\s+"([^"]+)"\s*$', text, re.MULTILINE)
    build = re.search(r'^\s*"buildid"\s+"([0-9]{1,20})"\s*$', text, re.MULTILINE)
    if not app or not folder or folder[1].casefold() != game.name.casefold() or not build:
        raise ValueError("The Steam manifest does not identify this installation and build")
    return build[1]


def localization_text(path: Path) -> dict:
    with path.open(encoding="utf-8-sig", newline="") as file:
        rows = csv.reader(file)
        if next(rows, None) != ["#format", "zedzone-localization-v1"]:
            raise ValueError("Unsupported ZED ZONE localization format")
        languages = [row for row in rows if row and row[0].startswith("#")]
        if not any(len(row) >= 3 and row[0] == "#target_lang" and row[2] == "ko" for row in languages):
            raise ValueError("Expected the Korean localization table")
        file.seek(0)
        return {
            int(row[0]): (row[2].strip(), row[1].strip())
            for row in csv.reader(file)
            if len(row) == 3 and row[0].isascii() and row[0].isdecimal()
        }


def read_guides(resources, text: dict, items: dict):
    skills = {}
    bosses = []
    technology = {}
    construction = []
    for path, pointer in resources:
        if path.startswith("properties/technodes/"):
            node = pointer.deref().parse_as_dict()
            if node["activedNode"]:
                technology[node["techNodeID"]] = node
        elif path.startswith("properties/character/skilltreenodeattrs/"):
            node = pointer.deref().parse_as_dict()
            if node["actived"] and not node["isHiddenSkill"] and not node["inDevelopment"]:
                skills.setdefault(node["skillDescriptionTextID"], []).append(node)
        elif path.startswith("properties/monsters/") and "boss" in path.rsplit("/", 1)[-1]:
            boss = pointer.deref().parse_as_dict()
            if boss["monsterName_English"]:
                bosses.append({"category": "boss", "korean": "", "english": boss["monsterName_English"],
                               "chinese": boss["monsterName"], "textId": boss["monsterId"],
                               "details": "몬스터 자산에 기록된 이름입니다. 등장 조건과 행동은 미검증입니다."})
        elif path.startswith("properties/terrainobjects/"):
            obj = pointer.deref().parse_as_dict()
            if obj["recipeData"]["recipeItems"]:
                construction.append(obj)

    guides = bosses
    for text_id, nodes in sorted(skills.items()):
        ko, en = text.get(text_id, ("", ""))
        ko_fields = [part.strip() for part in ko.split("|", 2)]
        en_fields = [part.strip() for part in en.split("|", 2)]
        descriptions = [fields[1] for fields in (ko_fields, en_fields) if len(fields) > 1 and fields[1]]
        if descriptions and (ko_fields[0] or en_fields[0]):
            details = descriptions[0].replace(r"\n", "\n")
            if len({node["itemRequired"] for node in nodes}) > 1:
                details += "\n같은 설명을 공유하는 노드의 재료 조건이 달라 해금 조건은 표시하지 않습니다."
            guides.append({"category": "skill", "korean": ko_fields[0], "english": en_fields[0],
                           "chinese": "", "textId": text_id, "details": details})

    for text_id, expected in ((902400, "Shopping Mall"), (902401, "Armory"),
                              (902404, "Old Airport"), (902405, "Army Camp")):
        ko, en = text.get(text_id, ("", ""))
        if en == expected:
            guides.append({"category": "map", "korean": ko, "english": en, "chinese": "",
                           "textId": text_id,
                           "details": "먼 섬의 지역입니다. 지역 이름은 현지화 표, 먼 섬 소속은 Steam 1.0 공지에서 확인했습니다."})

    def tech_name(node_id):
        node = technology.get(node_id)
        if node is None:
            return f"ID {node_id}"
        ko, en = text.get(node["techNameTextID"], ("", ""))
        return ko or en or f"ID {node_id}"

    unlocks = {}
    for node in technology.values():
        for item_id in node["unlockCraftItems"]:
            unlocks.setdefault(item_id, []).append({
                "name": tech_name(node["techNodeID"]), "default": node["unlockByDefault"],
                "prerequisites": [tech_name(i) for i in node["precondition"]["preIDList"]],
            })
    guides.extend(construction_guides(construction, technology, tech_name, text, items))
    return unlocks, guides


def construction_guides(objects, technology, tech_name, text, items):
    korean_by_english = {}
    for korean, english in text.values():
        if korean and korean != "[NO_SOURCE]" and english and english != "[NO_SOURCE]":
            korean_by_english.setdefault(english.casefold(), set()).add(korean)

    def item_name(item_id):
        korean, english = text.get(1_000_000 + item_id, ("", ""))
        asset = items.get(item_id, {})
        return next((name for name in (korean, english, asset.get("itemName_Runtime"))
                     if name and name != "[NO_SOURCE]"), f"아이템 ID {item_id}")

    for obj in sorted(objects, key=lambda obj: obj["id"]):
        object_id = obj["id"]
        recipe = obj["recipeData"]
        english = obj["englishName"].strip()
        chinese = obj["chineseName"].strip()
        matches = korean_by_english.get(english.casefold(), set())
        korean = next(iter(matches)) if len(matches) == 1 else ""
        lines = [f"배치물 ID  {object_id}", "설치 방식  건설 메뉴 · 로컬 배치물 자산"]
        if recipe["itemId"] == object_id and recipe["outputItemNumber"] > 0:
            lines.append(f"회당 배치  {recipe['outputItemNumber']}개")
        else:
            lines.append(f"배치 수량  결과 ID {recipe['itemId']} · 실제 배치 미확인")
        materials = {}
        for material in recipe["recipeItems"]:
            item_id, quantity = material["itemId"], material["itemNumber"]
            if item_id in items and quantity > 0:
                materials[item_id] = materials.get(item_id, 0) + quantity
            else:
                lines.append(f"해석 미확인  {item_name(item_id)} ({quantity:.6g})")
        for item_id, quantity in materials.items():
            lines.append(f"필요 재료  {item_name(item_id)} × {quantity:.6g}")
        if recipe["toolType"]:
            lines.append(f"도구 유형  코드 {recipe['toolType']} · 의미 미검증")
        if recipe["iqReqiared"] or recipe["craftReqiared"]:
            lines.append(f"자산 요구 수치  지능 {recipe['iqReqiared']:.6g} / 제작 {recipe['craftReqiared']:.6g}")
        lines.append(f"기본 해금 플래그  {'켜짐' if obj['unlockByDefault'] else '꺼짐'}")
        for node in technology.values():
            if object_id in node["unlockBuildingItems"]:
                required = ", ".join(tech_name(i) for i in node["precondition"]["preIDList"])
                lines.append(f"연결 연구  {tech_name(node['techNodeID'])}" + (f" · 직전 선행 {required}" if required else ""))
        lines.append(f"자산 기본 내구도  {obj['defaultHP']:.6g}")
        if obj["electricConsuming"] and obj["electricWattage"] > 0:
            lines.append(f"자산 소비 전력  {obj['electricWattage']:.6g} W")
        yield {"category": "construction", "korean": korean, "english": english or ("" if chinese else f"배치물 {object_id}"),
               "chinese": chinese, "textId": object_id, "details": "\n".join(lines)}


def write_snapshot(game: Path) -> Path:
    game = Path(os.path.abspath(game))
    data = game / "ZEDZONE_Data"
    manifest = game.parents[1] / "appmanifest_1211600.acf"
    localization = data / "StreamingAssets" / "Localization" / "Localization_en_to_ko.csv"
    sources = [manifest, localization, game / "GameAssembly.dll", data / "globalgamemanagers",
               data / "globalgamemanagers.assets", data / "resources.assets",
               data / "il2cpp_data" / "Metadata" / "global-metadata.dat"]
    before = [file.stat().st_mtime_ns for file in sources]
    build = steam_build(manifest, game)
    environment, resources = read_resources(game)
    assets = read_item_assets(game, resources)
    names = localization_text(localization)
    unlocks, guides = read_guides(resources, names, assets)
    if before != [file.stat().st_mtime_ns for file in sources] or build != steam_build(manifest, game):
        raise ValueError("Steam files changed during extraction; run it again after the update")

    def clean(text: str) -> str:
        return "" if text == "[NO_SOURCE]" else text

    items = []
    for item_id, asset in sorted(assets.items()):
        korean, english = map(clean, names.get(1_000_000 + item_id, ("", "")))
        if not korean and not english:
            english = clean((asset["itemName_Runtime"] or asset["m_Name"]).strip())
        variants = []
        for row in asset["recipeDataAry"]:
            recipe = {
                "outputId": row["itemId"], "output": row["outputItemNumber"],
                "platform": row["craftPlatform"], "toolType": row["toolType"],
                "intelligence": row["iqReqiared"], "crafting": row["craftReqiared"],
                "difficulty": row["craftDifficultyLevel"],
                "materials": [{"id": m["itemId"], "quantity": m["itemNumber"]} for m in row["recipeItems"]],
            }
            if recipe not in variants:
                variants.append(recipe)
        items.append({"id": item_id, "korean": korean, "english": english,
                      "defaultUnlock": bool(asset["unlockByDefault"]), "recipes": variants,
                      "research": unlocks.get(item_id, [])})

    directory = Path(os.environ["LOCALAPPDATA"]) / "ZEDZONE.Companion"
    directory.mkdir(exist_ok=True)
    destination = directory / f"recipes-{build}.json"
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=directory, delete=False) as file:
        temporary = Path(file.name)
        try:
            json.dump({"schema": 1, "build": build, "items": items, "guides": guides},
                      file, ensure_ascii=False, allow_nan=False)
            file.close()
            temporary.replace(destination)
        finally:
            temporary.unlink(missing_ok=True)
    return destination


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("game", type=Path, help="Steam ZED ZONE installation folder")
    selection = parser.add_mutually_exclusive_group()
    selection.add_argument("--item", type=int, action="append", default=[], help="Show one item ID; repeatable")
    selection.add_argument("--all", action="store_true", help="Show every serialized recipe variant locally")
    selection.add_argument("--snapshot", action="store_true", help="Build the local Companion recipe cache")
    args = parser.parse_args()

    if args.snapshot:
        print(write_snapshot(args.game))
        return

    items = read_item_assets(args.game)
    recipes = {item_id: item["recipeDataAry"] for item_id, item in items.items() if item["recipeDataAry"]}
    variants = [recipe for choices in recipes.values() for recipe in choices]
    materials = [material for recipe in variants for material in recipe["recipeItems"]]

    print(f"Item assets: {len(items)}")
    print(f"Items with serialized recipes: {len(recipes)}")
    print(f"Serialized recipe variants: {len(variants)}")
    print("Variants by serialized platform:", dict(sorted(Counter(r["craftPlatform"] for r in variants).items())))
    print(f"Recipe owners with unlockByDefault=false: {sum(not items[i]['unlockByDefault'] for i in recipes)}")
    print(f"Variants with output ID different from owner: {sum(r['itemId'] != i for i, rs in recipes.items() for r in rs)}")
    print(f"Negative ingredient quantities: {sum(m['itemNumber'] < 0 for m in materials)}")
    print(f"Fractional ingredient quantities: {sum(not m['itemNumber'].is_integer() for m in materials)}")
    print("Unresolved ingredient IDs:", sorted({m["itemId"] for m in materials} - items.keys()))

    for item_id in sorted(recipes) if args.all else args.item:
        if item_id not in items:
            print(f"Item {item_id}: missing")
            continue
        print(f"Item {item_id}:")
        for recipe in recipes.get(item_id, []):
            quantities = [(m["itemId"], m["itemNumber"]) for m in recipe["recipeItems"]]
            print(f"  platform={recipe['craftPlatform']} output={recipe['itemId']} x{recipe['outputItemNumber']}"
                  f" inputs={quantities}")


if __name__ == "__main__":
    main()
