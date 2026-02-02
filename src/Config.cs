using System;
using System.Collections.Generic;
using System.Net;

namespace HttpLib
{
    /// <summary>
    /// 全局配置
    /// </summary>
    public class Config
    {
        public static string? CacheFolder { get; set; }

        #region 请求头

        public static List<Val>? headers { get; set; }

        /// <summary>
        /// 请求头
        /// </summary>
        /// <param name="vals">多个参数</param>
        public static void header(params Val[] vals)
        {
            headers ??= new List<Val>(vals.Length);
            headers.AddRange(vals);
        }

        /// <summary>
        /// 请求头
        /// </summary>
        /// <param name="vals">多个参数</param>
        public static void header(IList<Val> vals)
        {
            headers ??= new List<Val>(vals.Count);
            headers.AddRange(vals);
        }

        /// <summary>
        /// 请求头
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="val">值</param>
        public static void header(string key, string? val)
        {
            if (headers == null) headers = new List<Val> { new Val(key, val) };
            else headers.Add(new Val(key, val));
        }

        /// <summary>
        /// 请求头
        /// </summary>
        /// <param name="vals">多个参数</param>
        public static void header(IDictionary<string, string> vals)
        {
            headers ??= new List<Val>(vals.Count);
            foreach (var it in vals) headers.Add(new Val(it.Key, it.Value));
        }

        #endregion

        /// <summary>
        /// 用户标识
        /// </summary>
        public static string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/88.0.4324.190 Safari/537.36";

        /// <summary>
        /// 表示文件压缩和解压缩编码格式，该格式将用来压缩在 System.Net.HttpWebRequest 的响应中收到的数据
        /// </summary>
        public static DecompressionMethods DecompressionMethod { get; set; } = DecompressionMethods.GZip;

        /// <summary>
        /// 全局是否自动重定向
        /// </summary>
        public static bool Redirect { get; set; } = false;

        #region 全局错误

        public delegate void ErrEventHandler(HttpCore core, ResultResponse result);

        /// <summary>
        /// 接口调用失败的回调函数（带响应头）
        /// </summary>
        public static event ErrEventHandler? fail;

        public static void OnFail(HttpCore core, ResultResponse result) => fail?.Invoke(core, result);

        #endregion

        #region 代理

        public static IWebProxy? _proxy { get; set; }

        /// <summary>
        /// 全局代理
        /// </summary>
        /// <param name="address">代理服务器的 URI</param>
        public static void proxy(string address) => _proxy = new WebProxy(address);
        /// <summary>
        /// 全局代理
        /// </summary>
        /// <param name="address">代理服务器的 URI</param>
        public static void proxy(Uri address) => _proxy = new WebProxy(address);

        /// <summary>
        /// 全局代理
        /// </summary>
        /// <param name="host">代理主机的名称</param>
        /// <param name="port">要使用的 Host 上的端口号</param>
        public static void proxy(string host, int port) => _proxy = new WebProxy(host, port);

        /// <summary>
        /// 全局代理
        /// </summary>
        /// <param name="host">代理主机的名称</param>
        /// <param name="port">要使用的 Host 上的端口号</param>
        /// <param name="username">用户名</param>
        /// <param name="password">密码</param>
        public static void proxy(string host, int port, string username, string password)
        {
            _proxy = new WebProxy(host, port);
            if (!string.IsNullOrEmpty(username)) _proxy.Credentials = new NetworkCredential(username, password);
        }

        #endregion

        public static int CacheSize { get; set; } = 4096;

        /// <summary>
        /// 重试次数
        /// </summary>
        public static int RetryCount { get; set; } = 3;

        /// <summary>
        /// 超时时长
        /// </summary>
        public static int TimeOut { get; set; } = 10000;
    }
}