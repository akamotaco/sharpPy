using System;
using System.Collections.Generic;

namespace SharpPy
{
    // Control flow statements
    public class IfStatement : Statement
    {
        public override string NodeType => "If";
        public Expression Test { get; }
        public List<Statement> Body { get; }
        public List<Statement> OrElse { get; }

        public IfStatement(Expression test, List<Statement> body, List<Statement> orElse)
        {
            Test = test;
            Body = body;
            OrElse = orElse;
        }

        public override PyObject Evaluate(PyScope scope)
        {
            var testResult = Test.Evaluate(scope);
            if (testResult.ToBool())
            {
                PyObject result = PyNone.Instance;
                foreach (var stmt in Body)
                {
                    result = stmt.Evaluate(scope);
                }
                return result;
            }
            // Performance: Eliminated LINQ - replaced Any() with Count check
            else if (OrElse != null && OrElse.Count > 0)
            {
                PyObject result = PyNone.Instance;
                foreach (var stmt in OrElse)
                {
                    result = stmt.Evaluate(scope);
                }
                return result;
            }
            return PyNone.Instance;
        }

        public override string ToString() => $"if {Test}: ...";
    }
}