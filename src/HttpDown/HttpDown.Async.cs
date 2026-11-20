#if !NET40
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace HttpLib
{
    public partial class HttpDown
    {
        #region 异步 API

        /// <summary>
        /// 异步下载，线程数取 CPU 核心数。net40 不支持本方法。
        /// </summary>
        /// <returns>保存后的文件路径；失败或取消返回 null</returns>
        public Task<string?> GoAsync() => GoAsync(Environment.ProcessorCount, null, CancellationToken.None);

        /// <summary>
        /// 异步下载并指定并发数。net40 不支持本方法。
        /// </summary>
        /// <param name="threadCount">并发下载数，小于 1 时按 1 处理</param>
        public Task<string?> GoAsync(int threadCount) => GoAsync(threadCount, null, CancellationToken.None);

        /// <summary>
        /// 异步下载并指定保存文件名。net40 不支持本方法。
        /// </summary>
        /// <param name="fileName">保存文件名</param>
        public Task<string?> GoAsync(string fileName) => GoAsync(Environment.ProcessorCount, fileName, CancellationToken.None);

        /// <summary>
        /// 异步下载并指定并发数与文件名。net40 不支持本方法。
        /// </summary>
        public Task<string?> GoAsync(int threadCount, string? fileName) =>
            GoAsync(threadCount, fileName, CancellationToken.None);

        /// <summary>
        /// 完整的异步下载入口：使用异步请求与异步流 IO，不占用线程等待网络。
        /// <para>外部取消、Stop()、Dispose() 都会中止下载；Suspend()/Resume() 依然可用。</para>
        /// </summary>
        /// <param name="threadCount">并发下载数</param>
        /// <param name="fileName">保存文件名，为空时自动推断</param>
        /// <param name="cancellationToken">外部取消令牌</param>
        public Task<string?> GoAsync(int threadCount, string? fileName, CancellationToken cancellationToken)
        {
            if (_disposed || IsStopped) return Task.FromResult<string?>(null);
            CanSpeed = true;
            return GoAsyncCore(threadCount, fileName, cancellationToken);
        }

        async Task<string?> GoAsyncCore(int threadCount, string? fileName, CancellationToken cancellationToken)
        {
            // 把外部令牌与内部的 Stop()/Dispose() 信号合并
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, StopSource.Token))
            {
                string workPath = CreateWorkPath();
                try
                {
                    FileProbeResult probe = await HttpDownLib
                        .ProbeRemoteFileAsync(this, threadCount, linked.Token)
                        .ConfigureAwait(false);
                    DownloadPlan plan = BuildPlan(probe, threadCount, fileName, workPath);
                    return await HttpDownLib.ExecuteAsync(plan, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    SetState(DownState.Complete, "主动停止");
                    return null;
                }
                catch (Exception ex)
                {
                    SetState(DownState.Fail, ex.Message);
                    workPath.DeleteDirectory();
                    return null;
                }
            }
        }

        #endregion
    }

    internal static partial class HttpDownLib
    {
        #region 异步预请求

        /// <summary>
        /// 异步探测文件长度、Range 支持与 Content-Disposition。
        /// </summary>
        internal static async Task<FileProbeResult> ProbeRemoteFileAsync(
            HttpDown download,
            int threadCount,
            CancellationToken token)
        {
            try
            {
                download.core.range();
                using (var response = (HttpWebResponse)await download.core
                    .CreateRequest()
                    .GetResponseAsync()
                    .ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    string? disposition = response.Headers?["Content-Disposition"];
                    long length = response.ContentLength;
                    bool canRange = false;

                    if (threadCount > 1 && length > 1)
                    {
                        try
                        {
                            download.core.range(1, length - 1);
                            using (var rangeResponse = (HttpWebResponse)await download.core
                                .CreateRequest()
                                .GetResponseAsync()
                                .ConfigureAwait(false))
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new FileProbeResult(0, false, null);
            }
        }

        #endregion

        #region 异步执行

        /// <summary>
        /// 异步执行下载计划，用 SemaphoreSlim 限制并发，避免为大量分块创建同等数量的线程。
        /// </summary>
        internal static async Task<string?> ExecuteAsync(DownloadPlan plan, CancellationToken token)
        {
            HttpDown owner = plan.Owner;
            owner.SetState(DownState.Downloading);
            owner.CanSpeed = true;

            using (new SpeedMonitor(owner))
            {
                try
                {
                    int concurrency = Math.Max(1, Math.Min(plan.ThreadCount, plan.Chunks.Length));
                    using (var gate = new SemaphoreSlim(concurrency))
                    {
                        var tasks = new Task[plan.Chunks.Length];
                        for (int i = 0; i < plan.Chunks.Length; i++)
                        {
                            tasks[i] = DownloadChunkWithRetryAsync(plan, plan.Chunks[i], gate, token);
                        }
                        await Task.WhenAll(tasks).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    FinishSpeed(owner);
                    owner.SetState(DownState.Complete, "主动停止");
                    return null;
                }
                catch (Exception ex)
                {
                    FinishSpeed(owner);
                    owner.SetState(DownState.Fail, ex.Message);
                    return null;
                }
            }

            FinishSpeed(owner);

            if (token.IsCancellationRequested || owner.IsStopped)
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
                string path = await CombineChunksAsync(plan, token).ConfigureAwait(false);
                owner.SetState(DownState.Complete);
                return path;
            }
            catch (OperationCanceledException)
            {
                owner.SetState(DownState.Complete, "主动停止");
                return null;
            }
            catch (Exception ex)
            {
                owner.SetState(DownState.Fail, ex.Message);
                plan.WorkPath.DeleteDirectory();
                return null;
            }
        }

        /// <summary>
        /// 停止速度采样并清零当前转速/剩余时间
        /// </summary>
        static void FinishSpeed(HttpDown owner)
        {
            owner.CanSpeed = false;
            owner.SetSpeed(0);
            owner.SetTime(null);
        }

        /// <summary>
        /// 单个分块的异步下载与重试。暂停使用轻量轮询等待，取消由令牌立即打断。
        /// </summary>
        static async Task DownloadChunkWithRetryAsync(
            DownloadPlan plan,
            DownloadChunk chunk,
            SemaphoreSlim gate,
            CancellationToken token)
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                int attempt = 0;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    await WaitWhilePausedAsync(plan.Owner, token).ConfigureAwait(false);

                    string? error = await TryDownloadChunkAsync(plan, chunk, token).ConfigureAwait(false);
                    if (error == null)
                    {
                        chunk.HasError = false;
                        chunk.ErrorMessage = null;
                        return;
                    }

                    chunk.ErrorMessage = error;
                    token.ThrowIfCancellationRequested();

                    attempt++;
                    if (attempt > plan.Owner.RetryCount)
                    {
                        chunk.HasError = true;
                        return;
                    }

                    await Task.Delay(Math.Min(100 * attempt, 1000), token).ConfigureAwait(false);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// 暂停时让出执行权等待；ManualResetEventSlim 没有 WaitAsync，
        /// 暂停是低频操作，50ms 轮询不会带来可观的开销。
        /// </summary>
        static async Task WaitWhilePausedAsync(HttpDown owner, CancellationToken token)
        {
            while (!owner.PauseGate.IsSet)
            {
                await Task.Delay(50, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// 异步下载单个分块。成功返回 null，失败返回错误信息。
        /// 不使用 out 参数是因为 C# 不允许 async 方法带 out/ref 参数。
        /// </summary>
        static async Task<string?> TryDownloadChunkAsync(
            DownloadPlan plan,
            DownloadChunk chunk,
            CancellationToken token)
        {
            long expected = chunk.ExpectedLength;
            long already = 0;

            try
            {
                if (File.Exists(chunk.FilePath))
                {
                    already = new FileInfo(chunk.FilePath).Length;
                    if (expected > 0 && already >= expected)
                    {
                        if (already > expected) TruncateChunkFile(chunk.FilePath, expected);
                        plan.Owner.SetMaxValue(chunk.Index, expected);
                        plan.Owner.SetValue(chunk.Index, expected);
                        return null;
                    }
                    if (expected <= 0 || !plan.CanRange) already = 0;
                }

                var request = plan.Owner.core.CreateRequest();
                bool rangeRequested = plan.CanRange && expected > 0;
                if (rangeRequested) request.AddRange(chunk.StartOffset + already, chunk.EndOffset - 1);

                // HttpWebRequest 并非在所有目标框架上都实现 IDisposable，不使用 using
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();

                    // 服务端忽略 Range 时从头覆盖，避免拼接出错位的文件
                    if (rangeRequested && already > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                        already = 0;

                    long reported = response.ContentLength > 0
                        ? response.ContentLength
                        : (expected > 0 ? expected - already : 0);
                    plan.Owner.SetMaxValue(chunk.Index, already + (reported > 0 ? reported : 0));

                    var responseStream = response.GetResponseStream();
                    if (responseStream == null) return "响应流为空";

                    using (var stream = responseStream)
                    using (var file = new FileStream(
                        chunk.FilePath,
                        FileMode.OpenOrCreate,
                        FileAccess.Write,
                        FileShare.Read,
                        BufferPool.CombineBufferSize,
                        useAsync: true))
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
                                int read = await stream.ReadAsync(buffer, 0, count, token).ConfigureAwait(false);
                                if (read <= 0) break;

                                token.ThrowIfCancellationRequested();
                                if (!plan.Owner.PauseGate.IsSet)
                                    await WaitWhilePausedAsync(plan.Owner, token).ConfigureAwait(false);

                                await file.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                                total += read;
                                plan.Owner.SetValue(chunk.Index, total);
                            }
                        }
                        finally
                        {
                            BufferPool.Return(buffer);
                        }

                        await file.FlushAsync(token).ConfigureAwait(false);
                        if (expected > 0 && total < expected)
                            return "分块 " + chunk.Index + " 数据不完整（" + total + "/" + expected + "）";
                    }
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        #endregion

        #region 异步合并

        /// <summary>
        /// 异步按序号合并分块，只在每个分块的期望长度内复制，避免旧实现的多余字节问题。
        /// </summary>
        static async Task<string> CombineChunksAsync(DownloadPlan plan, CancellationToken token)
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
                BufferPool.CombineBufferSize,
                useAsync: true))
            {
                byte[] buffer = BufferPool.Rent(BufferPool.CombineBufferSize);
                try
                {
                    foreach (DownloadChunk chunk in plan.Chunks)
                    {
                        long remaining = chunk.ExpectedLength;
                        using (var input = new FileStream(
                            chunk.FilePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            BufferPool.CombineBufferSize,
                            useAsync: true))
                        {
                            while (remaining > 0)
                            {
                                int count = (int)Math.Min(BufferPool.CombineBufferSize, remaining);
                                int read = await input.ReadAsync(buffer, 0, count, token).ConfigureAwait(false);
                                if (read <= 0) throw new IOException("分块 " + chunk.Index + " 内容不足，无法合并");
                                await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
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
    }
}
#endif
