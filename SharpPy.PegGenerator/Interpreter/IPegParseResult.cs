using System;
using System.Collections.Generic;

namespace SharpPy.PegGenerator.Interpreter
{
    /// <summary>
    /// Base interface for all PEG parse results to replace object? usage
    /// Provides type safety and better performance by avoiding boxing/unboxing
    /// </summary>
    public interface IPegParseResult
    {
        /// <summary>
        /// Whether the parsing was successful
        /// </summary>
        bool IsSuccess { get; }

        /// <summary>
        /// The position where parsing ended (for successful results)
        /// </summary>
        int EndPosition { get; }
    }

    /// <summary>
    /// Abstract base class for all parse results
    /// </summary>
    public abstract class PegParseResult : IPegParseResult
    {
        public bool IsSuccess { get; protected set; }
        public int EndPosition { get; protected set; }

        protected PegParseResult(bool isSuccess, int endPosition)
        {
            IsSuccess = isSuccess;
            EndPosition = endPosition;
        }
    }

    /// <summary>
    /// Represents a parsing failure - singleton pattern for efficiency
    /// </summary>
    public sealed class PegFailure : PegParseResult
    {
        public static readonly PegFailure Instance = new PegFailure();

        private PegFailure() : base(false, -1) { }

        public override string ToString() => "PegFailure";
    }

    /// <summary>
    /// Represents successful parsing of a token
    /// </summary>
    public sealed class PegTokenResult : PegParseResult
    {
        public ITokenInfo Token { get; }

        public PegTokenResult(ITokenInfo token, int endPosition)
            : base(true, endPosition)
        {
            Token = token ?? throw new ArgumentNullException(nameof(token));
        }

        public override string ToString() => $"PegTokenResult({Token.Type}:{Token.Value})";
    }

    /// <summary>
    /// Represents successful parsing of multiple items
    /// </summary>
    public sealed class PegListResult : PegParseResult
    {
        public IReadOnlyList<IPegParseResult> Items { get; }

        public PegListResult(IReadOnlyList<IPegParseResult> items, int endPosition)
            : base(true, endPosition)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
        }

        public override string ToString() => $"PegListResult({Items.Count} items)";
    }

    /// <summary>
    /// Represents successful parsing that produced an AST node
    /// </summary>
    public sealed class PegAstResult : PegParseResult
    {
        public object AstNode { get; }

        public PegAstResult(object astNode, int endPosition)
            : base(true, endPosition)
        {
            AstNode = astNode ?? throw new ArgumentNullException(nameof(astNode));
        }

        public override string ToString() => $"PegAstResult({AstNode.GetType().Name})";
    }

    /// <summary>
    /// Represents Cut operation results for control flow
    /// </summary>
    public sealed class PegCutResult : PegParseResult
    {
        public bool IsCutFailure { get; }
        public IPegParseResult? InnerResult { get; }

        public PegCutResult(bool isCutFailure, IPegParseResult? innerResult, int endPosition)
            : base(!isCutFailure, endPosition)
        {
            IsCutFailure = isCutFailure;
            InnerResult = innerResult;
        }

        public override string ToString() =>
            IsCutFailure ? "PegCutFailure" : $"PegCutSuccess({InnerResult})";
    }

    /// <summary>
    /// Represents simple successful parsing (e.g., for optional matches)
    /// </summary>
    public sealed class PegSuccess : PegParseResult
    {
        public static readonly PegSuccess Instance = new PegSuccess(-1);

        public PegSuccess(int endPosition) : base(true, endPosition) { }

        public override string ToString() => $"PegSuccess(pos:{EndPosition})";
    }

    /// <summary>
    /// Represents successful parsing with an action result
    /// </summary>
    public sealed class PegActionResult : PegParseResult
    {
        public object ActionResult { get; }

        public PegActionResult(object actionResult, int endPosition)
            : base(true, endPosition)
        {
            ActionResult = actionResult ?? throw new ArgumentNullException(nameof(actionResult));
        }

        public override string ToString() => $"PegActionResult({ActionResult.GetType().Name})";
    }
}