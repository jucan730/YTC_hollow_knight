# -*- coding: utf-8 -*-
"""候选怪物名逐条核对：只保留在游戏文件里真实出现过的名字，并输出清单（名字+出现关卡数+分类）。"""
import json
import re
import os

OUT = r"C:/Users/ruitg/WorkBuddy/2026-11-14-15-19-46/hk-mod/out"

COMMON = [
    "Crawler", "Abyss Crawler", "Mines Crawler", "Fung Crawler", "Crystal Crawler", "First Crawler",
    "Zombie Hornhead", "Zombie Guard", "Zombie Barger", "Royal Zombie Fat", "Royal Zombie Coward",
    "Great Shield Zombie", "Gorgeous Husk",
    "Fat Fly", "Big Buzzer", "Angry Buzzer", "Buzzer",
    "Hopper", "Giant Hopper", "Flip Hopper", "Grass Hopper",
    "Spitter", "Super Spitter", "Honey Spitter",
    "Mushroom Roller", "Moss Charger", "Mega Moss Charger",
    "Mushroom Turret", "Laser Turret", "Laser Turret Mega", "Plant Turret", "Mawlek Turret",
    "Moss Flyer", "Moss Walker", "Moss Knight", "Moss Knight Fat",
    "Fungus Flyer", "Fungoon Baby", "Fungus Base Scarab",
    "Bee Stinger", "Bee Dropper", "Big Bee", "Mega Fat Bee",
    "Spider Mini", "Spider Flyer", "Tiny Spider",
    "Worm", "Dust Worm",
    "Fluke Fly", "Flukeman", "Fat Fluke",
    "Blob", "Mage Blob 1", "Mage Blob 2",
    "Shade Sibling", "White Palace Fly",
    "Jellyfish", "Jellyfish Baby", "Mega Jellyfish",
    "Hatcher", "Centipede Hatcher",
    "Mantis", "Mantis Heavy Flyer", "Mantis Flyer Child",
    "Lobster Lancer", "Orange Scuttler", "Zoteling",
    "Crystal Flyer", "Blocker Shield", "Royal Guard",
]

BOSS = [
    "False Knight New", "False Knight", "False Knight Dream",
    "Hornet Boss 1", "Hornet Boss 2", "Hornet",
    "Mantis Lords", "Mantis Lord", "Mantis Traitor Lord",
    "Mage Lord", "Mage Lord 2",
    "Dung Defender", "White Defender", "Watcher Knights",
    "Mawlek", "Lesser Mawlek",
    "Gruz Mother", "Fluke Mother", "Broken Vessel",
    "Jar Collector", "Nightmare King", "Nightmare Grimm",
    "Radiance", "Absolute Radiance", "Oblobble",
    "Hive Knight", "Crystal Guardian",
    "Ghost Warrior Xero", "Ghost Warrior Marmu", "Ghost Warrior Hu",
    "Ghost Warrior Galien", "Ghost Warrior Markoth",
]


def main():
    pool = json.load(open(os.path.join(OUT, "all_strings.json"), encoding="utf-8"))
    found, missing = [], []
    for kind, names in (("普通敌人", COMMON), ("头目/BOSS", BOSS)):
        for n in names:
            if n in pool:
                found.append((kind, n, pool[n]))
            else:
                missing.append((kind, n))
    with open(os.path.join(OUT, "enemy_final.txt"), "w", encoding="utf-8") as fh:
        for kind, n, c in found:
            fh.write("%s\t%s\t%d\n" % (kind, n, c))
    print("found", len(found), "missing", len(missing))
    print("MISSING:", ", ".join(n for _, n in missing))


if __name__ == "__main__":
    main()
