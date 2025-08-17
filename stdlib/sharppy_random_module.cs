// sharppy_random_module.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    public static class RandomModule
    {
        // Global random state for the module
        private static RandomState _globalState = new RandomState();

        public static PythonModule CreateRandomModule(List<string> searchPaths = null)
        {
            var module = new PythonModule("random", searchPaths);

            // === Seed and State Management ===
            
            // random.seed(a=None)
            module.SetAttribute("seed", new BuiltinFunction("seed", args =>
            {
                if (args.Count > 1)
                    throw new PythonException("TypeError", "seed() takes at most 1 argument");

                if (args.Count == 0 || args[0] is PythonNone)
                {
                    _globalState = new RandomState();
                }
                else if (NumberHelper.IsNumber(args[0]))
                {
                    int seed = NumberHelper.ToInt(args[0]);
                    _globalState = new RandomState(seed);
                }
                else if (args[0] is PythonString str)
                {
                    int seed = str.Value.GetHashCode();
                    _globalState = new RandomState(seed);
                }
                else
                {
                    throw new PythonException("TypeError", "seed() argument must be an int, str, or None");
                }

                return PythonNone.Instance;
            }));

            // random.getstate()
            module.SetAttribute("getstate", new BuiltinFunction("getstate", args =>
            {
                if (args.Count != 0)
                    throw new PythonException("TypeError", "getstate() takes no arguments");

                // Return state as a tuple (simplified version)
                var stateTuple = new PythonTuple();
                stateTuple.Items.Add(PythonInt.Create(3)); // Version number
                stateTuple.Items.Add(PythonInt.Create(_globalState.GetInternalSeed()));
                stateTuple.Items.Add(PythonNone.Instance); // Gauss state
                return stateTuple;
            }));

            // random.setstate(state)
            module.SetAttribute("setstate", new BuiltinFunction("setstate", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "setstate() takes exactly 1 argument");

                if (args[0] is PythonTuple tuple && tuple.Items.Count >= 2)
                {
                    if (tuple.Items[1] is PythonInt seed)
                    {
                        _globalState = new RandomState(seed.Value);
                    }
                }
                else
                {
                    throw new PythonException("TypeError", "setstate() argument must be a state tuple");
                }

                return PythonNone.Instance;
            }));

            // === Basic Random Functions ===

            // random.random() - Random float in [0.0, 1.0)
            module.SetAttribute("random", new BuiltinFunction("random", args =>
            {
                if (args.Count != 0)
                    throw new PythonException("TypeError", "random() takes no arguments");
                return new PythonFloat(_globalState.Random());
            }));

            // random.uniform(a, b) - Random float N such that a <= N <= b
            module.SetAttribute("uniform", new BuiltinFunction("uniform", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "uniform() takes exactly 2 arguments");

                if (!NumberHelper.IsNumber(args[0]) || !NumberHelper.IsNumber(args[1]))
                    throw new PythonException("TypeError", "uniform() arguments must be numbers");

                double a = NumberHelper.ToDouble(args[0]);
                double b = NumberHelper.ToDouble(args[1]);
                
                return new PythonFloat(_globalState.Uniform(a, b));
            }));

            // random.triangular(low=0.0, high=1.0, mode=None)
            module.SetAttribute("triangular", new BuiltinFunction("triangular", args =>
            {
                if (args.Count > 3)
                    throw new PythonException("TypeError", "triangular() takes at most 3 arguments");

                double low = 0.0, high = 1.0, mode;
                
                if (args.Count >= 1 && NumberHelper.IsNumber(args[0]))
                    low = NumberHelper.ToDouble(args[0]);
                if (args.Count >= 2 && NumberHelper.IsNumber(args[1]))
                    high = NumberHelper.ToDouble(args[1]);
                
                // Default mode is midpoint
                mode = (low + high) / 2.0;
                if (args.Count >= 3 && NumberHelper.IsNumber(args[2]))
                    mode = NumberHelper.ToDouble(args[2]);

                return new PythonFloat(_globalState.Triangular(low, high, mode));
            }));

            // === Integer Functions ===

            // random.randint(a, b) - Random integer N such that a <= N <= b
            module.SetAttribute("randint", new BuiltinFunction("randint", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "randint() takes exactly 2 arguments");

                if (!NumberHelper.IsNumber(args[0]) || !NumberHelper.IsNumber(args[1]))
                    throw new PythonException("TypeError", "randint() arguments must be integers");

                int a = NumberHelper.ToInt(args[0]);
                int b = NumberHelper.ToInt(args[1]);
                
                if (a > b)
                    throw new PythonException("ValueError", "randint() lower bound greater than upper bound");

                return PythonInt.Create(_globalState.RandInt(a, b));
            }));

            // random.randrange(start, stop=None, step=1)
            module.SetAttribute("randrange", new BuiltinFunction("randrange", args =>
            {
                if (args.Count < 1 || args.Count > 3)
                    throw new PythonException("TypeError", "randrange() takes 1 to 3 arguments");

                int start, stop, step = 1;

                if (args.Count == 1)
                {
                    start = 0;
                    stop = NumberHelper.ToInt(args[0]);
                }
                else
                {
                    start = NumberHelper.ToInt(args[0]);
                    stop = NumberHelper.ToInt(args[1]);
                    if (args.Count == 3)
                        step = NumberHelper.ToInt(args[2]);
                }

                if (step == 0)
                    throw new PythonException("ValueError", "randrange() step argument must not be zero");

                var range = new List<int>();
                if (step > 0)
                {
                    for (int i = start; i < stop; i += step)
                        range.Add(i);
                }
                else
                {
                    for (int i = start; i > stop; i += step)
                        range.Add(i);
                }

                if (range.Count == 0)
                    throw new PythonException("ValueError", "randrange() empty range");

                int index = _globalState.RandInt(0, range.Count - 1);
                return PythonInt.Create(range[index]);
            }));

            // random.getrandbits(k) - Generate k random bits
            module.SetAttribute("getrandbits", new BuiltinFunction("getrandbits", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "getrandbits() takes exactly 1 argument");

                if (!NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "getrandbits() argument must be an integer");

                int k = NumberHelper.ToInt(args[0]);
                if (k <= 0)
                    throw new PythonException("ValueError", "number of bits must be greater than zero");

                return PythonInt.Create(_globalState.GetRandBits(k));
            }));

            // === Sequence Functions ===

            // random.choice(seq) - Choose a random element from a non-empty sequence
            module.SetAttribute("choice", new BuiltinFunction("choice", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "choice() takes exactly 1 argument");

                List<PythonTypeObject> items = GetSequenceItems(args[0], "choice");
                
                if (items.Count == 0)
                    throw new PythonException("IndexError", "Cannot choose from an empty sequence");

                int index = _globalState.RandInt(0, items.Count - 1);
                return items[index];
            }));

            // random.choices(population, weights=None, k=1)
            module.SetAttribute("choices", new BuiltinFunction("choices", args =>
            {
                if (args.Count < 1 || args.Count > 3)
                    throw new PythonException("TypeError", "choices() takes 1 to 3 arguments");

                List<PythonTypeObject> population = GetSequenceItems(args[0], "choices");
                if (population.Count == 0)
                    throw new PythonException("ValueError", "Cannot choose from an empty population");

                List<double> weights = null;
                int k = 1;

                // Parse weights (second argument)
                if (args.Count >= 2 && !(args[1] is PythonNone))
                {
                    if (args[1] is PythonList weightList)
                    {
                        weights = new List<double>();
                        foreach (var w in weightList.Items)
                        {
                            if (!NumberHelper.IsNumber(w))
                                throw new PythonException("TypeError", "weights must be numeric");
                            weights.Add(NumberHelper.ToDouble(w));
                        }
                        
                        if (weights.Count != population.Count)
                            throw new PythonException("ValueError", "weights and population must be the same length");
                    }
                    else
                    {
                        throw new PythonException("TypeError", "weights must be a list");
                    }
                }

                // Parse k (third argument)
                if (args.Count >= 3)
                {
                    if (!NumberHelper.IsNumber(args[2]))
                        throw new PythonException("TypeError", "k must be an integer");
                    k = NumberHelper.ToInt(args[2]);
                    if (k < 0)
                        throw new PythonException("ValueError", "k must be non-negative");
                }

                var result = new PythonList();
                for (int i = 0; i < k; i++)
                {
                    if (weights != null)
                    {
                        result.Items.Add(_globalState.WeightedChoice(population, weights));
                    }
                    else
                    {
                        int index = _globalState.RandInt(0, population.Count - 1);
                        result.Items.Add(population[index]);
                    }
                }

                return result;
            }));

            // random.shuffle(x) - Shuffle list x in place
            module.SetAttribute("shuffle", new BuiltinFunction("shuffle", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "shuffle() takes exactly 1 argument");

                if (!(args[0] is PythonList list))
                    throw new PythonException("TypeError", "shuffle() argument must be a list");

                for (int i = list.Items.Count - 1; i > 0; i--)
                {
                    int j = _globalState.RandInt(0, i);
                    (list.Items[i], list.Items[j]) = (list.Items[j], list.Items[i]);
                }

                return PythonNone.Instance;
            }));

            // random.sample(population, k) - Choose k unique random elements
            module.SetAttribute("sample", new BuiltinFunction("sample", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "sample() takes exactly 2 arguments");

                List<PythonTypeObject> population = GetSequenceItems(args[0], "sample");
                
                if (!NumberHelper.IsNumber(args[1]))
                    throw new PythonException("TypeError", "sample() k must be an integer");
                
                int k = NumberHelper.ToInt(args[1]);
                
                if (k < 0)
                    throw new PythonException("ValueError", "sample size must be non-negative");
                if (k > population.Count)
                    throw new PythonException("ValueError", "sample larger than population");

                // Create a copy and shuffle
                var indices = Enumerable.Range(0, population.Count).ToList();
                var result = new PythonList();
                
                for (int i = 0; i < k; i++)
                {
                    int j = _globalState.RandInt(i, population.Count - 1);
                    (indices[i], indices[j]) = (indices[j], indices[i]);
                    result.Items.Add(population[indices[i]]);
                }

                return result;
            }));

            // === Distribution Functions ===

            // random.gauss(mu, sigma) - Gaussian distribution
            module.SetAttribute("gauss", new BuiltinFunction("gauss", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "gauss() takes exactly 2 arguments");

                if (!NumberHelper.IsNumber(args[0]) || !NumberHelper.IsNumber(args[1]))
                    throw new PythonException("TypeError", "gauss() arguments must be numbers");

                double mu = NumberHelper.ToDouble(args[0]);
                double sigma = NumberHelper.ToDouble(args[1]);
                
                return new PythonFloat(_globalState.Gauss(mu, sigma));
            }));

            // random.normalvariate(mu, sigma) - Normal distribution
            module.SetAttribute("normalvariate", new BuiltinFunction("normalvariate", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "normalvariate() takes exactly 2 arguments");

                if (!NumberHelper.IsNumber(args[0]) || !NumberHelper.IsNumber(args[1]))
                    throw new PythonException("TypeError", "normalvariate() arguments must be numbers");

                double mu = NumberHelper.ToDouble(args[0]);
                double sigma = NumberHelper.ToDouble(args[1]);
                
                return new PythonFloat(_globalState.NormalVariate(mu, sigma));
            }));

            // random.expovariate(lambd) - Exponential distribution
            module.SetAttribute("expovariate", new BuiltinFunction("expovariate", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "expovariate() takes exactly 1 argument");

                if (!NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "expovariate() argument must be a number");

                double lambd = NumberHelper.ToDouble(args[0]);
                if (lambd <= 0.0)
                    throw new PythonException("ValueError", "expovariate() lambd must be positive");
                
                return new PythonFloat(_globalState.ExpoVariate(lambd));
            }));

            // random.gammavariate(alpha, beta) - Gamma distribution
            module.SetAttribute("gammavariate", new BuiltinFunction("gammavariate", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "gammavariate() takes exactly 2 arguments");

                if (!NumberHelper.IsNumber(args[0]) || !NumberHelper.IsNumber(args[1]))
                    throw new PythonException("TypeError", "gammavariate() arguments must be numbers");

                double alpha = NumberHelper.ToDouble(args[0]);
                double beta = NumberHelper.ToDouble(args[1]);
                
                if (alpha <= 0.0 || beta <= 0.0)
                    throw new PythonException("ValueError", "gammavariate() alpha and beta must be positive");
                
                return new PythonFloat(_globalState.GammaVariate(alpha, beta));
            }));

            // random.betavariate(alpha, beta) - Beta distribution
            module.SetAttribute("betavariate", new BuiltinFunction("betavariate", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "betavariate() takes exactly 2 arguments");

                if (!NumberHelper.IsNumber(args[0]) || !NumberHelper.IsNumber(args[1]))
                    throw new PythonException("TypeError", "betavariate() arguments must be numbers");

                double alpha = NumberHelper.ToDouble(args[0]);
                double beta = NumberHelper.ToDouble(args[1]);
                
                if (alpha <= 0.0 || beta <= 0.0)
                    throw new PythonException("ValueError", "betavariate() alpha and beta must be positive");
                
                return new PythonFloat(_globalState.BetaVariate(alpha, beta));
            }));

            // random.paretovariate(alpha) - Pareto distribution
            module.SetAttribute("paretovariate", new BuiltinFunction("paretovariate", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "paretovariate() takes exactly 1 argument");

                if (!NumberHelper.IsNumber(args[0]))
                    throw new PythonException("TypeError", "paretovariate() argument must be a number");

                double alpha = NumberHelper.ToDouble(args[0]);
                if (alpha <= 0.0)
                    throw new PythonException("ValueError", "paretovariate() alpha must be positive");
                
                return new PythonFloat(_globalState.ParetoVariate(alpha));
            }));

            // random.weibullvariate(alpha, beta) - Weibull distribution
            module.SetAttribute("weibullvariate", new BuiltinFunction("weibullvariate", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "weibullvariate() takes exactly 2 arguments");

                if (!NumberHelper.IsNumber(args[0]) || !NumberHelper.IsNumber(args[1]))
                    throw new PythonException("TypeError", "weibullvariate() arguments must be numbers");

                double alpha = NumberHelper.ToDouble(args[0]);
                double beta = NumberHelper.ToDouble(args[1]);
                
                if (alpha <= 0.0 || beta <= 0.0)
                    throw new PythonException("ValueError", "weibullvariate() alpha and beta must be positive");
                
                return new PythonFloat(_globalState.WeibullVariate(alpha, beta));
            }));

            // === Constants ===
            
            // Random.VERSION (compatibility constant)
            module.SetAttribute("VERSION", PythonInt.Create(3));

            return module;
        }

        private static List<PythonTypeObject> GetSequenceItems(PythonTypeObject obj, string funcName)
        {
            switch (obj)
            {
                case PythonList list:
                    return new List<PythonTypeObject>(list.Items);
                case PythonTuple tuple:
                    return new List<PythonTypeObject>(tuple.Items);
                case PythonString str:
                    var chars = new List<PythonTypeObject>();
                    foreach (char c in str.Value)
                        chars.Add(new PythonString(c.ToString()));
                    return chars;
                default:
                    throw new PythonException("TypeError", $"{funcName}() argument must be a sequence");
            }
        }
    }

    // Internal random state management
    internal class RandomState
    {
        private Random _random;
        private int _seed;
        
        // For Gaussian distribution (Box-Muller transform)
        private bool _hasGaussNext = false;
        private double _gaussNext;

        public RandomState()
        {
            // 방법 1: System.Environment를 명시적으로 사용
            _seed = System.Environment.TickCount;
            
            // 방법 2: DateTime을 사용한 대안 (더 나은 엔트로피)
            // _seed = unchecked((int)DateTime.Now.Ticks);
            
            // 방법 3: Guid를 사용한 대안 (더 나은 무작위성)
            // _seed = Guid.NewGuid().GetHashCode();
            _random = new Random(_seed);
        }

        public RandomState(int seed)
        {
            _seed = seed;
            _random = new Random(seed);
        }

        public int GetInternalSeed() => _seed;

        public double Random() => _random.NextDouble();

        public double Uniform(double a, double b)
        {
            double range = b - a;
            return a + _random.NextDouble() * range;
        }

        public double Triangular(double low, double high, double mode)
        {
            double u = _random.NextDouble();
            double c = (mode - low) / (high - low);
            
            if (u <= c)
            {
                return low + Math.Sqrt(u * (high - low) * (mode - low));
            }
            else
            {
                return high - Math.Sqrt((1 - u) * (high - low) * (high - mode));
            }
        }

        public int RandInt(int a, int b)
        {
            if (a > b) throw new ArgumentException("Lower bound greater than upper bound");
            return _random.Next(a, b + 1);
        }

        public int GetRandBits(int k)
        {
            if (k <= 0) throw new ArgumentException("Number of bits must be positive");
            if (k > 31) k = 31; // Limit to int size
            
            int maxValue = (1 << k) - 1;
            return _random.Next(0, maxValue + 1);
        }

        public PythonTypeObject WeightedChoice(List<PythonTypeObject> population, List<double> weights)
        {
            double totalWeight = weights.Sum();
            double randomValue = _random.NextDouble() * totalWeight;
            
            double cumulative = 0.0;
            for (int i = 0; i < population.Count; i++)
            {
                cumulative += weights[i];
                if (randomValue <= cumulative)
                    return population[i];
            }
            
            return population[population.Count - 1];
        }

        // Box-Muller transform for Gaussian distribution
        public double Gauss(double mu, double sigma)
        {
            if (_hasGaussNext)
            {
                _hasGaussNext = false;
                return mu + sigma * _gaussNext;
            }

            double u1 = 1.0 - _random.NextDouble(); // (0, 1]
            double u2 = _random.NextDouble(); // [0, 1)
            
            double z0 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            double z1 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            
            _gaussNext = z1;
            _hasGaussNext = true;
            
            return mu + sigma * z0;
        }

        public double NormalVariate(double mu, double sigma)
        {
            // Same as Gauss but using a different algorithm name for compatibility
            return Gauss(mu, sigma);
        }

        public double ExpoVariate(double lambd)
        {
            return -Math.Log(1.0 - _random.NextDouble()) / lambd;
        }

        public double GammaVariate(double alpha, double beta)
        {
            // Simplified implementation using Marsaglia and Tsang method
            if (alpha > 1)
            {
                double d = alpha - 1.0 / 3.0;
                double c = 1.0 / Math.Sqrt(9.0 * d);
                
                while (true)
                {
                    double x = Gauss(0, 1);
                    double v = Math.Pow(1.0 + c * x, 3);
                    
                    if (v > 0)
                    {
                        double u = _random.NextDouble();
                        if (u < 1 - 0.0331 * x * x * x * x)
                            return d * v / beta;
                        if (Math.Log(u) < 0.5 * x * x + d * (1 - v + Math.Log(v)))
                            return d * v / beta;
                    }
                }
            }
            else
            {
                // For alpha <= 1, use Johnk's generator
                return GammaVariate(alpha + 1, beta) * Math.Pow(_random.NextDouble(), 1.0 / alpha);
            }
        }

        public double BetaVariate(double alpha, double beta)
        {
            double y1 = GammaVariate(alpha, 1.0);
            double y2 = GammaVariate(beta, 1.0);
            return y1 / (y1 + y2);
        }

        public double ParetoVariate(double alpha)
        {
            return 1.0 / Math.Pow(1.0 - _random.NextDouble(), 1.0 / alpha);
        }

        public double WeibullVariate(double alpha, double beta)
        {
            return alpha * Math.Pow(-Math.Log(1.0 - _random.NextDouble()), 1.0 / beta);
        }
    }
}