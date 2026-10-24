"""Synthetic PDFs test the tooling; they are never committed as Excel references."""
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

import fitz
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import excel_reference as subject


class ExcelReferenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.case = {'id': 'sample', 'input_sha256': 'a' * 64, 'selected_sheets': ['Sheet1']}
        self.pdf = self.root / 'sample.pdf'
        self.fixture(self.pdf)

    def fixture(self, path, text='Sentinel', shift=0, line=True, shape=True, image=True,
                extra=False, width=160, producer='Microsoft Excel', recolor=False, font='helv'):
        with fitz.open() as doc:
            page = doc.new_page(width=width, height=160)
            page.insert_text((20 + shift, 35), text, fontname=font)
            if line:
                page.draw_line((20, 55), (130, 55), width=1, color=(1, 0, 0) if recolor else (0, 0, 0))
            if shape:
                page.draw_circle((55, 90), 10, fill=(0, 1, 0))
            if image:
                import io
                stream = io.BytesIO()
                Image.new('RGB', (10, 10), 'blue').save(stream, format='PNG')
                page.insert_image(fitz.Rect(100, 90, 120, 110), stream=stream.getvalue())
            if extra:
                doc.new_page(width=160, height=160)
            doc.set_metadata({'creator': producer})
            doc.save(path)

    def evidence(self):
        return {'reference_kind': subject.KIND, 'case_id': 'sample', 'input_sha256': 'a' * 64,
                'pdf_sha256': subject.sha(self.pdf), 'selected_sheets': ['Sheet1'],
                'export_method': 'ExportAsFixedFormat', 'quality': 'standard', 'ignore_print_areas': False,
                'calculation': 'manual-no-recalculate', 'excel_version': '16.0', 'excel_build': '123',
                'windows_version': 'Windows test fixture', 'exported_at_utc': '2026-10-24T00:00:00Z',
                'operator': 'synthetic-test-only', 'active_printer': 'test',
                'font_files': [{'name': 'test.ttf', 'sha256': 'b' * 64}]}

    def test_catalog_is_exactly_the_existing_22_inputs(self):
        self.assertEqual(len(subject.cases()), 22)

    def test_identical_appearance_and_reencoded_metadata_match(self):
        other = self.root / 'metadata.pdf'
        with fitz.open(self.pdf) as doc:
            doc.set_metadata({'producer': 'different implementation', 'title': 'different metadata'})
            doc.save(other, deflate=True, garbage=4)
        result = subject.compare_pdf(self.pdf, other, self.root / 'diff')
        self.assertEqual(result['status'], 'matched')
        self.assertNotEqual(result['expected_sha256'], result['actual_sha256'])

    def test_mutations_retain_visible_differences_and_component_evidence(self):
        for name, options, component in [
            ('text', {'text': 'Missing'}, 'characters'),
            ('position', {'shift': 3}, 'characters'),
            ('subpixel-position', {'shift': .05}, 'characters'),
            ('border', {'line': False}, 'border_strokes'),
            ('border-color', {'recolor': True}, 'border_strokes'),
            ('shape', {'shape': False}, 'other_paths'),
            ('image', {'image': False}, 'images'),
            ('paper', {'width': 170}, 'size'),
            ('font', {'font': 'cour'}, 'fonts'),
            ('extra-page', {'extra': True}, None),
        ]:
            with self.subTest(name=name):
                path = self.root / (name + '.pdf')
                self.fixture(path, **options)
                result = subject.compare_pdf(self.pdf, path, self.root / name)
                self.assertEqual(result['status'], 'review-required')
                if component:
                    self.assertFalse(result['pages'][0]['component_equal'][component])
                else:
                    self.assertFalse(result['page_count_equal'])
                    self.assertEqual(len(result['pages']), 2)
                self.assertTrue((self.root / name / 'page-001-144dpi/difference.png').exists())
                self.assertEqual(len(result['pages'][0]['rasters']), 2)
        self.assertTrue(result['pages'][1]['rasters'])

    def test_registration_rejects_wrong_source_and_unattested_pdf(self):
        evidence = self.evidence()
        subject.validate_reference(self.case, self.pdf, evidence)
        for key, value in [('reference_kind', 'libreoffice-provisional-not-excel'),
                           ('input_sha256', 'bad'), ('selected_sheets', ['wrong']), ('pdf_sha256', 'bad'),
                           ('font_files', []), ('excel_build', ''), ('calculation', 'recalculate')]:
            with self.subTest(key=key):
                bad = dict(evidence, **{key: value})
                with self.assertRaises(ValueError):
                    subject.validate_reference(self.case, self.pdf, bad)
        for name in ['LibreOffice', 'Skia/PDF', 'PDFsharp', 'Microsoft Excel LibreOffice']:
            path = self.root / (name.replace('/', '-') + '.pdf')
            self.fixture(path, producer=name)
            with self.assertRaises(ValueError):
                subject.validate_reference(self.case, path, dict(evidence, pdf_sha256=subject.sha(path)))

    def test_registration_roundtrip_tampering_and_no_silent_replacement(self):
        bundle = self.root / 'bundle'
        bundle.mkdir()
        import shutil
        shutil.copyfile(self.pdf, bundle / 'sample.pdf')
        subject.save(bundle / 'sample.json', self.evidence())
        reference = self.root / 'reference'
        with patch.object(subject, 'cases', return_value=[self.case]):
            subject.register(bundle, reference)
            with self.assertRaises(ValueError):
                subject.register(bundle, reference)
        entry = subject.read(reference / 'manifest.json')['references']['sample']
        registered = subject.reference_pdf(self.case, reference, entry)
        registered.write_bytes(registered.read_bytes() + b'changed')
        with self.assertRaises(ValueError):
            subject.reference_pdf(self.case, reference, entry)

    def test_missing_reference_is_skipped_and_never_a_pass(self):
        reference = self.root / 'reference'
        subject.save(reference / 'manifest.json', {'schema_version': 1, 'reference_kind': subject.KIND, 'references': {}})
        result = subject.compare(self.root / 'run', reference, [self.case])
        self.assertEqual(result['summary'], {'unverified': 2})
        self.assertFalse(result['fidelity_passed'])
        self.assertFalse(result['coverage_complete'])
        self.assertEqual((self.root / 'run/junit.xml').read_text().count('<skipped'), 2)

    def test_registered_missing_pdf_is_error_not_unverified(self):
        reference = self.root / 'reference'
        subject.save(reference / 'manifest.json', {'schema_version': 1, 'reference_kind': subject.KIND,
                     'references': {'sample': {'pdf': 'absent.pdf', 'evidence': 'absent.json'}}})
        result = subject.compare(self.root / 'run', reference, [self.case])
        self.assertEqual(result['summary'], {'error': 2})

    def test_full_registered_comparison_and_actual_hash_validation(self):
        reference = self.root / 'reference'
        output = self.root / 'run'
        import shutil
        subject.save(self.root / 'sample.json', self.evidence())
        with patch.object(subject, 'cases', return_value=[self.case]):
            subject.register(self.root, reference)
        for engine in ('main', 'core'):
            pdf = output / 'actual' / engine / 'sample.pdf'
            pdf.parent.mkdir(parents=True)
            shutil.copyfile(self.pdf, pdf)
            subject.save(pdf.with_suffix('.run.json'), {
                'status': 'generated', 'case_id': 'sample', 'engine': engine,
                'input_sha256': self.case['input_sha256'], 'selected_sheets': ['Sheet1'],
                'pdf_sha256': subject.sha(pdf)})
        result = subject.compare(output, reference, [self.case])
        self.assertEqual(result['summary'], {'matched': 2})
        self.assertTrue(result['fidelity_passed'])
        pdf.write_bytes(pdf.read_bytes() + b'changed')
        result = subject.compare(output, reference, [self.case])
        self.assertEqual(result['summary'], {'matched': 1, 'error': 1})
        self.assertFalse(result['fidelity_passed'])

    def test_missing_reference_exit_policy(self):
        reference = self.root / 'reference'
        subject.save(reference / 'manifest.json', {'schema_version': 1,
                     'reference_kind': subject.KIND, 'references': {}})
        args = ['excel_reference.py', 'run', '--no-generate', '--reference', str(reference),
                '--output', str(self.root / 'run')]
        with patch.object(subject, 'cases', return_value=[self.case]):
            with patch.object(sys, 'argv', args):
                self.assertEqual(subject.main(), 0)
            with patch.object(sys, 'argv', args + ['--require-all-references']):
                self.assertEqual(subject.main(), 1)

    def test_path_traversal_and_foreign_registry_rejected(self):
        with self.assertRaises(ValueError):
            subject.inside(self.root, '../outside.pdf')
        with self.assertRaises(ValueError):
            subject.validate_registry({'schema_version': 1, 'reference_kind': 'libreoffice', 'references': {}}, {})


if __name__ == '__main__':
    unittest.main()
