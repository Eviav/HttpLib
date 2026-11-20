using System;
using System.IO;
using System.Threading;

namespace HttpLib
{
    partial class HttpDown
    {
        #region 参数

        /// <summary>
        /// 自定义任务 ID。相同 ID 的下载会复用工作目录，可用于断点续传。
        /// </summary>
        public string? ID { get; set; }

        /// <summary>
        /// 下载地址
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// 保存目录（内部始终以目录分隔符结尾）
        /// </summary>
        public string SavePath { get; }

        /// <summary>
        /// 单次请求超时时长（毫秒）
        /// </summary>
        public int TimeOut = Config.TimeOut;

        /// <summary>
        /// 单个分块失败后的重试次数
        /// </summary>
        public int RetryCount = Config.RetryCount;

        /// <summary>
        /// 下载缓冲区大小（字节）。异步实现会从缓冲区池租用该大小的数组。
        /// </summary>
        public int CacheSize = Config.CacheSize;

        /// <summary>
        /// 当前下载速度（字节/秒）
        /// </summary>
        public double Speed => _Speed;

        /// <summary>
        /// 预计剩余时间，格式 HH:MM:SS 或 MM:SS；无法估算时为 null
        /// </summary>
        public string? Time => _Time;

        /// <summary>
        /// 已下载字节总数
        /// </summary>
        public long Value => _Value;

        /// <summary>
        /// 文件总字节数，未知长度时为 0
        /// </summary>
        public long MaxValue => _MaxValue;

        /// <summary>
        /// 下载进度（0~1）。文件大小未知时返回 0，避免返回 NaN。
        /// </summary>
        public float Prog => _MaxValue > 0 ? _Value * 1F / _MaxValue : 0F;

        /// <summary>
        /// 任务状态
        /// </summary>
        public DownState State => _State;

        #region 链式设置

        /// <summary>设置自定义任务 ID</summary>
        public HttpDown SetID(string? value)
        {
            ID = value;
            return this;
        }

        /// <summary>设置单个分块失败后的重试次数</summary>
        public HttpDown SetRetryCount(int value)
        {
            RetryCount = value < 0 ? 0 : value;
            return this;
        }

        /// <summary>设置下载缓冲区大小（字节）</summary>
        public HttpDown SetCacheSize(int value)
        {
            CacheSize = value < 1024 ? 1024 : value;
            return this;
        }

        /// <summary>设置请求超时时长（毫秒）</summary>
        public HttpDown SetTimeOut(int value)
        {
            TimeOut = value;
            return this;
        }

        #endregion

        #region 内部状态

        DownState _State = DownState.Ready;
        long _MaxValue, _Value;
        double _Speed;
        string? _Time;

        // 每个分块的进度。写入使用 Interlocked.Exchange，保证多线程累加不丢失。
        long[] ValTmp = new long[0];
        long[] MaxTmp = new long[0];
        long _downloadedTotal;
        long _expectedTotal;

        // 所有事件在同一个锁内发布，避免多线程回调乱序（尤其是 UI 进度更新）。
        readonly object _eventSync = new object();

        /// <summary>
        /// 暂停闸门：Set 表示运行，Reset 表示暂停。
        /// 使用 ManualResetEventSlim 而非 ManualResetEvent，避免为短等待创建内核句柄。
        /// </summary>
        internal readonly ManualResetEventSlim PauseGate = new ManualResetEventSlim(true);

        /// <summary>
        /// 终止信号：一旦取消不可恢复，与「暂停/恢复」语义分离。
        /// </summary>
        internal readonly CancellationTokenSource StopSource = new CancellationTokenSource();

        /// <summary>供速度监控读取的已下载字节数（原子读）</summary>
        internal long DownloadedBytes => Interlocked.Read(ref _downloadedTotal);

        /// <summary>是否已收到终止（停止/释放）信号</summary>
        public bool IsStopped => StopSource.IsCancellationRequested;

        /// <summary>
        /// 当前工作线程是否应当退出：若处于暂停状态会先阻塞等待，
        /// 恢复后再检查终止信号。旧实现用 ManualResetEvent.Wait() 的返回值表达
        /// 「是否停止」，语义相反且读起来费解，这里改为语义明确的写法。
        /// </summary>
        internal bool ShouldStop()
        {
            if (StopSource.IsCancellationRequested) return true;
            PauseGate.Wait();
            return StopSource.IsCancellationRequested;
        }

        /// <summary>发出终止信号并唤醒所有因暂停而阻塞的线程</summary>
        internal void SignalStop()
        {
            try { StopSource.Cancel(); } catch (ObjectDisposedException) { }
            try { PauseGate.Set(); } catch (ObjectDisposedException) { }
        }

        #endregion

        #endregion

        #region 任务事件

        /// <summary>状态变化回调：(状态, 补充说明/错误信息)</summary>
        public delegate void StateHandler(DownState e, string err);

        #region 文件大小

        Action<long>? _MaxValueChange;

        /// <summary>注册「文件大小变化」回调</summary>
        public HttpDown MaxValueChange(Action<long> action)
        {
            _MaxValueChange = action;
            return this;
        }

        /// <summary>初始化分块容量并发布一次文件大小</summary>
        internal void ResetSizeState(long[] chunkMaxValues)
        {
            MaxTmp = chunkMaxValues;
            Interlocked.Exchange(ref _expectedTotal, Sum(chunkMaxValues));
            PublishMaxValue(Interlocked.Read(ref _expectedTotal));
        }

        internal void SetMaxValue(int index, long value)
        {
            long previous = Interlocked.Exchange(ref MaxTmp[index], value);
            long delta = value - previous;
            if (delta == 0) return;
            PublishMaxValue(Interlocked.Add(ref _expectedTotal, delta));
        }

        void PublishMaxValue(long value)
        {
            lock (_eventSync)
            {
                if (_MaxValue == value) return;
                _MaxValue = value;
                _MaxValueChange?.Invoke(value);
            }
        }

        static long Sum(long[] values)
        {
            long result = 0;
            for (int i = 0; i < values.Length; i++) result += values[i];
            return result;
        }

        #endregion

        #region 当前下载值

        Action<long>? _ValueChange;

        /// <summary>注册「已下载字节数变化」回调</summary>
        public HttpDown ValueChange(Action<long> action)
        {
            _ValueChange = action;
            return this;
        }

        internal void ResetValueState(int chunkCount)
        {
            ValTmp = new long[chunkCount];
            Interlocked.Exchange(ref _downloadedTotal, 0);
            PublishValue(0);
        }

        /// <summary>更新指定分块已下载的字节数，并原子累加到总值</summary>
        internal void SetValue(int index, long value)
        {
            long previous = Interlocked.Exchange(ref ValTmp[index], value);
            long delta = value - previous;
            if (delta == 0) return;
            PublishValue(Interlocked.Add(ref _downloadedTotal, delta));
        }

        void PublishValue(long value)
        {
            lock (_eventSync)
            {
                if (_Value == value) return;
                _Value = value;
                _ValueChange?.Invoke(value);
            }
        }

        #endregion

        #region 下载速度

        Action<long>? _SpeedChange;

        /// <summary>注册「下载速度变化」回调（字节/秒）</summary>
        public HttpDown SpeedChange(Action<long> action)
        {
            _SpeedChange = action;
            return this;
        }

        internal volatile bool CanSpeed = true;

        internal void SetSpeed(long value)
        {
            if (!CanSpeed) return;
            lock (_eventSync)
            {
                if (_Speed == value) return;
                _Speed = value;
                _SpeedChange?.Invoke(value);
            }
        }

        #endregion

        #region 剩余时间

        Action<string?>? _TimeChange;

        /// <summary>注册「预计剩余时间变化」回调</summary>
        public HttpDown TimeChange(Action<string?> action)
        {
            _TimeChange = action;
            return this;
        }

        internal void SetTime(string? value)
        {
            if (!CanSpeed) return;
            lock (_eventSync)
            {
                if (_Time == value) return;
                _Time = value;
                _TimeChange?.Invoke(value);
            }
        }

        #endregion

        #region 任务状态

        Action<DownState, string?>? _StateChange;

        /// <summary>注册「任务状态变化」回调</summary>
        public HttpDown StateChange(Action<DownState, string?> action)
        {
            _StateChange = action;
            return this;
        }

        internal void SetState(DownState value, string? err = null)
        {
            lock (_eventSync)
            {
                if (_State == value) return;
                _State = value;
                _StateChange?.Invoke(value, err);
            }
        }

        #endregion

        #endregion

        #region 初始化

        /// <summary>目标地址（已解析）</summary>
        public Uri Uri;

        /// <summary>用于创建下载请求的 HTTP 核心实例</summary>
        public HttpCore core;

        /// <summary>
        /// 创建多线程下载器。
        /// </summary>
        /// <param name="core">已配置好请求头/代理/超时等选项的 HTTP 核心实例</param>
        /// <param name="savePath">文件保存目录</param>
        /// <param name="id">任务 ID，用于复用断点续传的临时目录</param>
        public HttpDown(HttpCore core, string savePath, string? id = null)
        {
            this.core = core ?? throw new ArgumentNullException(nameof(core));
            Uri = core.option.uri;
            ID = id;
            SavePath = savePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Url = core.option.uri.AbsoluteUri;
        }

        #endregion
    }

    /// <summary>
    /// 下载任务状态
    /// </summary>
    public enum DownState
    {
        /// <summary>准备中，尚未开始下载</summary>
        Ready,
        /// <summary>正在下载</summary>
        Downloading,
        /// <summary>已暂停（可恢复）</summary>
        Stop,
        /// <summary>下载完成</summary>
        Complete,
        /// <summary>下载失败</summary>
        Fail
    }
}