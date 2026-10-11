using System;
using System.Collections.Generic;

namespace SharpPy
{
    // CPython 3.12.15 Objects/listobject.c: binarysort, count_run,
    // gallop_left/right, merge_lo/hi and powersort scheduling.
    // Comparison order and the permutation retained on errors are observable.
    internal sealed class PyListSort
    {
        private readonly PyObject[] keys;
        private readonly PyObject[] values;
        private PyObject[] tempKeys = Array.Empty<PyObject>();
        private PyObject[] tempValues;
        private int minGallop = 7;
        private readonly List<(int start, int length, int power)> runs = new();

        internal PyListSort(PyObject[] keys, PyObject[] values)
        {
            this.keys = keys;
            this.values = ReferenceEquals(keys, values) ? null : values;
        }

        private static bool Less(PyObject a, PyObject b) =>
            PyObject.CompareBool(a, b, PyObject.CompareOp.LT);

        // Below minrun no merge state or temporary array is needed. Work on
        // the detached backing list to avoid copying small, unkeyed inputs.
        internal static void SortSmall(List<PyObject> items)
        {
            if (items.Count < 2) return;
            int end = 2;
            bool descending = Less(items[1], items[0]);
            while (end < items.Count && Less(items[end], items[end - 1]) == descending) end++;
            if (descending) items.Reverse(0, end);
            for (int next = end; next < items.Count; next++)
            {
                var pivot = items[next];
                int left = 0, right = next;
                while (left < right)
                {
                    int middle = left + ((right - left) >> 1);
                    if (Less(pivot, items[middle])) right = middle;
                    else left = middle + 1;
                }
                for (int i = next; i > left; i--) items[i] = items[i - 1];
                items[left] = pivot;
            }
        }

        private void Move(int target, PyObject[] sourceKeys, PyObject[] sourceValues, int source, int count)
        {
            Array.Copy(sourceKeys, source, keys, target, count);
            if (values != null) Array.Copy(sourceValues, source, values, target, count);
        }

        private void Save(int start, int count)
        {
            if (tempKeys.Length < count)
            {
                tempKeys = new PyObject[count];
                if (values != null) tempValues = new PyObject[count];
            }
            Array.Copy(keys, start, tempKeys, 0, count);
            if (values != null) Array.Copy(values, start, tempValues, 0, count);
        }

        private void Reverse(int start, int count)
        {
            Array.Reverse(keys, start, count);
            if (values != null) Array.Reverse(values, start, count);
        }

        internal void Sort()
        {
            int size = keys.Length;
            if (size < 2) return;
            int minrun = size, remainder = 0;
            while (minrun >= 64) { remainder |= minrun & 1; minrun >>= 1; }
            minrun += remainder;
            for (int start = 0; start < size;)
            {
                int end = start + 1;
                if (end < size)
                {
                    bool descending = Less(keys[end++], keys[start]);
                    while (end < size && Less(keys[end], keys[end - 1]) == descending) end++;
                    if (descending) Reverse(start, end - start);
                }
                int limit = Math.Min(size, start + minrun);
                for (int next = end; next < limit; next++)
                {
                    var pivot = keys[next];
                    var value = values == null ? null : values[next];
                    int left = start, right = next;
                    while (left < right)
                    {
                        int middle = left + ((right - left) >> 1);
                        if (Less(pivot, keys[middle])) right = middle;
                        else left = middle + 1;
                    }
                    Move(left + 1, keys, values, left, next - left);
                    keys[left] = pivot;
                    if (values != null) values[left] = value;
                }
                end = Math.Max(end, limit);
                int length = end - start;
                if (runs.Count > 0)
                {
                    var last = runs[runs.Count - 1];
                    long a = 2L * last.start + last.length;
                    long b = a + last.length + length;
                    int power = 0;
                    while (true)
                    {
                        power++;
                        if (a >= size) { a -= size; b -= size; }
                        else if (b >= size) break;
                        a <<= 1; b <<= 1;
                    }
                    while (runs.Count > 1 && runs[runs.Count - 2].power > power)
                        MergeAt(runs.Count - 2);
                    last = runs[runs.Count - 1];
                    runs[runs.Count - 1] = (last.start, last.length, power);
                }
                runs.Add((start, length, 0));
                start = end;
            }
            while (runs.Count > 1)
            {
                int index = runs.Count - 2;
                if (index > 0 && runs[index - 1].length < runs[index + 1].length) index--;
                MergeAt(index);
            }
        }

        private static int GallopLeft(PyObject key, PyObject[] data, int start, int count, int hint)
        {
            int last = 0, offset = 1;
            if (Less(data[start + hint], key))
            {
                int max = count - hint;
                while (offset < max && Less(data[start + hint + offset], key))
                { last = offset; offset = (int)Math.Min(max, 2L * offset + 1); }
                offset = Math.Min(offset, max);
                last += hint; offset += hint;
            }
            else
            {
                int max = hint + 1;
                while (offset < max && !Less(data[start + hint - offset], key))
                { last = offset; offset = (int)Math.Min(max, 2L * offset + 1); }
                offset = Math.Min(offset, max);
                int previous = last; last = hint - offset; offset = hint - previous;
            }
            for (last++; last < offset;)
            {
                int middle = last + ((offset - last) >> 1);
                if (Less(data[start + middle], key)) last = middle + 1;
                else offset = middle;
            }
            return offset;
        }

        private static int GallopRight(PyObject key, PyObject[] data, int start, int count, int hint)
        {
            int last = 0, offset = 1;
            if (Less(key, data[start + hint]))
            {
                int max = hint + 1;
                while (offset < max && Less(key, data[start + hint - offset]))
                { last = offset; offset = (int)Math.Min(max, 2L * offset + 1); }
                offset = Math.Min(offset, max);
                int previous = last; last = hint - offset; offset = hint - previous;
            }
            else
            {
                int max = count - hint;
                while (offset < max && !Less(key, data[start + hint + offset]))
                { last = offset; offset = (int)Math.Min(max, 2L * offset + 1); }
                offset = Math.Min(offset, max);
                last += hint; offset += hint;
            }
            for (last++; last < offset;)
            {
                int middle = last + ((offset - last) >> 1);
                if (Less(key, data[start + middle])) offset = middle;
                else last = middle + 1;
            }
            return offset;
        }

        private void MergeAt(int index)
        {
            var a = runs[index]; var b = runs[index + 1];
            runs[index] = (a.start, a.length + b.length, a.power);
            runs.RemoveAt(index + 1);
            int skip = GallopRight(keys[b.start], keys, a.start, a.length, 0);
            a.start += skip; a.length -= skip;
            if (a.length == 0) return;
            b.length = GallopLeft(keys[a.start + a.length - 1], keys, b.start, b.length, b.length - 1);
            if (b.length == 0) return;
            if (a.length <= b.length) MergeLow(a.start, a.length, b.start, b.length);
            else MergeHigh(a.start, a.length, b.start, b.length);
        }

        private void MergeLow(int start, int na, int right, int nb)
        {
            Save(start, na);
            int left = 0, dest = start, threshold = minGallop;
            Move(dest++, keys, values, right++, 1); nb--;
            try
            {
                while (na > 1 && nb > 0)
                {
                    int acount = 0, bcount = 0;
                    do
                    {
                        if (Less(keys[right], tempKeys[left]))
                        {
                            Move(dest++, keys, values, right++, 1); nb--;
                            bcount++; acount = 0;
                            if (nb == 0) return;
                        }
                        else
                        {
                            Move(dest++, tempKeys, tempValues, left++, 1); na--;
                            acount++; bcount = 0;
                            if (na == 1) break;
                        }
                    } while (acount < threshold && bcount < threshold);
                    if (na == 1) break;
                    threshold++;
                    do
                    {
                        if (threshold > 1) threshold--;
                        minGallop = threshold;
                        acount = GallopRight(keys[right], tempKeys, left, na, 0);
                        Move(dest, tempKeys, tempValues, left, acount);
                        dest += acount; left += acount; na -= acount;
                        if (na <= 1) break;
                        Move(dest++, keys, values, right++, 1); nb--;
                        if (nb == 0) return;
                        bcount = GallopLeft(tempKeys[left], keys, right, nb, 0);
                        Move(dest, keys, values, right, bcount);
                        dest += bcount; right += bcount; nb -= bcount;
                        if (nb == 0) return;
                        Move(dest++, tempKeys, tempValues, left++, 1); na--;
                        if (na == 1) break;
                    } while (acount >= 7 || bcount >= 7);
                    if (na <= 1) break;
                    minGallop = ++threshold;
                }
                if (na == 1 && nb > 0)
                {
                    Move(dest, keys, values, right, nb);
                    dest += nb;
                }
            }
            finally { if (na > 0) Move(dest, tempKeys, tempValues, left, na); }
        }

        private void MergeHigh(int start, int na, int rightStart, int nb)
        {
            Save(rightStart, nb);
            int left = start + na - 1, right = nb - 1, dest = rightStart + nb - 1;
            int threshold = minGallop;
            Move(dest--, keys, values, left--, 1); na--;
            try
            {
                while (na > 0 && nb > 1)
                {
                    int acount = 0, bcount = 0;
                    do
                    {
                        if (Less(tempKeys[right], keys[left]))
                        {
                            Move(dest--, keys, values, left--, 1); na--;
                            acount++; bcount = 0;
                            if (na == 0) return;
                        }
                        else
                        {
                            Move(dest--, tempKeys, tempValues, right--, 1); nb--;
                            bcount++; acount = 0;
                            if (nb == 1) break;
                        }
                    } while (acount < threshold && bcount < threshold);
                    if (nb == 1) break;
                    threshold++;
                    do
                    {
                        if (threshold > 1) threshold--;
                        minGallop = threshold;
                        acount = na - GallopRight(tempKeys[right], keys, start, na, na - 1);
                        Move(dest - acount + 1, keys, values, left - acount + 1, acount);
                        dest -= acount; left -= acount; na -= acount;
                        if (na == 0) return;
                        Move(dest--, tempKeys, tempValues, right--, 1); nb--;
                        if (nb == 1) break;
                        bcount = nb - GallopLeft(keys[left], tempKeys, 0, nb, nb - 1);
                        Move(dest - bcount + 1, tempKeys, tempValues, right - bcount + 1, bcount);
                        dest -= bcount; right -= bcount; nb -= bcount;
                        if (nb <= 1) break;
                        Move(dest--, keys, values, left--, 1); na--;
                        if (na == 0) return;
                    } while (acount >= 7 || bcount >= 7);
                    if (nb <= 1) break;
                    minGallop = ++threshold;
                }
                if (nb == 1 && na > 0)
                {
                    Move(dest - na + 1, keys, values, left - na + 1, na);
                    dest -= na;
                }
            }
            finally { if (nb > 0) Move(dest - nb + 1, tempKeys, tempValues, 0, nb); }
        }
    }
}
