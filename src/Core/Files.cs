using System;
using System.Collections.Generic;
using System.IO;

namespace HttpLib
{
    /// <summary>
    /// HTTP 请求文件
    /// </summary>
    public class Files : IDisposable
    {
        /// <summary>
        /// 参数名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 文件名
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// 文件类型
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// 文件流
        /// </summary>
        public Stream Stream { get; private set; }

        /// <summary>
        /// 文件大小
        /// </summary>
        public long Size { get; private set; }

        /// <summary>
        /// 添加文件
        /// </summary>
        /// <param name="name">参数名称</param>
        /// <param name="fileName">文件名称</param>
        /// <param name="contentType">文件类型</param>
        /// <param name="data">字节流</param>
        public Files(string name, string fileName, string contentType, byte[] data)
        {
            Name = name;
            FileName = fileName;
            ContentType = contentType;
            Size = data.Length;
            Stream = new MemoryStream(data);
        }

        /// <summary>
        /// 添加文件
        /// </summary>
        /// <param name="fileName">文件名称</param>
        /// <param name="contentType">文件类型</param>
        /// <param name="data">字节流</param>
        public Files(string fileName, string contentType, byte[] data) : this("file", fileName, contentType, data)
        { }

        /// <summary>
        /// 添加文件
        /// </summary>
        /// <param name="name">参数名称</param>
        /// <param name="fullName">文件路径</param>
        public Files(string name, string fullName)
        {
            Name = name;
            var fileInfo = new FileInfo(fullName);
            FileName = fileInfo.Name;
            ContentType = MimeMapping.GetMimeMapping(fullName);
            Stream = File.OpenRead(fullName);
            Size = Stream.Length;
        }

        /// <summary>
        /// 添加文件
        /// </summary>
        /// <param name="fullName">文件路径</param>
        public Files(string fullName) : this("file", fullName) { }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Stream?.Dispose();
            Stream = null!;
        }
    }

    class Seg
    {
        List<object> data;
        public Seg(int len)
        {
            data = new List<object>(len);
        }
        public long Add(byte[] bytes)
        {
            data.Add(bytes);
            return bytes.Length;
        }
        public long Add(byte[] bytes, Files file)
        {
            data.Add(bytes);
            data.Add(file);
            return bytes.Length + file.Size;
        }

        public object[] List => data.ToArray();
    }
}