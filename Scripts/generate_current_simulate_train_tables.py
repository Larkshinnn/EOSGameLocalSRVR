#!/usr/bin/env python3
"""Generate minimal Celica boss-practice TSV tables from current client data."""
from __future__ import annotations

import argparse
import csv
import json
from pathlib import Path
from typing import Any, Iterable

REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE = REPO_ROOT.parent / "PGR_DATA" / "en" / "bytes" / "share"
DEFAULT_OUTPUT = REPO_ROOT / "Resources" / "table" / "share" / "fuben" / "simulatetrain"


def resolve_source(source: Path) -> Path:
    nested = source / "fuben" / "simulatetrain"
    return nested if nested.is_dir() else source


def load_rows(source: Path, table_name: str) -> list[dict[str, Any]]:
    json_path = source / f"{table_name}.json"
    csv_path = source / f"{table_name.lower()}.csv"
    if json_path.is_file():
        try:
            value = json.loads(json_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise ValueError(f"{json_path}: unable to load JSON: {exc}") from exc
        if not isinstance(value, list) or not all(isinstance(row, dict) for row in value):
            raise ValueError(f"{json_path}: expected an array of objects")
        return value
    if csv_path.is_file():
        try:
            with csv_path.open("r", encoding="utf-8-sig", newline="") as handle:
                return list(csv.DictReader(handle))
        except (OSError, csv.Error) as exc:
            raise ValueError(f"{csv_path}: unable to load CSV: {exc}") from exc
    raise ValueError(f"missing {json_path.name} or {csv_path.name} in {source}")


def integer(row: dict[str, Any], field: str, *, default: int | None = None) -> int:
    value = row.get(field)
    if value in (None, ""):
        if default is not None:
            return default
        raise ValueError(f"row {row!r} is missing {field}")
    if isinstance(value, bool):
        raise ValueError(f"{field} must be an integer, got {value!r}")
    try:
        return int(value)
    except (TypeError, ValueError) as exc:
        raise ValueError(f"{field} must be an integer, got {value!r}") from exc


def integer_list(row: dict[str, Any], field: str) -> list[int]:
    value = row.get(field)
    if isinstance(value, list):
        return [int(item) for item in value if item not in (None, "")]

    indexed: list[tuple[int, int]] = []
    prefix = field + "["
    for key, item in row.items():
        if not key.startswith(prefix) or not key.endswith("]") or item in (None, ""):
            continue
        try:
            index = int(key[len(prefix):-1])
            indexed.append((index, int(item)))
        except ValueError as exc:
            raise ValueError(f"invalid {field} entry {key}={item!r}") from exc
    return [item for _, item in sorted(indexed)]


def period_buffs(row: dict[str, Any]) -> list[tuple[int, int]]:
    value = row.get("PeriodBuffId")
    if isinstance(value, dict):
        entries = sorted((int(period), int(buff_id)) for period, buff_id in value.items() if int(buff_id) > 0)
        if entries and entries[0][0] == 0:
            return [(period + 1, buff_id) for period, buff_id in entries]
        return entries
    if isinstance(value, list):
        return [(index + 1, int(buff_id)) for index, buff_id in enumerate(value) if buff_id not in (None, "", 0)]

    entries: list[tuple[int, int]] = []
    prefix = "PeriodBuffId["
    for key, item in row.items():
        if not key.startswith(prefix) or not key.endswith("]") or item in (None, "", 0, "0"):
            continue
        try:
            entries.append((int(key[len(prefix):-1]) + 1, int(item)))
        except ValueError as exc:
            raise ValueError(f"invalid period buff entry {key}={item!r}") from exc
    return sorted(entries)


def scalar(value: Any) -> str:
    text = str(value)
    if "\t" in text or "\r" in text or "\n" in text:
        raise ValueError(f"TSV scalar contains a tab or newline: {text!r}")
    return text


def table(columns: list[str], rows: Iterable[list[Any]]) -> bytes:
    lines = ["\t".join(columns)]
    for row in rows:
        if len(row) != len(columns):
            raise ValueError(f"expected {len(columns)} columns, got {len(row)}")
        lines.append("\t".join(scalar(value) for value in row).rstrip("\t"))
    return ("\n".join(lines) + "\n").encode("utf-8")


def repeated_columns(name: str, width: int) -> list[str]:
    return [f"{name}[{index}]" for index in range(1, width + 1)]


def padded(values: list[int], width: int) -> list[int | str]:
    return values + [""] * (width - len(values))


def generate(source: Path) -> dict[str, bytes]:
    source = resolve_source(source)
    monsters = load_rows(source, "SimulateTrainMonster")
    attacks = load_rows(source, "SimulateTrainAtk")
    health = load_rows(source, "SimulateTrainHp")

    monster_ids: set[int] = set()
    stage_ids: set[int] = set()
    normalized_monsters: list[tuple[int, int, int, int, list[int], list[int], list[int]]] = []
    normalized_periods: list[tuple[int, int, int]] = []
    for row in monsters:
        boss_id = integer(row, "Id")
        stage_id = integer(row, "StageId")
        if boss_id in monster_ids:
            raise ValueError(f"duplicate SimulateTrain boss Id {boss_id}")
        if stage_id in stage_ids:
            raise ValueError(f"duplicate SimulateTrain StageId {stage_id}")
        monster_ids.add(boss_id)
        stage_ids.add(stage_id)

        npc_ids = integer_list(row, "NpcId")
        npc_levels = integer_list(row, "NpcLevel")
        stage_buffs = integer_list(row, "StageBuffId")
        if not npc_ids or len(npc_ids) != len(npc_levels) or len(npc_ids) != len(stage_buffs):
            raise ValueError(
                f"boss {boss_id}: NpcId, NpcLevel, and StageBuffId must have equal non-zero lengths")
        normalized_monsters.append((
            boss_id,
            integer(row, "TimeId"),
            integer(row, "ImpasseTimeId"),
            stage_id,
            npc_ids,
            npc_levels,
            stage_buffs,
        ))
        normalized_periods.extend((boss_id, period, buff_id) for period, buff_id in period_buffs(row))

    normalized_monsters.sort(key=lambda row: row[0])
    width = max(len(row[4]) for row in normalized_monsters)
    monster_columns = (["Id", "TimeId", "ImpasseTimeId", "StageId"]
                       + repeated_columns("NpcId", width)
                       + repeated_columns("NpcLevel", width)
                       + repeated_columns("StageBuffId", width))
    monster_rows = [
        [boss_id, time_id, impasse_time_id, stage_id]
        + padded(npc_ids, width)
        + padded(npc_levels, width)
        + padded(stage_buffs, width)
        for boss_id, time_id, impasse_time_id, stage_id, npc_ids, npc_levels, stage_buffs
        in normalized_monsters
    ]

    normalized_periods.sort()
    if len({(boss_id, period) for boss_id, period, _ in normalized_periods}) != len(normalized_periods):
        raise ValueError("duplicate SimulateTrain boss/period buff mapping")

    attack_rows = sorted(
        ([integer(row, "AtkLevel"), integer(row, "AtkBuffId")] for row in attacks),
        key=lambda row: row[0])
    health_rows = sorted(
        ([integer(row, "HpLevel"), integer(row, "HpBuffId")] for row in health),
        key=lambda row: row[0])

    return {
        "SimulateTrainMonster.tsv": table(monster_columns, monster_rows),
        "SimulateTrainAtk.tsv": table(["AtkLevel", "AtkBuffId"], attack_rows),
        "SimulateTrainHp.tsv": table(["HpLevel", "HpBuffId"], health_rows),
        "SimulateTrainPeriodBuff.tsv": table(
            ["BossId", "Period", "BuffId"],
            ([boss_id, period, buff_id] for boss_id, period, buff_id in normalized_periods)),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--check", action="store_true", help="fail if generated files differ from disk")
    args = parser.parse_args()
    try:
        generated = generate(args.source.resolve())
        output = args.output.resolve()
        if args.check:
            mismatches = [name for name, content in generated.items()
                          if not (output / name).is_file() or (output / name).read_bytes() != content]
            if mismatches:
                raise ValueError("generated output differs: " + ", ".join(mismatches))
            print(f"checked {len(generated)} byte-stable tables in {output}")
            return 0

        output.mkdir(parents=True, exist_ok=True)
        for name, content in generated.items():
            (output / name).write_bytes(content)
        print("generated " + ", ".join(
            f"{name} ({content.count(bytes([10])) - 1} rows)" for name, content in generated.items()))
        return 0
    except ValueError as exc:
        print(f"error: {exc}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
