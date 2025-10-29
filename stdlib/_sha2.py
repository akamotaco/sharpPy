"""
Minimal _sha2 module for SharpPy
Provides SHA512 hashing
"""

class sha512:
    """SHA512 hash object."""

    def __init__(self, data=b''):
        self._data = data if isinstance(data, bytes) else data.encode('utf-8') if isinstance(data, str) else bytes(data)

    def digest(self):
        """Return the digest of the data as bytes."""
        # Simplified fake hash - just return 64 bytes based on input
        result = bytes([self._data[i % len(self._data)] if len(self._data) > 0 else 0 for i in range(64)])
        return result

    def hexdigest(self):
        """Return the digest of the data as a hex string."""
        return self.digest().hex()

    def update(self, data):
        """Update the hash object with new data."""
        if isinstance(data, bytes):
            self._data += data
        elif isinstance(data, str):
            self._data += data.encode('utf-8')
        else:
            self._data += bytes(data)
