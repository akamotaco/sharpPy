namespace SharpPy
{
    /// <summary>
    /// The magic number of SharpPy's bytecode: what importlib writes at the start of a .pyc
    /// (_bootstrap_external.MAGIC_NUMBER, its little-endian two bytes and "\r\n") and refuses a
    /// cache for when it differs, and what a bundle (.spk) is checked against. SharpPy's bytecode is
    /// its own, not CPython's, so the number is outside CPython's (3531 for 3.12): a cache another
    /// interpreter wrote is recompiled, as CPython recompiles another version's.
    ///
    /// Bump it whenever the compiler's output changes, as CPython does in
    /// Lib/importlib/_bootstrap_external.py; tools/bytecode_fingerprint.py fails the gate when the
    /// stdlib compiles differently under an unchanged number. Then run it with --update.
    /// </summary>
    internal static class PycMagic
    {
        internal const int Number = 5004;
    }
}
