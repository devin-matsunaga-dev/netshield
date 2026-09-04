"""The protocol layer: value decoding, subtree walking, and what a credential turns into.

``PySnmpSession`` is the one module in NetShield that touches a socket and an untyped library,
so what is testable without a device is tested here and the rest is proved through the fixture
session everything above it uses.
"""

from __future__ import annotations

import ast
import pathlib

import pytest
from pysnmp.proto.rfc1902 import Integer, IpAddress, OctetString, TimeTicks

from collector.models import CredentialKind
from collector.snmp import oids
from collector.snmp.session import (
    FixtureSession,
    SnmpError,
    _auth_data,
    _is_ipv6,
    decode,
)
from tests.conftest import snmp_credential, walk_fixture

COLLECTOR = pathlib.Path(__file__).resolve().parents[1] / "collector"


READ_PRIMITIVES = frozenset({"get_cmd", "bulk_cmd"})
"""The only two pysnmp commands NetShield may issue: read one object, read a page of a subtree."""


def _imported_from_pysnmp(source: str) -> set[str]:
    """Every name this module imports from ``pysnmp``, by its bare spelling."""
    return {
        alias.asname or alias.name
        for node in ast.walk(ast.parse(source))
        if isinstance(node, ast.ImportFrom) and (node.module or "").startswith("pysnmp")
        for alias in node.names
    }


def test_the_only_commands_imported_from_pysnmp_are_the_two_read_ones() -> None:
    """ARCHITECTURE.md §1 and SPEC.md §3: there is no write path, and none may appear.

    Stated as what is imported rather than as what is forbidden, so that a write primitive fails
    this by not being one of the two rather than by being on a list somebody has to keep current.
    ``pysnmp`` exports its write command from the very module ``get_cmd`` and ``bulk_cmd`` come
    from, so an accidental import is an autocomplete away.

    ``CollectorIsolationTests`` on the .NET side scans every file here for the write spellings as
    text, which catches the same mistake made through any other import.
    """
    session = (COLLECTOR / "snmp" / "session.py").read_text()

    commands = {name for name in _imported_from_pysnmp(session) if name.endswith("_cmd")}

    assert commands == READ_PRIMITIVES


def test_no_other_collector_module_imports_pysnmp_at_all() -> None:
    """One module touches the library, which is what makes the rule above sufficient."""
    importers = sorted(
        str(path.relative_to(COLLECTOR.parent))
        for path in COLLECTOR.rglob("*.py")
        if _imported_from_pysnmp(path.read_text())
    )

    assert importers == ["collector/snmp/session.py"]


def test_an_octet_string_of_text_decodes_as_text() -> None:
    assert decode(OctetString("GigabitEthernet0/1")) == "GigabitEthernet0/1"


def test_an_octet_string_of_raw_bytes_decodes_as_hex() -> None:
    """ifDescr and ifPhysAddress share a type; a MAC address must not come back as mojibake."""
    assert decode(OctetString(hexValue="001a2b3c4d01")) == "00:1A:2B:3C:4D:01"


def test_an_ip_address_decodes_as_an_address_rather_than_as_its_four_bytes() -> None:
    assert decode(IpAddress("192.0.2.10")) == "192.0.2.10"


def test_numbers_and_ticks_decode_as_their_digits() -> None:
    assert decode(Integer(1500)) == "1500"
    assert decode(TimeTicks(123456789)) == "123456789"


def test_an_empty_octet_string_decodes_as_an_empty_string() -> None:
    assert decode(OctetString("")) == ""


async def test_a_fixture_walk_returns_only_the_requested_subtree() -> None:
    session = FixtureSession(walk_fixture("cisco_ios"))

    walked = await session.walk(oids.IF_X_TABLE, max_rows=1000)

    assert walked
    assert all(oid.startswith(f"{oids.IF_X_TABLE}.") for oid in walked)


async def test_a_fixture_walk_stops_at_the_row_ceiling() -> None:
    session = FixtureSession(walk_fixture("cisco_ios"))

    assert len(await session.walk(oids.IF_TABLE, max_rows=3)) == 3


async def test_a_read_of_an_object_the_device_lacks_is_absent_rather_than_an_error() -> None:
    session = FixtureSession(walk_fixture("mikrotik_routeros"))

    answered = await session.get([oids.SYS_DESCR, "1.3.6.1.4.1.9.3.6.3.0"])

    assert oids.SYS_DESCR in answered
    assert "1.3.6.1.4.1.9.3.6.3.0" not in answered


def test_a_v2c_credential_becomes_a_community() -> None:
    auth = _auth_data(snmp_credential())

    assert str(auth.communityName) == "fixture-community"


def test_a_v2c_credential_with_no_community_is_refused() -> None:
    with pytest.raises(SnmpError, match="no community string"):
        _auth_data(snmp_credential(community=None))


def test_a_v3_credential_becomes_a_usm_user() -> None:
    auth = _auth_data(
        snmp_credential(
            kind=CredentialKind.SNMP_V3,
            community=None,
            username="netshield-ro",
            auth_algorithm="Sha256",
            privacy_algorithm="Aes128",
            auth_password="fixture-auth-password",
            privacy_password="fixture-privacy-password",
        )
    )

    assert str(auth.userName) == "netshield-ro"


def test_a_v3_credential_with_authnopriv_needs_no_privacy_password() -> None:
    """WP-1.2 made None a member of the privacy algorithm precisely so this is expressible."""
    auth = _auth_data(
        snmp_credential(
            kind=CredentialKind.SNMP_V3,
            community=None,
            username="netshield-ro",
            auth_algorithm="Sha1",
            privacy_algorithm="None",
            auth_password="fixture-auth-password",
        )
    )

    assert str(auth.userName) == "netshield-ro"


@pytest.mark.parametrize(
    ("overrides", "message"),
    [
        ({"username": None}, "no user name"),
        ({"username": "u", "auth_algorithm": "Sha3"}, "authentication algorithm"),
        (
            {"username": "u", "auth_algorithm": "Sha256", "privacy_algorithm": "Rot13"},
            "privacy algorithm",
        ),
        (
            {"username": "u", "auth_algorithm": "Sha256", "auth_password": None},
            "no authentication password",
        ),
        (
            {
                "username": "u",
                "auth_algorithm": "Sha256",
                "privacy_algorithm": "Aes256",
                "auth_password": "fixture-auth-password",
                "privacy_password": None,
            },
            "carries no password",
        ),
    ],
)
def test_an_incomplete_v3_credential_is_refused_before_a_packet_is_sent(
    overrides: dict[str, str | None],
    message: str,
) -> None:
    values: dict[str, str | None] = {
        "auth_password": "fixture-auth-password",
        "privacy_password": "fixture-privacy-password",
        "privacy_algorithm": "None",
    }
    values.update(overrides)

    with pytest.raises(SnmpError, match=message):
        _auth_data(snmp_credential(kind=CredentialKind.SNMP_V3, community=None, **values))


@pytest.mark.parametrize(
    ("address", "ipv6"),
    [
        ("192.0.2.10", False),
        ("2001:db8::1", True),
        ("switch-01.example.invalid", False),
    ],
)
def test_the_transport_family_follows_the_address(address: str, ipv6: bool) -> None:
    """A device whose primary address is IPv6 is one the inventory already accepts."""
    assert _is_ipv6(address) is ipv6


def test_an_ssh_credential_cannot_authenticate_a_walk() -> None:
    """A job carrying one was queued wrongly, and coercing it would hide that."""
    with pytest.raises(SnmpError, match="cannot authenticate an SNMP walk"):
        _auth_data(snmp_credential(kind=CredentialKind.SSH_PASSWORD, community=None))
