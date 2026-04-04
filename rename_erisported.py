#!/usr/bin/env python3
"""Rename erisported PNG files to match the standard Guns RSI naming convention."""

import os
import json
import re

ERISPORTED_DIR = r"C:\Users\Adrian\Documents\GitHub\Oxyd14\Resources\Textures\Oxyd\erisported"


def is_secondary(name):
    """Return True if this state/filename matches a known secondary pattern."""
    # Exact matches and prefix patterns (check longer prefixes first)
    secondary_patterns = [
        ('lefthand_doble_', 'prefix'),
        ('righthand_doble_', 'prefix'),
        ('lefthand_', 'prefix'),
        ('righthand_', 'prefix'),
        ('back_', 'prefix'),
        ('onsuit_', 'prefix'),
    ]
    for pattern, kind in secondary_patterns:
        if kind == 'prefix' and name.startswith(pattern):
            return True

    # Exact match or dash+number variant (e.g. lefthand, lefthand-50, lefthand_doble-25)
    for base in ['lefthand_doble', 'righthand_doble', 'lefthand', 'righthand', 'back', 'onsuit']:
        if re.match(rf'^{re.escape(base)}(-\d+)?$', name):
            return True

    return False


def get_secondary_new_name(name):
    """Return the new standardised state name for a secondary pattern, or None."""
    # Wielded (doble) - check before plain lefthand/righthand
    if name == 'lefthand_doble':
        return 'wielded-inhand-left'
    if name == 'righthand_doble':
        return 'wielded-inhand-right'

    m = re.match(r'^lefthand_doble(-\d+)$', name)
    if m:
        return f'wielded-inhand-left{m.group(1)}'
    m = re.match(r'^righthand_doble(-\d+)$', name)
    if m:
        return f'wielded-inhand-right{m.group(1)}'

    if name.startswith('lefthand_doble_'):
        return 'wielded-inhand-left-' + name[len('lefthand_doble_'):]
    if name.startswith('righthand_doble_'):
        return 'wielded-inhand-right-' + name[len('righthand_doble_'):]

    # Plain inhand
    if name == 'lefthand':
        return 'inhand-left'
    if name == 'righthand':
        return 'inhand-right'

    m = re.match(r'^lefthand(-\d+)$', name)
    if m:
        return f'inhand-left{m.group(1)}'
    m = re.match(r'^righthand(-\d+)$', name)
    if m:
        return f'inhand-right{m.group(1)}'

    if name.startswith('lefthand_'):
        return 'inhand-left-' + name[len('lefthand_'):]
    if name.startswith('righthand_'):
        return 'inhand-right-' + name[len('righthand_'):]

    # Equipped slots
    if name == 'back':
        return 'equipped-BACKPACK'
    if name == 'onsuit':
        return 'equipped-SUITSTORAGE'

    if name.startswith('back_'):
        return 'equipped-BACKPACK-' + name[len('back_'):]
    if name.startswith('onsuit_'):
        return 'equipped-SUITSTORAGE-' + name[len('onsuit_'):]

    return None


def find_base_icon(non_secondary_names):
    """
    Identify the base icon state name among non-secondary states.
    The base icon is the shortest name that is a valid prefix of all others in the group.
    """
    if not non_secondary_names:
        return None
    if len(non_secondary_names) == 1:
        return non_secondary_names[0]

    sorted_names = sorted(non_secondary_names, key=len)

    def is_valid_prefix(candidate, name):
        if name == candidate:
            return True
        # Followed by underscore, dash, or directly a digit
        if name.startswith(candidate + '_') or name.startswith(candidate + '-'):
            return True
        if re.match(rf'^{re.escape(candidate)}\d', name):
            return True
        return False

    for candidate in sorted_names:
        if all(is_valid_prefix(candidate, n) for n in non_secondary_names):
            return candidate

    return None


def process_rsi_dir(rsi_dir_path):
    dir_name = os.path.basename(rsi_dir_path)
    if not dir_name.endswith('.rsi'):
        return

    try:
        files = os.listdir(rsi_dir_path)
    except Exception as e:
        print(f"  ERROR listing: {e}")
        return

    png_files = [f for f in files if f.endswith('.png')]

    secondary_pngs = []
    non_secondary_pngs = []
    for png in png_files:
        (secondary_pngs if is_secondary(png[:-4]) else non_secondary_pngs).append(png)

    rename_map = {}

    # Base icon
    non_secondary_names = [p[:-4] for p in non_secondary_pngs]
    base_name = find_base_icon(non_secondary_names)
    if base_name:
        rename_map[base_name + '.png'] = 'base.png'

    # Secondary patterns
    for png in secondary_pngs:
        new = get_secondary_new_name(png[:-4])
        if new:
            rename_map[png] = new + '.png'

    # Drop no-ops
    rename_map = {k: v for k, v in rename_map.items() if k != v}

    if not rename_map:
        return

    state_rename_map = {k[:-4]: v[:-4] for k, v in rename_map.items()}

    # Rename PNGs
    for old, new in sorted(rename_map.items()):
        old_path = os.path.join(rsi_dir_path, old)
        new_path = os.path.join(rsi_dir_path, new)

        if not os.path.exists(old_path):
            print(f"  SKIP (missing src): {old}")
            continue
        if os.path.exists(new_path):
            print(f"  SKIP (target exists): {old} -> {new}")
            continue

        os.rename(old_path, new_path)
        print(f"  RENAMED: {old} -> {new}")

    # Update meta.json
    meta_path = os.path.join(rsi_dir_path, 'meta.json')
    if os.path.exists(meta_path):
        try:
            with open(meta_path, 'r', encoding='utf-8') as f:
                meta = json.load(f)

            changed = False
            for state in meta.get('states', []):
                old_state = state['name']
                if old_state in state_rename_map:
                    state['name'] = state_rename_map[old_state]
                    changed = True

            if changed:
                with open(meta_path, 'w', encoding='utf-8') as f:
                    json.dump(meta, f, indent=2)
                print(f"  Updated meta.json")
        except Exception as e:
            print(f"  ERROR updating meta.json: {e}")


def find_rsi_dirs(base_dir):
    rsi_dirs = []
    try:
        for entry in os.scandir(base_dir):
            if not entry.is_dir():
                continue
            if entry.name.endswith('.rsi'):
                rsi_dirs.append(entry.path)
            else:
                # One level of grouped subdirectories (ak/, modular/, os/)
                try:
                    for sub in os.scandir(entry.path):
                        if sub.is_dir() and sub.name.endswith('.rsi'):
                            rsi_dirs.append(sub.path)
                except Exception:
                    pass
    except Exception as e:
        print(f"ERROR scanning {base_dir}: {e}")
    return rsi_dirs


def main():
    rsi_dirs = find_rsi_dirs(ERISPORTED_DIR)
    print(f"Found {len(rsi_dirs)} .rsi directories\n")

    for rsi_dir in sorted(rsi_dirs):
        rel = os.path.relpath(rsi_dir, ERISPORTED_DIR)
        print(f"Processing: {rel}")
        process_rsi_dir(rsi_dir)

    print("\nDone!")


if __name__ == '__main__':
    main()
