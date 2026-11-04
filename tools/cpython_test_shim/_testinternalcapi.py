"""What test modules touch when they import test.test_import, which imports this at the top.
On CPython the real _testinternalcapi is found first. Nothing here is a C API: a test that
calls into it fails, as it should."""
