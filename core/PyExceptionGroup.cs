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
        }

        public override PyType GetPyType() => PyType.BaseExceptionGroupType;

        public override string ToString()
        {
            var exceptionsText = string.Join(", ", Exceptions.Select(e => e.GetType().Name));
            return $"{Message} ({Exceptions.Count} sub-exception{(Exceptions.Count != 1 ? "s" : "")}): {exceptionsText}";
        }

        /// <summary>
        /// Create a new exception group with filtered exceptions
        /// </summary>
        public PyBaseExceptionGroup Subgroup(System.Type exceptionType)
        {
            var filtered = Exceptions.Where(e => exceptionType.IsAssignableFrom(e.GetType())).ToList();
            if (filtered.Count == 0) return null;
            return new PyBaseExceptionGroup(Message, filtered);
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