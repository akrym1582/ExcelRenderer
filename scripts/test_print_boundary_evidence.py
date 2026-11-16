"""Check exact boundary follow-ups and reject shifted clips, lost text and added ink."""
from copy import deepcopy
from pathlib import Path
import tempfile
import unittest

import fitz

from print_boundary_evidence import ROOT, checked_pdf, contracts, unclipped_commands
from libreoffice_regression import compare


class PrintBoundaryTests(unittest.TestCase):
    def historical(self, key):
        parts = key.split('/')
        return (ROOT / 'tests/Shared/PdfRemovalBaseline' / parts[1] / 'skiasharp.pdf' if parts[0] == 'full' else
                ROOT / 'tests/Shared/LibreOfficeComparisonBaseline' / parts[1] / (parts[2] + '.pdf'))

    def test_all_references_preserve_geometry_and_only_add_the_recorded_clips(self):
        for key in contracts():
            with self.subTest(key=key):
                self.assertIsNotNone(checked_pdf(key, self.historical(key)))

    def test_wrong_clip_or_offset_is_rejected(self):
        import json
        row = contracts()['full/rich-theme']['pages']['1']
        commands = row['commands']
        index = next(i for i,c in enumerate(commands) if c.startswith('DrawViewportCommand'))
        original = json.loads(commands[index][len('DrawViewportCommand'):])
        for key in ('offset', 'width'):
            changed = deepcopy(original)
            if key == 'offset':
                changed['OffsetX'] = 1
            else:
                changed['Clip']['Width'] += 1
            lines = list(commands)
            lines[index] = 'DrawViewportCommand' + json.dumps(changed)
            with self.subTest(key=key), self.assertRaises(ValueError):
                unclipped_commands(lines, [row['clip']])

    def test_missing_visible_text_and_added_ink_are_rejected(self):
        key = 'full/rich-theme'
        expected = checked_pdf(key, self.historical(key))
        for mutation in ('missing-text', 'extra-ink'):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory, fitz.open(expected) as document:
                if mutation == 'missing-text':
                    document[0].add_redact_annot(fitz.Rect(document[0].get_text('blocks')[0][:4]))
                    document[0].apply_redactions()
                else:
                    document[0].draw_rect(fitz.Rect(20,20,30,30), fill=(0,0,0))
                target = Path(directory) / 'bad.pdf'
                document.save(target)
                self.assertFalse(compare(expected, target, (144,))['passed'])


if __name__ == '__main__':
    unittest.main()
