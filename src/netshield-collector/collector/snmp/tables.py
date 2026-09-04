"""Turning a walked subtree into rows.

An SNMP table arrives as a flat mapping of OID to value, where the OID is a column followed by
an index: ``1.3.6.1.2.1.2.2.1.2.7`` is ``ifDescr`` for ``ifIndex`` 7. Every table in this package
is read the same way, so the reassembly is written once here rather than three times.

Nothing in this module knows what any column means. That belongs to :mod:`collector.snmp.
interfaces` and :mod:`collector.snmp.fingerprint`, which ask for rows and then name the columns
they want.
"""

from __future__ import annotations

from collections.abc import Mapping


def rows(walked: Mapping[str, str], table: str) -> dict[str, dict[str, str]]:
    """Group ``walked`` into ``{index: {column OID: value}}`` for the columns under ``table``.

    The index is kept as the dotted string that followed the column, not parsed as a number: an
    ``entPhysicalIndex`` is one integer and an ``ipNetToMediaEntry`` index is five, and a table
    reader that assumed the first shape would silently mis-key the second.
    """
    prefix = f"{table}."
    grouped: dict[str, dict[str, str]] = {}

    for oid, value in walked.items():
        if not oid.startswith(prefix):
            continue

        remainder = oid[len(prefix) :]
        column, separator, index = remainder.partition(".")

        if not separator or not index:
            continue

        grouped.setdefault(index, {})[f"{table}.{column}"] = value

    return grouped


def text(row: Mapping[str, str], column: str) -> str | None:
    """One column as trimmed text, or nothing when it is absent or empty.

    An agent that answers with an empty string is saying it has no value, and storing one would
    put a blank where the API's "unknown" already reads correctly.
    """
    value = row.get(column)

    if value is None:
        return None

    stripped = value.strip()

    return stripped or None


def number(row: Mapping[str, str], column: str) -> int | None:
    """One column as an integer, or nothing when it is absent or not one.

    A value that will not parse is dropped rather than raising. One malformed counter on one
    interface should cost that field, not the walk.
    """
    value = text(row, column)

    if value is None:
        return None

    try:
        return int(value)
    except ValueError:
        return None
