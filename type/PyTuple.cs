using System;
using System.Linq;

namespace SharpPy
{
    /// <summary>
    /// Python tuple 타입 구현 - 불변 순서 컬렉션
    /// </summary>
    public class PyTuple : PyObject
    {
        #region Core Properties

        public PyObject[] Items { get; }

        public PyTuple(params PyObject[] items) => Items = items ?? new PyObject[0];

        public override PyType GetPyType() => PyType.TupleType;
        public override string GetTypeName() => "tuple";

        #endregion

        #region String Representation

        public override string ToStr() => ToString();
        
        public override string ToRepr()
        {
            if (Items.Length == 0) return "()";
            if (Items.Length == 1) return $"({Items[0].ToRepr()},)";
            return $"({string.Join(", ", Items.Select(i => i.ToRepr()))})";
        }

        public override string ToString() => ToRepr();

        #endregion

        #region Hash and Equality

        public override int ToHash()
        {
            int hash = 0x345678;
            foreach (var item in Items)
            {
                hash = ((hash << 5) + hash) ^ item.ToHash();
            }
            return hash;
        }

        protected override PyObject PyEquals(PyObject other)
        {
            return other switch
            {
                PyTuple otherTuple => PyBool.FromBool(Items.Length == otherTuple.Items.Length && 
                    Items.Zip(otherTuple.Items, (a, b) => ((PyBool)a.RichCompare(b, CompareOp.EQ)).Value).All(x => x)),
                _ => PyBool.False
            };
        }

        #endregion

        #region Comparison Operations

        protected override PyObject PyLess(PyObject other)
        {
            return other switch
            {
                PyTuple otherTuple => PyBool.FromBool(CompareTuples(otherTuple) < 0),
                _ => throw PyTypeError.Create($"'<' not supported between instances of 'tuple' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyLessEqual(PyObject other)
        {
            return other switch
            {
                PyTuple otherTuple => PyBool.FromBool(CompareTuples(otherTuple) <= 0),
                _ => throw PyTypeError.Create($"'<=' not supported between instances of 'tuple' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreater(PyObject other)
        {
            return other switch
            {
                PyTuple otherTuple => PyBool.FromBool(CompareTuples(otherTuple) > 0),
                _ => throw PyTypeError.Create($"'>' not supported between instances of 'tuple' and '{other.GetTypeName()}'")
            };
        }

        protected override PyObject PyGreaterEqual(PyObject other)
        {
            return other switch
            {
                PyTuple otherTuple => PyBool.FromBool(CompareTuples(otherTuple) >= 0),
                _ => throw PyTypeError.Create($"'>=' not supported between instances of 'tuple' and '{other.GetTypeName()}'")
            };
        }

        private int CompareTuples(PyTuple other)
        {
            int minLength = Math.Min(Items.Length, other.Items.Length);
            for (int i = 0; i < minLength; i++)
            {
                var cmpResult = Items[i].RichCompare(other.Items[i], CompareOp.LT);
                if (((PyBool)cmpResult).Value) return -1;
                
                cmpResult = Items[i].RichCompare(other.Items[i], CompareOp.GT);
                if (((PyBool)cmpResult).Value) return 1;
            }
            return Items.Length.CompareTo(other.Items.Length);
        }

        #endregion

        #region Tuple Operations

        /// <summary>
        /// 튜플 연결 (+ 연산자)
        /// </summary>
        public PyObject Add(PyObject other)
        {
            return other switch
            {
                PyTuple otherTuple => new PyTuple(Items.Concat(otherTuple.Items).ToArray()),
                _ => throw PyTypeError.Create($"can only concatenate tuple (not \"{other.GetTypeName()}\") to tuple")
            };
        }

        /// <summary>
        /// 튜플 반복 (* 연산자)
        /// </summary>
        public PyObject Multiply(PyObject other)
        {
            return other switch
            {
                PyInt count => count.Value <= 0 
                    ? new PyTuple() 
                    : new PyTuple(Enumerable.Range(0, count.Value).SelectMany(_ => Items).ToArray()),
                _ => throw PyTypeError.Create($"can't multiply sequence by non-int of type '{other.GetTypeName()}'")
            };
        }

        /// <summary>
        /// 인덱스 접근 tuple[i]
        /// </summary>
        public PyObject GetItem(int index)
        {
            // Python식 음수 인덱스 지원
            if (index < 0) index += Items.Length;
            
            if (index < 0 || index >= Items.Length)
                throw PyIndexError.Create("tuple index out of range");
            
            return Items[index];
        }

        /// <summary>
        /// 슬라이싱 tuple[start:end:step]
        /// </summary>
        public PyTuple GetSlice(int? start = null, int? end = null, int step = 1)
        {
            if (step == 0)
                throw PyValueError.Create("slice step cannot be zero");
            
            var len = Items.Length;
            var actualStart = start ?? (step > 0 ? 0 : len - 1);
            var actualEnd = end ?? (step > 0 ? len : -1);
            
            // 음수 인덱스 정규화
            if (actualStart < 0) actualStart += len;
            if (actualEnd < 0) actualEnd += len;
            
            var result = new System.Collections.Generic.List<PyObject>();
            
            if (step > 0)
            {
                for (int i = Math.Max(0, actualStart); i < Math.Min(len, actualEnd); i += step)
                {
                    result.Add(Items[i]);
                }
            }
            else
            {
                for (int i = Math.Min(len - 1, actualStart); i > Math.Max(-1, actualEnd); i += step)
                {
                    result.Add(Items[i]);
                }
            }
            
            return new PyTuple(result.ToArray());
        }

        /// <summary>
        /// 요소 포함 여부 확인 (in 연산자)
        /// </summary>
        public PyBool Contains(PyObject item)
        {
            return PyBool.FromBool(Items.Any(x => ((PyBool)x.RichCompare(item, CompareOp.EQ)).Value));
        }

        /// <summary>
        /// 첫 번째 일치하는 요소의 인덱스 반환
        /// </summary>
        public PyInt Index(PyObject value, int start = 0, int? stop = null)
        {
            var actualStop = stop ?? Items.Length;
            
            for (int i = start; i < actualStop && i < Items.Length; i++)
            {
                if (((PyBool)Items[i].RichCompare(value, CompareOp.EQ)).Value)
                    return new PyInt(i);
            }
            
            throw PyValueError.Create($"{value.ToRepr()} is not in tuple");
        }

        /// <summary>
        /// 특정 값의 개수
        /// </summary>
        public PyInt Count(PyObject value)
        {
            int count = Items.Count(item => ((PyBool)item.RichCompare(value, CompareOp.EQ)).Value);
            return new PyInt(count);
        }

        #endregion

        #region Length and Type Checking

        public override int Length() => Items.Length;
        public override bool PyBoolValue() => Items.Length > 0;

        #endregion

        #region Static Factory Methods

        public static PyTuple Empty => new PyTuple();

        #endregion

        #region Evaluate Method (NotImplementedException)

        public PyObject Evaluate(PyScope scope)
        {
            // Tuple literals evaluate to themselves (CPython style)
            return this;
        }

        #endregion

        #region Type Conversion (CPython Compatible)

        // === To* Methods: Value Extraction (PyTuple → C# basic types) ===
        
        /// <summary>
        /// CPython PyLong_AsLong 호환: PyTuple은 일반적으로 int로 변환될 수 없음
        /// </summary>
        public override int ToInt()
        {
            throw PyTypeError.Create($"int() argument must be a string, a bytes-like object or a number, not 'tuple'");
        }
        
        /// <summary>
        /// CPython PyFloat_AsDouble 호환: PyTuple은 일반적으로 float로 변환될 수 없음
        /// </summary>
        public override double ToFloat()
        {
            throw PyTypeError.Create($"float() argument must be a string or a number, not 'tuple'");
        }
        
        
        // === As* Methods: Type Conversion (PyTuple → PyObject types) ===
        
        /// <summary>
        /// CPython 호환: PyTuple을 PyTuple로 변환 (복사본 생성)
        /// </summary>
        public override PyTuple AsTuple()
        {
            // CPython tuple() 생성자 동작: 새로운 복사본 생성
            return new PyTuple(Items.ToArray());
        }
        
        /// <summary>
        /// CPython 호환: PyTuple을 PyList로 변환
        /// </summary>
        public override PyList AsList()
        {
            // CPython list(tuple) 동작: 튜플 요소들을 리스트로 변환
            return new PyList(Items);
        }
        
        /// <summary>
        /// CPython 호환: PyTuple을 PyBool로 변환
        /// </summary>
        public override PyBool AsBool()
        {
            return PyBool.FromBool(Items.Length > 0);
        }
        
        /// <summary>
        /// CPython 호환: PyTuple을 PyString으로 변환 (str() 호출과 동일)
        /// </summary>
        public override PyString AsString()
        {
            return new PyString(ToRepr()); // CPython에서 str(tuple)는 repr(tuple)와 동일
        }

        #endregion
    }
}