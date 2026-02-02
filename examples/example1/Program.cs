// See https://aka.ms/new-console-template for more information

using HttpLib;

Config.fail += Config_fail;//全局异常

List<Val> headerss = new List<Val> {
    new Val("Accept","text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9"),
    new Val("User-Agent","Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.183 Safari/537.36 Edg/86.0.622.63"),
};

var ip = Http.Get("https://www.baidu.com/s").IP;
Console.WriteLine("IP:baidu.com " + ip);
var a = Http.Get("https://www.baidu.com/s").header(headerss)
    .data(new { wd = "GitHub - Haku-Men HttpLib" })
    .data(new Val("ie", "utf-8"))
       //.header(new { userAgent = "测试1", accept = "*/*" })
       .redirect(true)
       .before((response, result) =>
       {
           return true; //继续请求
       })
       .fail(result =>
       {
           if (result.Exception == null) return;
           Console.Write(result.Exception.GetType());
           Console.WriteLine(result.Exception.Message);
       }).request(out _);
Console.SetCursorPosition(0, 5);
Console.WriteLine(a);
Console.ReadLine();

return;

List<Val> headers = new List<Val> {
                new Val("accept","text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9"),
                new Val("user-agent","Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.183 Safari/537.36 Edg/86.0.622.63"),
            };
var a2 = Http.Post("http://localhost:61489/api/values").header(headers)
    .data(new { wd = new string[] { "GitHub - Haku-Men HttpLib", "321231" } })
    .data(new List<Files> { new Files(@"C:\Users\ttgx\Desktop\Flowing.exe") })
    .data(new List<Files> { new Files(@"C:\Users\ttgx\Desktop\ABT44B9D51AC6E96DA9220E39EED30D1945666375E8401AD7DE974193FD48211E72.jfif") })
    .data(new Val("abc", "123"))
    .data(new Val("abc1", "1234"))
    .uploadProgress(e =>
    {
        int top = 0;
        Console.SetCursorPosition(0, top);
        Console.Write("{0}% 上传 {1}/{2}            ", Math.Round(e.Prog * 100.0, 1).ToString("N1"), CountSize(e.Value), CountSize(e.MaxValue));

    })
    .downloadProgress(e =>
    {
        int top = 2;
        Console.SetCursorPosition(0, top);
        if (e.GetProg(out var prog))
        {
            Console.Write("{0}% 下载 {1}/{2}                  ", Math.Round(prog * 100.0, 1).ToString("N1"), CountSize(e.Value), CountSize(e.MaxValue));
        }
        else
        {
            Console.Write("{0} 下载            ", CountSize(e.Value));
        }
    }).fail(result =>
    {
    }).request(out _);
Console.SetCursorPosition(0, 5);
Console.WriteLine(a2);
Console.ReadLine();

static void Config_fail(HttpCore core, ResultResponse result)
{
    if (result.Exception == null) return;
    Console.Write(result.Exception.GetType());
    Console.Write(result.Exception.Message);
}
static string CountSize(double Size)
{
    string houzui = "B";
    double FactSize = Size;
    if (FactSize >= 1024)
    {
        houzui = "K";
        FactSize = (FactSize / 1024.00);
    }
    if (FactSize >= 1024)
    {
        houzui = "M";
        FactSize = (FactSize / 1024.00);
    }
    if (FactSize >= 1024)
    {
        houzui = "G";
        FactSize = (FactSize / 1024.00);
    }
    if (FactSize >= 1024)
    {
        houzui = "T";
        FactSize = (FactSize / 1024.00);
    }
    return string.Format("{0} {1}", Math.Round(FactSize, 2), houzui);
}