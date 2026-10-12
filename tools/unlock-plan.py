"""
Dry run of "build the main base as things unlock": reads a save, works out which groups are unlocked in it, and reports which pieces of the Full base
(src/Dashboard/Assets/main-base-template.json) would exist at that point. Writes nothing; the save is only read.

Run: python tools/unlock-plan.py [--save PATH] [--live] [--set Heat=12000000,Pressure=2500000] [--unlock Container3,Pod9xA] [--template FILE] [--ladder N]
  --save      the save to read (default: Standard-2.json in the game's save folder)
  --live      take the unlocked groups from the plugin's unlocks.json (what the game said when the world loaded) instead of working them out from the save
  --set       pretend these unit levels (Oxygen, Heat, Pressure, Plants, Insects, Animals, Purification; Terraformation and Biomass are derived unless given)
  --unlock    pretend these group ids are unlocked too (chips, messages), as if they were in the save's unlockedGroups
  --ladder N  how many not-yet-wanted kinds to list (default 25; 0 = all)

The rule (decompiled game, UnlockingInfos.GetIsUnlocked + UiWindowConstruction), see memory project-unlock-gated-base-plan:
  unlocked = the group's unit level on the start planet >= its value (a unit of Null never unlocks this way) OR its id is in the save's unlockedGroups.
Rules of this report (first version, to be argued about):
  - a piece is wanted when its own group is unlocked; the truck waits for the vehicle depot (it is made there)
  - a foundation is wanted when it is under a wanted piece (a pod or dome covers the 6 m cells under its footprint)
  - a sign is wanted when a wanted piece other than a foundation or sign stands within 8 m
"""
import json
import math
import os
import re
import sys
from collections import Counter, defaultdict

ARGS = sys.argv[1:]


def opt(name, default=None):
    return ARGS[ARGS.index(name) + 1] if name in ARGS else default


LOCAL = os.path.expandvars(r'%LOCALAPPDATA%\RRSOS-PCC-Live')
SAVE = opt('--save', os.path.expandvars(r'%USERPROFILE%\AppData\LocalLow\MijuGames\Planet Crafter\Standard-2.json'))
TEMPLATE = opt('--template', os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'Dashboard', 'Assets', 'main-base-template.json'))
LADDER = int(opt('--ladder', 25))
LIVE = '--live' in ARGS
SETS = dict(kv.split('=') for kv in opt('--set', '').split(',') if kv)
EXTRA = [g for g in opt('--unlock', '').split(',') if g]

CELL = 6.0  # the foundation grid
SIGN_REACH = 8.0
GATE_ON = {'VehicleTruck': 'VehicleCrafter1'}  # made in a building, not built on its own
FLAG_GROUPS = {  # the new-game switches, as the game turns them into unlocked groups (GameSettingsHandler.AddUnlockedGroups)
    'unlockedOreExtrators': ['OreExtractor1', 'OreExtractor2', 'OreExtractor3'],
    'unlockedDrones': ['DroneStation1'],
    'unlockedAutocrafter': ['AutoCrafter1'],
    'unlockedTeleporters': ['Teleporter1'],
    'unlockedSpaceTrading': ['TradePlatform1'],
}
# Metres, along x and z before any turn: measured in the game (the dashboard's BaseBuildingMainBase.razor PadSizes).
PAD = {
    'pod': (8.5, 8.5), 'podAngle': (8.3, 8.3), 'Pod4x': (17, 16.5), 'Pod9xA': (24, 24), 'Pod9xB': (24, 24), 'Pod9xC': (24.5, 24.5),
    'Biolab': (16.5, 16.5), 'biodome': (24, 20), 'Biodome2': (32, 27), 'ButterflyDome1': (32, 27), 'Aquarium2': (32, 26),
    'DeparturePlatform': (36, 24), 'TradePlatform1': (12, 12), 'InterplanetaryExchangePlatform1': (12, 12), 'PortalGenerator1': (12, 12),
    'PlanetaryDeliveryDepot1': (6, 6),
}
# Platforms with a measured deck: tile centres along, then across, in the piece's own frame.
DECK = {
    'LaunchPlatform': ([-18, -12, -6, 0, 6, 12, 18, 24], [-25, -19, -13, -7]),
    'VehicleCrafter1': ([-8.95, -2.95, 3.05], [-7.2, -1.2, 4.8]),
}


def yaw_of(rot):
    x, y, z, w = [float(v) for v in rot.split(',')]
    return math.degrees(2 * math.atan2(y, w))


def cell_of(v):
    return int(round(v / CELL))


# ---- the save ---------------------------------------------------------------------------------------------------------------
text = open(SAVE, encoding='utf-8-sig').read()
head = json.loads(re.search(r'\{"terraTokens".*?\}', text).group(0))
settings = json.loads(re.search(r'\{"saveDisplayName".*?\}', text).group(0))
planet = json.loads(re.search(r'\{"planetId":"[^"]*","unitOxygenLevel".*?\}', text).group(0))
lv = {
    'Oxygen': planet['unitOxygenLevel'], 'Heat': planet['unitHeatLevel'], 'Pressure': planet['unitPressureLevel'],
    'Plants': planet['unitPlantsLevel'], 'Insects': planet['unitInsectsLevel'], 'Animals': planet['unitAnimalsLevel'],
    'Purification': max(planet['unitPurificationLevel'], 0.0),
}
for k, v in SETS.items():
    lv[k] = float(v)
lv.setdefault('Biomass', lv['Plants'] + lv['Insects'] + lv['Animals'])
lv.setdefault('Terraformation', sum(lv[k] for k in ('Oxygen', 'Heat', 'Pressure', 'Plants', 'Insects', 'Animals', 'Purification')))
lv.setdefault('SystemTerraformation', lv['Terraformation'])  # one planet known here; the other planets would add to it

unlocks = json.load(open(os.path.join(LOCAL, 'unlocks.json'), encoding='utf-8-sig'))['groups']
info = {g['id']: g for g in unlocks}
bp = json.load(open(os.path.join(LOCAL, 'blueprints.json'), encoding='utf-8-sig'))
tier_of = {g: i for i, t in enumerate(bp['tiers'], 1) for g in t}
item_of = {r['group']: r['item'] for r in bp.get('itemUnlocks', [])}
story_of = {r['group']: r['event'] for r in bp.get('storyUnlocks', [])}

explicit = {g for g in head['unlockedGroups'].split(',') if g} | set(EXTRA)
for flag, ids in FLAG_GROUPS.items():
    if settings.get(flag):
        explicit |= set(ids)


def by_level(g):
    return g['unit'] != 'Null' and not g['planets'] and g['value'] <= lv.get(g['unit'], 0.0)


if LIVE:
    unlocked = {g['id'] for g in unlocks if g['unlockedNow']}
else:
    unlocked = {g['id'] for g in unlocks if by_level(g) or g['id'] in explicit}
if settings.get('unlockedEverything'):
    unlocked = set(info)

print('Save: %s (%s, planet %s)%s' % (os.path.basename(SAVE), settings['saveDisplayName'], planet['planetId'], '  [unlocked groups taken from the live unlocks.json]' if LIVE else ''))
print('Levels: ' + ', '.join('%s %s' % (k, '{:,.0f}'.format(lv[k])) for k in ('Terraformation', 'Oxygen', 'Heat', 'Pressure', 'Biomass', 'Plants', 'Insects', 'Animals', 'Purification')))
print('Unlocked groups: %d (explicit in the save: %s)' % (len(unlocked), ','.join(sorted(explicit)) or 'none'))
if not LIVE and not SETS and not EXTRA:
    live = {g['id'] for g in unlocks if g['unlockedNow']}
    if live != unlocked:
        print('Differs from the live unlocks.json (which is of whatever world was loaded last): only in live: %s; only here: %s' % (sorted(live - unlocked)[:12], sorted(unlocked - live)[:12]))
    else:
        print('Matches the live unlocks.json exactly (%d groups).' % len(live))

# ---- the plan ---------------------------------------------------------------------------------------------------------------
tpl = json.load(open(TEMPLATE, encoding='utf-8-sig'))
objs = tpl['objects']
for o in objs:
    o['i'] = None
total_by_kind = Counter(o['g'] for o in objs)


def footprint_cells(o):
    """The foundation cells (as (cx, cz)) a piece stands on."""
    g = o['g']
    if g in DECK:
        yaw = math.radians(yaw_of(o['rot']))
        cs, sn = round(math.cos(yaw)), round(math.sin(yaw))
        along, across = DECK[g]
        return {(cell_of(o['dx'] + a * cs + c * sn), cell_of(o['dz'] - a * sn + c * cs)) for a in along for c in across}
    if g in PAD:
        w, d = PAD[g]
        if abs(abs(yaw_of(o['rot'])) % 180 - 90) < 1:
            w, d = d, w
        x0, x1, z0, z1 = o['dx'] - w / 2, o['dx'] + w / 2, o['dz'] - d / 2, o['dz'] + d / 2
        cells = set()
        for cx in range(cell_of(x0) - 1, cell_of(x1) + 2):
            for cz in range(cell_of(z0) - 1, cell_of(z1) + 2):
                if x0 < cx * CELL + CELL / 2 - 0.5 and x1 > cx * CELL - CELL / 2 + 0.5 and z0 < cz * CELL + CELL / 2 - 0.5 and z1 > cz * CELL - CELL / 2 + 0.5:
                    cells.add((cx, cz))
        return cells
    return {(cell_of(o['dx']), cell_of(o['dz']))}


def plan(unlocked_set):
    """-> (wanted pieces list, foundations kept as a set of cells)"""
    def own_ok(g):
        return g in unlocked_set and (g not in GATE_ON or GATE_ON[g] in unlocked_set)

    solid = [o for o in objs if o['g'] not in ('Foundation', 'FoundationAngle', 'Sign') and own_ok(o['g'])]
    support = set()
    for o in solid:
        support |= footprint_cells(o)
    wanted = list(solid)
    for o in objs:
        if o['g'] == 'Foundation' and own_ok('Foundation') and (cell_of(o['dx']), cell_of(o['dz'])) in support:
            wanted.append(o)
        elif o['g'] == 'FoundationAngle' and own_ok('FoundationAngle'):
            wanted.append(o)
        elif o['g'] == 'Sign':
            if any(abs(s['dx'] - o['dx']) <= SIGN_REACH and abs(s['dz'] - o['dz']) <= SIGN_REACH for s in solid):
                wanted.append(o)
    return wanted


def gate_text(g):
    if g in GATE_ON:
        return 'needs ' + GATE_ON[g] + ' (made there)'
    i = info[g]
    parts = []
    if i['unit'] != 'Null':
        parts.append('%s >= %s' % (i['unit'], '{:,.0f}'.format(i['value'])))
    if g in tier_of:
        parts.append('blueprint chip, tier %d' % tier_of[g])
    if g in bp['loot']:
        parts.append('loot chip (deconstruct a found one)')
    if g in item_of:
        parts.append('chip item ' + item_of[g])
    if g in story_of:
        parts.append('story ' + story_of[g])
    if g in bp['messages']:
        parts.append('message')
    return ' or '.join(parts) or '?'


wanted = plan(unlocked)
w_by_kind = Counter(o['g'] for o in wanted)
print()
print('Would exist: %d of %d pieces (%d of %d kinds)' % (len(wanted), len(objs), len(w_by_kind), len(total_by_kind)))
print('  foundations %d of %d, signs %d of %d, everything else %d of %d' % (
    w_by_kind['Foundation'], total_by_kind['Foundation'], w_by_kind['Sign'], total_by_kind['Sign'],
    len(wanted) - w_by_kind['Foundation'] - w_by_kind['Sign'], len(objs) - total_by_kind['Foundation'] - total_by_kind['Sign']))
print()
print('Wanted kinds:')
for g, n in sorted(w_by_kind.items(), key=lambda kv: (-kv[1], kv[0])):
    print('  %-26s %4d of %-4d' % (g, n, total_by_kind[g]))

# What each missing kind would add on its own (its pieces, plus the foundations it newly needs).
rows = []
for g in total_by_kind:
    if g in ('Foundation', 'FoundationAngle', 'Sign') or w_by_kind[g] == total_by_kind[g]:
        continue
    if g in GATE_ON and GATE_ON[g] not in unlocked:
        add = plan(unlocked | {GATE_ON[g], g}) if g in info else wanted
    else:
        add = plan(unlocked | {g})
    gain = Counter(o['g'] for o in add) - w_by_kind
    i = info[g]
    key = (0, i['unit'], i['value']) if i['unit'] != 'Null' else (1, '', 0)
    rows.append((key, g, total_by_kind[g] - w_by_kind[g], gain['Foundation'], gain['Sign'], gate_text(g)))
rows.sort(key=lambda r: (r[0][0], r[0][1], r[0][2], r[1]))
print()
print('Not wanted yet (%d kinds), by unit and value; "+found" = foundations it would newly need, "+signs" = signs that would turn up with it:' % len(rows))
shown = rows if LADDER == 0 else rows[:LADDER]
for _, g, n, f, s, why in shown:
    print('  %-26s %3d pcs  +found %-3d +signs %-2d  %s' % (g, n, f, s, why))
if len(shown) < len(rows):
    print('  ... %d more (--ladder 0 for all)' % (len(rows) - len(shown)))
