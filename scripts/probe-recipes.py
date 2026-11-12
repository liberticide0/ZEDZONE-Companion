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


def item_name(item_id, text, items):
    korean, english = text.get(1_000_000 + item_id, ("", ""))
    return next((name for name in (korean, english, items.get(item_id, {}).get("itemName_Runtime"))
                 if name and name != "[NO_SOURCE]"), f"아이템 ID {item_id}")


def item_facts(asset, text, items):
    facts = []
    size = asset["itemSize"]
    if size["m_X"] > 0 and size["m_Y"] > 0:
        facts.append(f"인벤토리 크기  {size['m_X']} × {size['m_Y']}칸")
    if asset["stackNumber"] > 0:
        facts.append(f"스택 한도  {asset['stackNumber']:,}개")
    if asset["weight"] > 0:
        facts.append(f"무게  {asset['weight']:.6g}")
    for title, rows in (("수리 재료", asset["repairData"]["recipeItems"]),
                        ("분해 산출", asset["disassembleData"]["disassembledItems"])):
        for row in rows:
            target, quantity = row["itemId"], row["itemNumber"]
            name = item_name(target, text, items)
            if target in items and quantity > 0:
                facts.append(f"{title}  {name} × {quantity:.6g}")
            else:
                facts.append(f"{title} 미확인  {name} · 자산 값 {quantity:.6g}")
    if asset["disassembleData"]["requareGrinder"]:
        facts.append("분해 조건  그라인더 필요")
    for tool in asset["disassembleData"]["requiredTools"]:
        facts.append(f"분해 도구 미확인  자산 값 {tool}")
    if asset["meltItems"]:
        for target, occurrences in Counter(asset["meltItems"]).items():
            suffix = f" · 자산에 {occurrences}회 기록" if occurrences > 1 else ""
            facts.append(f"용해 산출 후보  {item_name(target, text, items)}{suffix}")
        if asset["meltYieldFloat"] > 0 and asset["meltYieldFloat"] != 1:
            facts.append(f"용해 수율 계수  {asset['meltYieldFloat']:.6g} · 실제 생산량 미확인")
    return facts


def effect_guide(asset, text, trait=False):
    id_key, text_key = ("perkId", "perkTextId") if trait else ("buffId", "buffDescriptionTextID")
    ko, en = text.get(asset[text_key], ("", ""))
    separator = "(" if trait else "|"
    names = [value.split(separator, 1)[0].strip() if value != "[NO_SOURCE]" else "" for value in (ko, en)]
    descriptions = [value.split(separator, 1)[1].strip().removesuffix(")" if trait else "")
                    for value in (ko, en) if separator in value]
    if not any(names):
        return None
    if trait:
        status = "활성" if asset["actived"] else "비활성"
        kind = "스킬 특성" if asset["isSkillPerk"] else "캐릭터 특성"
        lines = [f"상태  {status} · {kind}"]
        if asset["conflictPerkId"] > 0:
            ko_conflict, en_conflict = text.get(asset["conflictPerkId"], ("", ""))
            name = (ko_conflict or en_conflict).split("(", 1)[0].strip()
            lines.append("충돌 특성  " + (name or f"ID {asset['conflictPerkId']}"))
    else:
        kind = "식사 효과" if asset["isMealBuff"] else "상태 효과"
        lines = [f"종류  {kind}"]
        if not asset["m_Enabled"]:
            lines.append("상태  자산 비활성")
        for field, label in (("hpPerSecond", "초당 체력"), ("hungerPerSecond", "초당 포만감"),
                             ("thirstPerSecond", "초당 수분"), ("energyPerSecond", "초당 기력"),
                             ("bodyTemperaturePerSecond", "초당 체온")):
            if asset[field] != 0:
                lines.append(f"{label}  {asset[field]:+.6g}")
    if descriptions and descriptions[0]:
        lines.append(descriptions[0].replace(r"\n", "\n"))
    return {"category": "trait" if trait else "effect", "korean": names[0], "english": names[1],
            "chinese": "", "textId": asset[id_key], "details": "\n".join(lines)}


def read_guides(resources, text: dict, items: dict):
    skills = {}
    bosses = []
    effects = []
    technology = {}
    construction = []
    acquisitions = {}

    def source(item_id, label, description):
        if item_id in items:
            row = f"{label}  {description}"
            acquisitions.setdefault(item_id, {})[row] = None

    for path, pointer in resources:
        if path.startswith("properties/technodes/"):
            node = pointer.deref().parse_as_dict()
            if node["activedNode"]:
                technology[node["techNodeID"]] = node
        elif path.startswith("properties/character/skilltreenodeattrs/"):
            node = pointer.deref().parse_as_dict()
            skills.setdefault(node["skillDescriptionTextID"], []).append(node)
        elif path.startswith(("properties/buffs/", "properties/character/characterperkattrs/")):
            guide = effect_guide(pointer.deref().parse_as_dict(), text, trait="characterperkattrs/" in path)
            if guide:
                effects.append(guide)
        elif path.startswith("properties/monsters/"):
            monster = pointer.deref().parse_as_dict()
            name = monster["monsterName_English"] or monster["monsterName"] or f"몬스터 ID {monster['monsterId']}"
            for field, label in (("lootItems", "전리품 · 고정 목록"), ("slaughterItems", "도축 · 고정 목록"),
                                 ("rateLootItems", "전리품 · 확률"), ("tissueItems", "조직 · 의미 미확인")):
                for row in monster[field]:
                    value = row["itemNumber"]
                    if field == "rateLootItems" and 0 < value <= 1:
                        source(row["itemId"], label, f"{name} · 자산 확률값 {value:.2%}")
                    elif field in ("lootItems", "slaughterItems") and value > 0 and value.is_integer():
                        source(row["itemId"], label, f"{name} · 자산 수량 {value:.0f}개")
                    else:
                        source(row["itemId"], "의미 미확인", f"{name} · {label} · 자산 값 {value:.6g}")
            if "boss" in path.rsplit("/", 1)[-1] and monster["monsterName_English"]:
                drops = [f"{item_name(row['itemId'], text, items)} × {row['itemNumber']:.6g}"
                         for row in monster["lootItems"] if row["itemId"] in items and row["itemNumber"] > 0]
                details = "전리품 · 자산 고정 목록  " + ", ".join(drops) if drops else ""
                bosses.append({"category": "boss", "korean": "", "english": monster["monsterName_English"],
                               "chinese": monster["monsterName"], "textId": monster["monsterId"],
                               "details": details or "몬스터 자산에서 수량이 확인된 고정 전리품이 없습니다."})
        elif path.startswith("properties/terrainobjects/"):
            obj = pointer.deref().parse_as_dict()
            if obj["recipeData"]["recipeItems"]:
                construction.append(obj)
            name = obj["englishName"] or obj["chineseName"] or f"배치물 ID {obj['id']}"
            for item_id in set(obj["outputItemList"]) - {0}:
                action = "타격 산출 목록" if obj["produceItemOnHit"] else "산출 목록"
                source(item_id, "지형·배치물", f"{name} · {action} · 수량과 확률 미확인")

    guides = bosses + effects
    for text_id, nodes in sorted(skills.items()):
        ko, en = text.get(text_id, ("", ""))
        ko_fields = [part.strip() for part in ko.split("|", 2)]
        en_fields = [part.strip() for part in en.split("|", 2)]
        descriptions = [fields[1] for fields in (ko_fields, en_fields) if len(fields) > 1 and fields[1]]
        if descriptions and (ko_fields[0] or en_fields[0]):
            details = descriptions[0].replace(r"\n", "\n")
            states = Counter("개발 중" if n["inDevelopment"] else "숨김" if n["isHiddenSkill"] else
                             "활성" if n["actived"] else "비활성" for n in nodes)
            details = "노드 상태  " + ", ".join(f"{state} {count}개" for state, count in states.items()) + "\n" + details
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
    research = []
    for node_id, node in sorted(technology.items()):
        ko, en = text.get(node["techNameTextID"], ("", ""))
        research.append({
            "id": node_id, "korean": "" if ko == "[NO_SOURCE]" else ko,
            "english": "" if en == "[NO_SOURCE]" else en,
            "cost": node["unlockTechPointCost"], "default": bool(node["unlockByDefault"]),
            "prerequisites": node["precondition"]["preIDList"],
            "craft": node["unlockCraftItems"], "construction": node["unlockBuildingItems"],
        })
    return unlocks, guides, research, acquisitions


def construction_guides(objects, technology, tech_name, text, items):
    korean_by_english = {}
    for korean, english in text.values():
        if korean and korean != "[NO_SOURCE]" and english and english != "[NO_SOURCE]":
            korean_by_english.setdefault(english.casefold(), set()).add(korean)

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
                lines.append(f"해석 미확인  {item_name(item_id, text, items)} ({quantity:.6g})")
        for item_id, quantity in materials.items():
            lines.append(f"필요 재료  {item_name(item_id, text, items)} × {quantity:.6g}")
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
    unlocks, guides, research, acquisitions = read_guides(resources, names, assets)
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
                      "research": unlocks.get(item_id, []), "facts": item_facts(asset, names, assets),
                      "acquisition": sorted(acquisitions.get(item_id, {}))})

    directory = Path(os.environ["LOCALAPPDATA"]) / "ZEDZONE.Companion"
    directory.mkdir(exist_ok=True)
    destination = directory / f"recipes-{build}.json"
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=directory, delete=False) as file:
        temporary = Path(file.name)
        try:
            json.dump({"schema": 1, "build": build, "items": items, "guides": guides, "research": research},
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
