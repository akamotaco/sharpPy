using System;
using SharpPy.Tokenizer.Generated;

namespace SharpPy.Tokenizer
{
    /// <summary>
    /// Token info interface for PEG interpreter
    /// </summary>
    public interface ITokenInfo
    {
        object Type { get; }
        string Value { get; }
        int Line { get; }
        int Column { get; }
    }

    /// <summary>
    /// Wrapper to adapt GeneratedTokenInfo to ITokenInfo interface
    /// </summary>
    public class TokenInfoWrapper : ITokenInfo
    {
        private readonly GeneratedTokenInfo _token;

        public TokenInfoWrapper(GeneratedTokenInfo token)
        {
            _token = token ?? throw new ArgumentNullException(nameof(token));
        }

        public object Type => _token.Type;
        public string Value => _token.Value;
        public int Line => _token.Line;
        public int Column => _token.Column;
    }
}