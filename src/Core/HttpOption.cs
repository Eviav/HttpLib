using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace HttpLib
{
    public class HttpOption
    {
        /// <summary>
        /// 创建 HTTP 选项实例
        /// </summary>
        /// <param name="url">URL 地址</param>
        public HttpOption(string url)
        {
            uri = new Uri(url);
        }

        /// <summary>
        /// 创建 HTTP 选项实例
        /// </summary>
        /// <param name="url">URL 地址</param>
        /// <param name="_method">请求方法</param>
        public HttpOption(string url, HttpMethod _method)
        {
            uri = new Uri(url);
            method = _method;
        }

        /// <summary>
        /// 创建 HTTP 选项实例
        /// </summary>
        /// <param name="_uri">URL 地址</param>
        /// <param name="_method">请求方法</param>
        public HttpOption(Uri _uri, HttpMethod _method)
        {
            uri = _uri;
            method = _method;
        }

        public Uri uri { get; }
        public HttpMethod method { get; set; } = HttpMethod.Get;

        #region 参数

        /// <summary>
        /// Get参数
        /// </summary>
        public List<Val>? query { get; set; }
        /// <summary>
        /// 参数
        /// </summary>
        public List<Val>? data { get; set; }
        /// <summary>
        /// body参数
        /// </summary>
        public string? datastr { get; set; }

        /// <summary>
        /// 上传文件
        /// </summary>
        public List<Files>? file { get; set; }

        #endregion

        #region 头

        /// <summary>
        /// 头
        /// </summary>
        public List<Val>? header { get; set; }

        #endregion

        /// <summary>
        /// 代理
        /// </summary>
        public IWebProxy? proxy { get; set; }

        /// <summary>
        /// 编码
        /// </summary>
        public Encoding? encoding { get; set; }

        /// <summary>
        /// 自动编码
        /// </summary>
        public bool autoencode { get; set; }

        /// <summary>
        /// 请求重定向
        /// </summary>
        public bool redirect { get; set; }

        /// <summary>
        /// 请求超时时长
        /// </summary>
        public int timeout { get; set; }

        /// <summary>
        /// 获取域名IP
        /// </summary>
        public string? IP
        {
            get
            {
                try
                {
                    if (IPAddress.TryParse(uri.Host, out IPAddress? ip)) return ip.ToString();
                    else
                    {
                        var hostEntry = Dns.GetHostEntry(uri.Host);
                        var ipEndPoint = new IPEndPoint(hostEntry.AddressList[0], 0);
                        string _ip = ipEndPoint.Address.ToString();
                        if (_ip.StartsWith("::")) return "127.0.0.1";
                        else return _ip;
                    }
                }
                catch { }
                return null;
            }
        }

        /// <summary>
        /// 请求URL
        /// </summary>
        public Uri Url
        {
            get
            {
                #region 合并参数

                var data = new List<string>();
                if (query != null && query.Count > 0) foreach (var it in query) data.Add(it.ToStringEscape());

                if (method == HttpMethod.Get && this.data != null && this.data.Count > 0) foreach (var it in this.data) data.Add(it.ToStringEscape());

                #endregion

                if (data.Count > 0)
                {
                    if (string.IsNullOrEmpty(uri.Query)) return new Uri(uri.AbsoluteUri + "?" + string.Join("&", data));
                    else return new Uri(uri.AbsoluteUri + "&" + string.Join("&", data));
                }

                return uri;
            }
        }

        public string FileName(ResultResponse _web)
        {
            if (_web.Header.ContainsKey("Content-Disposition"))
            {
                string val = _web.Header["Content-Disposition"];
                if (!string.IsNullOrEmpty(val)) return val.FileNameDisposition() ?? uri.FileName();
            }
            return uri.FileName();
        }

        public override string ToString() => uri.AbsoluteUri;
    }
}