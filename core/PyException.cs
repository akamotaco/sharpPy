namespace SharpPy
{
    #region Exception Classes

    public class AttributeError : Exception
    {
        public AttributeError(string message) : base(message) { }
    }

    public class TypeError : Exception  
    {
        public TypeError(string message) : base(message) { }
    }

    public class NameError : Exception
    {
        public NameError(string message) : base(message) { }
    }

    public class ModuleNotFoundError : Exception
    {
        public ModuleNotFoundError(string message) : base(message) { }
    }

    public class ImportError : Exception
    {
        public ImportError(string message) : base(message) { }
    }

    public class NotImplementedError : Exception
    {
        public NotImplementedError(string message) : base(message) { }
    }

    public class InvalidOperationException : Exception
    {
        public InvalidOperationException(string message) : base(message) { }
    }

    #endregion
}