using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace HttpLib
{
    /// <summary>
    /// 多线程断点续传下载器。
    /// <para>Go 系列为兼容旧版本而保留：它在后台线程上执行同步下载流程。</para>
    /// <para>推荐使用 GoAsync 系列：net45+ / netstandard2.0+ 上为真正的异步 IO，不占用线程等待网络。</para>
    /// </summary>
    public partial class HttpDown : IDisposable
    {
        #region 对外 API

        /// <summary>
        /// 开始下载，线程数取 CPU 核心数（兼容旧版同步实现）
        /// </summary>
        /// <returns>保存后的文件路径；失败或取消返回 null</returns>
        public Task<string?> Go() => Go(Environment.ProcessorCount, null);

        /// <summary>
        /// 开始下载（兼容旧版同步实现）
        /// </summary>
        /// <param name="threadCount">并发下载线程数</param>
        public Task<string?> Go(int threadCount) => Go(threadCount, null);

        /// <summary>
        /// 开始下载并指定保存文件名（兼容旧版同步实现）
        /// </summary>
        /// <param name="fileName">保存到目录中的文件名</param>
        public Task<string?> Go(string fileName) => Go(Environment.ProcessorCount, fileName);

        /// <summary>
        /// 开始下载（兼容旧版同步实现）
        /// </summary>
        /// <param name="threadCount">并发下载线程数，小于 1 时按 1 处理</param>
        /// <param name="fileName">保存文件名，为空时从 URL 或 Content-Disposition 推断</param>
        public Task<string?> Go(int threadCount, string? fileName)
        {
            CanSpeed = true;
#if NET40
            return Task.Factory.StartNew(() => DownLoad(threadCount, fileName));
#else
            return Task.Run(() => DownLoad(threadCount, fileName));
#endif
        }

        /// <summary>
        /// 暂停下载。已下载的分块会被保留，恢复后从各分块的断点继续。
        /// </summary>
        public void Suspend()
        {
            if (IsStopped) return;
            PauseGate.Reset();
            SetState(DownState.Stop);
        }

        /// <summary>
        /// 恢复被暂停的下载
        /// </summary>
        public void Resume()
        {
            if (IsStopped) return;
            SetState(DownState.Downloading);
            PauseGate.Set();
        }

        /// <summary>
        /// 终止下载（不可恢复）。与 Suspend 不同，终止后不能再用 Resume 继续。
        /// </summary>
        public void Stop()
        {
            SignalStop();
            SetState(DownState.Stop, "主动停止");
        }

        /// <summary>
        /// 终止下载并释放信号对象。
        /// 注意：这里只发出终止信号并唤醒阻塞线程，不立即 Dispose 信号对象——
        /// 工作线程可能仍在使用它们，交给 GC 回收更安全。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SignalStop();
        }

        bool _disposed;

        /// <summary>返回下载 URL</summary>
        public override string ToString() => Url;

        #endregion

        #region 同步下载流程

        /// <summary>
        /// 同步执行完整下载流程，供 Go() 在后台线程上调用
        /// </summary>
        string? DownLoad(int threadCount, string? fileName)
        {
            if (_disposed || IsStopped) return null;

            string workPath = CreateWorkPath();
            try
            {
                FileProbeResult probe = HttpDownLib.ProbeRemoteFile(this, threadCount);
                DownloadPlan plan = BuildPlan(probe, threadCount, fileName, workPath);
                return HttpDownLib.Execute(plan);
            }
            catch (Exception ex)
            {
                SetState(DownState.Fail, ex.Message);
                workPath.DeleteDirectory();
                return null;
            }
        }

        /// <summary>
        /// 创建（或复用）当前任务的临时分块目录
        /// </summary>
        internal string CreateWorkPath()
        {
            string workPath = SavePath + (ID ?? Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
            workPath.CreateDirectory();
            return workPath;
        }

        /// <summary>
        /// 根据探测结果生成分块计划，并重置进度状态。
        /// 分块采用左闭右开区间，每个分块的期望长度可直接计算，
        /// 避免了旧实现「请求 end+1 字节、合并时再丢 1 字节」的脆弱做法。
        /// </summary>
        internal DownloadPlan BuildPlan(FileProbeResult probe, int threadCount, string? fileName, string workPath)
        {
            if (threadCount < 1) threadCount = 1;

            long length = probe.Length > 0 ? probe.Length : 0;
            string name = fileName ?? Uri.FileName(probe.Disposition);
            if (string.IsNullOrEmpty(name)) name = "download";

            const long ChunkSize = 2097152; // 2MB
            long singleLength = length;
            int chunkCount = 1;
            if (probe.CanRange && length > ChunkSize)
            {
                singleLength = ChunkSize;
                chunkCount = (int)Math.Ceiling(length / (double)ChunkSize);
                if (chunkCount < 1) chunkCount = 1;
            }

            var chunks = new DownloadChunk[chunkCount];
            var maxValues = new long[chunkCount];
            for (int i = 0; i < chunkCount; i++)
            {
                long start = singleLength * i;
                long end = length > 0 ? Math.Min(start + singleLength, length) : 0;
                // 文件名保留 start/end 便于人工核对断点文件
                string chunkFile = workPath + i + "_" + start + "_" + end + ".temp";
                chunks[i] = new DownloadChunk(i, chunkFile, start, end);
                maxValues[i] = chunks[i].ExpectedLength;
            }

            // 清掉探测阶段可能残留的范围设置；各分块直接在自己的 request 上设置 Range，
            // 避免并发工作线程修改共享 HttpCore 的状态。
            core.range();
            ResetSizeState(maxValues);
            ResetValueState(chunkCount);

            return new DownloadPlan(this, threadCount, SavePath + name, workPath, probe.CanRange, chunks);
        }

        #endregion
    }

    /// <summary>
    /// 下载核心执行逻辑（同步与异步共用计划构建、错误聚合与状态发布）
    /// </summary>
    internal static partial class HttpDownLib
    {
        #region 预请求

        /// <summary>
        /// 探测文件长度、是否支持 Range、以及 Content-Disposition。
        /// 任何网络异常都降级为「长度未知、不支持分段」，由单分块流程继续尝试。
        /// </summary>
        internal static FileProbeResult ProbeRemoteFile(HttpDown download, int threadCount)
        {
            try
            {
                download.core.range();
                using (var response = (HttpWebResponse)download.core.CreateRequest().GetResponse())
                {
                    string? disposition = response.Headers?["Content-Disposition"];
                    long length = response.ContentLength;
                    bool canRange = false;

                    if (threadCount > 1 && length > 1)
                    {
                        try
                        {
                            download.core.range(1, length - 1);
                            using (var rangeResponse = (HttpWebResponse)download.core.CreateRequest().GetResponse())
                            {
                                canRange = rangeResponse.ContentLength == length - 1;
                            }
                        }
                        catch
                        {
                            canRange = false;
                        }
                        finally
                        {
                            download.core.range();
                        }
                    }

                    return new FileProbeResult(length, canRange, disposition);
                }
            }
            catch
            {
                return new FileProbeResult(0, false, null);
            }
        }

        #endregion

        #region 同步执行

        /// <summary>
        /// 执行下载计划：并发下载分块 → 汇总错误 → 合并文件 → 发布终态
        /// </summary>
        internal static string? Execute(DownloadPlan plan)
        {
            HttpDown owner = plan.Owner;
            owner.SetState(DownState.Downloading);
            owner.CanSpeed = true;

            using (new SpeedMonitor(owner))
            {
                try
                {
                    if (plan.Chunks.Length > 1 && plan.ThreadCount > 1)
                    {
                        Parallel.ForEach(
                            plan.Chunks,
                            new ParallelOptions { MaxDegreeOfParallelism = plan.ThreadCount },
                            chunk => DownloadChunkWithRetry(plan, chunk));
                    }
                    else
                    {
                        foreach (DownloadChunk chunk in plan.Chunks) DownloadChunkWithRetry(plan, chunk);
                    }
                }
                catch (Exception ex)
                {
                    owner.CanSpeed = false;
                    owner.SetState(DownState.Fail, ex.Message);
                    return null;
                }
            }

            owner.CanSpeed = false;
            owner.SetSpeed(0);
            owner.SetTime(null);

            if (owner.IsStopped)
            {
                owner.SetState(DownState.Complete, "主动停止");
                return null;
            }

            if (HasChunkErrors(plan, out string? error))
            {
                owner.SetState(DownState.Fail, error);
                return null;
            }

            try
            {
                string path = CombineChunks(plan);
                owner.SetState(DownState.Complete);
                return path;
            }
            catch (Exception ex)
            {
                owner.SetState(DownState.Fail, ex.Message);
                plan.WorkPath.DeleteDirectory();
                return null;
            }
        }

        /// <summary>
        /// 单个分块的下载与重试。暂停时阻塞等待，终止时立即退出。
        /// </summary>
        static void DownloadChunkWithRetry(DownloadPlan plan, DownloadChunk chunk)
        {
            int attempt = 0;
            while (true)
            {
                if (plan.Owner.ShouldStop()) return;

                if (DownloadChunkCore(plan, chunk, out string? error))
                {
                    chunk.HasError = false;
                    chunk.ErrorMessage = null;
                    return;
                }

                chunk.ErrorMessage = error;
                if (plan.Owner.IsStopped || plan.Owner.ShouldStop()) return;

                attempt++;
                if (attempt > plan.Owner.RetryCount)
                {
                    chunk.HasError = true;
                    return;
                }

                // 递增退避，避免对端故障时疯狂重试；终止信号可打断等待
                int delay = Math.Min(100 * attempt, 1000);
                if (plan.Owner.StopSource.Token.WaitHandle.WaitOne(delay)) return;
            }
        }

        /// <summary>
        /// 把旧版本多写的尾部字节截掉，保证分块长度与计划一致
        /// </summary>
        internal static void TruncateChunkFile(string path, long length)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
                file.SetLength(length);
        }

        /// <summary>
        /// 下载单个分块。已存在且完整的临时文件会被直接复用（断点续传）。
        /// </summary>
        static bool DownloadChunkCore(DownloadPlan plan, DownloadChunk chunk, out string? error)
        {
            error = null;
            long expected = chunk.ExpectedLength;
            long already = 0;

            try
            {
                if (File.Exists(chunk.FilePath))
                {
                    already = new FileInfo(chunk.FilePath).Length;
                    if (expected > 0 && already >= expected)
                    {
                        // 旧版本分块会多出 1 个字节，这里统一校正到期望长度
                        if (already > expected) TruncateChunkFile(chunk.FilePath, expected);
                        plan.Owner.SetMaxValue(chunk.Index, expected);
                        plan.Owner.SetValue(chunk.Index, expected);
                        return true;
                    }

                    // 不支持 Range 或长度未知时无法续传，从头开始
                    if (expected <= 0 || !plan.CanRange) already = 0;
                }

                {
                    // HttpWebRequest 在所有目标框架上都不保证实现 IDisposable，
                    // 用普通局部变量 + 块作用域即可，请求对象随响应关闭后被回收。
                    var request = plan.Owner.core.CreateRequest();
                    bool rangeRequested = plan.CanRange && expected > 0;
                    if (rangeRequested)
                    {
                        // AddRange 的结束位置是「包含」的，因此用 EndOffset - 1
                        request.AddRange(chunk.StartOffset + already, chunk.EndOffset - 1);
                    }

                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        // 服务端忽略了 Range（返回 200 而不是 206）时，从 0 重新写入
                        if (rangeRequested && already > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                        {
                            already = 0;
                        }

                        long reported = response.ContentLength > 0
                            ? response.ContentLength
                            : (expected > 0 ? expected - already : 0);
                        plan.Owner.SetMaxValue(chunk.Index, already + (reported > 0 ? reported : 0));

                        using (var stream = response.GetResponseStream())
                        {
                            if (stream == null)
                            {
                                error = "响应流为空";
                                return false;
                            }
                            using (var file = new FileStream(chunk.FilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                            {
                                if (already > 0 && response.StatusCode == HttpStatusCode.PartialContent)
                                {
                                    file.Seek(already, SeekOrigin.Begin);
                                }
                                else
                                {
                                    already = 0;
                                    file.SetLength(0);
                                    plan.Owner.SetValue(chunk.Index, 0);
                                }

                                long total = already;
                                int bufferSize = plan.Owner.CacheSize;
                                byte[] buffer = BufferPool.Rent(bufferSize);
                                try
                                {
                                    while (expected <= 0 || total < expected)
                                    {
                                        int count = expected > 0 ? (int)Math.Min(bufferSize, expected - total) : bufferSize;
                                        int read = stream.Read(buffer, 0, count);
                                        if (read <= 0) break;

                                        if (plan.Owner.ShouldStop())
                                        {
                                            error = "主动停止";
                                            return false;
                                        }

                                        file.Write(buffer, 0, read);
                                        total += read;
                                        plan.Owner.SetValue(chunk.Index, total);
                                    }
                                }
                                finally
                                {
                                    BufferPool.Return(buffer);
                                }

                                file.Flush();
                                if (expected > 0 && total < expected)
                                {
                                    error = "分块 " + chunk.Index + " 数据不完整（" + total + "/" + expected + "）";
                                    return false;
                                }
                            }
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        #endregion

        #region 结果处理

        /// <summary>
        /// 汇总所有分块错误，返回第一条不重复的错误描述
        /// </summary>
        internal static bool HasChunkErrors(DownloadPlan plan, out string? error)
        {
            var messages = new List<string>(1);
            bool any = false;
            foreach (DownloadChunk chunk in plan.Chunks)
            {
                if (!chunk.HasError) continue;
                any = true;
                if (!string.IsNullOrEmpty(chunk.ErrorMessage))
                {
                    string message = chunk.ErrorMessage!;
                    if (!messages.Contains(message)) messages.Add(message);
                }
            }
            error = messages.Count > 0 ? string.Join(" ", messages) : (any ? "下载不完全" : null);
            return any;
        }

        /// <summary>
        /// 按序号把分块合并为目标文件。只复制每个分块的期望长度，
        /// 不依赖「少读一个字节」的技巧；临时目录在成功后清理。
        /// </summary>
        internal static string CombineChunks(DownloadPlan plan)
        {
            if (plan.Chunks.Length == 1)
            {
                if (File.Exists(plan.SaveFullPath)) File.Delete(plan.SaveFullPath);
                File.Move(plan.Chunks[0].FilePath, plan.SaveFullPath);
                plan.WorkPath.DeleteDirectory();
                return plan.SaveFullPath;
            }

            using (var output = new FileStream(
                plan.SaveFullPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferPool.CombineBufferSize))
            {
                byte[] buffer = BufferPool.Rent(BufferPool.CombineBufferSize);
                try
                {
                    foreach (DownloadChunk chunk in plan.Chunks)
                    {
                        long remaining = chunk.ExpectedLength;
                        using (var input = new FileStream(chunk.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            while (remaining > 0)
                            {
                                int count = (int)Math.Min(BufferPool.CombineBufferSize, remaining);
                                int read = input.Read(buffer, 0, count);
                                if (read <= 0) throw new IOException("分块 " + chunk.Index + " 内容不足，无法合并");
                                output.Write(buffer, 0, read);
                                remaining -= read;
                            }
                        }
                    }
                }
                finally
                {
                    BufferPool.Return(buffer);
                }
            }

            plan.WorkPath.DeleteDirectory();
            return plan.SaveFullPath;
        }

        #endregion

        #region 速度监控

        /// <summary>
        /// 每秒采样一次下载量，基于真实经过时间计算速度并估算剩余时间。
        /// 旧实现固定按 1 秒计算，线程调度延迟时速度会明显失真。
        /// </summary>
        internal sealed class SpeedMonitor : IDisposable
        {
            readonly CancellationTokenSource _stop = new CancellationTokenSource();
            readonly HttpDown _owner;

            public SpeedMonitor(HttpDown owner)
            {
                _owner = owner;
                ITask.Run(Loop);
            }

            void Loop()
            {
                try
                {
                    var watch = Stopwatch.StartNew();
                    long lastBytes = _owner.DownloadedBytes;
                    var samples = new List<int>(5);

                    while (!_stop.IsCancellationRequested)
                    {
                        Thread.Sleep(1000);
                        if (_stop.IsCancellationRequested) break;

                        long bytes = _owner.DownloadedBytes;
                        long delta = bytes - lastBytes;
                        lastBytes = bytes;

                        double seconds = watch.Elapsed.TotalSeconds;
                        if (delta <= 0 || seconds <= 0)
                        {
                            _owner.SetSpeed(0);
                            continue;
                        }

                        long speed = (long)(delta / seconds);
                        _owner.SetSpeed(speed);

                        long maxValue = _owner.MaxValue;
                        if (maxValue > bytes && speed > 0)
                        {
                            samples.Add((int)Math.Ceiling((maxValue - bytes) / (double)speed));
                            if (samples.Count > 4)
                            {
                                int average = 0;
                                for (int i = 0; i < samples.Count; i++) average += samples[i];
                                _owner.SetTime(FormatRemainingTime(average / samples.Count));
                                samples.Clear();
                            }
                        }
                        else
                        {
                            _owner.SetTime(null);
                        }
                    }
                }
                catch
                {
                    // 速度监控属于辅助功能，任何异常都不应影响下载本身
                }
            }

            static string FormatRemainingTime(int seconds)
            {
                var span = TimeSpan.FromSeconds(seconds < 0 ? 0 : seconds);
                if (span.TotalHours >= 1)
                    return ((int)span.TotalHours).ToString("00") + ":" + span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00");
                return span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00");
            }

            public void Dispose()
            {
                try { _stop.Cancel(); } catch { }
            }
        }

        #endregion
    }
}