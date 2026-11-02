"""Pure, fail-closed source policy shared by notebook and bounded tests.

These fields record evidence supplied by humans; their presence does not prove
legal permission. No default value grants permission.
"""
REQUIRED = ('description', 'license_or_terms', 'consent_basis',
            'retention_policy', 'responsible_person')
USES = {'research_training', 'evaluation_only', 'not_permitted'}

def source_issues(records, keys, purpose):
    if purpose not in {'training', 'evaluation'}:
        raise ValueError('Unknown source-policy purpose')
    result = []
    for key in sorted(set(keys)):
        rows = [r for r in records if r.get('source_key') == key]
        reasons = []
        if len(rows) != 1:
            reasons.append('missing or duplicate source record')
        else:
            row = rows[0]
            reasons += ['missing ' + f for f in REQUIRED if not str(row.get(f, '')).strip()]
            if row.get('contains_identifiable_faces') not in {'yes', 'no'}:
                reasons.append('identifiable-face status unresolved')
            use = row.get('permissible_use', '')
            allowed = {'research_training'} if purpose == 'training' else {'research_training', 'evaluation_only'}
            if use not in allowed:
                reasons.append('use not authorized for ' + purpose)
        if reasons:
            result.append({'source_key': key, 'purpose': purpose, 'reasons': '; '.join(reasons)})
    return result
