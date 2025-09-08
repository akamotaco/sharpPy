using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// PEP 654: Exception Groups implementation
    /// BaseExceptionGroup is the base class for grouping exceptions
    /// </summary>
    public class PyBaseExceptionGroup : PyException
    {
        public string Message { get; }
        public List<PyException> Exceptions { get; }

        public PyBaseExceptionGroup(string message, List<PyException> exceptions) : base(message)
        {
            Message = message;
            Exceptions = exceptions ?? new List<PyException>();
            
            // CPython 3.12: Validate non-empty exceptions sequence
            if (Exceptions.Count == 0)
            {
                throw PyTypeError.Create("second argument (exceptions) must be a non-empty sequence");
            }
        }

        public override PyType GetPyType() => PyType.BaseExceptionGroupType;

        public override string ToString()
        {
            var exceptionsText = string.Join(", ", Exceptions.Select(e => e.GetType().Name));
            return $"{Message} ({Exceptions.Count} sub-exception{(Exceptions.Count != 1 ? "s" : "")}): {exceptionsText}";
        }

        /// <summary>
        /// Create a new exception group with filtered exceptions (C# internal)
        /// </summary>
        public PyBaseExceptionGroup Subgroup(System.Type exceptionType)
        {
            var filtered = Exceptions.Where(e => exceptionType.IsAssignableFrom(e.GetType())).ToList();
            if (filtered.Count == 0) return null;
            return new PyBaseExceptionGroup(Message, filtered);
        }
        
        /// <summary>
        /// PEP 654: CPython-compatible subgroup() method
        /// Returns a subgroup containing only exceptions that match the condition
        /// </summary>
        public override PyObject GetAttribute(string name)
        {
            if (name == "subgroup")
            {
                return new PyBuiltinFunction("subgroup", args =>
                {
                    if (args.Length != 1)
                        throw PyTypeError.Create("subgroup() takes exactly one argument");
                    
                    var condition = args[0];
                    var matchedExceptions = new List<PyException>();
                    
                    // Handle exception type or tuple of exception types
                    if (condition is PyBuiltinType builtinType)
                    {
                        // Single exception type
                        foreach (var exc in Exceptions)
                        {
                            if (ExceptionMatches(exc, condition))
                            {
                                matchedExceptions.Add(exc);
                            }
                        }
                    }
                    else if (condition is PyTuple typeTuple)
                    {
                        // Tuple of exception types
                        foreach (var exc in Exceptions)
                        {
                            foreach (var exceptionType in typeTuple.Items)
                            {
                                if (ExceptionMatches(exc, exceptionType))
                                {
                                    matchedExceptions.Add(exc);
                                    break; // Don't add same exception twice
                                }
                            }
                        }
                    }
                    else if (condition is PyObject callable && callable.IsCallable())
                    {
                        // Callable condition
                        foreach (var exc in Exceptions)
                        {
                            var result = callable.Call(exc);
                            if (result.PyBoolValue())
                            {
                                matchedExceptions.Add(exc);
                            }
                        }
                    }
                    else
                    {
                        throw PyTypeError.Create("subgroup() condition must be an exception type, tuple of types, or callable");
                    }
                    
                    // Return None if no matches found
                    if (matchedExceptions.Count == 0)
                        return PyNone.Instance;
                    
                    // Create appropriate subgroup type
                    if (this is PyExceptionGroup)
                        return new PyExceptionGroup(Message, matchedExceptions);
                    else
                        return new PyBaseExceptionGroup(Message, matchedExceptions);
                });
            }
            
            return base.GetAttribute(name);
        }
        
        /// <summary>
        /// Helper method to check if exception matches type (similar to VM's ExceptionMatches)
        /// </summary>
        private bool ExceptionMatches(PyException exception, PyObject exceptionType)
        {
            if (exceptionType is PyBuiltinType builtinType)
            {
                string excTypeName = exception.GetType().Name;
                return excTypeName.Replace("Py", "") == builtinType.Name.Replace("Error", "Error");
            }
            return false;
        }

        public static PyBaseExceptionGroup Create(string message, List<PyException> exceptions)
        {
            return new PyBaseExceptionGroup(message, exceptions);
        }
    }

    /// <summary>
    /// ExceptionGroup is for non-BaseException exceptions
    /// </summary>
    public class PyExceptionGroup : PyBaseExceptionGroup
    {
        public PyExceptionGroup(string message, List<PyException> exceptions) : base(message, exceptions)
        {
        }

        public override PyType GetPyType() => PyType.ExceptionGroupType;

        public static new PyExceptionGroup Create(string message, List<PyException> exceptions)
        {
            return new PyExceptionGroup(message, exceptions);
        }
    }
}