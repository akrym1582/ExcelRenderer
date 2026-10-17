#!/usr/bin/env python3
"""Unit checks for provenance boundaries, typed generation and strict comparison."""
import argparse
import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
import oracle


class OracleToolTests(unittest.TestCase):
    def test_typed_inputs_preserve_numeric_text_and_empty_formulas(self):
        with tempfile.TemporaryDirectory() as tmp:
            args = argparse.Namespace(cases=oracle.FIXTURES/'cases.json', directory=Path(tmp))
            with contextlib.redirect_stdout(io.StringIO()):
                oracle.generate(args)
            with zipfile.ZipFile(Path(tmp)/'criteria-numeric-text-zero.xlsx') as book:
                xml = book.read('xl/worksheets/sheet1.xml').decode()
                self.assertIn('inlineStr', xml)
                self.assertIn('>2<', xml)
                self.assertNotIn('<v>0</v>', xml)
            with zipfile.ZipFile(Path(tmp)/'criteria-empty-formula-empty.xlsx') as book:
                xml = book.read('xl/worksheets/sheet1.xml').decode()
                self.assertIn('<f>""</f>', xml)
                self.assertNotIn('<v>', xml)

    def test_comparison_distinguishes_types_and_respects_case_tolerances(self):
        self.assertFalse(oracle.equal({'kind': 'Number', 'value': 0}, {'kind': 'Boolean', 'value': False}, {}))
        self.assertFalse(oracle.equal({'kind': 'Blank', 'value': None}, {'kind': 'Text', 'value': ''}, {}))
        self.assertFalse(oracle.equal({'kind': 'Number', 'value': 1.01}, {'kind': 'Number', 'value': 1}, {}))
        self.assertTrue(oracle.equal({'kind': 'Number', 'value': 1.001}, {'kind': 'Number', 'value': 1}, {'absTolerance': 0.002}))

    def test_strict_zero_observations_fails_and_never_reports_matches(self):
        with tempfile.TemporaryDirectory() as tmp:
            actual = Path(tmp)/'actual.json'
            actual.write_text('[]')
            args = argparse.Namespace(cases=oracle.FIXTURES/'cases.json', observed=oracle.FIXTURES/'excel-observed.json', actual=actual, exceptions=oracle.FIXTURES/'compatibility-exceptions.json', output=Path(tmp)/'report.json', strict=True, require_roundtrip=True)
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(1, oracle.compare(args))
            report = json.loads(args.output.read_text())
            self.assertEqual(0, report['excelObservedCases'])
            self.assertEqual(0, report['counts']['matched'])
            self.assertTrue(report['missingRequiredCategories'])

    def test_shared_string_cache_ignores_phonetic_and_xml_indentation(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp)/'synthetic-cache.xlsx'
            with zipfile.ZipFile(path, 'w') as archive:
                archive.writestr('xl/worksheets/sheet1.xml', f'<worksheet xmlns="{oracle.NS}"><sheetData><row><c r="Z1" t="s"><f>"日本語"</f><v>0</v></c></row></sheetData></worksheet>')
                archive.writestr('xl/sharedStrings.xml', f'<sst xmlns="{oracle.NS}"><si>\n<r><t>日本</t></r>\n<r><t>語</t></r><rPh sb="0" eb="2"><t>にほん</t></rPh>\n</si></sst>')
            self.assertEqual({'kind': 'Text', 'value': '日本語'}, oracle.cached(path, 'Z1'))

    def test_extract_rejects_non_excel_provenance(self):
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            (directory/'manifest.json').write_text(json.dumps({'source': 'profile', 'caseSha256': oracle.sha(oracle.FIXTURES/'cases.json')}))
            with self.assertRaisesRegex(ValueError, 'source or case hash'):
                oracle.extract(argparse.Namespace(cases=oracle.FIXTURES/'cases.json', directory=directory, output=directory/'observed.json'))


if __name__ == '__main__':
    unittest.main()
