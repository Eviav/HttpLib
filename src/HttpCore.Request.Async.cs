#if !NET40
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HttpLib
{
    partial class HttpCore
    {
        #region 异步请求 API

        /// <summary>
        /// 异步请求（不下载响应流）
        /// </summary>
        public async Task<ResultResponse> requestNoneAsync(CancellationToken token = default)
        {
            var result = await GoAsync(0, null, null, null, token).ConfigureAwait(false);
            return result.web;
        }

        /// <summary>
        /// 异步请求并返回字符串
        /// </summary>
        public async Task<string?> requestAsync(CancellationToken token = default)
        {
            var result = await GoAsync(1, null, null, null, token).ConfigureAwait(false);
            return result.str;
        }

        /// <summary>
        /// 异步请求并返回字符串与响应信息
        /// </summary>
        public async Task<RequestResult> requestWithResponseAsync(CancellationToken token = default)
        {
            var result = await GoAsync(1, null, null, null, token).ConfigureAwait(false);
            return new RequestResult { Body = result.str, Response = result.web };
        }

        /// <summary>
        /// 异步请求并返回字节数组
        /// </summary>
        public async Task<byte[]?> requestDataAsync(CancellationToken token = default)
        {
            var result = await GoAsync(2, null, null, null, token).ConfigureAwait(false);
            return result.data;
        }

        /// <summary>
        /// 异步请求并返回字节数组与响应信息
        /// </summary>
        public async Task<RequestResult> requestDataWithResponseAsync(CancellationToken token = default)
        {
            var result = await GoAsync(2, null, null, null, token).ConfigureAwait(false);
            return new RequestResult { Data = result.data, Response = result.web };
        }

        /// <summary>
        /// 异步下载文件到指定目录
        /// </summary>
        /// <param name="savePath">保存目录</param>
        /// <param name="saveName">保存文件名，为空时从响应推断</param>
        /// <param name="token">取消令牌</param>
        /// <returns>保存后的文件路径，失败返回 null</returns>
        public async Task<string?> downloadAsync(
            string savePath,
            string? saveName = null,
            CancellationToken token = default)
        {
            var result = await GoAsync(3, savePath, saveName, null, token).ConfigureAwait(false);
            return result.str;
        }

        /// <summary>
        /// 异步流式请求（逐行回调）
        /// </summary>
        public async Task<string?> requestAsync(Action<string?> eventstream, CancellationToken token = default)
        {
            var result = await GoAsync(1, null, null, eventstream, token).ConfigureAwait(false);
            return result.str;
        }

        #endregion

        #region 异步核心

        /// <summary>
        /// 异步请求核心流程。与同步版本共用 CreateRequest / ResultResponse / 缓存逻辑，
        /// 只在网络等待和流复制部分使用异步实现，避免重复维护两套业务规则。
        /// </summary>
        async Task<TaskResult> GoAsync(
            int resultMode,
            string? savePath,
            string? saveName,
            Action<string?>? eventstream,
            CancellationToken token)
        {
            try
            {
                if (_cache != null)
                {
                    TaskResult? cached = ReadCache(_cache, resultMode);
                    if (cached != null) return cached;
                }

                abort();
                var request = CreateRequest();
                action_Request?.Invoke(request);

                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();

                    long responseMax = response.ContentLength;
                    Max = responseMax;
                    _responseProgresMax?.Invoke(responseMax);

                    var web = new ResultResponse(response);
                    if (action_before != null && !action_before(response, web)) return new TaskResult(web);

                    switch (resultMode)
                    {
                        case 3:
                            {
                                using (var stream = response.GetResponseStream())
                                {
                                    StreamCopyResult copied = await DownStreamAsync(
                                        responseMax, web, stream, savePath, saveName, token).ConfigureAwait(false);
                                    return new TaskResult(web, copied.OutputPath);
                                }
                            }
                        case 1:
                            {
                                if (eventstream != null)
                                {
                                    using (var reader = new StreamReader(response.GetResponseStream()))
                                    {
                                        while (true)
                                        {
                                            token.ThrowIfCancellationRequested();
                                            string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                                            if (line == null) break;
                                            eventstream(line);
                                        }
                                        return new TaskResult(web);
                                    }
                                }

                                using (var stream = response.GetResponseStream())
                                {
                                    StreamCopyResult copied = await DownStreamAsync(
                                        responseMax, web, stream, null, null, token).ConfigureAwait(false);
                                    if (copied.Data == null) return new TaskResult(web);

                                    var encodings = option.encoding;
                                    if (encodings == null || option.autoencode) encodings = GetEncoding(response, copied.Data);
                                    string result = encodings.GetString(copied.Data);
                                    if (_cache != null)
                                    {
                                        _cache.path.CreateDirectory();
                                        WriteCacheText(_cache.file, result);
                                    }
                                    return new TaskResult(web, result);
                                }
                            }
                        case 2:
                            {
                                using (var stream = response.GetResponseStream())
                                {
                                    StreamCopyResult copied = await DownStreamAsync(
                                        responseMax, web, stream, null, null, token).ConfigureAwait(false);
                                    if (copied.Data != null && _cache != null)
                                    {
                                        _cache.path.CreateDirectory();
                                        WriteCache(_cache.file, copied.Data);
                                    }
                                    return new TaskResult(web, copied.Data);
                                }
                            }
                        default:
                            return new TaskResult(web);
                    }
                }
            }
            catch (OperationCanceledException ex)
            {
                // 与同步版本保持一致：异常通过 ResultResponse.Exception 暴露，不直接抛出
                return new TaskResult(new ResultResponse(option.Url, ex));
            }
            catch (WebException err)
            {
                var web = err.Response is HttpWebResponse http
                    ? new ResultResponse(http, err)
                    : new ResultResponse(option.Url, err);
                Config.OnFail(this, web);
                action_fail?.Invoke(web);
                return new TaskResult(web);
            }
            catch (Exception err)
            {
                var web = new ResultResponse(option.Url, err);
                Config.OnFail(this, web);
                action_fail?.Invoke(web);
                return new TaskResult(web);
            }
        }

        /// <summary>异步流复制结果</summary>
        sealed class StreamCopyResult
        {
            public byte[]? Data;
            public string? OutputPath;
        }

        /// <summary>
        /// 异步复制响应流到内存或文件，带进度回调与长度校验。
        /// 缓冲区从 BufferPool 租用，避免每个请求重复分配大数组。
        /// </summary>
        async Task<StreamCopyResult> DownStreamAsync(
            long responseMax,
            ResultResponse web,
            Stream stream,
            string? savePath,
            string? saveName,
            CancellationToken token)
        {
            Stream target;
            string? outfile = null;
            if (savePath == null)
            {
                target = new MemoryStream();
            }
            else
            {
                savePath = savePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                savePath.CreateDirectory();
                saveName ??= option.FileName(web);
                outfile = savePath + saveName;
                target = new FileStream(outfile, FileMode.Create, FileAccess.Write, FileShare.Read,
                    BufferPool.CombineBufferSize, useAsync: true);
            }

            long value = 0;
            using (target)
            {
                byte[] buffer = BufferPool.Rent(Config.CacheSize);
                try
                {
                    if (onDownloadProgress != null) onDownloadProgress(new Progress(0, responseMax));
                    else _responseProgres?.Invoke(0, responseMax);

                    while (true)
                    {
                        int read = await stream.ReadAsync(buffer, 0, Config.CacheSize, token).ConfigureAwait(false);
                        if (read <= 0) break;
                        await target.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                        value += read;
                        Val = value;
                        if (onDownloadProgress != null) onDownloadProgress(new Progress(value, responseMax));
                        else _responseProgres?.Invoke(value, responseMax);
                    }
                }
                finally
                {
                    BufferPool.Return(buffer);
                }

                web.OriginalSize = web.Size = value;

                // 声明了长度却没收全时丢弃结果，避免半截内容进入缓存或落盘
                if ((responseMax > 0 && value != responseMax) || value == 0)
                {
                    if (outfile != null)
                    {
                        try { if (File.Exists(outfile)) File.Delete(outfile); } catch { }
                    }
                    return new StreamCopyResult();
                }

                if (savePath == null) return new StreamCopyResult { Data = GetByStream(web, target) };
                return new StreamCopyResult { OutputPath = outfile };
            }
        }

        #endregion
    }

    /// <summary>
    /// 异步请求结果：同时携带响应头和响应体。
    /// 由于 C# 不允许异步方法使用 out 参数，这里用结果对象替代同步版本的 out 重载。
    /// </summary>
    public sealed class RequestResult
    {
        /// <summary>字符串响应体（requestWithResponseAsync 有效）</summary>
        public string? Body { get; internal set; }

        /// <summary>字节响应体（requestDataWithResponseAsync 有效）</summary>
        public byte[]? Data { get; internal set; }

        /// <summary>响应头与状态信息</summary>
        public ResultResponse Response { get; internal set; } = new ResultResponse(new Uri("about:blank"));
    }
}
#endif