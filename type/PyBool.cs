using System;

namespace SharpPy
{
    /// <summary>
    /// Python bool 타입 구현 - True/False 불린 값
    /// </summary>
    public class PyBool : PyObject
    {
        #region Core Properties

        public bool Value { get; }
        
        public static readonly PyBool True = new PyBool(true);
        public static readonly PyBool False = new PyBool(false);

        private PyBool(bool value) => Value = value;

        public static PyBool FromBool(bool value) => value ? True : False;

        public override PyType GetPyType() => PyType.BoolType;
        public override string GetTypeName() => "bool";

        #endregion

        #region String Representation

        public override string ToStr() => Value ? "True" : "False";
        public override string ToRepr() => Value ? "True" : "False";
        public override string ToString() => ToStr();

        #endregion

        #region Hash and Equality

        public override int ToHash() => Value ? 1 : 0;

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyBool otherBool => FromBool(Value == otherBool.Value),
                PyInt otherInt => FromBool((Value ? 1 : 0) == otherInt.Value),
                PyFloat otherFloat => FromBool((Value ? 1.0 : 0.0) == otherFloat.Value),
                _ => False
            };
        }

        #endregion

        #region Comparison Operations

        protected override PyObject PyLess(PyObject other)
        {
            return other switch
            {
                PyBool otherBool => FromBool(!Value && otherBool.Value), // False < True
                PyInt otherInt => FromBool((Value ? 1 : 0) < otherInt.Value),
                PyFloat otherFloat => FromBool((Value ? 1.0 : 0.0) < otherFloat.Value),
                _ => throw PyTypeError.Create($"'<' not supported between instances of 'bool' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            return other switch
            {
                PyBool otherBool => FromBool(!Value || otherBool.Value), // False <= anything, True <= True
                PyInt otherInt => FromBool((Value ? 1 : 0) <= otherInt.Value),
                PyFloat otherFloat => FromBool((Value ? 1.0 : 0.0) <= otherFloat.Value),
                _ => throw PyTypeError.Create($"'<=' not supported between instances of 'bool' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreater(PyObject other)
        {
            return other switch
            {
                PyBool otherBool => FromBool(Value && !otherBool.Value), // True > False
                PyInt otherInt => FromBool((Value ? 1 : 0) > otherInt.Value),
                PyFloat otherFloat => FromBool((Value ? 1.0 : 0.0) > otherFloat.Value),
                _ => throw PyTypeError.Create($"'>' not supported between instances of 'bool' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            return other switch
            {
                PyBool otherBool => FromBool(Value || !otherBool.Value), // True >= anything, False >= False
                PyInt otherInt => FromBool((Value ? 1 : 0) >= otherInt.Value),
                PyFloat otherFloat => FromBool((Value ? 1.0 : 0.0) >= otherFloat.Value),
                _ => throw PyTypeError.Create($"'>=' not supported between instances of 'bool' and '{other.GetTypeName()}'")
            };
        }

        #endregion

        #region Arithmetic Operations (bool은 int의 서브클래스처럼 동작)

        public override PyObject Add(PyObject other)
        {
            int boolAsInt = Value ? 1 : 0;
            return other switch
            {
                PyBool otherBool => new PyInt(boolAsInt + (otherBool.Value ? 1 : 0)),
                PyInt otherInt => new PyInt(boolAsInt + otherInt.Value),
                PyFloat otherFloat => new PyFloat(boolAsInt + otherFloat.Value),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Subtract(PyObject other)
        {
            int boolAsInt = Value ? 1 : 0;
            return other switch
            {
                PyBool otherBool => new PyInt(boolAsInt - (otherBool.Value ? 1 : 0)),
                PyInt otherInt => new PyInt(boolAsInt - otherInt.Value),
                PyFloat otherFloat => new PyFloat(boolAsInt - otherFloat.Value),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Multiply(PyObject other)
        {
            int boolAsInt = Value ? 1 : 0;
            return other switch
            {
                PyBool otherBool => new PyInt(boolAsInt * (otherBool.Value ? 1 : 0)),
                PyInt otherInt => new PyInt(boolAsInt * otherInt.Value),
                PyFloat otherFloat => new PyFloat(boolAsInt * otherFloat.Value),
                _ => PyNotImplemented.Instance
            };
        }

        public override PyObject Divide(PyObject other)
        {
            double boolAsDouble = Value ? 1.0 : 0.0;
            
            double otherValue = other switch
            {
                PyBool otherBool => otherBool.Value ? 1.0 : 0.0,
                PyInt otherInt => (double)otherInt.Value,
                PyFloat otherFloat => otherFloat.Value,
                _ => throw PyTypeError.Create($"unsupported operand type(s) for /: 'bool' and '{other.GetTypeName()}'")
            };

            if (otherValue == 0.0)
                throw PyZeroDivisionError.Create("division by zero");

            return new PyFloat(boolAsDouble / otherValue);
        }

        public override PyObject FloorDivide(PyObject other)
        {
            int boolAsInt = Value ? 1 : 0;
            
            if (other is PyBool otherBool)
            {
                if (!otherBool.Value)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return new PyInt(boolAsInt);
            }
            
            if (other is PyInt otherInt)
            {
                if (otherInt.Value == 0)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return new PyInt(boolAsInt / otherInt.Value);
            }
            
            if (other is PyFloat otherFloat)
            {
                if (otherFloat.Value == 0.0)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return new PyFloat(Math.Floor(boolAsInt / otherFloat.Value));
            }
            
            throw PyTypeError.Create($"unsupported operand type(s) for //: 'bool' and '{other.GetTypeName()}'");
        }

        public PyObject Modulo(PyObject other)
        {
            int boolAsInt = Value ? 1 : 0;
            
            if (other is PyBool otherBool)
            {
                if (!otherBool.Value)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return new PyInt(0); // 1 % 1 = 0, 0 % 1 = 0
            }
            
            if (other is PyInt otherInt)
            {
                if (otherInt.Value == 0)
                    throw PyZeroDivisionError.Create("integer division or modulo by zero");
                return new PyInt(boolAsInt % otherInt.Value);
            }
            
            if (other is PyFloat otherFloat)
            {
                if (otherFloat.Value == 0.0)
                    throw PyZeroDivisionError.Create("float modulo");
                return new PyFloat(boolAsInt % otherFloat.Value);
            }
            
            throw PyTypeError.Create($"unsupported operand type(s) for %: 'bool' and '{other.GetTypeName()}'");
        }

        public override PyObject Power(PyObject other)
        {
            int boolAsInt = Value ? 1 : 0;
            
            if (other is PyBool otherBool)
            {
                int otherAsInt = otherBool.Value ? 1 : 0;
                return new PyInt((int)Math.Pow(boolAsInt, otherAsInt));
            }
            
            if (other is PyInt otherInt)
            {
                if (otherInt.Value < 0)
                    return new PyFloat(Math.Pow(boolAsInt, otherInt.Value));
                return new PyInt((int)Math.Pow(boolAsInt, otherInt.Value));
            }
            
            throw PyTypeError.Create($"unsupported operand type(s) for ** or pow(): 'bool' and '{other.GetTypeName()}'");
        }

        #endregion

        #region Bitwise Operations

        public PyObject BitwiseAnd(PyObject other)
        {
            int boolAsInt = Value ? 1 : 0;
            return other switch
            {
                PyBool otherBool => new PyBool(Value && otherBool.Value),
                PyInt otherInt => new PyInt(boolAsInt & otherInt.Value),
                _ => throw PyTypeError.Create($"unsupported operand type(s) for &: 'bool' and '{other.GetTypeName()}'")
            };
        }

        public PyObject BitwiseOr(PyObject other)
        {
            int boolAsInt = Value ? 1 : 0;
            return other switch
            {
                PyBool otherBool => new PyBool(Value || otherBool.Value),
                PyInt otherInt => new PyInt(boolAsInt | otherInt.Value),
                _ => throw PyTypeError.Create($"unsupported operand type(s) for |: 'bool' and '{other.GetTypeName()}'")
            };
        }

        public PyObject BitwiseXor(PyObject other)
        {
            int boolAsInt = Value ? 1 : 0;
            return other switch
            {
                PyBool otherBool => new PyBool(Value ^ otherBool.Value),
                PyInt otherInt => new PyInt(boolAsInt ^ otherInt.Value),
                _ => throw PyTypeError.Create($"unsupported operand type(s) for ^: 'bool' and '{other.GetTypeName()}'")
            };
        }

        #endregion

        #region Unary Operations

        public PyObject Negative() => new PyInt(Value ? -1 : 0);
        public PyObject Positive() => new PyInt(Value ? 1 : 0);
        public PyObject Absolute() => new PyInt(Value ? 1 : 0);
        public PyObject Invert() => new PyInt(Value ? -2 : -1); // ~True = -2, ~False = -1

        #endregion

        #region Type Conversion

        public override int ToInt() => Value ? 1 : 0;
        public override double ToFloat() => Value ? 1.0 : 0.0;
        public override bool PyBoolValue() => Value;

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            // Boolean literals evaluate to themselves (CPython style)
            return this;
        }

        #endregion
    }
}