using System;
using System.Net;

namespace HttpLib
{
    partial class HttpCore
    {
        #region 回调

        #region 上传进度的回调

        Action<Progress>? onUploadProgress;
        /// <summary>
        /// 上传进度的回调函数
        /// </summary>
        public HttpCore uploadProgress(Action<Progress> action)
        {
            onUploadProgress = action;
            return this;
        }

        #region 过期

        Action<long, long> _requestProgres;
        Action<long> _requestProgresMax;
        /// <summary>
        /// 上传进度的回调函数
        /// </summary>
        [Obsolete("use uploadProgress")]
        public HttpCore requestProgres(Action<long, long> action)
        {
            _requestProgres = action;
            return this;
        }

        /// <summary>
        /// 上传进度的回调函数
        /// </summary>
        [Obsolete]
        public HttpCore requestProgresMax(Action<long> action)
        {
            _requestProgresMax = action;
            return this;
        }

        #endregion

        #endregion

        #region 下载进度的回调

        Action<Progress>? onDownloadProgress;
        /// <summary>
        /// 下载进度的回调函数
        /// </summary>
        public HttpCore downloadProgress(Action<Progress> action)
        {
            onDownloadProgress = action;
            return this;
        }

        #region 过期

        Action<long, long> _responseProgres;
        Action<long> _responseProgresMax;
        /// <summary>
        /// 下载进度的回调函数
        /// </summary>
        [Obsolete("use downloadProgress")]
        public HttpCore responseProgres(Action<long, long> action)
        {
            _responseProgres = action;
            return this;
        }

        /// <summary>
        /// 下载进度的回调函数
        /// </summary>
        [Obsolete]
        public HttpCore responseProgresMax(Action<long> action)
        {
            _responseProgresMax = action;
            return this;
        }

        #endregion

        #endregion

        #endregion

        #region 请求

        Action<HttpWebRequest> action_Request;
        public HttpCore webrequest(Action<HttpWebRequest> action)
        {
            action_Request = action;
            return this;
        }

        Func<HttpWebResponse, ResultResponse, bool> action_before;
        /// <summary>
        /// 请求之前处理
        /// </summary>
        /// <param name="action">请求之前处理回调</param>
        /// <returns>返回true继续 反之取消请求</returns>
        public HttpCore before(Func<HttpWebResponse, ResultResponse, bool> action)
        {
            action_before = action;
            return this;
        }

        Action<ResultResponse> action_fail;
        /// <summary>
        /// 接口调用失败的回调函数
        /// </summary>
        /// <param name="action">错误Http响应头+错误</param>
        /// <returns></returns>
        public HttpCore fail(Action<ResultResponse> action)
        {
            action_fail = action;
            return this;
        }

        #endregion

        #region 终止

        public void abort()
        {
            if (req != null)
            {
                try
                {
                    req.Abort();
                    req = null;
                }
                catch
                { }
            }
            if (response != null)
            {
                try
                {
                    response.Close();
#if !NET40
                    response.Dispose();
#endif
                    response = null;
                }
                catch
                { }
            }
        }

        #endregion
    }

    public class Progress
    {
        public Progress() { }
        public Progress(long value, long max)
        {
            Value = value;
            MaxValue = max;
        }

        public long Value { get; set; }
        public long MaxValue { get; set; }

        public bool GetProg(out float value)
        {
            if (MaxValue > 0)
            {
                value = Prog;
                return true;
            }
            else
            {
                value = 0;
                return false;
            }
        }

        public long bytesSent => Value;
        public long totalBytes => MaxValue;

        public float Prog => Value * 1F / MaxValue;
    }
}