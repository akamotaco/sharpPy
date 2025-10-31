using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// Minimal statistics module for SharpPy
    /// Implements basic statistical functions needed for random.py test function
    /// Compatible with CPython 3.12 statistics module API
    /// </summary>
    public static class StatisticsModule
    {
        public static PyModule CreateStatisticsModule()
        {
            var module = new PyModule("statistics", null);

            // Basic statistics functions
            module.ModuleDict["mean"] = new PyBuiltinFunction("mean", (args, kwargs) => Mean(args, kwargs));
            module.ModuleDict["fmean"] = new PyBuiltinFunction("fmean", (args, kwargs) => FMean(args, kwargs));
            module.ModuleDict["stdev"] = new PyBuiltinFunction("stdev", (args, kwargs) => StDev(args, kwargs));

            return module;
        }

        /// <summary>
        /// Return the sample arithmetic mean of data (integers or floats)
        /// mean(data) -> float or Fraction
        /// </summary>
        private static PyObject Mean(PyObject[] args, PyDict kwargs)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("mean() requires at least one argument");

            PyObject data = args[0];

            // Convert to list of numbers
            List<double> values = ExtractNumericValues(data);

            if (values.Count == 0)
                throw PyValueError.Create("mean requires at least one data point");

            // Performance: Eliminated LINQ
            double sum = 0.0;
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
            }
            double mean = sum / values.Count;

            return new PyFloat(mean);
        }

        /// <summary>
        /// Return the sample arithmetic mean of data as a floating point number
        /// fmean(data) -> float
        /// Faster than mean() and always returns a float
        /// </summary>
        private static PyObject FMean(PyObject[] args, PyDict kwargs)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("fmean() requires at least one argument");

            PyObject data = args[0];

            // Convert to list of numbers
            List<double> values = ExtractNumericValues(data);

            if (values.Count == 0)
                throw PyValueError.Create("fmean requires at least one data point");

            // Performance: Eliminated LINQ
            double sum = 0.0;
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
            }
            double mean = sum / values.Count;

            return new PyFloat(mean);
        }

        /// <summary>
        /// Return the sample standard deviation (square root of the sample variance)
        /// stdev(data, xbar=None) -> float
        /// </summary>
        private static PyObject StDev(PyObject[] args, PyDict kwargs)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("stdev() requires at least one argument");

            PyObject data = args[0];
            PyObject? xbar = args.Length > 1 ? args[1] : null;

            // If xbar not provided in positional args, check kwargs
            if (xbar == null && kwargs != null)
            {
                var xbarValue = kwargs.GetItem(new PyString("xbar"));
                if (xbarValue != null && xbarValue is not PyNone)
                {
                    xbar = xbarValue;
                }
            }

            // Convert to list of numbers
            List<double> values = ExtractNumericValues(data);

            if (values.Count < 2)
                throw PyValueError.Create("stdev requires at least two data points");

            // Calculate mean if not provided
            double mean;
            if (xbar == null || xbar is PyNone)
            {
                // Performance: Eliminated LINQ
                double sum = 0.0;
                for (int i = 0; i < values.Count; i++)
                {
                    sum += values[i];
                }
                mean = sum / values.Count;
            }
            else
            {
                mean = xbar.ToFloat();
            }

            // Calculate variance (sample variance, using n-1)
            double sumSquaredDiff = 0.0;
            foreach (double value in values)
            {
                double diff = value - mean;
                sumSquaredDiff += diff * diff;
            }

            double variance = sumSquaredDiff / (values.Count - 1);
            double stdev = Math.Sqrt(variance);

            return new PyFloat(stdev);
        }

        /// <summary>
        /// Extract numeric values from a Python iterable object
        /// Supports: list, tuple, range, iterator, etc.
        /// </summary>
        private static List<double> ExtractNumericValues(PyObject data)
        {
            List<double> values = new List<double>();

            // Handle different iterable types
            if (data is PyList list)
            {
                foreach (var item in list.Items)
                {
                    values.Add(item.ToFloat());
                }
            }
            else if (data is PyTuple tuple)
            {
                foreach (var item in tuple.Items)
                {
                    values.Add(item.ToFloat());
                }
            }
            else if (data is PyRange range)
            {
                // PyRange is iterable
                var iter = range.GetIterator();
                PyObject? item;
                while ((item = iter.Next()) != null)
                {
                    values.Add(item.ToFloat());
                }
            }
            else
            {
                // Try to iterate as a generic iterable
                try
                {
                    var iter = data.GetIterator();
                    PyObject? item;
                    while ((item = iter.Next()) != null)
                    {
                        values.Add(item.ToFloat());
                    }
                }
                catch
                {
                    throw PyTypeError.Create($"'{data.GetTypeName()}' object is not iterable");
                }
            }

            return values;
        }
    }
}
