using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace HttpLib
{
    /// <summary>
    /// HTTP 请求核心类
    /// </summary>
    public partial class HttpCore : IDisposable
    {
        /// <summary>
        /// HTTP 请求选项
        /// </summary>
        public HttpOption option;

        /// <summary>
        /// 创建 HTTP 请求核心实例
        /// </summary>
        /// <param name="uri">URL 地址</param>
        /// <param name="method">请求方法</param>
        public HttpCore(string uri, HttpMethod method)
        {
            option = new HttpOption(uri, method);
        }

        /// <summary>
        /// 创建 HTTP 请求核心实例
        /// </summary>
        /// <param name="uri">URL 地址</param>
        /// <param name="method">请求方法</param>
        public HttpCore(Uri uri, HttpMethod method)
        {
            option = new HttpOption(uri, method);
        }

        /// <summary>
        /// 创建 HTTP 请求核心实例
        /// </summary>
        /// <param name="_option">HTTP 请求选项</param>
        public HttpCore(HttpOption _option)
        {
            option = _option;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            abort();
        }

        #region 请求参(GET)

        /// <summary>
        /// 请求参(GET)
        /// </summary>
        /// <param name="vals">多个参数</param>
        public HttpCore query(params Val[] vals)
        {
            option.query ??= new List<Val>(vals.Length);
            option.query.AddRange(vals);
            return this;
        }

        /// <summary>
        /// 请求参(GET)
        /// </summary>
        /// <param name="vals">多个参数</param>
        public HttpCore query(IList<Val> vals)
        {
            option.query ??= new List<Val>(vals.Count);
            option.query.AddRange(vals);
            return this;
        }

        /// <summary>
        /// 请求参(GET)
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="val">值</param>
        public HttpCore query(string key, string? val)
        {
            if (option.query == null) option.query = new List<Val> { new Val(key, val) };
            else option.query.Add(new Val(key, val));
            return this;
        }

        /// <summary>
        /// 请求参(GET)
        /// </summary>
        /// <param name="vals">多个参数</param>
        public HttpCore query(IDictionary<string, string> vals)
        {
            option.query ??= new List<Val>(vals.Count);
            foreach (var it in vals) option.query.Add(new Val(it.Key, it.Value));
            return this;
        }

        /// <summary>
        /// 请求参(GET)
        /// </summary>
        /// <param name="data">多个参数</param>
        public HttpCore query(object data)
        {
            var list = data.GetType().GetProperties();
            var vals = new List<Val>(list.Length);
            foreach (var it in list)
            {
                string key = it.Name;
                if (key != "_" && key.EndsWith("_")) key = key.TrimEnd('_');
                var valO = it.GetValue(data, null);
                if (valO != null) vals.Add(new Val(key, valO.ToString()));
            }
            if (vals.Count > 0) return query(vals);
            return this;
        }

        #endregion

        #region 请求参

        /// <summary>
        /// 请求参
        /// </summary>
        /// <param name="vals">多个参数</param>
        public HttpCore data(params Val[] vals)
        {
            option.data ??= new List<Val>(vals.Length);
            option.data.AddRange(vals);
            return this;
        }

        /// <summary>
        /// 请求参
        /// </summary>
        /// <param name="vals">多个参数</param>
        public HttpCore data(IList<Val> vals)
        {
            option.data ??= new List<Val>(vals.Count);
            option.data.AddRange(vals);
            return this;
        }

        /// <summary>
        /// 请求参
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="val">值</param>
        public HttpCore data(string key, string? val)
        {
            if (option.data == null) option.data = new List<Val> { new Val(key, val) };
            else option.data.Add(new Val(key, val));
            return this;
        }

        /// <summary>
        /// 请求参
        /// </summary>
        /// <param name="vals">多个参数</param>
        public HttpCore data(IDictionary<string, string> vals)
        {
            option.data ??= new List<Val>(vals.Count);
            foreach (var it in vals) option.data.Add(new Val(it.Key, it.Value));
            return this;
        }

        /// <summary>
        /// 请求参
        /// </summary>
        /// <param name="_data">多个参数</param>
        public HttpCore data(object _data)
        {
            var list = _data.GetType().GetProperties();
            var vals = new List<Val>(list.Length);
            foreach (var it in list)
            {
                string key = it.Name;
                if (key != "_" && key.EndsWith("_")) key = key.TrimEnd('_');
                var valO = it.GetValue(_data, null);
                if (valO != null) vals.Add(new Val(key, valO.ToString()));
            }
            if (vals.Count > 0) return data(vals);
            return this;
        }

        /// <summary>
        /// 请求参（数组）
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="val">值</param>
        public HttpCore data(string key, IList<string> val)
        {
            if (option.method == HttpMethod.Get) throw new Exception("Get不支持数组");
            option.data ??= new List<Val>(val.Count);
            foreach (var it in val) option.data.Add(new Val(key + "[]", it));
            return this;
        }

        /// <summary>
        /// 请求参
        /// </summary>
        /// <param name="val">text/plain</param>
        /// <param name="contentType">类型</param>
        public HttpCore datastr(string val, string contentType = "text/plain")
        {
            option.datastr = val;
            header("Content-Type", contentType);
            return this;
        }

        #region 文件

        /// <summary>
        /// 请求参（文件）
        /// </summary>
        /// <param name="files">多个文件</param>
        public HttpCore data(params Files[] files)
        {
            option.file ??= new List<Files>(files.Length);
            option.file.AddRange(files);
            return this;
        }

        /// <summary>
        /// 请求参（文件）
        /// </summary>
        /// <param name="files">多个文件</param>
        public HttpCore data(IList<Files> files)
        {
            option.file ??= new List<Files>(files.Count);
            option.file.AddRange(files);
            return this;
        }

        /// <summary>
        /// 请求参（文件）
        /// </summary>
        /// <param name="vals">多个文件</param>
        public HttpCore file(params string[] vals)
        {
            option.file ??= new List<Files>(vals.Length);
            foreach (var item in vals) option.file.Add(new Files(item));
            return this;
        }

        /// <summary>
        /// 请求参（文件）
        /// </summary>
        /// <param name="vals">多个文件</param>
        public HttpCore file(params Files[] vals)
        {
            option.file ??= new List<Files>(vals.Length);
            option.file.AddRange(vals);
            return this;
        }

        /// <summary>
        /// 请求参（文件）
        /// </summary>
        /// <param name="vals">多个文件</param>
        public HttpCore file(IList<Files> vals)
        {
            option.file ??= new List<Files>(vals.Count);
            option.file.AddRange(vals);
            return this;
        }

        #endregion

        #endregion

        #region 请求头

        /// <summary>
        /// 请求头
        /// </summary>
        /// <param name="vals">多个参数</param>
        public HttpCore header(params Val[] vals)
        {
            option.header ??= new List<Val>(vals.Length);
            option.header.AddRange(vals);
            return this;
        }

        /// <summary>
        /// 请求头
        /// </summary>
        /// <param name="vals">多个参数</param>
        public HttpCore header(IList<Val> vals)
        {
            option.header ??= new List<Val>(vals.Count);
            option.header.AddRange(vals);
            return this;
        }

        /// <summary>
        /// 请求头
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="val">值</param>
        public HttpCore header(string key, string? val)
        {
            if (option.header == null) option.header = new List<Val> { new Val(key, val) };
            else option.header.Add(new Val(key, val));
            return this;
        }

        /// <summary>
        /// 请求头
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="val">值</param>
        public HttpCore header(string key, long val)
        {
            if (option.header == null) option.header = new List<Val> { new Val(key, val) };
            else option.header.Add(new Val(key, val));
            return this;
        }

        /// <summary>
        /// 请求头
        /// </summary>
        /// <param name="vals">多个参数</param>
        public HttpCore header(IDictionary<string, string> vals)
        {
            option.header ??= new List<Val>(vals.Count);
            foreach (var it in vals) option.header.Add(new Val(it.Key, it.Value));
            return this;
        }

        /// <summary>
        /// 请求头
        /// </summary>
        /// <param name="data">多个参数</param>
        public HttpCore header(object data)
        {
            var list = data.GetType().GetProperties();
            var vals = new List<Val>(list.Length);
            foreach (var it in list)
            {
                string key = GetTFName(it.Name).TrimStart('-');
                if (key != "_" && key.EndsWith("_")) key = key.TrimEnd('_');
                var valO = it.GetValue(data, null);
                if (valO != null) vals.Add(new Val(key, valO.ToString()));
            }
            if (vals.Count > 0) return header(vals);
            return this;
        }

        #endregion

        #region 代理

        /// <summary>
        /// 代理
        /// </summary>
        /// <param name="address">服务器URI</param>
        public HttpCore proxy(string address)
        {
            option.proxy = new WebProxy(address);
            return this;
        }

        /// <summary>
        /// 代理
        /// </summary>
        /// <param name="address">服务器URI</param>
        public HttpCore proxy(Uri address)
        {
            option.proxy = new WebProxy(address);
            return this;
        }

        /// <summary>
        /// 代理
        /// </summary>
        /// <param name="host">主机名称</param>
        /// <param name="port">端口</param>
        public HttpCore proxy(string host, int port)
        {
            option.proxy = new WebProxy(host, port);
            return this;
        }

        /// <summary>
        /// 代理
        /// </summary>
        /// <param name="host">主机名称</param>
        /// <param name="port">端口</param>
        /// <param name="username">用户名</param>
        /// <param name="password">密码</param>
        public HttpCore proxy(string host, int port, string? username, string? password)
        {
            option.proxy = new WebProxy(host, port);
            if (!string.IsNullOrEmpty(username)) option.proxy.Credentials = new NetworkCredential(username, password);
            return this;
        }

        #endregion

        #region 编码

        /// <summary>
        /// 编码
        /// </summary>
        /// <param name="encoding">编码</param>
        public HttpCore encoding(string encoding)
        {
            option.encoding = Encoding.GetEncoding(encoding);
            return this;
        }

        /// <summary>
        /// 编码
        /// </summary>
        /// <param name="encoding">编码</param>
        public HttpCore encoding(Encoding encoding)
        {
            option.encoding = encoding;
            return this;
        }

        /// <summary>
        /// 自动编码（识别html编码格式）
        /// </summary>
        public HttpCore autoencode(bool auto = true)
        {
            option.autoencode = auto;
            return this;
        }

        #endregion

        #region 重定向

        /// <summary>
        /// 重定向
        /// </summary>
        public HttpCore redirect(bool val = true)
        {
            option.redirect = val;
            return this;
        }

        #endregion

        #region 超时

        /// <summary>
        /// 超时
        /// </summary>
        /// <param name="time">毫秒</param>
        public HttpCore timeout(int time)
        {
            option.timeout = time;
            return this;
        }

        #endregion

        #region 缓存

        CacheModel? _cache;
        /// <summary>
        /// 设置缓存
        /// </summary>
        /// <param name="id">缓存id</param>
        public HttpCore cache(string id)
        {
            _cache = new CacheModel(id);
            return this;
        }

        /// <summary>
        /// 设置缓存
        /// </summary>
        /// <param name="id">缓存id</param>
        /// <param name="time">有效期 分钟</param>
        public HttpCore cache(string id, int time)
        {
            _cache = new CacheModel(id) { t = time };
            return this;
        }

        class CacheModel
        {
            public CacheModel(string _id)
            {
                if (Config.CacheFolder == null) throw new Exception("先配置 \"Config.CachePath\"");
                path = Config.CacheFolder;
                id = _id;
                file = path + _id;
            }
            public string id { get; set; }
            public int t = 0;
            public string path { get; set; }
            public string file { get; set; }
        }

        #endregion

        #region 分块

        /// <summary>
        /// 清空字节范围
        /// </summary>
        public HttpCore range()
        {
            _range = null;
            return this;
        }

        long[]? _range;
        /// <summary>
        /// 字节范围
        /// </summary>
        /// <param name="from">开始发送数据的位置</param>
        /// <param name="to">停止发送数据的位置</param>
        public HttpCore range(long from, long to)
        {
            _range = new long[] { from, to };
            return this;
        }

        /// <summary>
        /// 字节范围
        /// </summary>
        /// <param name="from">开始发送数据的位置</param>
        /// <param name="to">停止发送数据的位置</param>
        public HttpCore range(int from, int to)
        {
            _range = new long[] { from, to };
            return this;
        }

        #endregion
    }

    public enum HttpMethod
    {
        Get,
        Post,
        Put,
        Delete,
        Head,
        Patch,
        Options
    }
}