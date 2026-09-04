"""The collector's test suite.

A package rather than a bare directory so that ``tests.conftest`` is one module under one name —
mypy resolves a file found twice under two names as an error, and ``pytest`` imports a helper
from here by that path.
"""
