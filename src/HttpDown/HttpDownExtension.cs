namespace HttpLib
{
    public static class HttpDownExtension
    {
        /// <summary>
        /// 下载（多线程）
        /// </summary>
        /// <param name="core">核心</param>
        /// <param name="savePath">保存路径</param>
        /// <param name="id">任务id</param>
        public static HttpDown downLoad(this HttpCore core, string savePath, string? id = null) => new HttpDown(core, savePath, id);
    }
}