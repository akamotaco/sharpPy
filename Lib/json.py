"""
JSON (JavaScript Object Notation) encoder and decoder.

This module provides functions to serialize Python objects to JSON format
and deserialize JSON strings back to Python objects.
"""

import re
from typing import Any, Dict, List, Union, Optional, IO, Callable


class JSONDecodeError(ValueError):
    """Exception raised when JSON decoding fails."""

    def __init__(self, msg: str, doc: str = "", pos: int = 0):
        super().__init__(msg)
        self.msg = msg
        self.doc = doc
        self.pos = pos


class JSONEncoder:
    """Extensible JSON encoder for Python data structures."""

    def __init__(self, skipkeys=False, ensure_ascii=True, check_circular=True,
                 allow_nan=True, sort_keys=False, indent=None, separators=None,
                 default=None):
        self.skipkeys = skipkeys
        self.ensure_ascii = ensure_ascii
        self.check_circular = check_circular
        self.allow_nan = allow_nan
        self.sort_keys = sort_keys
        self.indent = indent
        self.separators = separators
        self.default = default

    def encode(self, obj):
        """Return a JSON string representation of a Python data structure."""
        return _encode_obj(obj, self)

    def iterencode(self, obj):
        """Encode the given object and return an iterator of string chunks."""
        yield self.encode(obj)


class JSONDecoder:
    """Simple JSON decoder."""

    def __init__(self, object_hook=None, parse_float=None, parse_int=None,
                 parse_constant=None, strict=True, object_pairs_hook=None):
        self.object_hook = object_hook
        self.parse_float = parse_float or float
        self.parse_int = parse_int or int
        self.parse_constant = parse_constant
        self.strict = strict
        self.object_pairs_hook = object_pairs_hook

    def decode(self, s):
        """Return the Python representation of s (a str containing a JSON document)."""
        return _decode_string(s, self)


def _encode_obj(obj, encoder):
    """Internal function to encode a Python object to JSON."""
    if obj is None:
        return "null"
    elif obj is True:
        return "true"
    elif obj is False:
        return "false"
    elif isinstance(obj, int):
        return str(obj)
    elif isinstance(obj, float):
        if not encoder.allow_nan and (obj != obj or obj == float('inf') or obj == float('-inf')):
            raise ValueError("Out of range float values are not JSON compliant")
        return str(obj)
    elif isinstance(obj, str):
        return _encode_string(obj, encoder.ensure_ascii)
    elif isinstance(obj, (list, tuple)):
        return _encode_list(obj, encoder)
    elif isinstance(obj, dict):
        return _encode_dict(obj, encoder)
    elif hasattr(obj, '__dict__'):
        # Try to serialize object with __dict__ attribute
        if encoder.default:
            return _encode_obj(encoder.default(obj), encoder)
        else:
            raise TypeError(f"Object of type {type(obj).__name__} is not JSON serializable")
    else:
        if encoder.default:
            return _encode_obj(encoder.default(obj), encoder)
        else:
            raise TypeError(f"Object of type {type(obj).__name__} is not JSON serializable")


def _encode_string(s, ensure_ascii=True):
    """Encode a string for JSON."""
    # Basic JSON string escaping
    s = s.replace('\\', '\\\\')
    s = s.replace('"', '\\"')
    s = s.replace('\n', '\\n')
    s = s.replace('\r', '\\r')
    s = s.replace('\t', '\\t')
    s = s.replace('\b', '\\b')
    s = s.replace('\f', '\\f')

    if ensure_ascii:
        # Escape non-ASCII characters
        result = []
        for char in s:
            if ord(char) > 127:
                result.append(f'\\u{ord(char):04x}')
            else:
                result.append(char)
        s = ''.join(result)

    return f'"{s}"'


def _encode_list(lst, encoder):
    """Encode a list/tuple for JSON."""
    if encoder.indent is not None:
        # Pretty printing with indentation
        items = []
        for item in lst:
            items.append(_encode_obj(item, encoder))
        return '[\n  ' + ',\n  '.join(items) + '\n]'
    else:
        # Compact format
        items = [_encode_obj(item, encoder) for item in lst]
        sep = ', ' if encoder.separators is None else encoder.separators[1]
        return '[' + sep.join(items) + ']'


def _encode_dict(dct, encoder):
    """Encode a dictionary for JSON."""
    if encoder.skipkeys:
        # Skip non-string keys
        items = [(k, v) for k, v in dct.items() if isinstance(k, str)]
    else:
        # Ensure all keys are strings
        items = []
        for k, v in dct.items():
            if not isinstance(k, str):
                raise TypeError(f"keys must be a string, not {type(k).__name__}")
            items.append((k, v))

    if encoder.sort_keys:
        items.sort(key=lambda x: x[0])

    if encoder.indent is not None:
        # Pretty printing with indentation
        pairs = []
        for k, v in items:
            key_str = _encode_string(k, encoder.ensure_ascii)
            val_str = _encode_obj(v, encoder)
            pairs.append(f'{key_str}: {val_str}')
        return '{\n  ' + ',\n  '.join(pairs) + '\n}'
    else:
        # Compact format
        pairs = []
        for k, v in items:
            key_str = _encode_string(k, encoder.ensure_ascii)
            val_str = _encode_obj(v, encoder)
            pairs.append(f'{key_str}:{val_str}')
        item_sep = ', ' if encoder.separators is None else encoder.separators[1]
        key_sep = ': ' if encoder.separators is None else encoder.separators[0]
        return '{' + item_sep.join(f'{key_str}{key_sep}{val_str}'
                                   for key_str, val_str in [(
                                       _encode_string(k, encoder.ensure_ascii),
                                       _encode_obj(v, encoder)
                                   ) for k, v in items]) + '}'


def _decode_string(s, decoder):
    """Internal function to decode a JSON string."""
    s = s.strip()
    try:
        return _parse_value(s, 0, decoder)[0]
    except (IndexError, KeyError, ValueError) as e:
        raise JSONDecodeError(f"Expecting value: {e}", s, 0)


def _parse_value(s, index, decoder):
    """Parse a JSON value starting at the given index."""
    # Skip whitespace
    while index < len(s) and s[index].isspace():
        index += 1

    if index >= len(s):
        raise JSONDecodeError("Expecting value", s, index)

    char = s[index]

    if char == '"':
        return _parse_string(s, index)
    elif char == '{':
        return _parse_object(s, index, decoder)
    elif char == '[':
        return _parse_array(s, index, decoder)
    elif char == 't':
        if s[index:index+4] == 'true':
            return True, index + 4
        else:
            raise JSONDecodeError("Expecting 'true'", s, index)
    elif char == 'f':
        if s[index:index+5] == 'false':
            return False, index + 5
        else:
            raise JSONDecodeError("Expecting 'false'", s, index)
    elif char == 'n':
        if s[index:index+4] == 'null':
            return None, index + 4
        else:
            raise JSONDecodeError("Expecting 'null'", s, index)
    elif char == '-' or char.isdigit():
        return _parse_number(s, index, decoder)
    else:
        raise JSONDecodeError(f"Unexpected character: {char}", s, index)


def _parse_string(s, index):
    """Parse a JSON string."""
    if s[index] != '"':
        raise JSONDecodeError("Expecting '\"'", s, index)

    index += 1  # Skip opening quote
    result = []

    while index < len(s):
        char = s[index]
        if char == '"':
            return ''.join(result), index + 1
        elif char == '\\':
            if index + 1 >= len(s):
                raise JSONDecodeError("Unterminated string escape", s, index)
            escape_char = s[index + 1]
            if escape_char == '"':
                result.append('"')
            elif escape_char == '\\':
                result.append('\\')
            elif escape_char == '/':
                result.append('/')
            elif escape_char == 'b':
                result.append('\b')
            elif escape_char == 'f':
                result.append('\f')
            elif escape_char == 'n':
                result.append('\n')
            elif escape_char == 'r':
                result.append('\r')
            elif escape_char == 't':
                result.append('\t')
            elif escape_char == 'u':
                # Unicode escape
                if index + 5 >= len(s):
                    raise JSONDecodeError("Incomplete unicode escape", s, index)
                try:
                    code_point = int(s[index+2:index+6], 16)
                    result.append(chr(code_point))
                    index += 4  # Extra increment for unicode
                except ValueError:
                    raise JSONDecodeError("Invalid unicode escape", s, index)
            else:
                raise JSONDecodeError(f"Invalid escape character: {escape_char}", s, index)
            index += 2
        else:
            result.append(char)
            index += 1

    raise JSONDecodeError("Unterminated string", s, index)


def _parse_number(s, index, decoder):
    """Parse a JSON number."""
    start = index

    # Handle negative sign
    if s[index] == '-':
        index += 1

    # Parse integer part
    if index >= len(s) or not s[index].isdigit():
        raise JSONDecodeError("Expecting digit", s, index)

    if s[index] == '0':
        index += 1
    else:
        while index < len(s) and s[index].isdigit():
            index += 1

    # Parse decimal part
    if index < len(s) and s[index] == '.':
        index += 1
        if index >= len(s) or not s[index].isdigit():
            raise JSONDecodeError("Expecting digit", s, index)
        while index < len(s) and s[index].isdigit():
            index += 1

    # Parse exponent part
    if index < len(s) and s[index].lower() == 'e':
        index += 1
        if index < len(s) and s[index] in '+-':
            index += 1
        if index >= len(s) or not s[index].isdigit():
            raise JSONDecodeError("Expecting digit", s, index)
        while index < len(s) and s[index].isdigit():
            index += 1

    number_str = s[start:index]
    try:
        if '.' in number_str or 'e' in number_str.lower():
            return decoder.parse_float(number_str), index
        else:
            return decoder.parse_int(number_str), index
    except ValueError as e:
        raise JSONDecodeError(f"Invalid number: {e}", s, start)


def _parse_array(s, index, decoder):
    """Parse a JSON array."""
    if s[index] != '[':
        raise JSONDecodeError("Expecting '['", s, index)

    index += 1  # Skip opening bracket
    result = []

    # Skip whitespace
    while index < len(s) and s[index].isspace():
        index += 1

    # Empty array
    if index < len(s) and s[index] == ']':
        return result, index + 1

    while index < len(s):
        # Parse value
        value, index = _parse_value(s, index, decoder)
        result.append(value)

        # Skip whitespace
        while index < len(s) and s[index].isspace():
            index += 1

        if index >= len(s):
            raise JSONDecodeError("Expecting ',' or ']'", s, index)

        if s[index] == ']':
            return result, index + 1
        elif s[index] == ',':
            index += 1
        else:
            raise JSONDecodeError("Expecting ',' or ']'", s, index)

    raise JSONDecodeError("Expecting ']'", s, index)


def _parse_object(s, index, decoder):
    """Parse a JSON object."""
    if s[index] != '{':
        raise JSONDecodeError("Expecting '{'", s, index)

    index += 1  # Skip opening brace
    result = {}

    # Skip whitespace
    while index < len(s) and s[index].isspace():
        index += 1

    # Empty object
    if index < len(s) and s[index] == '}':
        return result, index + 1

    while index < len(s):
        # Parse key (must be string)
        if s[index] != '"':
            raise JSONDecodeError("Expecting property name enclosed in double quotes", s, index)

        key, index = _parse_string(s, index)

        # Skip whitespace
        while index < len(s) and s[index].isspace():
            index += 1

        # Expect colon
        if index >= len(s) or s[index] != ':':
            raise JSONDecodeError("Expecting ':'", s, index)
        index += 1

        # Parse value
        value, index = _parse_value(s, index, decoder)
        result[key] = value

        # Skip whitespace
        while index < len(s) and s[index].isspace():
            index += 1

        if index >= len(s):
            raise JSONDecodeError("Expecting ',' or '}'", s, index)

        if s[index] == '}':
            if decoder.object_hook:
                result = decoder.object_hook(result)
            return result, index + 1
        elif s[index] == ',':
            index += 1
        else:
            raise JSONDecodeError("Expecting ',' or '}'", s, index)

    raise JSONDecodeError("Expecting '}'", s, index)


# Public API functions
def dumps(obj, skipkeys=False, ensure_ascii=True, check_circular=True,
          allow_nan=True, cls=None, indent=None, separators=None,
          default=None, sort_keys=False, **kw):
    """Serialize obj to a JSON formatted str."""
    if cls is not None:
        encoder = cls(skipkeys=skipkeys, ensure_ascii=ensure_ascii,
                     check_circular=check_circular, allow_nan=allow_nan,
                     indent=indent, separators=separators, default=default,
                     sort_keys=sort_keys, **kw)
    else:
        encoder = JSONEncoder(skipkeys=skipkeys, ensure_ascii=ensure_ascii,
                             check_circular=check_circular, allow_nan=allow_nan,
                             indent=indent, separators=separators, default=default,
                             sort_keys=sort_keys)
    return encoder.encode(obj)


def dump(obj, fp, skipkeys=False, ensure_ascii=True, check_circular=True,
         allow_nan=True, cls=None, indent=None, separators=None,
         default=None, sort_keys=False, **kw):
    """Serialize obj as a JSON formatted stream to fp."""
    json_str = dumps(obj, skipkeys=skipkeys, ensure_ascii=ensure_ascii,
                    check_circular=check_circular, allow_nan=allow_nan,
                    cls=cls, indent=indent, separators=separators,
                    default=default, sort_keys=sort_keys, **kw)
    fp.write(json_str)


def loads(s, cls=None, object_hook=None, parse_float=None,
          parse_int=None, parse_constant=None, object_pairs_hook=None,
          **kw):
    """Deserialize s (a str, bytes or bytearray containing a JSON document) to a Python object."""
    if isinstance(s, (bytes, bytearray)):
        s = s.decode('utf-8')

    if cls is not None:
        decoder = cls(object_hook=object_hook, parse_float=parse_float,
                     parse_int=parse_int, parse_constant=parse_constant,
                     object_pairs_hook=object_pairs_hook, **kw)
    else:
        decoder = JSONDecoder(object_hook=object_hook, parse_float=parse_float,
                             parse_int=parse_int, parse_constant=parse_constant,
                             object_pairs_hook=object_pairs_hook)
    return decoder.decode(s)


def load(fp, cls=None, object_hook=None, parse_float=None,
         parse_int=None, parse_constant=None, object_pairs_hook=None,
         **kw):
    """Deserialize fp (a .read()-supporting file-like object containing a JSON document) to a Python object."""
    return loads(fp.read(), cls=cls, object_hook=object_hook,
                parse_float=parse_float, parse_int=parse_int,
                parse_constant=parse_constant, object_pairs_hook=object_pairs_hook,
                **kw)


# Export public API
__all__ = [
    'dump', 'dumps', 'load', 'loads',
    'JSONDecoder', 'JSONEncoder', 'JSONDecodeError'
]