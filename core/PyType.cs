namespace SharpPy
{
    #region Type System and MRO

    // Python의 type 역할 - C3 선형화 MRO 구현
    public class PyType : PyObject
    {
        public static readonly PyType ObjectType = new PyType("object", new PyType[0]);
        public static readonly PyType TypeType = new PyType("type", new[] { ObjectType });
        public static readonly PyType IntType = new PyType("int", new[] { ObjectType });
        public static readonly PyType StrType = new PyType("str", new[] { ObjectType });
        public static readonly PyType FunctionType = new PyType("function", new[] { ObjectType });
        public static readonly PyType ModuleType = new PyType("module", new[] { ObjectType });

        public string Name { get; }
        public PyType[] BaseTypes { get; }
        public List<PyType> MRO { get; private set; }

        public PyType(string name, PyType[] baseTypes)
        {
            Name = name;
            BaseTypes = baseTypes ?? new PyType[0];
            MRO = CalculateC3MRO();
        }

        public override PyType GetPyType() => TypeType;
        public override string GetTypeName() => "type";

        // C3 선형화 알고리즘 구현
        private List<PyType> CalculateC3MRO()
        {
            if (Name == "object")
            {
                return new List<PyType> { this };
            }

            try
            {
                return C3Linearize(this);
            }
            catch (Exception)
            {
                throw new TypeError($"Cannot create a consistent method resolution order (MRO) for class {Name}");
            }
        }

        private List<PyType> C3Linearize(PyType cls)
        {
            var result = new List<PyType> { cls };

            if (cls.BaseTypes.Length == 0)
            {
                return result;
            }

            // 부모 클래스들의 MRO 수집
            var basesMROs = new List<List<PyType>>();
            foreach (var baseType in cls.BaseTypes)
            {
                basesMROs.Add(new List<PyType>(baseType.MRO));
            }

            // 부모 클래스 리스트도 추가
            basesMROs.Add(new List<PyType>(cls.BaseTypes));

            // C3 merge 수행
            var merged = C3Merge(basesMROs);
            result.AddRange(merged);

            return result;
        }

        private List<PyType> C3Merge(List<List<PyType>> sequences)
        {
            var result = new List<PyType>();

            while (true)
            {
                // 빈 시퀀스들 제거
                sequences = sequences.Where(seq => seq.Count > 0).ToList();

                if (sequences.Count == 0)
                    break;

                PyType candidate = null;

                // 좋은 후보 찾기 (다른 시퀀스의 tail에 없는 head)
                foreach (var seq in sequences)
                {
                    var head = seq[0];
                    var isTail = sequences.Any(s => s.Skip(1).Contains(head));

                    if (!isTail)
                    {
                        candidate = head;
                        break;
                    }
                }

                if (candidate == null)
                {
                    throw new TypeError("Inconsistent MRO");
                }

                result.Add(candidate);

                // 모든 시퀀스에서 candidate 제거
                foreach (var seq in sequences)
                {
                    if (seq.Count > 0 && seq[0] == candidate)
                    {
                        seq.RemoveAt(0);
                    }
                }
            }

            return result;
        }

        public void PrintMRO()
        {
            Console.WriteLine($"{Name} MRO: [{string.Join(", ", MRO.Select(t => t.Name))}]");
        }

        public override string ToString() => $"<class '{Name}'>";
    }
#endregion
}