"""Exercise print-origin follow-ups and reject defects against the exact reviewed output."""
from pathlib import Path
import tempfile
import unittest

import fitz

from print_origin_evidence import ROOT, checked_pdf, contracts, evidence, translation_errors
from libreoffice_regression import compare


class PrintOriginTests(unittest.TestCase):
    def test_all_bound_inputs_and_pdfs_keep_the_checked_translation(self):
        for key, record in contracts().items():
            with self.subTest(key=key):
                self.assertIsNotNone(checked_pdf(key, ROOT / record['historical_pdf']))

    def test_image_origin_contract_rejects_shift_missing_image_and_extra_ink(self):
        record = contracts()['full/image-anchor']
        source = checked_pdf('full/image-anchor', ROOT / record['historical_pdf'])
        for mutation in ('shift', 'missing-image', 'extra-ink'):
            with self.subTest(mutation=mutation), fitz.open(ROOT / record['historical_pdf']) as old, fitz.open(source) as actual:
                page = actual[0]
                if mutation == 'shift':
                    xref = page.get_contents()[0]
                    actual.update_stream(xref, b'1 0 0 1 1 0 cm\n' + actual.xref_stream(xref))
                elif mutation == 'missing-image':
                    xref = actual.get_new_xref()
                    actual.update_object(xref, '<<>>')
                    actual.update_stream(xref, b'')
                    page.set_contents(xref)
                else:
                    page.draw_rect(fitz.Rect(20, 20, 30, 30), fill=(0, 0, 0))
                self.assertFalse(evidence(old[0], page, 'image-anchor', 1)['passed'])

    def test_wrong_source_axis_shift_is_rejected(self):
        record = contracts()['full/image-anchor']
        with fitz.open(ROOT / record['historical_pdf']) as old, fitz.open(checked_pdf('full/image-anchor', ROOT / record['historical_pdf'])) as new:
            self.assertTrue(translation_errors(old[0], new[0], 0, 0))

    def test_corrected_border_reference_still_rejects_missing_text_and_extra_ink(self):
        for engine in ('main', 'core'):
            key = 'appearance/' + engine + '/09-cell-border'
            record = contracts()[key]
            expected = checked_pdf(key, ROOT / record['historical_pdf'])
            for mutation in ('missing-text', 'extra-ink'):
                with self.subTest(engine=engine, mutation=mutation), tempfile.TemporaryDirectory() as directory, fitz.open(expected) as document:
                    if mutation == 'missing-text':
                        page = document[0]
                        bounds = page.get_text('blocks')[0][:4]
                        page.add_redact_annot(fitz.Rect(bounds))
                        page.apply_redactions()
                    else:
                        document[0].draw_rect(fitz.Rect(20, 20, 30, 30), fill=(0, 0, 0))
                    output = Path(directory) / 'mutated.pdf'
                    document.save(output)
                    self.assertFalse(compare(expected, output, (144,))['passed'])


if __name__ == '__main__':
    unittest.main()
