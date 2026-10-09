"""
Fills every crate of a Main Base template that is for a real item, so a base is built with those crates completely full (when "Pre-fill the base" is ticked),
and empties every crate that is not for one.

Run: python tools/fill-crates.py <template.json> [more templates...]     (tools/capture-tier.py does this itself after a capture)

A crate (any Container*) counts as being for an item when (the item list is plugin 0.15's unlocks.json)
  1. it is stocked with exactly one kind of real item (the ore crates, whose labels may be misspelt: "Uranum"), or
  2. its label is a real item id, ignoring case ("Iron", "waterbottle1", "AstroFood").
Such a crate is stocked with that item up to its inventory size ("stock": {item: size}). Every other crate (Supplier, Misc, the egg and larvae crates, the mixed
Seeds crate, an unlabelled one) is emptied: it has no stock. A filter on its own does not make a crate one for an item.
"""
import json
import os
import sys

UNLOCKS = os.path.expandvars(r'%LOCALAPPDATA%\RRSOS-PCC-Live\unlocks.json')


def real_items():
    if not os.path.exists(UNLOCKS):
        sys.exit('unlocks.json is not there: start the game with plugin 0.15 or newer and load a world once')
    return {g['id'] for g in json.load(open(UNLOCKS, encoding='utf-8-sig'))['groups'] if g['kind'] == 'item'}


def item_for(o, items, by_lower):
    inv = o.get('inv') or {}
    stock = inv.get('stock') or {}
    if len(stock) == 1 and next(iter(stock)) in items:
        return next(iter(stock))
    return by_lower.get((o.get('text') or '').strip().lower())


def fill(doc, items):
    by_lower = {i.lower(): i for i in items}
    filled, kept, emptied = [], [], []
    for o in doc['objects']:
        if not o['g'].startswith('Container') or 'inv' not in o:
            continue
        item = item_for(o, items, by_lower)
        if item is None:
            if o['inv'].pop('stock', None) or o['inv'].pop('held', None):
                emptied.append(o)
            kept.append(o)
            continue
        o['inv']['stock'] = {item: o['inv']['size']}
        filled.append((o, item))
    return filled, kept, emptied


if __name__ == '__main__':
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    items = real_items()
    for path in sys.argv[1:]:
        doc = json.load(open(path, encoding='utf-8-sig'))
        filled, kept, emptied = fill(doc, items)
        json.dump(doc, open(path, 'w', encoding='utf-8', newline='\n'), separators=(',', ':'))
        print('%s: %d crates filled to their size, %d crates for no item left empty (%d of them emptied: %s)' % (
            os.path.basename(path), len(filled), len(kept), len(emptied), ', '.join(sorted({repr(o.get('text')) for o in emptied})) or 'none'))
        print('    empty crates:', ', '.join(sorted({repr(o.get('text')) for o in kept})))
