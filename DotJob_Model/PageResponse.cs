namespace DotJob_Model;

public class PageResponse<T>
{
    /// <summary>
    /// 数据
    /// </summary>
    public List<T> Data { get; set; }

    /// <summary>
    /// 分页信息
    /// </summary>
    public PageInfo PageInfo { get; set; }
}