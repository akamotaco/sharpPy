using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy.Modules
{
    /// <summary>
    /// Python random 모듈 구현 - 난수 생성 및 확률 분포
    /// </summary>
    public static class RandomModule
    {
        private static Random _random = new Random();
        private static Random _seededRandom = null;

        public static PyModule CreateRandomModule()
        {
            var module = new PyModule("random", "C:\\Users\\m11\\Desktop\\work\\sharpPy\\modules\\random.py");

            // 기본 난수 생성 함수들
            module.ModuleDict["random"] = new PyRandomFunction("random");
            module.ModuleDict["seed"] = new PyRandomFunction("seed");
            module.ModuleDict["getstate"] = new PyRandomFunction("getstate");
            module.ModuleDict["setstate"] = new PyRandomFunction("setstate");

            // 정수 관련 함수들
            module.ModuleDict["randint"] = new PyRandomFunction("randint");
            module.ModuleDict["randrange"] = new PyRandomFunction("randrange");
            module.ModuleDict["getrandbits"] = new PyRandomFunction("getrandbits");

            // 시퀀스 관련 함수들
            module.ModuleDict["choice"] = new PyRandomFunction("choice");
            module.ModuleDict["choices"] = new PyRandomFunction("choices");
            module.ModuleDict["shuffle"] = new PyRandomFunction("shuffle");
            module.ModuleDict["sample"] = new PyRandomFunction("sample");

            // 실수 분포 함수들
            module.ModuleDict["uniform"] = new PyRandomFunction("uniform");
            module.ModuleDict["triangular"] = new PyRandomFunction("triangular");
            module.ModuleDict["betavariate"] = new PyRandomFunction("betavariate");
            module.ModuleDict["expovariate"] = new PyRandomFunction("expovariate");
            module.ModuleDict["gammavariate"] = new PyRandomFunction("gammavariate");
            module.ModuleDict["gauss"] = new PyRandomFunction("gauss");
            module.ModuleDict["lognormvariate"] = new PyRandomFunction("lognormvariate");
            module.ModuleDict["normalvariate"] = new PyRandomFunction("normalvariate");
            module.ModuleDict["vonmisesvariate"] = new PyRandomFunction("vonmisesvariate");
            module.ModuleDict["paretovariate"] = new PyRandomFunction("paretovariate");
            module.ModuleDict["weibullvariate"] = new PyRandomFunction("weibullvariate");

            return module;
        }

        public static Random GetRandom()
        {
            return _seededRandom ?? _random;
        }

        public static void SetSeed(int seed)
        {
            _seededRandom = new Random(seed);
        }

        public static void ResetSeed()
        {
            _seededRandom = null;
        }
    }

    /// <summary>
    /// random 모듈의 함수 구현
    /// </summary>
    public class PyRandomFunction : PyObject
    {
        public string Name { get; }

        public PyRandomFunction(string name)
        {
            Name = name;
        }

        public override string GetTypeName() => "builtin_function_or_method";
        public override bool IsCallable() => true;

        public override PyObject Call(params PyObject[] args)
        {
            return Name switch
            {
                // 기본 함수들
                "random" => CallRandom(args),
                "seed" => CallSeed(args),
                "getstate" => CallGetState(args),
                "setstate" => CallSetState(args),

                // 정수 관련
                "randint" => CallRandint(args),
                "randrange" => CallRandrange(args),
                "getrandbits" => CallGetrandbits(args),

                // 시퀀스 관련
                "choice" => CallChoice(args),
                "choices" => CallChoices(args),
                "shuffle" => CallShuffle(args),
                "sample" => CallSample(args),

                // 실수 분포
                "uniform" => CallUniform(args),
                "triangular" => CallTriangular(args),
                "betavariate" => CallBetavariate(args),
                "expovariate" => CallExpovariate(args),
                "gammavariate" => CallGammavariate(args),
                "gauss" => CallGauss(args),
                "lognormvariate" => CallLognormvariate(args),
                "normalvariate" => CallNormalvariate(args),
                "vonmisesvariate" => CallVonmisesvariate(args),
                "paretovariate" => CallParetovariate(args),
                "weibullvariate" => CallWeibullvariate(args),

                _ => throw PyNotImplementedError.Create($"random.{Name} not implemented")
            };
        }

        // === 기본 함수들 ===

        private PyObject CallRandom(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"random() takes no arguments ({args.Length} given)");

            return new PyFloat(RandomModule.GetRandom().NextDouble());
        }

        private PyObject CallSeed(PyObject[] args)
        {
            if (args.Length > 1)
                throw PyTypeError.Create($"seed() takes at most 1 argument ({args.Length} given)");

            if (args.Length == 0)
            {
                RandomModule.ResetSeed();
            }
            else if (args[0] is PyInt seed)
            {
                RandomModule.SetSeed(seed.Value);
            }
            else if (args[0] == PyNone.Instance)
            {
                RandomModule.ResetSeed();
            }
            else
            {
                throw PyTypeError.Create("seed must be an integer or None");
            }

            return PyNone.Instance;
        }

        private PyObject CallGetState(PyObject[] args)
        {
            if (args.Length != 0)
                throw PyTypeError.Create($"getstate() takes no arguments ({args.Length} given)");

            // 간단한 구현 - 실제 상태를 반환하지는 않음
            return new PyTuple(new PyString("MT19937"), new PyInt(1), PyNone.Instance);
        }

        private PyObject CallSetState(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"setstate() takes exactly one argument ({args.Length} given)");

            // 간단한 구현 - 실제로는 상태를 복원하지 않음
            return PyNone.Instance;
        }

        // === 정수 관련 함수들 ===

        private PyObject CallRandint(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"randint() takes exactly 2 arguments ({args.Length} given)");

            if (args[0] is PyInt a && args[1] is PyInt b)
            {
                if (a.Value > b.Value)
                    throw PyValueError.Create("empty range for randint()");

                return new PyInt(RandomModule.GetRandom().Next(a.Value, b.Value + 1));
            }
            else
            {
                throw PyTypeError.Create("'int' object cannot be interpreted as an integer");
            }
        }

        private PyObject CallRandrange(PyObject[] args)
        {
            int start, stop, step = 1;

            switch (args.Length)
            {
                case 1:
                    if (args[0] is PyInt stop1)
                    {
                        start = 0;
                        stop = stop1.Value;
                    }
                    else
                    {
                        throw PyTypeError.Create("'int' object cannot be interpreted as an integer");
                    }
                    break;

                case 2:
                    if (args[0] is PyInt start2 && args[1] is PyInt stop2)
                    {
                        start = start2.Value;
                        stop = stop2.Value;
                    }
                    else
                    {
                        throw PyTypeError.Create("'int' object cannot be interpreted as an integer");
                    }
                    break;

                case 3:
                    if (args[0] is PyInt start3 && args[1] is PyInt stop3 && args[2] is PyInt step3)
                    {
                        start = start3.Value;
                        stop = stop3.Value;
                        step = step3.Value;
                    }
                    else
                    {
                        throw PyTypeError.Create("'int' object cannot be interpreted as an integer");
                    }
                    break;

                default:
                    throw PyTypeError.Create($"randrange() takes from 1 to 3 positional arguments ({args.Length} given)");
            }

            if (step == 0)
                throw PyValueError.Create("step argument must not be zero");

            if (step > 0 && start >= stop)
                throw PyValueError.Create("empty range for randrange()");

            if (step < 0 && start <= stop)
                throw PyValueError.Create("empty range for randrange()");

            var range = (stop - start) / step;
            var randomIndex = RandomModule.GetRandom().Next(0, Math.Abs(range));
            return new PyInt(start + randomIndex * step);
        }

        private PyObject CallGetrandbits(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"getrandbits() takes exactly one argument ({args.Length} given)");

            if (args[0] is PyInt k)
            {
                if (k.Value <= 0)
                    throw PyValueError.Create("number of bits must be greater than zero");

                var random = RandomModule.GetRandom();
                var result = 0;
                for (int i = 0; i < k.Value; i++)
                {
                    result = (result << 1) | random.Next(0, 2);
                }
                return new PyInt(result);
            }
            else
            {
                throw PyTypeError.Create("'int' object cannot be interpreted as an integer");
            }
        }

        // === 시퀀스 관련 함수들 ===

        private PyObject CallChoice(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"choice() takes exactly one argument ({args.Length} given)");

            var seq = args[0];
            var items = GetSequenceItems(seq);

            if (items.Count == 0)
                throw PyIndexError.Create("cannot choose from an empty sequence");

            var randomIndex = RandomModule.GetRandom().Next(0, items.Count);
            return items[randomIndex];
        }

        private PyObject CallChoices(PyObject[] args)
        {
            if (args.Length < 1)
                throw PyTypeError.Create("choices() missing required argument: 'population'");

            var population = GetSequenceItems(args[0]);
            var k = 1; // 기본값

            // 간단한 구현 - weights와 cum_weights는 무시
            if (args.Length > 1)
            {
                // k 파라미터 찾기
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] is PyInt kVal)
                    {
                        k = kVal.Value;
                        break;
                    }
                }
            }

            var result = new List<PyObject>();
            var random = RandomModule.GetRandom();

            for (int i = 0; i < k; i++)
            {
                var randomIndex = random.Next(0, population.Count);
                result.Add(population[randomIndex]);
            }

            return new PyList(result.ToArray());
        }

        private PyObject CallShuffle(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"shuffle() takes exactly one argument ({args.Length} given)");

            if (args[0] is PyList list)
            {
                var items = list.Items.ToList();
                var random = RandomModule.GetRandom();

                // Fisher-Yates shuffle
                for (int i = items.Count - 1; i > 0; i--)
                {
                    int j = random.Next(0, i + 1);
                    (items[i], items[j]) = (items[j], items[i]);
                }

                // 원본 리스트 수정
                for (int i = 0; i < items.Count; i++)
                {
                    list.Items[i] = items[i];
                }

                return PyNone.Instance;
            }
            else
            {
                throw PyTypeError.Create("shuffle() argument must be a list");
            }
        }

        private PyObject CallSample(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"sample() takes exactly 2 arguments ({args.Length} given)");

            var population = GetSequenceItems(args[0]);
            
            if (args[1] is PyInt k)
            {
                if (k.Value < 0)
                    throw PyValueError.Create("sample size must be non-negative");

                if (k.Value > population.Count)
                    throw PyValueError.Create("sample larger than population");

                var result = new List<PyObject>();
                var availableItems = new List<PyObject>(population);
                var random = RandomModule.GetRandom();

                for (int i = 0; i < k.Value; i++)
                {
                    var randomIndex = random.Next(0, availableItems.Count);
                    result.Add(availableItems[randomIndex]);
                    availableItems.RemoveAt(randomIndex);
                }

                return new PyList(result.ToArray());
            }
            else
            {
                throw PyTypeError.Create("'int' object cannot be interpreted as an integer");
            }
        }

        // === 실수 분포 함수들 ===

        private PyObject CallUniform(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"uniform() takes exactly 2 arguments ({args.Length} given)");

            var a = GetFloatValue(args[0]);
            var b = GetFloatValue(args[1]);

            return new PyFloat(a + RandomModule.GetRandom().NextDouble() * (b - a));
        }

        private PyObject CallTriangular(PyObject[] args)
        {
            if (args.Length < 2 || args.Length > 3)
                throw PyTypeError.Create($"triangular() takes 2 to 3 arguments ({args.Length} given)");

            var low = GetFloatValue(args[0]);
            var high = GetFloatValue(args[1]);
            var mode = args.Length > 2 ? GetFloatValue(args[2]) : (low + high) / 2.0;

            var u = RandomModule.GetRandom().NextDouble();
            var c = (mode - low) / (high - low);

            if (u < c)
            {
                return new PyFloat(low + Math.Sqrt(u * (high - low) * (mode - low)));
            }
            else
            {
                return new PyFloat(high - Math.Sqrt((1 - u) * (high - low) * (high - mode)));
            }
        }

        private PyObject CallGauss(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"gauss() takes exactly 2 arguments ({args.Length} given)");

            var mu = GetFloatValue(args[0]);
            var sigma = GetFloatValue(args[1]);

            // Box-Muller 변환 사용
            var u1 = RandomModule.GetRandom().NextDouble();
            var u2 = RandomModule.GetRandom().NextDouble();
            var z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            return new PyFloat(mu + sigma * z0);
        }

        private PyObject CallNormalvariate(PyObject[] args)
        {
            return CallGauss(args); // gauss와 동일
        }

        private PyObject CallExpovariate(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"expovariate() takes exactly one argument ({args.Length} given)");

            var lambd = GetFloatValue(args[0]);
            if (lambd <= 0)
                throw PyValueError.Create("lambda must be positive");

            var u = RandomModule.GetRandom().NextDouble();
            return new PyFloat(-Math.Log(1 - u) / lambd);
        }

        // 간단한 구현들 (복잡한 분포는 기본 공식 사용)
        private PyObject CallBetavariate(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"betavariate() takes exactly 2 arguments ({args.Length} given)");

            // 간단한 베타 분포 근사
            return new PyFloat(RandomModule.GetRandom().NextDouble());
        }

        private PyObject CallGammavariate(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"gammavariate() takes exactly 2 arguments ({args.Length} given)");

            // 간단한 감마 분포 근사
            return new PyFloat(RandomModule.GetRandom().NextDouble());
        }

        private PyObject CallLognormvariate(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"lognormvariate() takes exactly 2 arguments ({args.Length} given)");

            var mu = GetFloatValue(args[0]);
            var sigma = GetFloatValue(args[1]);

            // 로그 정규 분포 = exp(정규 분포)
            var normal = CallGauss(new PyObject[] { new PyFloat(mu), new PyFloat(sigma) });
            return new PyFloat(Math.Exp(((PyFloat)normal).Value));
        }

        private PyObject CallVonmisesvariate(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"vonmisesvariate() takes exactly 2 arguments ({args.Length} given)");

            // 간단한 von Mises 분포 근사
            return new PyFloat(RandomModule.GetRandom().NextDouble() * 2 * Math.PI);
        }

        private PyObject CallParetovariate(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"paretovariate() takes exactly one argument ({args.Length} given)");

            var alpha = GetFloatValue(args[0]);
            var u = RandomModule.GetRandom().NextDouble();
            return new PyFloat(1.0 / Math.Pow(u, 1.0 / alpha));
        }

        private PyObject CallWeibullvariate(PyObject[] args)
        {
            if (args.Length != 2)
                throw PyTypeError.Create($"weibullvariate() takes exactly 2 arguments ({args.Length} given)");

            var alpha = GetFloatValue(args[0]);
            var beta = GetFloatValue(args[1]);
            var u = RandomModule.GetRandom().NextDouble();
            return new PyFloat(alpha * Math.Pow(-Math.Log(1 - u), 1.0 / beta));
        }

        // === 헬퍼 메서드들 ===

        private List<PyObject> GetSequenceItems(PyObject obj)
        {
            return obj switch
            {
                PyList list => list.Items.ToList(),
                PyTuple tuple => tuple.Items.ToList(),
                PyString str => str.Value.Select(c => new PyString(c.ToString())).Cast<PyObject>().ToList(),
                PyRange range => range.GetValues().Cast<PyObject>().ToList(),
                _ => throw PyTypeError.Create($"'{obj.GetTypeName()}' object is not iterable")
            };
        }

        private double GetFloatValue(PyObject obj)
        {
            return obj switch
            {
                PyFloat f => f.Value,
                PyInt i => (double)i.Value,
                PyBool b => b.Value ? 1.0 : 0.0,
                _ => throw PyTypeError.Create("a float is required")
            };
        }

        public override string ToString() => $"<built-in function {Name}>";

        public override PyObject GetAttribute(string name)
        {
            return name switch
            {
                "__name__" => new PyString(Name),
                "__module__" => new PyString("random"),
                _ => throw PyAttributeError.Create($"'builtin_function_or_method' object has no attribute '{name}'")
            };
        }
    }
}