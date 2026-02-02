using System;

namespace HttpLib
{
    /// <summary>
    /// HTTP 请求参数值
    /// </summary>
    public class Val
    {
        /// <summary>
        /// 创建参数值实例
        /// </summary>
        /// <param name="key">参数键</param>
        /// <param name="value">参数值</param>
        public Val(string key, int? value) : this(key, value?.ToString()) { }

        /// <summary>
        /// 创建参数值实例
        /// </summary>
        /// <param name="key">参数键</param>
        /// <param name="value">参数值</param>
        public Val(string key, long? value) : this(key, value?.ToString()) { }

        /// <summary>
        /// 创建参数值实例
        /// </summary>
        /// <param name="key">参数键</param>
        /// <param name="value">参数值</param>
        public Val(string key, double? value) : this(key, value?.ToString()) { }

        /// <summary>
        /// 创建参数值实例
        /// </summary>
        /// <param name="key">参数键</param>
        /// <param name="value">参数值</param>
        public Val(string key, int value) : this(key, value.ToString()) { }

        /// <summary>
        /// 创建参数值实例
        /// </summary>
        /// <param name="key">参数键</param>
        /// <param name="value">参数值</param>
        public Val(string key, long value) : this(key, value.ToString()) { }

        /// <summary>
        /// 创建参数值实例
        /// </summary>
        /// <param name="key">参数键</param>
        /// <param name="value">参数值</param>
        public Val(string key, double value) : this(key, value.ToString()) { }

        /// <summary>
        /// 创建参数值实例
        /// </summary>
        /// <param name="key">参数键</param>
        /// <param name="value">参数值</param>
        public Val(string key, string? value)
        {
            Key = key;
            Value = value;
        }

        /// <summary>
        /// 参数键
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// 参数值
        /// </summary>
        public string? Value { get; set; }

        /// <summary>
        /// 转换为字符串表示
        /// </summary>
        /// <returns>键值对字符串</returns>
        public override string ToString() => Key + "=" + Value;

        /// <summary>
        /// 转换为 URL 编码的字符串表示
        /// </summary>
        /// <returns>URL 编码的键值对字符串</returns>
        public string ToStringEscape()
        {
            if (Value != null) return Key + "=" + Uri.EscapeDataString(Value);
            else return Key + "=";
        }
    }
}