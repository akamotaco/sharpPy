using System;

namespace SharpPy.Modules.Bz2
{
    /// <summary>
    /// bzlib.h and bzlib_private.h of bzip2 1.0.8: the return codes, the actions, the stream that
    /// carries the caller's buffers (bz_stream), the CRC table (crctable.c) and the randomisation
    /// table (randtable.c). The library itself is in BzCompress.cs (bzlib.c's compression side,
    /// blocksort.c, compress.c, huffman.c) and BzDecompress.cs (bzlib.c's decompression side,
    /// decompress.c).
    /// </summary>
    internal static class Bz
    {
        internal const int RUN = 0, FLUSH = 1, FINISH = 2;

        internal const int OK = 0, RUN_OK = 1, FLUSH_OK = 2, FINISH_OK = 3, STREAM_END = 4;
        internal const int SEQUENCE_ERROR = -1, PARAM_ERROR = -2, MEM_ERROR = -3, DATA_ERROR = -4,
            DATA_ERROR_MAGIC = -5, IO_ERROR = -6, UNEXPECTED_EOF = -7, OUTBUFF_FULL = -8, CONFIG_ERROR = -9;

        // Header bytes.
        internal const int HDR_B = 0x42, HDR_Z = 0x5a, HDR_h = 0x68, HDR_0 = 0x30;

        // Constants for the back end.
        internal const int MAX_ALPHA_SIZE = 258, MAX_CODE_LEN = 23, RUNA = 0, RUNB = 1, N_GROUPS = 6,
            G_SIZE = 50, N_ITERS = 4, MAX_SELECTORS = 2 + (900000 / G_SIZE);

        internal const int N_RADIX = 2, N_QSORT = 12, N_SHELL = 18, N_OVERSHOOT = N_RADIX + N_QSORT + N_SHELL + 2;

        /// <summary>BZ2_crc32Table (crctable.c): the big-endian CRC-32 of polynomial 0x04c11db7.</summary>
        internal static readonly uint[] Crc32Table = MakeCrcTable();

        private static uint[] MakeCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i << 24;
                for (int k = 0; k < 8; k++)
                    c = (c & 0x80000000) != 0 ? (c << 1) ^ 0x04c11db7 : c << 1;
                table[i] = c;
            }
            return table;
        }

        /// <summary>BZ2_rNums (randtable.c): the table for (de)randomising repetitive blocks.</summary>
        internal static readonly int[] RNums = {
            619, 720, 127, 481, 931, 816, 813, 233, 566, 247, 985, 724, 205, 454, 863, 491, 741, 242, 949, 214,
            733, 859, 335, 708, 621, 574, 73, 654, 730, 472, 419, 436, 278, 496, 867, 210, 399, 680, 480, 51,
            878, 465, 811, 169, 869, 675, 611, 697, 867, 561, 862, 687, 507, 283, 482, 129, 807, 591, 733, 623,
            150, 238, 59, 379, 684, 877, 625, 169, 643, 105, 170, 607, 520, 932, 727, 476, 693, 425, 174, 647,
            73, 122, 335, 530, 442, 853, 695, 249, 445, 515, 909, 545, 703, 919, 874, 474, 882, 500, 594, 612,
            641, 801, 220, 162, 819, 984, 589, 513, 495, 799, 161, 604, 958, 533, 221, 400, 386, 867, 600, 782,
            382, 596, 414, 171, 516, 375, 682, 485, 911, 276, 98, 553, 163, 354, 666, 933, 424, 341, 533, 870,
            227, 730, 475, 186, 263, 647, 537, 686, 600, 224, 469, 68, 770, 919, 190, 373, 294, 822, 808, 206,
            184, 943, 795, 384, 383, 461, 404, 758, 839, 887, 715, 67, 618, 276, 204, 918, 873, 777, 604, 560,
            951, 160, 578, 722, 79, 804, 96, 409, 713, 940, 652, 934, 970, 447, 318, 353, 859, 672, 112, 785,
            645, 863, 803, 350, 139, 93, 354, 99, 820, 908, 609, 772, 154, 274, 580, 184, 79, 626, 630, 742,
            653, 282, 762, 623, 680, 81, 927, 626, 789, 125, 411, 521, 938, 300, 821, 78, 343, 175, 128, 250,
            170, 774, 972, 275, 999, 639, 495, 78, 352, 126, 857, 956, 358, 619, 580, 124, 737, 594, 701, 612,
            669, 112, 134, 694, 363, 992, 809, 743, 168, 974, 944, 375, 748, 52, 600, 747, 642, 182, 862, 81,
            344, 805, 988, 739, 511, 655, 814, 334, 249, 515, 897, 955, 664, 981, 649, 113, 974, 459, 893, 228,
            433, 837, 553, 268, 926, 240, 102, 654, 459, 51, 686, 754, 806, 760, 493, 403, 415, 394, 687, 700,
            946, 670, 656, 610, 738, 392, 760, 799, 887, 653, 978, 321, 576, 617, 626, 502, 894, 679, 243, 440,
            680, 879, 194, 572, 640, 724, 926, 56, 204, 700, 707, 151, 457, 449, 797, 195, 791, 558, 945, 679,
            297, 59, 87, 824, 713, 663, 412, 693, 342, 606, 134, 108, 571, 364, 631, 212, 174, 643, 304, 329,
            343, 97, 430, 751, 497, 314, 983, 374, 822, 928, 140, 206, 73, 263, 980, 736, 876, 478, 430, 305,
            170, 514, 364, 692, 829, 82, 855, 953, 676, 246, 369, 970, 294, 750, 807, 827, 150, 790, 288, 923,
            804, 378, 215, 828, 592, 281, 565, 555, 710, 82, 896, 831, 547, 261, 524, 462, 293, 465, 502, 56,
            661, 821, 976, 991, 658, 869, 905, 758, 745, 193, 768, 550, 608, 933, 378, 286, 215, 979, 792, 961,
            61, 688, 793, 644, 986, 403, 106, 366, 905, 644, 372, 567, 466, 434, 645, 210, 389, 550, 919, 135,
            780, 773, 635, 389, 707, 100, 626, 958, 165, 504, 920, 176, 193, 713, 857, 265, 203, 50, 668, 108,
            645, 990, 626, 197, 510, 357, 358, 850, 858, 364, 936, 638,
        };
    }

    /// <summary>
    /// bz_stream: the caller's input and output windows. next_in/next_out are offsets into InBuf and
    /// OutBuf; avail_in/avail_out are 32-bit counts as in C (the caller feeds at most UINT_MAX).
    /// </summary>
    internal sealed class BzStream
    {
        internal byte[] InBuf;
        internal int NextIn;
        internal uint AvailIn;
        internal byte[] OutBuf;
        internal int NextOut;
        internal uint AvailOut;
        internal ulong TotalIn, TotalOut;
        internal BzEState CState;
        internal BzDState DState;
    }
}
