using Tools.DateTimeExtend;

namespace Model;

public class ApiResponse
{
    public ApiResponse()
    {
        Timestamp = DateTimeHelper.GetTotalMilliseconds(DateTime.Now);
    }

    public bool Success { get; set; } = true;

    public int Code { get; set; }

    public string Message { get; set; }

    public long Timestamp { get; set; }

    public object? Data { get; set; }
}