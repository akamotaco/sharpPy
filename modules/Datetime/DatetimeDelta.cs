using System;

namespace SharpPy.Modules.Datetime
{
    /// <summary>Modules/_datetimemodule.c: PyDateTime_Delta implementation (lines 1842-2849).</summary>
    internal static partial class DatetimeModule
    {
        // delta_to_microseconds
        internal static PyObject DeltaToMicroseconds(PyDateTimeDelta self)
        {
            var x1 = PyInt.From(self.Days);
            var x2 = Number(x1, seconds_per_day, BinaryOpType.MULTIPLY);
            x1 = PyInt.From(self.Seconds);
            var x3 = Number(x1, x2, BinaryOpType.ADD);
            var us = Number(x3, us_per_second, BinaryOpType.MULTIPLY);
            return Number(us, PyInt.From(self.Microseconds), BinaryOpType.ADD);
        }

        // checked_divmod
        internal static PyTuple CheckedDivmod(PyObject a, PyObject b)
        {
            var result = Number(a, b, BinaryOpType.DIVMOD);
            if (result is not PyTuple tuple)
                throw PyTypeError.Create($"divmod() returned non-tuple (type {TpName(result)})");
            if (tuple.Items.Length != 2)
                throw PyTypeError.Create($"divmod() returned a tuple of size {tuple.Items.Length}");
            return tuple;
        }

        // _PyLong_AsInt
        private static int LongAsInt(PyObject num) => PyArgs.Int(num);

        // microseconds_to_delta_ex
        internal static PyObject MicrosecondsToDeltaEx(PyObject pyus, PyType type)
        {
            var tuple = CheckedDivmod(pyus, us_per_second);
            int us = LongAsInt(tuple.Items[1]);
            if (!(0 <= us && us < 1000000))
                throw PyTypeError.Create("divmod() returned a value out of range");
            var num = tuple.Items[0];
            tuple = CheckedDivmod(num, seconds_per_day);
            int s = LongAsInt(tuple.Items[1]);
            if (!(0 <= s && s < 24 * 3600))
                throw PyTypeError.Create("divmod() returned a value out of range");
            int d = LongAsInt(tuple.Items[0]);
            return NewDeltaEx(d, s, us, false, type);
        }

        // microseconds_to_delta
        internal static PyObject MicrosecondsToDelta(PyObject pymicros) => MicrosecondsToDeltaEx(pymicros, DeltaType);

        // multiply_int_timedelta
        private static PyObject MultiplyIntTimedelta(PyObject intobj, PyDateTimeDelta delta)
        {
            var pyus_in = DeltaToMicroseconds(delta);
            var pyus_out = Number(intobj, pyus_in, BinaryOpType.MULTIPLY);
            return MicrosecondsToDelta(pyus_out);
        }

        // get_float_as_integer_ratio
        private static PyTuple GetFloatAsIntegerRatio(PyObject floatobj)
        {
            var ratio = floatobj.GetAttribute("as_integer_ratio").Call(Array.Empty<PyObject>(), null);
            if (ratio is not PyTuple tuple)
                throw PyTypeError.Create($"unexpected return type from as_integer_ratio(): expected tuple, got '{TpName(ratio)}'");
            if (tuple.Items.Length != 2)
                throw PyValueError.Create("as_integer_ratio() must return a 2-tuple");
            return tuple;
        }

        // multiply_truedivide_timedelta_float: op 0 multiplies, 1 divides.
        private static PyObject MultiplyTruedivideTimedeltaFloat(PyDateTimeDelta delta, PyObject floatobj, int op)
        {
            var pyus_in = DeltaToMicroseconds(delta);
            var ratio = GetFloatAsIntegerRatio(floatobj);
            var temp = Number(pyus_in, ratio.Items[op], BinaryOpType.MULTIPLY);
            var pyus_out = DivideNearest(temp, ratio.Items[op == 0 ? 1 : 0]);
            return MicrosecondsToDelta(pyus_out);
        }

        // divide_timedelta_int
        private static PyObject DivideTimedeltaInt(PyDateTimeDelta delta, PyObject intobj)
        {
            var pyus_in = DeltaToMicroseconds(delta);
            var pyus_out = Number(pyus_in, intobj, BinaryOpType.FLOOR_DIVIDE);
            return MicrosecondsToDelta(pyus_out);
        }

        // divide_timedelta_timedelta
        private static PyObject DivideTimedeltaTimedelta(PyDateTimeDelta left, PyDateTimeDelta right)
        {
            var pyus_left = DeltaToMicroseconds(left);
            var pyus_right = DeltaToMicroseconds(right);
            return Number(pyus_left, pyus_right, BinaryOpType.FLOOR_DIVIDE);
        }

        // truedivide_timedelta_timedelta
        private static PyObject TruedivideTimedeltaTimedelta(PyDateTimeDelta left, PyDateTimeDelta right)
        {
            var pyus_left = DeltaToMicroseconds(left);
            var pyus_right = DeltaToMicroseconds(right);
            return Number(pyus_left, pyus_right, BinaryOpType.TRUE_DIVIDE);
        }

        // truedivide_timedelta_int
        private static PyObject TruedivideTimedeltaInt(PyDateTimeDelta delta, PyObject i)
        {
            var pyus_in = DeltaToMicroseconds(delta);
            // _PyLong_DivmodNear reads both as ints.
            var pyus_out = DivideNearest(pyus_in, PyNumberSlots.AsLong(i));
            return MicrosecondsToDelta(pyus_out);
        }

        // delta_add
        internal static PyObject DeltaAdd(PyObject left, PyObject right)
        {
            if (left is PyDateTimeDelta l && right is PyDateTimeDelta r)
            {
                int days = l.Days + r.Days;
                int seconds = l.Seconds + r.Seconds;
                int microseconds = l.Microseconds + r.Microseconds;
                return NewDelta(days, seconds, microseconds, true);
            }
            return PyNotImplemented.Instance;
        }

        // delta_negative
        internal static PyObject DeltaNegative(PyDateTimeDelta self) => NewDelta(-self.Days, -self.Seconds, -self.Microseconds, true);

        // delta_positive
        internal static PyObject DeltaPositive(PyDateTimeDelta self) => NewDelta(self.Days, self.Seconds, self.Microseconds, false);

        // delta_abs
        internal static PyObject DeltaAbs(PyDateTimeDelta self) => self.Days < 0 ? DeltaNegative(self) : DeltaPositive(self);

        // delta_subtract
        internal static PyObject DeltaSubtract(PyObject left, PyObject right)
        {
            if (left is PyDateTimeDelta l && right is PyDateTimeDelta r)
            {
                int days = l.Days - r.Days;
                int seconds = l.Seconds - r.Seconds;
                int microseconds = l.Microseconds - r.Microseconds;
                return NewDelta(days, seconds, microseconds, true);
            }
            return PyNotImplemented.Instance;
        }

        // delta_cmp
        private static int DeltaCmp(PyDateTimeDelta self, PyDateTimeDelta other)
        {
            int diff = self.Days - other.Days;
            if (diff == 0)
            {
                diff = self.Seconds - other.Seconds;
                if (diff == 0)
                    diff = self.Microseconds - other.Microseconds;
            }
            return diff;
        }

        // delta_richcompare
        internal static PyObject DeltaRichCompare(PyObject self, PyObject other, PyObject.CompareOp op)
        {
            if (other is PyDateTimeDelta o)
                return DiffToBool(DeltaCmp((PyDateTimeDelta)self, o), op);
            return PyNotImplemented.Instance;
        }

        // delta_hash
        internal static long DeltaHash(PyDateTimeDelta self)
        {
            if (self.Hashcode == -1)
                self.Hashcode = DeltaGetstate(self).GetPythonHash();
            return self.Hashcode;
        }

        // delta_multiply
        internal static PyObject DeltaMultiply(PyObject left, PyObject right)
        {
            if (left is PyDateTimeDelta l)
            {
                if (PyLongCheck(right))
                    return MultiplyIntTimedelta(right, l);
                if (PyFloatCheck(right))
                    return MultiplyTruedivideTimedeltaFloat(l, right, 0);
            }
            else if (PyLongCheck(left))
                return MultiplyIntTimedelta(left, (PyDateTimeDelta)right);
            else if (PyFloatCheck(left))
                return MultiplyTruedivideTimedeltaFloat((PyDateTimeDelta)right, left, 0);
            return PyNotImplemented.Instance;
        }

        // delta_divide
        internal static PyObject DeltaDivide(PyObject left, PyObject right)
        {
            if (left is PyDateTimeDelta l)
            {
                if (PyLongCheck(right))
                    return DivideTimedeltaInt(l, right);
                if (right is PyDateTimeDelta r)
                    return DivideTimedeltaTimedelta(l, r);
            }
            return PyNotImplemented.Instance;
        }

        // delta_truedivide
        internal static PyObject DeltaTruedivide(PyObject left, PyObject right)
        {
            if (left is PyDateTimeDelta l)
            {
                if (right is PyDateTimeDelta r)
                    return TruedivideTimedeltaTimedelta(l, r);
                if (PyFloatCheck(right))
                    return MultiplyTruedivideTimedeltaFloat(l, right, 1);
                if (PyLongCheck(right))
                    return TruedivideTimedeltaInt(l, right);
            }
            return PyNotImplemented.Instance;
        }

        // delta_remainder
        internal static PyObject DeltaRemainder(PyObject left, PyObject right)
        {
            if (left is not PyDateTimeDelta l || right is not PyDateTimeDelta r)
                return PyNotImplemented.Instance;
            var pyus_left = DeltaToMicroseconds(l);
            var pyus_right = DeltaToMicroseconds(r);
            var pyus_remainder = Number(pyus_left, pyus_right, BinaryOpType.MODULO);
            return MicrosecondsToDelta(pyus_remainder);
        }

        // delta_divmod
        internal static PyObject DeltaDivmod(PyObject left, PyObject right)
        {
            if (left is not PyDateTimeDelta l || right is not PyDateTimeDelta r)
                return PyNotImplemented.Instance;
            var pyus_left = DeltaToMicroseconds(l);
            var pyus_right = DeltaToMicroseconds(r);
            var divmod = CheckedDivmod(pyus_left, pyus_right);
            var delta = MicrosecondsToDelta(divmod.Items[1]);
            return PyTuple.From(divmod.Items[0], delta);
        }

        // modf
        private static double Modf(double value, out double intpart)
        {
            if (double.IsInfinity(value))
            {
                intpart = value;
                return value > 0 ? 0.0 : -0.0;
            }
            intpart = Math.Truncate(value);
            return value - intpart;
        }

        // PyLong_FromDouble
        private static PyObject LongFromDouble(double value)
        {
            if (double.IsInfinity(value))
                throw PyOverflowError.Create("cannot convert float infinity to integer");
            if (double.IsNaN(value))
                throw PyValueError.Create("cannot convert float NaN to integer");
            return PyInt.From(new System.Numerics.BigInteger(value));
        }

        // accum
        private static PyObject Accum(string tag, PyObject sofar, PyObject num, PyObject factor, ref double leftover)
        {
            if (PyLongCheck(num))
            {
                var prod = Number(num, factor, BinaryOpType.MULTIPLY);
                return Number(sofar, prod, BinaryOpType.ADD);
            }
            if (PyFloatCheck(num))
            {
                double dnum = num.ToFloat();
                double fracpart = Modf(dnum, out double intpart);
                var x = LongFromDouble(intpart);
                var prod = Number(x, factor, BinaryOpType.MULTIPLY);
                var sum = Number(sofar, prod, BinaryOpType.ADD);
                if (fracpart == 0.0)
                    return sum;
                dnum = ((PyInt)factor).AsDouble();
                dnum *= fracpart;
                fracpart = Modf(dnum, out intpart);
                x = LongFromDouble(intpart);
                var y = Number(sum, x, BinaryOpType.ADD);
                leftover += fracpart;
                return y;
            }
            throw PyTypeError.Create($"unsupported type for timedelta {tag} component: {TpName(num)}");
        }

        private static readonly string[] delta_keywords =
            { "days", "seconds", "microseconds", "milliseconds", "minutes", "hours", "weeks" };

        // delta_new
        internal static PyObject DeltaNew(PyType type, PyObject[] args, PyDict kw)
        {
            var parsed = ParseTupleAndKeywords("__new__", args, kw, delta_keywords, 0);
            PyObject day = parsed[0], second = parsed[1], us = parsed[2], ms = parsed[3],
                minute = parsed[4], hour = parsed[5], week = parsed[6];
            PyObject x = PyInt.From(0);
            double leftover_us = 0.0;
            if (us != null)
                x = Accum("microseconds", x, us, PyInt.From(1), ref leftover_us);
            if (ms != null)
                x = Accum("milliseconds", x, ms, us_per_ms, ref leftover_us);
            if (second != null)
                x = Accum("seconds", x, second, us_per_second, ref leftover_us);
            if (minute != null)
                x = Accum("minutes", x, minute, us_per_minute, ref leftover_us);
            if (hour != null)
                x = Accum("hours", x, hour, us_per_hour, ref leftover_us);
            if (day != null)
                x = Accum("days", x, day, us_per_day, ref leftover_us);
            if (week != null)
                x = Accum("weeks", x, week, us_per_week, ref leftover_us);
            if (leftover_us != 0)
            {
                // round() of C: half away from zero.
                double whole_us = Math.Round(leftover_us, MidpointRounding.AwayFromZero);
                if (Math.Abs(whole_us - leftover_us) == 0.5)
                {
                    var temp = Number(x, PyInt.From(1), BinaryOpType.AND);
                    int x_is_odd = temp.IsTrue() ? 1 : 0;
                    whole_us = 2.0 * Math.Round((leftover_us + x_is_odd) * 0.5, MidpointRounding.AwayFromZero) - x_is_odd;
                }
                // (long)whole_us: a C long, 32 bits on Windows.
                x = Number(x, PyInt.From(OperatingSystem.IsWindows() ? (int)whole_us : (long)whole_us), BinaryOpType.ADD);
            }
            return MicrosecondsToDeltaEx(x, type);
        }

        // delta_bool
        internal static bool DeltaBool(PyDateTimeDelta self) => self.Days != 0 || self.Seconds != 0 || self.Microseconds != 0;

        // delta_repr
        internal static PyObject DeltaRepr(PyDateTimeDelta self)
        {
            string args = "";
            string sep = "";
            if (self.Days != 0)
            {
                args = $"days={self.Days}";
                sep = ", ";
            }
            if (self.Seconds != 0)
            {
                args = $"{args}{sep}seconds={self.Seconds}";
                sep = ", ";
            }
            if (self.Microseconds != 0)
                args = $"{args}{sep}microseconds={self.Microseconds}";
            if (args.Length == 0)
                args = "0";
            return PyString.From($"{TpName(self)}({args})");
        }

        // delta_str
        internal static PyObject DeltaStr(PyDateTimeDelta self)
        {
            int us = self.Microseconds;
            int seconds = self.Seconds;
            int minutes = Divmod(seconds, 60, out seconds);
            int hours = Divmod(minutes, 60, out minutes);
            int days = self.Days;
            string plural = days == 1 || days == -1 ? "" : "s";
            if (days != 0)
            {
                if (us != 0)
                    return PyString.From($"{days} day{plural}, {hours}:{minutes:D2}:{seconds:D2}.{us:D6}");
                return PyString.From($"{days} day{plural}, {hours}:{minutes:D2}:{seconds:D2}");
            }
            if (us != 0)
                return PyString.From($"{hours}:{minutes:D2}:{seconds:D2}.{us:D6}");
            return PyString.From($"{hours}:{minutes:D2}:{seconds:D2}");
        }

        // delta_getstate
        internal static PyObject DeltaGetstate(PyDateTimeDelta self) =>
            PyTuple.From(PyInt.From(self.Days), PyInt.From(self.Seconds), PyInt.From(self.Microseconds));

        // delta_total_seconds
        private static PyObject DeltaTotalSeconds(PyObject self)
        {
            var total_microseconds = DeltaToMicroseconds((PyDateTimeDelta)self);
            return Number(total_microseconds, us_per_second, BinaryOpType.TRUE_DIVIDE);
        }

        // delta_reduce
        private static PyObject DeltaReduce(PyDateTimeDelta self) => PyTuple.From(self.GetPyType(), DeltaGetstate(self));

        // PyDateTime_DeltaType
        private static PyType MakeDeltaType()
        {
            var type = new PyType("timedelta", new[] { PyType.ObjectType }, "datetime");
            DeltaType = type;
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type,
                (cls, args, kwargs) => DeltaNew((PyType)cls, args, kwargs), 0, int.MaxValue, acceptsKwargs: true);
            Slot(type, "__repr__", (self, a) => DeltaRepr((PyDateTimeDelta)self));
            Slot(type, "__hash__", (self, a) => PyInt.From(DeltaHash((PyDateTimeDelta)self)));
            Slot(type, "__str__", (self, a) => DeltaStr((PyDateTimeDelta)self));
            TypeTables.GenericAttributes(type);
            RichCompareSlots(type, (self, other, op) => DeltaRichCompare(self, other, op));
            BinarySlots(type, "add", DeltaAdd);
            BinarySlots(type, "sub", DeltaSubtract);
            BinarySlots(type, "mul", DeltaMultiply);
            BinarySlots(type, "mod", DeltaRemainder);
            BinarySlots(type, "divmod", DeltaDivmod);
            Slot(type, "__neg__", (self, a) => DeltaNegative((PyDateTimeDelta)self));
            Slot(type, "__pos__", (self, a) => DeltaPositive((PyDateTimeDelta)self));
            Slot(type, "__abs__", (self, a) => DeltaAbs((PyDateTimeDelta)self));
            Slot(type, "__bool__", (self, a) => PyBool.FromBool(DeltaBool((PyDateTimeDelta)self)));
            BinarySlots(type, "floordiv", DeltaDivide);
            BinarySlots(type, "truediv", DeltaTruedivide);
            // delta_methods
            PyArgs.Define(type, "total_seconds", PyCall.NoArgs, (self, args) => DeltaTotalSeconds(self));
            PyArgs.Define(type, "__reduce__", PyCall.NoArgs, (self, args) => DeltaReduce((PyDateTimeDelta)self));
            // delta_members: T_INT READONLY
            type.TypeDict["days"] = new PyMemberDescriptor("days", type, self => PyInt.From(((PyDateTimeDelta)self).Days), readOnly: true);
            type.TypeDict["seconds"] = new PyMemberDescriptor("seconds", type, self => PyInt.From(((PyDateTimeDelta)self).Seconds), readOnly: true);
            type.TypeDict["microseconds"] = new PyMemberDescriptor("microseconds", type, self => PyInt.From(((PyDateTimeDelta)self).Microseconds), readOnly: true);
            return type;
        }
    }
}
