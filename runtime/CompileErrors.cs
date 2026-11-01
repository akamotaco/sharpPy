using System;

namespace SharpPy
{
    // CPython 3.12 스타일 SyntaxError 예외 클래스
    public class PySyntaxErrorException : Exception
    {
        public string? FileName { get; set; }
        public int LineNumber { get; set; } = -1;
        public int ColumnOffset { get; set; } = -1;
        public int EndColumnOffset { get; set; } = -1;
        public string? SourceLine { get; set; }

        public PySyntaxErrorException(string message) : base(message) { }

        public PySyntaxErrorException(string message, string fileName, int lineNumber, int columnOffset)
            : base(message)
        {
            FileName = fileName;
            LineNumber = lineNumber;
            ColumnOffset = columnOffset;
        }

        public PySyntaxErrorException(string message, string fileName, int lineNumber, int columnOffset, int endColumnOffset, string sourceLine)
            : base(message)
        {
            FileName = fileName;
            LineNumber = lineNumber;
            ColumnOffset = columnOffset;
            EndColumnOffset = endColumnOffset;
            SourceLine = sourceLine;
        }
    }

    public class SyntaxErrorException : Exception
    {
        public string? FileName { get; }
        public int LineNumber { get; }
        public int ColumnOffset { get; }
        public string Text { get; }

        public SyntaxErrorException(string message, string? fileName, int lineNumber, int columnOffset)
            : base($"SyntaxError: {message} (file {fileName}, line {lineNumber}, column {columnOffset})")
        {
            Text = message;
            FileName = fileName;
            LineNumber = lineNumber;
            ColumnOffset = columnOffset;
        }

        public int EndLineNumber { get; }
        public int EndColumnOffset { get; }

        public SyntaxErrorException(string message, string? fileName, int lineNumber, int columnOffset, int endLine, int endColumn)
            : this(message, fileName, lineNumber, columnOffset)
        {
            EndLineNumber = endLine;
            EndColumnOffset = endColumn;
        }

        public SyntaxErrorException(string message) : base($"SyntaxError: {message}")
        {
            Text = message;
        }
    }

    // CPython 3.12 호환: MAKE_FUNCTION 플래그 상수
    public static class MakeFunctionFlags
    {
        public const int DEFAULTS = 0x01;        // 기본값 있음
        public const int KWDEFAULTS = 0x02;      // 키워드 기본값 있음
        public const int ANNOTATIONS = 0x04;     // 타입 어노테이션 있음
        public const int CLOSURE = 0x08;         // 클로저 있음
    }
    
}
