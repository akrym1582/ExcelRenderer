#!/usr/bin/env python3
"""Compatibility entry point for the current four-package consumer validation."""
import runpy
from pathlib import Path

runpy.run_path(str(Path(__file__).with_name('validate-formulas-packages.py')), run_name='__main__')
