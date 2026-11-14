#!/usr/bin/env python3
"""Keep round-level ratios and structural checks alongside every raw sample."""
import hashlib
import json
import pathlib
import statistics

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUTPUT = ROOT/'docs/validation/core-performance-optimization'

def read_lines(path):
    return [json.loads(line) for line in path.read_text().splitlines()]

def aggregate(records, product):
    result = []
    for record in records:
        samples = record['samples'][1:]
        keys = ['Milliseconds', 'AllocatedBytes', 'PeakRss'] if product == 'Core' else ['TotalMs', 'AllocatedBytes', 'PeakWorkingSet64']
        result.append({key: record[key] for key in ['round', 'case', 'phase', 'format'] if key in record} | {
            'product': product, 'cold': record['samples'][0],
            'warm_medians': {key: statistics.median(sample[key] for sample in samples) for key in keys}})
    return result

def ratios(records, before, after):
    result = []
    for old in records:
        if old['phase'] != before:
            continue
        new = next(entry for entry in records if entry['phase'] == after and entry['round'] == old['round']
                   and entry['case'] == old['case'] and entry.get('format') == old.get('format'))
        values = {key: new['warm_medians'][key]/value for key, value in old['warm_medians'].items()}
        result.append({key: old[key] for key in ['product', 'round', 'case', 'format'] if key in old} | {
            'ratios': values, 'over_five_percent': [key for key, value in values.items() if key != 'AllocatedBytes' and value > 1.05]})
    return result

primary_core = aggregate(read_lines(OUTPUT/'samples.jsonl'), 'Core')
primary_main = aggregate(json.loads((OUTPUT/'main-samples.json').read_text()), 'main')
controlled_core = aggregate(read_lines(OUTPUT/'controlled-samples.jsonl'), 'Core')
rechecks = read_lines(OUTPUT/'regression-rechecks.jsonl')
controlled_core += aggregate([record for record in rechecks if record['product'] == 'Core'], 'Core')
controlled_main = aggregate([record for record in rechecks if record['product'] == 'main'], 'main')
report = {
    'primary_core': primary_core, 'primary_main': primary_main,
    'primary_ratios': ratios(primary_core, 'baseline', 'phase5') + ratios(primary_main, 'baseline-main', 'final-main'),
    'controlled_core': controlled_core, 'controlled_main': controlled_main,
    'controlled_ratios': ratios(controlled_core, 'baseline', 'phase5') + ratios(controlled_main, 'baseline-main', 'final-main'),
    'interpretation': 'Ratios are after/before per round; values above 1.05 require investigation. Controlled runs change tiering and warm count, and do not replace the primary protocol. Small-case time/RSS non-regression within 5% is not established in this environment.',
}
(OUTPUT/'analysis.json').write_text(json.dumps(report, indent=2)+'\n')

diagnostics = json.loads((OUTPUT/'diagnostics.json').read_text())
baselines = {row['case']: row for row in json.loads((OUTPUT/'baseline-observations.json').read_text())['records']}
proof = []
for case in dict.fromkeys(entry['case'] for entry in diagnostics):
    old = next(entry for entry in diagnostics if entry['case'] == case and entry['phase'] == 'baseline')['sample']
    new = next(entry for entry in diagnostics if entry['case'] == case and entry['phase'] == 'phase5')['sample']
    before, after = old['Metrics'], new['Metrics']
    observed = baselines[case]['sample']['Metrics']
    assert baselines[case]['output_verification']['pixel_text_coordinates_and_subsets_equal'], case
    assert observed.get('positionArrayElements', 0) == after.get('pdfPositionsWritten', 0), case
    assert observed.get('pdfFontRunsCollected', 0) == after.get('pdfFontRunsCollected', 0), case
    assert observed.get('paintCreated', 0) >= after.get('paintCreated', 0), case
    assert before.get('pdfFontRunsCollected', 0) == after.get('pdfFontRunsCollected', 0), case
    assert before['coreCommandBuild'] == 2*new['Pages'], case
    assert after['coreCommandBuild'] == new['Pages'], case
    # Historical reports stored the peak under pagePayloadActive. New reports separate current and peak.
    maximum_payload = after.get('pagePayloadMax', after['pagePayloadActive'])
    assert maximum_payload == 1, case
    if 'pagePayloadMax' in after:
        assert after['pagePayloadActive'] == 0, case
    assert after.get('layoutCacheEntries', 0) <= 512, case
    assert after.get('layoutCacheRetainedUnits', 0) <= 131072, case
    assert after.get('pageCellIndexBuild', 0) == after.get('pageCellDeduplicateSort', 0) == 0, case
    assert after.get('paintCreated', 0) <= 2, case
    for name in ['paint', 'pathEffect', 'typeface', 'pdfFont', 'pdfSubsetTypeface']:
        assert after.get(name+'Created', 0) == after.get(name+'Disposed', 0), (case, name)
    proof.append({'case': case, 'font_runs_collected_equal': True, 'full_command_builds_before': before['coreCommandBuild'],
                  'full_command_builds_after': after['coreCommandBuild'], 'maximum_active_payload': maximum_payload,
                  'border_candidates_before': before.get('borderCandidates', 0), 'border_candidates_after': after.get('borderCandidates', 0),
                  'paint_created': after.get('paintCreated', 0), 'native_counts_balanced': True,
                  'cache_entries_max': after.get('layoutCacheEntries', 0), 'cache_retained_units_max': after.get('layoutCacheRetainedUnits', 0),
                  'baseline_glyph_calls': observed.get('textGlyphCalls', 0), 'final_glyph_calls': after.get('textGlyphCalls', 0),
                  'baseline_glyph_characters': observed.get('textGlyphCharacters', 0), 'final_glyph_characters': after.get('textGlyphCharacters', 0),
                  'baseline_paint_created': observed.get('paintCreated', 0),
                  'baseline_position_arrays': observed.get('positionArrayCreated', 0), 'final_position_arrays': 0,
                  'position_element_count_equal': True, 'baseline_page_cell_indexes': observed.get('pageCellIndexBuild', 0),
                  'baseline_page_cell_sorts': observed.get('pageCellDeduplicateSort', 0),
                  'baseline_collection_nontext_commands': observed.get('collectionNonTextCommands', 0),
                  'baseline_collection_fill_commands': observed.get('collectionFillCommands', 0),
                  'baseline_collection_image_commands': observed.get('collectionImageCommands', 0),
                  'final_collection_nontext_commands': 0})
(OUTPUT/'structural-validation.json').write_text(json.dumps(proof, indent=2)+'\n')
print('All structural assertions passed for', len(proof), 'cases')

# The manifest excludes itself; refresh it after retaining any additional logs/evidence.
hashes = {str(path.relative_to(OUTPUT)): hashlib.sha256(path.read_bytes()).hexdigest()
          for path in sorted(OUTPUT.rglob('*')) if path.is_file() and path.name != 'artifact-hashes.json'}
(OUTPUT/'artifact-hashes.json').write_text(json.dumps(hashes, indent=2)+'\n')
