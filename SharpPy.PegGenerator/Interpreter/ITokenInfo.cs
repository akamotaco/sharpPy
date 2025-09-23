using System;

namespace SharpPy.PegGenerator.Interpreter
{
    /// <summary>
    /// Token info interface for PEG interpreter - independent from SharpPy.Tokenizer
    /// </summary>
    public interface ITokenInfo
    {
        object Type { get; }
        string Value { get; }
        int Line { get; }
        int Column { get; }
    }

    /// <summary>
    /// Adapter to convert SharpPy.Tokenizer.Generated.GeneratedTokenInfo to PEG interpreter's ITokenInfo
    /// Used only in generated parser code
    /// </summary>
    public class PegTokenInfoAdapter : ITokenInfo
    {
        private readonly object _token;

        public PegTokenInfoAdapter(object token)
        {
            _token = token ?? throw new ArgumentNullException(nameof(token));
        }

        public object Type => GetProperty("Type");
        public string Value => GetProperty("Value")?.ToString() ?? "";
        public int Line => (int)(GetProperty("Line") ?? 0);
        public int Column => (int)(GetProperty("Column") ?? 0);

        private object? GetProperty(string propertyName)
        {
            var property = _token.GetType().GetProperty(propertyName);
            return property?.GetValue(_token);
        }
    }
}