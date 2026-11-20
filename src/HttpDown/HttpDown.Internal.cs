using System.Collections.Concurrent;
#if NETSTANDARD2_1 || NET6_0_OR_GREATER
using System.Buffers;
#endif

namespace HttpLib
{
    /// <summary>
    /// 下载缓冲区池。高版本框架直接使用 ArrayPool&lt;byte&gt; 减少大数组分配，
    /// 低版本框架（net40/net45）使用基于 ConcurrentBag 的简易池。
    /// 租用的数组可能大于请求长度，调用方必须以请求长度为准。
    /// </summary>
    internal static class BufferPool
    {
        /// <summary>合并文件时使用的缓冲区大小</summary>
        internal const int CombineBufferSize = 81920;

        const int MaxPooledLength = 1024 * 1024;
        const int MaxPooledPerBucket = 24;

#if !(NETSTANDARD2_1 || NET6_0_OR_GREATER)
        static readonly ConcurrentDictionary<int, ConcurrentBag<byte[]>> Buckets =
            new ConcurrentDictionary<int, ConcurrentBag<byte[]>>();
#endif

        /// <summary>租用一块至少为 <paramref name="length"/> 字节的缓冲区</summary>
        internal static byte[] Rent(int length)
        {
            if (length < 1) length = 1;
#if NETSTANDARD2_1 || NET6_0_OR_GREATER
            return ArrayPool<byte>.Shared.Rent(length);
#else
            int bucket = Normalize(length);
            if (bucket <= MaxPooledLength)
            {
                var bag = Buckets.GetOrAdd(bucket, _ => new ConcurrentBag<byte[]>());
                if (bag.TryTake(out byte[]? cached) && cached != null) return cached;
            }
            return new byte[length];
#endif
        }

        /// <summary>归还缓冲区，超大数组直接交给 GC</summary>
        internal static void Return(byte[] buffer)
        {
            if (buffer == null || buffer.Length > MaxPooledLength) return;
#if NETSTANDARD2_1 || NET6_0_OR_GREATER
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
#else
            var bag = Buckets.GetOrAdd(buffer.Length, _ => new ConcurrentBag<byte[]>());
            if (bag.Count < MaxPooledPerBucket) bag.Add(buffer);
#endif
        }

#if !(NETSTANDARD2_1 || NET6_0_OR_GREATER)
        static int Normalize(int length)
        {
            int size = 1024;
            while (size < length && size < MaxPooledLength) size <<= 1;
            return size >= length ? size : length;
        }
#endif
    }

    /// <summary>
    /// 一个下载分块。区间为左闭右开 [StartOffset, EndOffset)，
    /// 因此 ExpectedLength = EndOffset - StartOffset，不再需要合并时「丢掉最后一个字节」的补丁。
    /// </summary>
    internal sealed class DownloadChunk
    {
        public DownloadChunk(int index, string filePath, long startOffset, long endOffset)
        {
            Index = index;
            FilePath = filePath;
            StartOffset = startOffset;
            EndOffset = endOffset;
        }

        /// <summary>分块序号，同时也是进度数组的索引</summary>
        public int Index { get; }

        /// <summary>分块临时文件完整路径</summary>
        public string FilePath { get; }

        /// <summary>分块在完整文件中的起始偏移（含）</summary>
        public long StartOffset { get; }

        /// <summary>分块在完整文件中的结束偏移（不含）；0 表示服务端未提供长度</summary>
        public long EndOffset { get; }

        /// <summary>分块应有字节数；0 表示长度未知，读到流结束为止</summary>
        public long ExpectedLength => EndOffset - StartOffset;

        /// <summary>该分块重试后仍失败</summary>
        public volatile bool HasError;

        /// <summary>该分块的最后一次错误信息</summary>
        public string? ErrorMessage;
    }

    /// <summary>
    /// 一次下载任务的完整执行计划（探测结果 + 分块列表），
    /// 同步与异步实现共用，避免两套逻辑对分块规则产生分歧。
    /// </summary>
    internal sealed class DownloadPlan
    {
        public DownloadPlan(
            HttpDown owner,
            int threadCount,
            string saveFullPath,
            string workPath,
            bool canRange,
            DownloadChunk[] chunks)
        {
            Owner = owner;
            ThreadCount = threadCount < 1 ? 1 : threadCount;
            SaveFullPath = saveFullPath;
            WorkPath = workPath;
            CanRange = canRange;
            Chunks = chunks;
        }

        /// <summary>发起下载的 HttpDown 实例</summary>
        public HttpDown Owner { get; }

        /// <summary>期望的并发下载数</summary>
        public int ThreadCount { get; }

        /// <summary>最终保存文件的完整路径</summary>
        public string SaveFullPath { get; }

        /// <summary>分块临时文件所在目录</summary>
        public string WorkPath { get; }

        /// <summary>服务端是否支持 Range 请求</summary>
        public bool CanRange { get; }

        /// <summary>按序号排列的分块</summary>
        public DownloadChunk[] Chunks { get; }
    }

    /// <summary>预请求（探测）结果</summary>
    internal readonly struct FileProbeResult
    {
        public FileProbeResult(long length, bool canRange, string? disposition)
        {
            Length = length;
            CanRange = canRange;
            Disposition = disposition;
        }

        /// <summary>文件总长度，未知时为 0 或 -1</summary>
        public long Length { get; }

        /// <summary>是否支持分段下载</summary>
        public bool CanRange { get; }

        /// <summary>Content-Disposition 响应头</summary>
        public string? Disposition { get; }
    }
}