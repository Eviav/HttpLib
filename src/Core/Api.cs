using System;
using System.IO;
using System.Net.Mime;

namespace HttpLib
{
    internal static class Api
    {
        #region 文件处理

        public static bool DeleteFile(this string dir)
        {
            if (File.Exists(dir))
            {
                try
                {
                    File.Delete(dir);
                }
                catch { return false; }
            }
            return true;
        }

        #region 删除文件夹

        /// <summary>
        /// 删除文件夹以及文件夹内所有文件
        /// </summary>
        public static bool DeleteDirectory(this string dir)
        {
            if (Directory.Exists(dir))
            {
                try
                {
                    DeleteDirectory(new DirectoryInfo(dir));
                    Directory.Delete(dir, true);
                }
                catch { return false; }
            }
            return true;
        }

        /// <summary>
        /// 清除文件夹内容，但是保留文件夹
        /// </summary>
        public static bool ClearDirectory(this string dir)
        {
            if (Directory.Exists(dir))
            {
                try
                {
                    DeleteDirectory(new DirectoryInfo(dir));
                }
                catch { return false; }
            }
            else Directory.CreateDirectory(dir);
            return true;
        }
        public static void CreateDirectory(this string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
        static void DeleteDirectory(DirectoryInfo dir)
        {
            if (dir.Exists)
            {
                foreach (FileInfo item in dir.GetFiles())
                {
                    try
                    {
                        item.Delete();
                    }
                    catch { }
                }
                foreach (DirectoryInfo item in dir.GetDirectories()) DeleteDirectory(item);
            }
        }

        #endregion

        #endregion

        public static string RandomString(this int length)
        {
            string allowedChars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNOPQRSTUVWXYZ0123456789";
            char[] chars = new char[length];
            var rd = new Random();
            for (int i = 0; i < length; i++) chars[i] = allowedChars[rd.Next(0, allowedChars.Length)];
            return new string(chars);
        }

        public static string? FileNameDisposition(this string disposition)
        {
            if (disposition.Contains("filename*=UTF-8"))
            {
                try
                {
                    return Uri.UnescapeDataString(disposition.Substring(disposition.IndexOf("filename*=UTF-8") + 15).Trim('\''));
                }
                catch { }
            }
            return new ContentDisposition(disposition).FileName;
        }
        public static string FileName(this Uri uri)
        {
            if (uri.Query.Length > 0) return Path.GetFileName(uri.AbsoluteUri.Substring(0, uri.AbsoluteUri.Length - uri.Query.Length));
            return Path.GetFileName(uri.AbsoluteUri);
        }

        public static string FileName(this Uri uri, string? disposition)
        {
            if (disposition != null && !string.IsNullOrEmpty(disposition)) return disposition.FileNameDisposition() ?? uri.FileName();
            return uri.FileName();
        }
    }
}