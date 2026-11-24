// Modules/_decimal/libmpdec/transpose.h and transpose.c (CPython 3.12.15, libmpdec 2.5.1), CONFIG_64.
// The C stack buffers (2 x 4096 and 2 x 128*128 words) are per-thread arrays here, so a large
// transform does not need 256 KiB of stack (Godot threads have small stacks).
using System;
using System.Diagnostics;

namespace SharpPy.Modules.Mpdecimal
{
    internal static partial class Mpd
    {
        private const int BUFSIZE = 4096;
        private const int SIDE = 128;

        // enum {FORWARD_CYCLE, BACKWARD_CYCLE};
        private const int FORWARD_CYCLE = 0;
        private const int BACKWARD_CYCLE = 1;

        [ThreadStatic] private static ulong[] t_halfrow_buf1, t_halfrow_buf2, t_square_buf1, t_square_buf2;

        /* Bignum: The transpose functions are used for very large transforms
           in sixstep.c and fourstep.c. */

        // pointerswap (transpose.h)
        private static void pointerswap(ref ulong[] a, ref ulong[] b)
        {
            ulong[] tmp;

            tmp = b;
            b = a;
            a = tmp;
        }

        /* Definition of the matrix transpose */
        public static void std_trans(Span<ulong> dest, ReadOnlySpan<ulong> src, ulong rows, ulong cols)
        {
            ulong idest, isrc;
            ulong r, c;

            for (r = 0; r < rows; r++)
            {
                isrc = r * cols;
                idest = r;
                for (c = 0; c < cols; c++)
                {
                    dest[(int)idest] = src[(int)isrc];
                    isrc += 1;
                    idest += rows;
                }
            }
        }

        /*
         * Swap half-rows of 2^n * (2*2^n) matrix.
         * FORWARD_CYCLE: even/odd permutation of the halfrows.
         * BACKWARD_CYCLE: reverse the even/odd permutation.
         */
        private static bool swap_halfrows_pow2(Span<ulong> matrix, ulong rows, ulong cols, int dir)
        {
            ulong[] buf1 = t_halfrow_buf1 ??= new ulong[BUFSIZE];
            ulong[] buf2 = t_halfrow_buf2 ??= new ulong[BUFSIZE];
            ulong[] readbuf, writebuf;
            Span<ulong> hp;
            ulong[] done;
            ulong dbits;
            ulong b = BUFSIZE, stride;
            ulong hn, hmax; /* halfrow number */
            ulong m, r = 0;
            ulong offset;
            ulong next;

            Debug.Assert(cols == mul_size_t(2, rows));

            if (dir == FORWARD_CYCLE)
                r = rows;
            else if (dir == BACKWARD_CYCLE)
                r = 2;
            else
                throw mpd_err_fatal("swap_halfrows_pow2: invalid direction"); /* GCOV_NOT_REACHED */

            m = cols - 1;
            hmax = rows; /* cycles start at odd halfrows */
            dbits = 8 * sizeof(ulong);
            if ((done = mpd_calloc(hmax / sizeof(ulong) + 1)) == null)
                return false;

            for (hn = 1; hn <= hmax; hn += 2)
            {
                if ((done[hn / dbits] & mpd_bits[hn % dbits]) != 0)
                    continue;

                readbuf = buf1; writebuf = buf2;

                for (offset = 0; offset < cols / 2; offset += b)
                {
                    stride = (offset + b < cols / 2) ? b : cols / 2 - offset;

                    hp = matrix.Slice((int)(hn * cols / 2));
                    hp.Slice((int)offset, (int)stride).CopyTo(readbuf);
                    pointerswap(ref readbuf, ref writebuf);

                    next = mulmod_size_t(hn, r, m);
                    hp = matrix.Slice((int)(next * cols / 2));

                    while (next != hn)
                    {
                        hp.Slice((int)offset, (int)stride).CopyTo(readbuf);
                        writebuf.AsSpan(0, (int)stride).CopyTo(hp.Slice((int)offset));
                        pointerswap(ref readbuf, ref writebuf);

                        done[next / dbits] |= mpd_bits[next % dbits];

                        next = mulmod_size_t(next, r, m);
                        hp = matrix.Slice((int)(next * cols / 2));
                    }

                    writebuf.AsSpan(0, (int)stride).CopyTo(hp.Slice((int)offset));

                    done[hn / dbits] |= mpd_bits[hn % dbits];
                }
            }

            mpd_free(done);
            return true;
        }

        /* In-place transpose of a square matrix */
        private static void squaretrans(Span<ulong> buf, ulong cols)
        {
            ulong tmp;
            ulong idest, isrc;
            ulong r, c;

            for (r = 0; r < cols; r++)
            {
                c = r + 1;
                isrc = r * cols + c;
                idest = c * cols + r;
                for (c = r + 1; c < cols; c++)
                {
                    tmp = buf[(int)isrc];
                    buf[(int)isrc] = buf[(int)idest];
                    buf[(int)idest] = tmp;
                    isrc += 1;
                    idest += cols;
                }
            }
        }

        /*
         * Transpose 2^n * 2^n matrix. For cache efficiency, the matrix is split into
         * square blocks with side length 'SIDE'. First, the blocks are transposed,
         * then a square transposition is done on each individual block.
         */
        private static void squaretrans_pow2(Span<ulong> matrix, ulong size)
        {
            ulong[] buf1 = t_square_buf1 ??= new ulong[SIDE * SIDE];
            ulong[] buf2 = t_square_buf2 ??= new ulong[SIDE * SIDE];
            ulong to, from; /* offsets: into matrix or into a buffer, as the C pointers */
            ulong b = size;
            ulong r, c;
            ulong i;

            while (b > SIDE) b >>= 1;

            for (r = 0; r < size; r += b)
            {
                for (c = r; c < size; c += b)
                {
                    from = r * size + c;
                    to = 0;
                    for (i = 0; i < b; i++)
                    {
                        matrix.Slice((int)from, (int)b).CopyTo(buf1.AsSpan((int)to));
                        from += size;
                        to += b;
                    }
                    squaretrans(buf1, b);

                    if (r == c)
                    {
                        to = r * size + c;
                        from = 0;
                        for (i = 0; i < b; i++)
                        {
                            buf1.AsSpan((int)from, (int)b).CopyTo(matrix.Slice((int)to));
                            from += b;
                            to += size;
                        }
                        continue;
                    }
                    else
                    {
                        from = c * size + r;
                        to = 0;
                        for (i = 0; i < b; i++)
                        {
                            matrix.Slice((int)from, (int)b).CopyTo(buf2.AsSpan((int)to));
                            from += size;
                            to += b;
                        }
                        squaretrans(buf2, b);

                        to = c * size + r;
                        from = 0;
                        for (i = 0; i < b; i++)
                        {
                            buf1.AsSpan((int)from, (int)b).CopyTo(matrix.Slice((int)to));
                            from += b;
                            to += size;
                        }

                        to = r * size + c;
                        from = 0;
                        for (i = 0; i < b; i++)
                        {
                            buf2.AsSpan((int)from, (int)b).CopyTo(matrix.Slice((int)to));
                            from += b;
                            to += size;
                        }
                    }
                }
            }
        }

        /*
         * In-place transposition of a 2^n x 2^n or a 2^n x (2*2^n)
         * or a (2*2^n) x 2^n matrix.
         */
        public static bool transpose_pow2(Span<ulong> matrix, ulong rows, ulong cols)
        {
            ulong size = mul_size_t(rows, cols);

            Debug.Assert(ispower2(rows));
            Debug.Assert(ispower2(cols));

            if (cols == rows)
            {
                squaretrans_pow2(matrix, rows);
            }
            else if (cols == mul_size_t(2, rows))
            {
                if (!swap_halfrows_pow2(matrix, rows, cols, FORWARD_CYCLE))
                    return false;
                squaretrans_pow2(matrix, rows);
                squaretrans_pow2(matrix.Slice((int)(size / 2)), rows);
            }
            else if (rows == mul_size_t(2, cols))
            {
                squaretrans_pow2(matrix, cols);
                squaretrans_pow2(matrix.Slice((int)(size / 2)), cols);
                if (!swap_halfrows_pow2(matrix, cols, rows, BACKWARD_CYCLE))
                    return false;
            }
            else
            {
                throw mpd_err_fatal("transpose_pow2: invalid matrix shape"); /* GCOV_NOT_REACHED */
            }

            return true;
        }
    }
}
