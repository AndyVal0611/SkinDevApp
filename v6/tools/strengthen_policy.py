"""Targeted source edits; no notebook execution or dataset writes."""
from pathlib import Path
root = Path(__file__).resolve().parents[1]
def edit(name, old, new):
    p = root / 'notebook_src' / name
    s = p.read_text(encoding='utf-8')
    assert old in s, (name, old[:60])
    p.write_text(s.replace(old,new), encoding='utf-8')

policy = (root / 'tools/source_policy.py').read_text()
runtime = '''
def require_source_permission(splits, purpose):
    # Reload at every boundary: stale caches/flags cannot grant permission.
    records = pd.read_csv(CFG.PROVENANCE_CSV, dtype=str).fillna("").to_dict("records")
    keys = DF.loc[DF.split.isin(splits), "source_key"].unique()
    issues = source_issues(records, keys, purpose)
    if issues:
        raise GateError(f"Source permission blocked {purpose} for {splits}: {issues}")
    return True

'''
edit('03_dataset_audit.cellsrc', 'PERMITTED_TRAIN =', policy + '\n' + runtime + 'PERMITTED_TRAIN =')
edit('03_dataset_audit.cellsrc', 'prov_ok = (not miss) and (not bad_use)',
     'prov_issues = source_issues(PROV.to_dict("records"), keys, "evaluation")\n'
     'prov_ok = (not miss) and (not bad_use) and not prov_issues')
edit('03_dataset_audit.cellsrc', 'prov_ok or not CFG.REQUIRE_PROVENANCE_FOR_TRAINING,', 'prov_ok,')
edit('03_dataset_audit.cellsrc', 'invalid permissible_use for {len(bad_use)}:',
     'invalid permissible_use for {len(bad_use)} / unresolved evidence for {len(prov_issues)}:')
edit('02_environment_security.cellsrc', 'def gate(name):\n', '''def gate(name):
    if name.startswith(("annotations_", "regions_")):
        split = name.split("_", 1)[1]
        require_source_permission([split], "training" if split in ("train", "fusion") else "evaluation")
''')
edit('07_roi_branch.cellsrc', 'def build_roi_table(split):\n',
     'def build_roi_table(split):\n    require_source_permission([split], "training" if split in ("train", "fusion") else "evaluation")\n')
edit('07_roi_branch.cellsrc', 'def cached_roi_sets(split, tiers=(1, 2, 3)):\n',
     'def cached_roi_sets(split, tiers=(1, 2, 3)):\n    require_source_permission([split], "training" if split in ("train", "fusion") else "evaluation")\n')
edit('08_fusion.cellsrc', 'if FUSION_STATE.get("oof"):\n',
     'if FUSION_STATE.get("oof"):\n    require_source_permission(["fusion"], "training")\n')
edit('10_evaluation.cellsrc', 'def open_test_once():\n',
     'def open_test_once():\n    require_source_permission(["test"], "evaluation")\n')
edit('06_yolo_three_detectors.cellsrc', 'def localisation_metrics(split_names, cond, only_deployed=False):\n',
     'def localisation_metrics(split_names, cond, only_deployed=False):\n    require_source_permission(split_names, "evaluation")\n')
print('Source-policy boundaries updated; no input records changed.')
