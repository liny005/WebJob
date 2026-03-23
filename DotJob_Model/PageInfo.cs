namespace DotJob_Model;

public class PageInfo
{
    /// <summary>
    /// 数量总数
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// 页面大小
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// 当前页码
    /// </summary>
    public int PageNumber { get; set; } = 1;
}