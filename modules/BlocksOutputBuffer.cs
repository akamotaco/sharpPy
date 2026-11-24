using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// _BlocksOutputBuffer (Include/internal/pycore_blocks_output_buffer.h) for the codecs whose
    /// stream struct keeps next_out/avail_out itself (_bz2, _lzma): blocks of growing size, the
    /// first limited by max_length, joined into one bytes object at the end.
    /// </summary>
    internal sealed class BlocksOutputBuffer
    {
        // BUFFER_BLOCK_SIZE
        private static readonly int[] BlockSize =
        {
            32 * 1024, 64 * 1024, 256 * 1024, 1024 * 1024, 4 << 20, 8 << 20, 16 << 20, 16 << 20,
            32 << 20, 32 << 20, 32 << 20, 32 << 20, 64 << 20, 64 << 20, 128 << 20, 128 << 20, 256 << 20,
        };

        private readonly List<byte[]> _blocks = new();
        private long _allocated, _maxLength = -1;

        /// <summary>_BlocksOutputBuffer_InitAndGrow: the first block; returns its size.</summary>
        internal int InitAndGrow(long maxLength, out byte[] block)
        {
            _maxLength = maxLength;
            int size = BlockSize[0];
            if (maxLength >= 0 && maxLength < size)
                size = (int)maxLength;
            block = new byte[size];
            _blocks.Add(block);
            _allocated = size;
            return size;
        }

        /// <summary>_BlocksOutputBuffer_Grow: the next block, once the current one is full.</summary>
        internal int Grow(out byte[] block, long availOut)
        {
            if (availOut != 0)
                throw PySystemError.Create("avail_out is non-zero in _BlocksOutputBuffer_Grow().");
            long size = _blocks.Count < BlockSize.Length ? BlockSize[_blocks.Count] : BlockSize[^1];
            if (_maxLength >= 0)
            {
                long rest = _maxLength - _allocated;
                if (rest <= 0)
                    throw PyMemoryError.Create("Unable to allocate output buffer.");
                size = Math.Min(size, rest);
            }
            if (_allocated + size > Array.MaxLength)
                throw PyMemoryError.Create("Unable to allocate output buffer.");
            block = new byte[size];
            _blocks.Add(block);
            _allocated += size;
            return (int)size;
        }

        /// <summary>_BlocksOutputBuffer_GetDataSize</summary>
        internal long GetDataSize(long availOut) => _allocated - availOut;

        /// <summary>_BlocksOutputBuffer_Finish: the blocks as one bytes object.</summary>
        internal byte[] Finish(long availOut)
        {
            if (_blocks.Count == 1 && availOut == 0)
                return _blocks[0];
            var result = new byte[GetDataSize(availOut)];
            int at = 0;
            for (int i = 0; i < _blocks.Count; i++)
            {
                int n = i == _blocks.Count - 1 ? _blocks[i].Length - (int)availOut : _blocks[i].Length;
                Array.Copy(_blocks[i], 0, result, at, n);
                at += n;
            }
            return result;
        }
    }
}
