using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// CPython 3.12 _random 모듈 구현 - Random 클래스만 제공 (C 확장 모듈)
    /// random.py가 이 클래스를 상속받아 모든 기능 구현
    /// </summary>
    public static class RandomModule
    {
        public static PyModule CreateRandomModule()
        {
            var module = new PyModule("_random");

            // CPython 3.12 호환: Random 클래스만 제공
            var randomClass = new PyClass("Random", new[] { PyType.ObjectType }, new Dictionary<string, PyObject>());
            randomClass.ClassDict["random"] = new PyBuiltinMethod("random", RandomMethod);
            randomClass.ClassDict["seed"] = new PyBuiltinMethod("seed", SeedMethod);
            randomClass.ClassDict["getstate"] = new PyBuiltinMethod("getstate", GetStateMethod);
            randomClass.ClassDict["setstate"] = new PyBuiltinMethod("setstate", SetStateMethod);
            randomClass.ClassDict["getrandbits"] = new PyBuiltinMethod("getrandbits", GetRandBitsMethod);

            module.ModuleDict["Random"] = randomClass;

            return module;
        }

        // Random 인스턴스별 상태 저장
        private static Dictionary<PyObject, Random> _instances = new Dictionary<PyObject, Random>();

        private static Random GetOrCreateRandom(PyObject self)
        {
            if (!_instances.ContainsKey(self))
            {
                _instances[self] = new Random();
            }
            return _instances[self];
        }

        private static PyObject RandomMethod(PyObject self, PyObject[] args)
        {
            var rng = GetOrCreateRandom(self);
            return new PyFloat(rng.NextDouble());
        }

        private static PyObject SeedMethod(PyObject self, PyObject[] args)
        {
            int seed = args.Length > 0 && args[0] != PyNone.Instance
                ? (int)((PyInt)args[0]).Value
                : Environment.TickCount;
            _instances[self] = new Random(seed);
            return PyNone.Instance;
        }

        private static PyObject GetStateMethod(PyObject self, PyObject[] args)
        {
            // 간단한 구현: CPython 호환 형식으로 더미 상태 반환
            // 실제 구현은 Mersenne Twister 상태 624개 정수 배열 반환
            return new PyTuple(new PyObject[] {
                new PyInt(3),  // 버전
                new PyTuple(new PyObject[0]),  // 상태 (빈 배열)
                PyNone.Instance
            });
        }

        private static PyObject SetStateMethod(PyObject self, PyObject[] args)
        {
            // 간단한 구현: 상태 복원 (완전하지 않음)
            return PyNone.Instance;
        }

        private static PyObject GetRandBitsMethod(PyObject self, PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("getrandbits() missing required argument: 'k' (pos 1)");

            int k = (int)((PyInt)args[0]).Value;
            if (k <= 0)
                throw PyValueError.Create("number of bits must be greater than zero");

            var rng = GetOrCreateRandom(self);

            // k 비트의 난수 생성
            long result = 0;
            for (int i = 0; i < k; i++)
            {
                if (rng.Next(2) == 1)
                    result |= (1L << i);
            }
            return new PyInt(result);
        }
    }
}
