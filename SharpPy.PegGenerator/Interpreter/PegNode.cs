namespace SharpPy.PegGenerator.Interpreter
{
    /// <summary>
    /// PEG interpreter-specific base type - replaces GeneratedPtr dependency
    /// Used to avoid object boxing/unboxing while maintaining type safety
    /// Independent from GeneratedPtr - exists only at compile time (parser generation)
    /// Following CPython principle: compiler tools are separate from runtime parser
    /// </summary>
    public abstract class PegNode
    {
        // Minimal base class - just provides type hierarchy for PEG interpreter
        // This allows PegInterpreter to work without depending on Generated runtime types
    }
}
