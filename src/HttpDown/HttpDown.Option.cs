using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace HttpLib
{
    partial class HttpDown
    {
        #region 参数

        /// <summary>
        /// 自定义ID
        /// </summary>
        public string? ID { get; set; }

        /// <summary>
        /// 下载地址
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// 保存路径
        /// </summary>
        public string SavePath { get; }

        /// <summary>
        /// 超时时长
        /// </summary>
        public int TimeOut = Config.TimeOut;

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount = Config.RetryCount;

        /// <summary>
        /// 下载缓存大小
        /// </summary>
        public int CacheSize = Config.CacheSize;

        /// <summary>
        /// 下载速度
        /// </summary>
        public double Speed => _Speed;

        /// <summary>
        /// 剩余时间
        /// </summary>
        public string? Time => _Time;

        /// <summary>
        /// 当前下载值
        /// </summary>
        public long Value => _Value;

        /// <summary>
        /// 文件大小
        /// </summary>
        public long MaxValue => _MaxValue;

        /// <summary>
        /// 当前下载进度
        /// </summary>
        public float Prog => _Value * 1F / _MaxValue;

        /// <summary>
        /// 任务状态
        /// </summary>
        public DownState State => _State;

        #region 设置

        public HttpDown SetID(string? value)
        {
            ID = value;
            return this;
        }
        public HttpDown SetRetryCount(int value)
        {
            RetryCount = value;
            return this;
        }
        public HttpDown SetCacheSize(int value)
        {
            CacheSize = value;
            return this;
        }
        public HttpDown SetTimeOut(int value)
        {
            TimeOut = value;
            return this;
        }

        #endregion

        #region 非公开

        DownState _State = DownState.Ready;
        long _MaxValue, _Value, _Speed;
        string? _Time;

        long[] ValTmp = new long[0];
        long[] MaxTmp = new long[0];
        internal ManualResetEvent resetState = new ManualResetEvent(true);

        #endregion

        #endregion

        #region 任务事件

        public delegate void StateHandler(DownState e, string err);

        #region 文件大小

        int DownCount = 0, TotalCount = 0;

        Action<long>? _MaxValueChange;
        /// <summary>
        /// 文件大小改变
        /// </summary>
        public HttpDown MaxValueChange(Action<long> action)
        {
            _MaxValueChange = action;
            return this;
        }

        void SetMaxValue(long value)
        {
            if (_MaxValue == value) return;
            _MaxValue = value;
            _MaxValueChange?.Invoke(value);
        }

        void SetMaxValue()
        {
            var val = MaxTmp.Sum();
            if (TotalCount > 0) SetMaxValue(val * TotalCount / DownCount);
            else SetMaxValue(val);
        }

        internal void SetMaxValue(int i, long value)
        {
            if (MaxTmp[i] == value) return;
            MaxTmp[i] = value;
            var val = MaxTmp.Sum();
            if (TotalCount > 0) SetMaxValue(val * TotalCount / DownCount);
            else SetMaxValue(val);
        }

        #endregion

        #region 当前下载值

        Action<long>? _ValueChange;
        /// <summary>
        /// 当前下载值改变
        /// </summary>
        public HttpDown ValueChange(Action<long> action)
        {
            _ValueChange = action;
            return this;
        }

        void SetValue(long value)
        {
            if (_Value == value) return;
            _Value = value;
            _ValueChange?.Invoke(value);
        }

        internal void SetValue(int i, long value)
        {
            ValTmp[i] = value;
            SetValue(ValTmp.Sum());
        }

        #endregion

        #region 下载速度

        Action<long>? _SpeedChange;
        /// <summary>
        /// 下载速度改变
        /// </summary>
        public HttpDown SpeedChange(Action<long> action)
        {
            _SpeedChange = action;
            return this;
        }

        internal bool CanSpeed = true;
        internal void SetSpeed(long value)
        {
            if (!CanSpeed) return;
            if (_Speed == value) return;
            _Speed = value;
            _SpeedChange?.Invoke(value);
        }

        #endregion

        #region 剩余时间

        Action<string?>? _TimeChange;
        /// <summary>
        /// 剩余时间改变
        /// </summary>
        public HttpDown TimeChange(Action<string?> action)
        {
            _TimeChange = action;
            return this;
        }

        internal void SetTime(string? value)
        {
            if (!CanSpeed) return;
            if (_Time == value) return;
            _Time = value;
            _TimeChange?.Invoke(value);
        }

        #endregion

        #region 任务状态

        Action<DownState, string?>? _StateChange;
        /// <summary>
        /// 下载状态改变
        /// </summary>
        public HttpDown StateChange(Action<DownState, string?> action)
        {
            _StateChange = action;
            return this;
        }

        internal void SetState(DownState value, string? err = null)
        {
            if (_State == value) return;
            _State = value;
            _StateChange?.Invoke(value, err);
        }

        #endregion

        #endregion

        #region 初始化

        public Uri Uri;
        public HttpCore core;
        public HttpDown(HttpCore _core, string _savePath, string? id = null)
        {
            core = _core;
            Uri = core.option.uri;
            ID = id;
            SavePath = _savePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Url = core.option.uri.AbsoluteUri;
        }

        #endregion
    }

    public enum DownState
    {
        /// <summary>
        /// 准备中
        /// </summary>
        Ready,
        /// <summary>
        /// 下载中
        /// </summary>
        Downloading,
        /// <summary>
        /// 已停止
        /// </summary>
        Stop,
        /// <summary>
        /// 完成
        /// </summary>
        Complete,
        /// <summary>
        /// 异常
        /// </summary>
        Fail
    }
}