namespace PurePythonInterpreter
{
    // Operator Nodes
    public class BinaryOpNode : ASTNode
    {
        public ASTNode Left { get; }
        public string Operator { get; }
        public ASTNode Right { get; }

        public BinaryOpNode(ASTNode left, string op, ASTNode right)
        {
            Left = left;
            Operator = op;
            Right = right;
        }

        public override object Evaluate(Environment env)
        {
            var leftVal = Left.Evaluate(env);
            var rightVal = Right.Evaluate(env);

            return Operator switch
            {
                "+" => Add(leftVal, rightVal),
                "-" => Subtract(leftVal, rightVal),
                "*" => Multiply(leftVal, rightVal),
                "/" => Divide(leftVal, rightVal),
                "%" => Modulo(leftVal, rightVal),
                "**" => Power(leftVal, rightVal),
                "==" => IsEqual(leftVal, rightVal),
                "!=" => !IsEqual(leftVal, rightVal),
                "<" => IsLess(leftVal, rightVal),
                ">" => IsGreater(leftVal, rightVal),
                "<=" => IsLessOrEqual(leftVal, rightVal),
                ">=" => IsGreaterOrEqual(leftVal, rightVal),
                "and" => IsTrue(leftVal) && IsTrue(rightVal),
                "or" => IsTrue(leftVal) || IsTrue(rightVal),
                "in" => IsIn(leftVal, rightVal),
                "is" => IsIdentical(leftVal, rightVal),
                "is not" => !IsIdentical(leftVal, rightVal),
                _ => throw new PythonException("TypeError", $"Unknown operator: {Operator}")
            };
        }

        private object Add(object left, object right)
        {
            if (left is double l && right is double r) return l + r;
            if (left is string || right is string) return left?.ToString() + right?.ToString();
            if (left is PythonList ll && right is PythonList rl)
            {
                var newList = new PythonList();
                newList.Items.AddRange(ll.Items);
                newList.Items.AddRange(rl.Items);
                return newList;
            }
            if (left is PythonTuple lt && right is PythonTuple rt)
            {
                var newTuple = new PythonTuple();
                newTuple.Items.AddRange(lt.Items);
                newTuple.Items.AddRange(rt.Items);
                return newTuple;
            }
            if (left is PythonDict ld && right is PythonDict rd)
            {
                var newDict = new PythonDict();
                foreach (var kvp in ld.Items) newDict.Items[kvp.Key] = kvp.Value;
                foreach (var kvp in rd.Items) newDict.Items[kvp.Key] = kvp.Value;
                return newDict;
            }
            throw new PythonException("TypeError", $"Cannot add {GetTypeName(left)} and {GetTypeName(right)}");
        }

        private object Subtract(object left, object right)
        {
            if (left is double l && right is double r) return l - r;
            throw new PythonException("TypeError", $"Cannot subtract {GetTypeName(right)} from {GetTypeName(left)}");
        }

        private object Multiply(object left, object right)
        {
            if (left is double l && right is double r) return l * r;
            if (left is string s && right is double n) return string.Concat(Enumerable.Repeat(s, (int)n));
            if (left is double n2 && right is string s2) return string.Concat(Enumerable.Repeat(s2, (int)n2));
            if (left is PythonList list && right is double n3)
            {
                var newList = new PythonList();
                for (int i = 0; i < (int)n3; i++)
                    newList.Items.AddRange(list.Items);
                return newList;
            }
            if (left is double n4 && right is PythonList list2)
            {
                var newList = new PythonList();
                for (int i = 0; i < (int)n4; i++)
                    newList.Items.AddRange(list2.Items);
                return newList;
            }
            if (left is PythonTuple tuple && right is double n5)
            {
                var newTuple = new PythonTuple();
                for (int i = 0; i < (int)n5; i++)
                    newTuple.Items.AddRange(tuple.Items);
                return newTuple;
            }
            if (left is double n6 && right is PythonTuple tuple2)
            {
                var newTuple = new PythonTuple();
                for (int i = 0; i < (int)n6; i++)
                    newTuple.Items.AddRange(tuple2.Items);
                return newTuple;
            }
            throw new PythonException("TypeError", $"Cannot multiply {GetTypeName(left)} and {GetTypeName(right)}");
        }

        private object Divide(object left, object right)
        {
            if (left is double l && right is double r)
            {
                if (r == 0) throw new PythonException("ZeroDivisionError", "Division by zero");
                return l / r;
            }
            throw new PythonException("TypeError", $"Cannot divide {GetTypeName(left)} by {GetTypeName(right)}");
        }

        private object Modulo(object left, object right)
        {
            if (left is double l && right is double r) 
            {
                if (r == 0) throw new PythonException("ZeroDivisionError", "Modulo by zero");
                return l % r;
            }
            throw new PythonException("TypeError", $"Cannot modulo {GetTypeName(left)} by {GetTypeName(right)}");
        }

        private object Power(object left, object right)
        {
            if (left is double l && right is double r) return Math.Pow(l, r);
            throw new PythonException("TypeError", $"Cannot raise {GetTypeName(left)} to power of {GetTypeName(right)}");
        }

        private bool IsEqual(object left, object right) => Equals(left, right);
        
        private bool IsLess(object left, object right)
        {
            if (left is double l && right is double r) return l < r;
            if (left is string ls && right is string rs) return string.Compare(ls, rs) < 0;
            throw new PythonException("TypeError", $"'<' not supported between instances of '{GetTypeName(left)}' and '{GetTypeName(right)}'");
        }
        
        private bool IsGreater(object left, object right)
        {
            if (left is double l && right is double r) return l > r;
            if (left is string ls && right is string rs) return string.Compare(ls, rs) > 0;
            throw new PythonException("TypeError", $"'>' not supported between instances of '{GetTypeName(left)}' and '{GetTypeName(right)}'");
        }
        
        private bool IsLessOrEqual(object left, object right) 
        {
            try { return IsEqual(left, right) || IsLess(left, right); }
            catch { throw new PythonException("TypeError", $"'<=' not supported between instances of '{GetTypeName(left)}' and '{GetTypeName(right)}'"); }
        }
        
        private bool IsGreaterOrEqual(object left, object right) 
        {
            try { return IsEqual(left, right) || IsGreater(left, right); }
            catch { throw new PythonException("TypeError", $"'>=' not supported between instances of '{GetTypeName(left)}' and '{GetTypeName(right)}'"); }
        }

        private bool IsIn(object item, object container)
        {
            if (container is PythonList list) return list.Items.Contains(item);
            if (container is PythonTuple tuple) return tuple.Items.Contains(item);
            if (container is string str && item is string s) return str.Contains(s);
            if (container is PythonDict dict) return dict.Items.ContainsKey(item);
            throw new PythonException("TypeError", $"argument of type '{GetTypeName(container)}' is not iterable");
        }

        private bool IsIdentical(object left, object right)
        {
            // None 비교 - Python에서 가장 일반적인 "is" 사용법
            if (left == null && right == null) return true;
            if (left == null || right == null) return false;
            
            // 동일한 참조인지 확인
            if (ReferenceEquals(left, right)) return true;
            
            // Python에서 작은 정수들과 일부 문자열은 인턴된다 (같은 객체를 재사용)
            // 이를 시뮬레이션하기 위해 특정 값들에 대해 값 비교를 한다
            if (left is double ld && right is double rd)
            {
                // 작은 정수들 (-5 ~ 256)은 Python에서 인턴됨
                if (ld == rd && ld >= -5 && ld <= 256 && ld == Math.Truncate(ld))
                    return true;
            }
            
            // 빈 튜플은 싱글톤
            if (left is PythonTuple lt && right is PythonTuple rt)
            {
                if (lt.Items.Count == 0 && rt.Items.Count == 0)
                    return true;
            }
            
            // 불린 값들은 싱글톤
            if (left is bool lb && right is bool rb)
                return lb == rb;
            
            // 작은 문자열들도 종종 인턴됨 (단순화를 위해 길이 1인 문자열만)
            if (left is string ls && right is string rs)
            {
                if (ls.Length <= 1 && rs.Length <= 1)
                    return ls == rs;
            }
            
            return false;
        }

        private bool IsTrue(object obj)
        {
            if (obj == null) return false;
            if (obj is bool b) return b;
            if (obj is double d) return d != 0;
            if (obj is string s) return !string.IsNullOrEmpty(s);
            if (obj is PythonList l) return l.Items.Count > 0;
            if (obj is PythonTuple t) return t.Items.Count > 0;
            if (obj is PythonDict dict) return dict.Items.Count > 0;
            return true;
        }

        private string GetTypeName(object obj)
        {
            return obj switch
            {
                null => "NoneType",
                bool => "bool",
                double => "int",
                string => "str",
                PythonList => "list",
                PythonTuple => "tuple",
                PythonDict => "dict",
                _ => obj.GetType().Name
            };
        }
    }

    public class UnaryOpNode : ASTNode
    {
        public string Operator { get; }
        public ASTNode Operand { get; }

        public UnaryOpNode(string op, ASTNode operand)
        {
            Operator = op;
            Operand = operand;
        }

        public override object Evaluate(Environment env)
        {
            var value = Operand.Evaluate(env);
            return Operator switch
            {
                "-" => value is double d ? -d : throw new PythonException("TypeError", "Cannot negate non-number"),
                "not" => !IsTrue(value),
                _ => throw new PythonException("TypeError", $"Unknown unary operator: {Operator}")
            };
        }

        private bool IsTrue(object obj)
        {
            if (obj == null) return false;
            if (obj is bool b) return b;
            if (obj is double d) return d != 0;
            if (obj is string s) return !string.IsNullOrEmpty(s);
            if (obj is PythonList l) return l.Items.Count > 0;
            if (obj is PythonTuple t) return t.Items.Count > 0;
            if (obj is PythonDict dict) return dict.Items.Count > 0;
            return true;
        }
    }

    // Assignment Nodes
    public class AssignmentNode : ASTNode
    {
        public string VariableName { get; }
        public ASTNode Value { get; }

        public AssignmentNode(string name, ASTNode value)
        {
            VariableName = name;
            Value = value;
        }

        public override object Evaluate(Environment env)
        {
            var value = Value.Evaluate(env);
            env.SetVariable(VariableName, value);
            return value;
        }
    }

    public class MultipleAssignmentNode : ASTNode
    {
        public List<string> VariableNames { get; }
        public ASTNode Value { get; }

        public MultipleAssignmentNode(List<string> names, ASTNode value)
        {
            VariableNames = names;
            Value = value;
        }

        public override object Evaluate(Environment env)
        {
            var value = Value.Evaluate(env);
            
            // 값을 iterable로 변환
            List<object> items;
            if (value is PythonList list)
                items = list.Items;
            else if (value is PythonTuple tuple)
                items = tuple.Items;
            else if (value is string str)
                items = str.Select(c => c.ToString()).Cast<object>().ToList();
            else
                throw new PythonException("TypeError", "Cannot unpack non-iterable object");

            // 언더스코어(_) 처리 - 무시할 변수들
            var validNames = new List<string>();
            var validIndices = new List<int>();
            
            for (int i = 0; i < VariableNames.Count; i++)
            {
                if (VariableNames[i] != "_")
                {
                    validNames.Add(VariableNames[i]);
                    validIndices.Add(i);
                }
            }

            // 길이 검증 (언더스코어는 제외하고)
            if (items.Count != VariableNames.Count)
                throw new PythonException("ValueError", $"Cannot unpack {items.Count} values into {VariableNames.Count} variables");

            // 변수에 값 할당 (언더스코어는 건너뛰기)
            for (int i = 0; i < validNames.Count; i++)
            {
                int actualIndex = validIndices[i];
                env.SetVariable(validNames[i], items[actualIndex]);
            }

            return value;
        }
    }

    public class IndexAssignmentNode : ASTNode
    {
        public ASTNode Object { get; }
        public ASTNode Index { get; }
        public ASTNode Value { get; }

        public IndexAssignmentNode(ASTNode obj, ASTNode index, ASTNode value)
        {
            Object = obj;
            Index = index;
            Value = value;
        }

        public override object Evaluate(Environment env)
        {
            var obj = Object.Evaluate(env);
            var index = Index.Evaluate(env);
            var value = Value.Evaluate(env);

            if (obj is PythonList list && index is double d)
            {
                int i = (int)d;
                if (i < 0) i += list.Items.Count;
                if (i >= 0 && i < list.Items.Count)
                {
                    list.Items[i] = value;
                    return value;
                }
                throw new PythonException("IndexError", "list assignment index out of range");
            }
            else if (obj is PythonDict dict)
            {
                dict.Items[index] = value;
                return value;
            }

            throw new PythonException("TypeError", $"'{obj?.GetType()}' object does not support item assignment");
        }
    }

    public class AttributeAssignmentNode : ASTNode
    {
        public ASTNode Object { get; }
        public string Attribute { get; }
        public ASTNode Value { get; }

        public AttributeAssignmentNode(ASTNode obj, string attribute, ASTNode value)
        {
            Object = obj;
            Attribute = attribute;
            Value = value;
        }

        public override object Evaluate(Environment env)
        {
            var obj = Object.Evaluate(env);
            var value = Value.Evaluate(env);

            if (obj is PythonInstance instance)
            {
                instance.SetAttribute(Attribute, value);
                return value;
            }

            throw new PythonException("AttributeError", $"'{obj?.GetType()}' object has no attribute '{Attribute}'");
        }
    }
}