"""
Test package bootstrap.

Keeps CI output readable: project modules emit a lot of INFO lines
(``SnapshotCache initialized``, ``Initialized PropagandaDetector``, etc.)
that are useful in prod but wall-of-text noise in a 100+ test run. We raise
the root logger to WARNING here so only real warnings and errors surface
during tests; individual tests that assert on log output can opt back in
by resetting the level in setUp.
"""

import logging
import os

# Respect an explicit CIVIC_TEST_LOG_LEVEL override for debugging.
_level_name = os.environ.get("CIVIC_TEST_LOG_LEVEL", "WARNING").upper()
logging.basicConfig(level=getattr(logging, _level_name, logging.WARNING), force=True)
logging.getLogger().setLevel(getattr(logging, _level_name, logging.WARNING))
