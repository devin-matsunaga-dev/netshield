"""Talking SNMP to one device, and the seam that makes the rest of this package testable.

Everything above this module — the fingerprint resolver, the vendor adapters, the interface
inventory — works from a mapping of OID to decoded string and never imports ``pysnmp``. That is
what lets CONVENTIONS.md §7 be met honestly: a recorded walk is a JSON file, a test replays it
through :class:`FixtureSession`, and the logic under test is the logic that runs in production
rather than a mock of it.

Two rules hold in here and nowhere else has to think about them:

* **Read only.** This module calls ``get_cmd`` and ``bulk_cmd``. ``pysnmp`` exports a write
  primitive from the same module; it is never imported, and two gates keep that true —
  ``test_snmp_session.py`` pins the exact set of names imported from ``pysnmp`` here, and
  ``CollectorIsolationTests`` fails the build if any spelling of a write appears anywhere under
  ``src/netshield-collector`` (SPEC.md §3, ARCHITECTURE.md §1).
* **Numeric OIDs only.** Nothing is resolved through a MIB module, so nothing can send
  ``pysnmp`` looking for one on the internet. See :mod:`collector.snmp.oids`.
"""

from __future__ import annotations

import asyncio
import ipaddress
import string
from collections.abc import Mapping, Sequence
from contextlib import AbstractAsyncContextManager
from types import TracebackType
from typing import Any, Final, Protocol

import structlog
from pysnmp.hlapi.v3arch.asyncio import (
    CommunityData,
    ContextData,
    SnmpEngine,
    Udp6TransportTarget,
    UdpTransportTarget,
    UsmUserData,
    bulk_cmd,
    get_cmd,
    usmAesCfb128Protocol,
    usmAesCfb192Protocol,
    usmAesCfb256Protocol,
    usmDESPrivProtocol,
    usmHMAC128SHA224AuthProtocol,
    usmHMAC192SHA256AuthProtocol,
    usmHMAC256SHA384AuthProtocol,
    usmHMAC384SHA512AuthProtocol,
    usmHMACMD5AuthProtocol,
    usmHMACSHAAuthProtocol,
    usmNoPrivProtocol,
)
from pysnmp.proto.rfc1902 import IpAddress, OctetString
from pysnmp.smi.rfc1902 import ObjectIdentity, ObjectType

from collector.models import CredentialKind, JobCredential

_LOG: Final = structlog.get_logger(__name__)

SNMP_PORT: Final = 161
"""The port every walk is sent to. A device on another port cannot be recorded today — the
inventory has no column for one — so this is a constant rather than a parameter nothing sets."""

_PRINTABLE: Final = frozenset(string.printable) - frozenset("\x0b\x0c")
"""What counts as text when deciding whether an octet string is a name or a MAC address."""

_AUTH_PROTOCOLS: Final = {
    "Md5": usmHMACMD5AuthProtocol,
    "Sha1": usmHMACSHAAuthProtocol,
    "Sha224": usmHMAC128SHA224AuthProtocol,
    "Sha256": usmHMAC192SHA256AuthProtocol,
    "Sha384": usmHMAC256SHA384AuthProtocol,
    "Sha512": usmHMAC384SHA512AuthProtocol,
}
"""``SnmpAuthAlgorithm`` as the API spells it, to the USM protocol it names."""

_PRIVACY_PROTOCOLS: Final = {
    "None": usmNoPrivProtocol,
    "Des": usmDESPrivProtocol,
    "Aes128": usmAesCfb128Protocol,
    "Aes192": usmAesCfb192Protocol,
    "Aes256": usmAesCfb256Protocol,
}
"""``SnmpPrivacyAlgorithm`` as the API spells it, to the USM protocol it names."""


class SnmpError(RuntimeError):
    """The device could not be walked, and nothing was learned about it.

    Everything that reaches the runner as a failed job comes out of here: a timeout, an
    authentication failure, a credential that is not an SNMP credential, an agent whose OIDs do
    not increase. None of it is evidence about what the device *is*, which is why a walk that
    fails leaves the fingerprint on the device exactly as it was.
    """


class SnmpSession(Protocol):
    """One authenticated conversation with one device."""

    async def get(self, oids: Sequence[str]) -> dict[str, str]:
        """Read the named scalars. Objects the device does not implement are simply absent."""
        ...

    async def walk(self, root: str, *, max_rows: int) -> dict[str, str]:
        """Read every object under ``root``, as OID to decoded value.

        Stops at the end of the subtree, at the end of the MIB, or at ``max_rows`` — an agent
        with ten thousand interfaces must not be able to make one job consume the process.
        """
        ...


class SnmpSessionFactory(Protocol):
    """How the executor gets a session, so a test can hand it a recorded walk instead.

    :class:`PySnmpSession` is the only implementation that opens a socket. The seam exists
    because CONVENTIONS.md §7 asks for protocol interactions tested against recorded fixtures,
    and a factory is the smallest thing that lets the executor's whole path — parameters,
    credential, walk, payload — run against one.
    """

    def __call__(
        self,
        address: str,
        credential: JobCredential,
        *,
        timeout_seconds: float,
        retries: int,
        max_repetitions: int,
    ) -> AbstractAsyncContextManager[SnmpSession]:
        """A session for one device, not yet opened."""
        ...


def decode(value: object) -> str:
    """Turn one pysnmp value into the string a recorded walk would have held.

    Octet strings are the only interesting case. An ``ifDescr`` is text and an ``ifPhysAddress``
    is six raw bytes, and SNMP gives both the same type — so a value whose every byte is
    printable is decoded as text, and anything else is rendered as colon-separated uppercase hex,
    which is what a MAC address needs and what ``snmpwalk`` itself prints.

    ``IpAddress`` is an octet string too and is deliberately excluded: four raw bytes that mean
    an address should read as an address.
    """
    if isinstance(value, bytes):
        return _decode_octets(value)

    if isinstance(value, IpAddress):
        return str(value.prettyPrint())

    if isinstance(value, OctetString):
        return _decode_octets(bytes(value.asOctets()))

    return str(value)


def _decode_octets(raw: bytes) -> str:
    if not raw:
        return ""

    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        return _hex(raw)

    return text if all(character in _PRINTABLE for character in text) else _hex(raw)


def _hex(raw: bytes) -> str:
    return ":".join(f"{byte:02X}" for byte in raw)


class PySnmpSession:
    """An :class:`SnmpSession` over a real UDP socket.

    Built from the credential the API leased with the job and nothing else. The credential is
    read here, used here, and never written anywhere: no cache, no file, no log line
    (ARCHITECTURE.md §7).
    """

    def __init__(
        self,
        address: str,
        credential: JobCredential,
        *,
        timeout_seconds: float,
        retries: int,
        max_repetitions: int,
    ) -> None:
        self._address = address
        self._auth = _auth_data(credential)
        self._timeout_seconds = timeout_seconds
        self._retries = retries
        self._max_repetitions = max_repetitions
        self._engine = SnmpEngine()
        self._context = ContextData()
        self._transport: Any | None = None

    async def __aenter__(self) -> PySnmpSession:
        return self

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc: BaseException | None,
        traceback: TracebackType | None,
    ) -> None:
        self.close()

    def close(self) -> None:
        """Release the engine's dispatcher. Safe to call more than once."""
        try:
            self._engine.close_dispatcher()
        except Exception:  # a dispatcher that is already shut is not an error
            _LOG.debug("collector.snmp.dispatcher-close-failed", address=self._address)

    async def get(self, oids: Sequence[str]) -> dict[str, str]:
        if not oids:
            return {}

        error_indication, error_status, error_index, var_binds = await get_cmd(
            self._engine,
            self._auth,
            await self._target(),
            self._context,
            *(ObjectType(ObjectIdentity(oid)) for oid in oids),
        )

        # A GET of several objects fails as a whole when one of them is not implemented, which is
        # the common case on a device that has no ENTITY-MIB. The objects that did come back are
        # kept and the missing one is simply absent, because "this device does not have that" is
        # an answer rather than a failure.
        if error_indication is not None:
            raise SnmpError(
                f"The device did not answer a read of {len(oids)} objects: {error_indication}."
            )

        if error_status:
            _LOG.debug(
                "collector.snmp.get-partial",
                address=self._address,
                status=str(error_status),
                index=int(error_index or 0),
            )

        return _collect(var_binds)

    async def walk(self, root: str, *, max_rows: int) -> dict[str, str]:
        target = await self._target()
        prefix = f"{root}."
        found: dict[str, str] = {}
        cursor = root

        while len(found) < max_rows:
            error_indication, error_status, error_index, var_binds = await bulk_cmd(
                self._engine,
                self._auth,
                target,
                self._context,
                0,
                self._max_repetitions,
                ObjectType(ObjectIdentity(cursor)),
            )

            if error_indication is not None:
                raise SnmpError(
                    f"The device stopped answering a walk of {root}: {error_indication}."
                )

            if error_status:
                raise SnmpError(
                    f"The device refused a walk of {root}: {error_status} at index {error_index}."
                )

            advanced = False

            for oid, value in _pairs(var_binds):
                if not oid.startswith(prefix):
                    return found

                if oid in found:
                    # A non-increasing agent. Continuing would loop for ever, and the runner's
                    # job timeout would be the only thing that stopped it.
                    raise SnmpError(f"The device repeated {oid} during a walk of {root}.")

                found[oid] = value
                cursor = oid
                advanced = True

                if len(found) >= max_rows:
                    _LOG.warning(
                        "collector.snmp.walk-truncated",
                        address=self._address,
                        root=root,
                        maxRows=max_rows,
                    )

                    return found

            if not advanced:
                return found

        return found

    async def _target(self) -> Any:
        """The resolved socket address, made once and reused for the life of the session.

        ``create`` is what turns the inventory's address into a socket address, so a bad address
        arrives as this job's failure rather than as something raised out of the loop.

        The two transports are separate classes in ``pysnmp`` rather than one that picks a
        family, so the address decides which is built. A device whose ``primary_ip_address`` is
        IPv6 is a device the inventory already accepts, and answering it with an IPv4-only
        transport would have failed as an unhelpful socket error.
        """
        if self._transport is None:
            transport = Udp6TransportTarget if _is_ipv6(self._address) else UdpTransportTarget

            try:
                self._transport = await transport.create(
                    (self._address, SNMP_PORT),
                    timeout=self._timeout_seconds,
                    retries=self._retries,
                )
            except Exception as error:
                raise SnmpError(
                    f"{self._address} is not an address this collector can reach: {error}."
                ) from error

        return self._transport


def _is_ipv6(address: str) -> bool:
    """Whether this address needs the IPv6 transport.

    A host name is not an address and is answered with the IPv4 transport, which is what resolves
    it. NetShield's inventory holds addresses rather than names, so that is the uncommon path.
    """
    try:
        return ipaddress.ip_address(address).version == 6
    except ValueError:
        return False


def _pairs(var_binds: Any) -> list[tuple[str, str]]:
    """The variable bindings as (dotted OID, decoded value), dropping the exception markers."""
    pairs: list[tuple[str, str]] = []

    for binding in var_binds or ():
        oid, value = binding

        # noSuchObject / noSuchInstance / endOfMibView. The device is telling us it has nothing
        # there, which is an absence rather than a value.
        if value is None or _is_exception(value):
            continue

        pairs.append((str(oid), decode(value)))

    return pairs


def _collect(var_binds: Any) -> dict[str, str]:
    return dict(_pairs(var_binds))


def _is_exception(value: object) -> bool:
    return type(value).__name__ in {"NoSuchObject", "NoSuchInstance", "EndOfMibView"}


def _auth_data(credential: JobCredential) -> CommunityData | UsmUserData:
    """The USM or community configuration this credential describes.

    A credential of any other kind is refused here rather than being coerced: an SSH key is not
    an SNMP credential, and a job carrying one was queued wrongly.
    """
    if credential.kind is CredentialKind.SNMP_V2C:
        community = credential.material.community

        if community is None:
            raise SnmpError("The SNMPv2c credential carries no community string.")

        # mpModel 1 is SNMPv2c. NetShield does not speak v1: SPEC.md §2 asks for SNMP reads of a
        # modern estate, and v1 has neither counters wide enough for it nor a usable error model.
        #
        # S508 objects to v2c on principle, and it is right to. It is suppressed rather than
        # obeyed because WP-1.2 settled that a credential profile may be SnmpV2c, and an estate
        # of switches that speak only v2c is the reason: refusing the kind here would mean the
        # API can store a credential the collector will not use. SNMPv3 is preferred wherever a
        # device has one — the on-demand walk picks a v3 profile over a v2c profile — and the
        # community string is a SecretStr the whole way, masked in every repr and model dump.
        return CommunityData(community.get_secret_value(), mpModel=1)  # noqa: S508

    if credential.kind is CredentialKind.SNMP_V3:
        return _usm(credential)

    raise SnmpError(f"A {credential.kind} credential cannot authenticate an SNMP walk.")


def _usm(credential: JobCredential) -> UsmUserData:
    if not credential.username:
        raise SnmpError("The SNMPv3 credential carries no user name.")

    auth_algorithm = credential.auth_algorithm or ""
    privacy_algorithm = credential.privacy_algorithm or "None"

    if auth_algorithm not in _AUTH_PROTOCOLS:
        named = auth_algorithm or "An absent algorithm"

        raise SnmpError(f"{named} is not an SNMPv3 authentication algorithm this collector has.")

    if privacy_algorithm not in _PRIVACY_PROTOCOLS:
        named = privacy_algorithm

        raise SnmpError(f"{named} is not an SNMPv3 privacy algorithm this collector has.")

    if credential.material.auth_password is None:
        raise SnmpError("The SNMPv3 credential carries no authentication password.")

    encrypted = privacy_algorithm != "None"
    privacy_password = credential.material.privacy_password

    if encrypted and privacy_password is None:
        raise SnmpError(
            f"The SNMPv3 credential names {privacy_algorithm} privacy and carries no password."
        )

    return UsmUserData(
        credential.username,
        authKey=credential.material.auth_password.get_secret_value(),
        privKey=privacy_password.get_secret_value() if encrypted and privacy_password else None,
        authProtocol=_AUTH_PROTOCOLS[auth_algorithm],
        privProtocol=_PRIVACY_PROTOCOLS[privacy_algorithm],
    )


class FixtureSession:
    """An :class:`SnmpSession` that replays a recorded walk.

    This is how every vendor's fingerprint is tested (CONVENTIONS.md §7). It is in the package
    rather than in the test suite because it is the definition of what a fixture *is* — a flat
    mapping of dotted OID to the value ``snmpwalk`` printed — and the recorder that eventually
    writes real ones from ``snmpsim`` has to agree with the replayer.
    """

    def __init__(self, values: Mapping[str, str], *, hang: bool = False) -> None:
        # Sorted by OID the way an agent must walk them, so a fixture written in any order
        # replays in lexicographic-numeric order like a real device.
        self._values = dict(sorted(values.items(), key=lambda item: _numeric(item[0])))
        self._hang = hang

    async def __aenter__(self) -> FixtureSession:
        return self

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc: BaseException | None,
        traceback: TracebackType | None,
    ) -> None:
        return None

    async def _maybe_hang(self) -> None:
        """A device that accepts the read and never answers.

        Not a raised timeout: the point of the test that uses it is that the *runner* is what
        bounds a hung session, so this has to be indistinguishable from a slow device.
        """
        if self._hang:
            await asyncio.sleep(3600)

    async def get(self, oids: Sequence[str]) -> dict[str, str]:
        await self._maybe_hang()

        return {oid: self._values[oid] for oid in oids if oid in self._values}

    async def walk(self, root: str, *, max_rows: int) -> dict[str, str]:
        await self._maybe_hang()

        prefix = f"{root}."

        found = {oid: value for oid, value in self._values.items() if oid.startswith(prefix)}

        return dict(list(found.items())[:max_rows])


def _numeric(oid: str) -> tuple[int, ...]:
    return tuple(int(part) for part in oid.split(".") if part)
