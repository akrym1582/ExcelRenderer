"""Validate native stream preservation and quantify metadata precision against frozen stage B."""
import hashlib
import io
import json
import re
from pathlib import Path
import fitz
from fontTools.ttLib import TTFont
from pdf_geometry_evidence import verify_outlines

ROOT = Path(__file__).resolve().parents[1]
NUMBER = re.compile(r'(?<![A-Za-z_])[-+]?(?:\d+\.?\d*|\.\d+)(?![A-Za-z_])')
REFERENCE = re.compile(r'(\d+)\s+\d+\s+R')


def verify_frozen_baseline():
    root = ROOT / 'tests/Shared/PdfRemovalBaseline'
    manifest = json.loads((root / 'manifest.json').read_text())
    for name, expected in manifest['files'].items():
        actual = hashlib.sha256((root / name).read_bytes()).hexdigest()
        if actual != expected:
            raise RuntimeError(f'Frozen baseline changed: {name}')
    return manifest


def stream_digest(document, references):
    return [hashlib.sha256(document.xref_stream(reference)).hexdigest() for reference in references]


def resource_digests(document, images_only=False):
    return sorted(hashlib.sha256(document.xref_stream_raw(xref)).hexdigest()
                  for xref in range(1, document.xref_length())
                  if document.xref_is_stream(xref) and ((not images_only and document.xref_get_key(xref, 'Length1')[0] != 'null')
                      or document.xref_get_key(xref, 'Subtype')[1] == '/Image'))


def drawing_content(document, page):
    # Skia may omit a clipped empty BT/ET scope when unused outlines are removed.
    # Remove only balanced scopes containing empty text and nonpainting state;
    # every actual text/path/image paint operation remains byte-exact.
    state = re.compile(rb'(?:BT|ET|(?:[-+\d.]+ ){5}[-+\d.]+ cm|(?:[-+\d.]+ ){3}[-+\d.]+ re|W\*? n|(?:[-+\d.]+ ){2}[-+\d.]+ RG (?:[-+\d.]+ ){2}[-+\d.]+ rg|/G\d+ gs)')
    scopes = [([], False)]
    removed = 0
    for line in b''.join(document.xref_stream(xref) for xref in page.get_contents()).splitlines(keepends=True):
        if line.strip() == b'q':
            scopes.append(([line], False))
        elif line.strip() == b'Q' and len(scopes) > 1:
            lines, empty_text = scopes.pop()
            body = b''.join(lines[1:])
            empty_text |= b'BT\nET\n' in body
            if empty_text and all(state.fullmatch(part.strip()) for part in lines[1:]):
                removed += 1
                scopes[-1] = (scopes[-1][0], True)
            else:
                scopes[-1][0].extend([*lines, line])
        else:
            scopes[-1][0].append(line)
    if len(scopes) != 1:
        raise ValueError('Unbalanced PDF graphics state scopes')
    return b''.join(scopes[0][0]), removed


def subset_descriptor(document, descriptor, data, name, changes, errors):
    font = TTFont(io.BytesIO(data))
    head = font['head']
    bounds = [(glyph.xMin, glyph.yMin, glyph.xMax, glyph.yMax)
              for glyph in (font['glyf'][name] for name in font.getGlyphOrder()) if glyph.numberOfContours != 0]
    union = ([min(row[0] for row in bounds), min(row[1] for row in bounds),
              max(row[2] for row in bounds), max(row[3] for row in bounds)] if bounds else [0, 0, 0, 0])
    if [head.xMin, head.yMin, head.xMax, head.yMax] != union:
        errors.append(f'{name}: embedded head bounds differ from retained glyph bounds')
    expected = [getattr(head, key) * 1000 / head.unitsPerEm for key in ('xMin', 'yMin', 'xMax', 'yMax')]
    actual = list(map(float, NUMBER.findall(document.xref_get_key(descriptor, 'FontBBox')[1])))
    if len(actual) != 4 or max(abs(a - b) for a, b in zip(actual, expected)) > .0005001:
        errors.append(f'{name}: descriptor FontBBox differs from embedded head bounds')
    # The retained cmap resolves Skia's i/I/!/1 stem probes to empty outlines
    # when any probe was unused. Its descriptor estimate must then be zero.
    cmap, glyf = font.getBestCmap(), font['glyf']
    empty_probe = any(glyf[cmap.get(ord(char), '.notdef')].numberOfContours == 0 for char in 'iI!1')
    if not empty_probe or document.xref_get_key(descriptor, 'StemV')[1] != '0':
        errors.append(f'{name}: changed StemV is not explained by an empty subset stem probe')
    changes.append({'font': name, 'headBounds': expected, 'stemV': 0, 'emptyStemProbe': empty_probe})


def metadata_difference(before, after, label, differences, errors):
    if before == after:
        return
    # This only compares known numeric font/graphics metadata arrays, never parses product PDF input.
    shape_before = NUMBER.sub('#', before)
    shape_after = NUMBER.sub('#', after)
    if shape_before != shape_after:
        errors.append(f'{label}: nonnumeric metadata changed')
        return
    a, b = list(map(float, NUMBER.findall(before))), list(map(float, NUMBER.findall(after)))
    if len(a) != len(b):
        errors.append(f'{label}: metadata array shape changed')
        return
    delta = max((abs(x - y) for x, y in zip(a, b)), default=0)
    if delta > .0005001:
        errors.append(f'{label}: beyond proven three-decimal serialization bound ({delta})')
    differences.append({'field': label, 'maxDelta': delta})


def evidence(directory):
    directory = Path(directory)
    errors, changes, subset_changes = [], [], []
    baseline = directory / 'baseline-skiasharp.pdf'
    raw_path = directory / 'skiasharp-raw.pdf'
    current = directory / 'skiasharp.pdf'
    if not baseline.exists() or not raw_path.exists():
        return {'passed': False, 'errors': ['Frozen B and raw C PDFs are required']}
    manifest = json.loads((ROOT / 'tests/Shared/PdfRemovalBaseline/manifest.json').read_text())
    expected_hash = manifest['files'][directory.name + '/skiasharp.pdf']
    if hashlib.sha256(baseline.read_bytes()).hexdigest() != expected_hash:
        errors.append('Frozen B PDF differs from its recorded hash')
    old, new, raw = fitz.open(baseline), fitz.open(current), fitz.open(raw_path)
    # A single-revision structural save replaces the original native-prefix contract.
    # Preserve every encoded stream at its original object identity, exactly once.
    raw_streams = {xref: hashlib.sha256(raw.xref_stream_raw(xref)).hexdigest()
                   for xref in range(1, raw.xref_length()) if raw.xref_is_stream(xref)}
    new_streams = {xref: hashlib.sha256(new.xref_stream_raw(xref)).hexdigest()
                   for xref in range(1, new.xref_length()) if new.xref_is_stream(xref)}
    if raw_streams != new_streams:
        errors.append('Structural save changed encoded streams or their object identities')
    final_bytes = current.read_bytes()
    payload_counts = []
    for xref in raw_streams:
        payload = raw.xref_stream_raw(xref)
        if len(payload) > 65536 and raw.xref_get_key(xref, 'Subtype')[1] == '/Image':
            count = final_bytes.count(payload)
            payload_counts.append({'xref': xref, 'sha256': raw_streams[xref], 'occurrences': count})
            if count != 1:
                errors.append(f'Image {xref}: encoded payload occurs {count} times in output bytes')
    if len(old) != len(new) or len(raw) != len(new):
        errors.append('Page count changed')
    content_hashes = []
    for index, (a, b, native) in enumerate(zip(old, new, raw), 1):
        x, y, z = (stream_digest(document, page.get_contents()) for document, page in ((old, a), (new, b), (raw, native)))
        content_hashes.append({'page': index, 'B': x, 'C': y, 'rawC': z})
        if x != y or y != z:
            normalized = [drawing_content(document, page) for document, page in ((old, a), (new, b), (raw, native))]
            if not normalized[0][0] == normalized[1][0] == normalized[2][0]:
                errors.append(f'Page {index}: native drawing content differs')
            content_hashes[-1]['emptyTextScopesRemoved'] = [row[1] for row in normalized]
        metadata_difference(str(list(a.mediabox)), str(list(b.mediabox)), f'page-{index}/MediaBox', changes, errors)
        # get_fonts exposes document resource names, independent of global font IDs.
        fonts_a = {font[4]: font for font in a.get_fonts(full=True)}
        fonts_b = {font[4]: font for font in b.get_fonts(full=True)}
        if fonts_a.keys() != fonts_b.keys():
            errors.append(f'Page {index}: font resources differ')
        for label, page in (('B', a), ('C', b)):
            outlines = verify_outlines(page)
            if not outlines['passed']:
                errors.extend(f'{label}: {error}' for error in outlines['errors'])
        for name in fonts_a.keys() & fonts_b.keys():
            left_xref, right_xref = fonts_a[name][0], fonts_b[name][0]
            before_font, after_font = old.extract_font(left_xref)[3], new.extract_font(right_xref)[3]
            changed_font = before_font != after_font
            if changed_font and (not before_font or not after_font):
                errors.append(f'{name}: embedded font missing or changed representation')
            descendants = []
            for document, xref in ((old, left_xref), (new, right_xref)):
                refs = REFERENCE.findall(document.xref_get_key(xref, 'DescendantFonts')[1])
                descendants.append(int(refs[0]) if refs else xref)
            for key in ('W', 'DW', 'FontMatrix', 'FontBBox'):
                metadata_difference(old.xref_get_key(descendants[0], key)[1], new.xref_get_key(descendants[1], key)[1], f'{name}/{key}', changes, errors)
            descriptor_refs = [REFERENCE.findall(document.xref_get_key(xref, 'FontDescriptor')[1])
                               for document, xref in ((old, descendants[0]), (new, descendants[1]))]
            if all(descriptor_refs):
                right_descriptor = int(descriptor_refs[1][0])
                if changed_font and after_font:
                    subset_descriptor(new, right_descriptor, after_font, name, subset_changes, errors)
                for key in ('FontBBox', 'Ascent', 'Descent', 'CapHeight', 'StemV'):
                    if changed_font and after_font and key in ('FontBBox', 'StemV'):
                        continue  # Independently verified against the selected subset above.
                    metadata_difference(old.xref_get_key(int(descriptor_refs[0][0]), key)[1], new.xref_get_key(int(descriptor_refs[1][0]), key)[1], f'{name}/descriptor/{key}', changes, errors)
        states = []
        for document, page in ((old, a), (new, b)):
            syntax = document.xref_get_key(page.xref, 'Resources/ExtGState')[1]
            states.append({name: int(reference) for name, reference in re.findall(r'/([^\s/<>]+)\s+(\d+)\s+\d+\s+R', syntax)})
        if states[0].keys() != states[1].keys():
            errors.append('Graphics state resources changed')
        for name in states[0].keys() & states[1].keys():
            for key in ('CA', 'ca', 'LW', 'ML'):
                metadata_difference(old.xref_get_key(states[0][name], key)[1], new.xref_get_key(states[1][name], key)[1], f'{name}/{key}', changes, errors)
    if resource_digests(raw) != resource_digests(new):
        errors.append('Structural correction changed encoded font or image streams')
    if resource_digests(old, images_only=True) != resource_digests(new, images_only=True):
        errors.append('Frozen B and C image resource streams differ')
    return {'passed': not errors, 'errors': errors, 'encodedStreamsPreserved': raw_streams == new_streams, 'imagePayloadOccurrences': payload_counts,
            'contentHashes': content_hashes, 'numericMetadataDifferences': changes, 'subsetMetadata': subset_changes,
            'maxMetadataDelta': max((row['maxDelta'] for row in changes), default=0)}
