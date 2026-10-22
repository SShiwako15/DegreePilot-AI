"""Session middleware signs cookies; pwdlib handles new password hashes."""

import base64
import hashlib
import hmac
import struct

from pwdlib import PasswordHash

passwords = PasswordHash.recommended()


def verify_password(password: str, encoded: str) -> tuple[bool, bool]:
    if encoded.startswith("$argon2"):
        try:
            return passwords.verify(password, encoded), False
        except Exception:
            return False, False
    # Read the documented Identity V2/V3 PBKDF2 envelope for existing local accounts.
    # New hashes always use pwdlib; a successful legacy login upgrades its hash.
    try:
        raw = base64.b64decode(encoded, validate=True)
        if raw[0] == 0 and len(raw) == 49:
            algorithm, iterations, salt, expected = "sha1", 1000, raw[1:17], raw[17:]
        elif raw[0] == 1 and len(raw) >= 45:
            prf, iterations, size = struct.unpack(">III", raw[1:13])
            if prf not in {0, 1, 2} or not 1000 <= iterations <= 2_000_000 or not 16 <= size <= 128:
                return False, False
            algorithm = {0: "sha1", 1: "sha256", 2: "sha512"}[prf]
            salt, expected = raw[13 : 13 + size], raw[13 + size :]
            if len(expected) < 16:
                return False, False
        else:
            return False, False
        valid = hmac.compare_digest(
            hashlib.pbkdf2_hmac(algorithm, password.encode(), salt, iterations, len(expected)), expected
        )
        return valid, valid
    except (ValueError, IndexError, struct.error):
        return False, False
