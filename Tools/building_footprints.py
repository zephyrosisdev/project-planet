"""Stable construction/model dimensions in 8 metre cells, keyed by catalogue ID."""
FOOTPRINTS = {
    'command': (5,5), 'outpost': (2,2), 'barracks': (3,2),
    'light_depot': (3,3), 'factory': (4,4), 'ordnance': (4,3),
    'helipad': (3,3), 'airbase': (6,4), 'naval_yard': (5,4),
    'lab': (3,3), 'advanced_lab': (4,4), 'strategic_lab': (5,4),
    'logistics': (3,3), 'repair_depot': (3,3), 'field_command': (3,2),
    'radar_station': (2,2), 'phased_radar': (3,2), 'horizon_radar': (4,2),
    'counter_radar': (2,2), 'mine': (3,3), 'oil': (2,2), 'gas': (2,2),
    'rare': (3,3), 'isotope': (3,3), 'synthetic': (3,3),
    'material_storage': (3,2), 'fuel_storage': (2,2),
    'rare_storage': (2,2), 'isotope_storage': (2,2), 'wind': (2,2),
    'solar': (4,2), 'gas_turbine': (3,2), 'reactor': (4,4),
    'fusion': (5,5), 'exotic': (6,6), 'battery': (2,1),
    'wall': (1,1), 'gate': (3,1), 'autocannon': (1,1),
    'at_turret': (1,1), 'aa_turret': (1,1), 'sam': (2,1),
    'advanced_sam': (2,2), 'point_defence': (1,1),
    'artillery_battery': (3,2), 'missile_defence': (2,2),
    'laser': (1,1), 'shield': (3,3), 'silo': (4,4),
}

def apply_footprints(buildings):
    ids = {b['id'] for b in buildings}
    if ids != FOOTPRINTS.keys():
        raise ValueError(f'Footprint coverage mismatch: {ids ^ FOOTPRINTS.keys()}')
    for building in buildings:
        building['footprintWidth'], building['footprintDepth'] = FOOTPRINTS[building['id']]

if __name__ == '__main__':
    import json
    from pathlib import Path
    path = Path('Assets/MassiveArmy/Resources/MassiveArmy/RtsCatalog.json')
    catalogue = json.loads(path.read_text())
    apply_footprints(catalogue['buildings'])
    path.write_text(json.dumps(catalogue, indent=2)+'\n')
    print(f"Validated footprints for {len(catalogue['buildings'])} buildings")
