# Test module with function accessing module-level variable
import sys

MODULE_VAR = "test_mod variable"

def test_function():
    # Should access module-level sys
    print(f"Accessing sys from test_mod.test_function: {sys}")
    return sys.version
