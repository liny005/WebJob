using System.Text.Json;
using DotJob_Model.Entity;
using Host;
using Quartz;

namespace Job_Scheduler.Application.Jobs;


/// <summary>
/// http 执行
/// </summary>
[DisallowConcurrentExecution]
public class HttpJob : JobBase, IJob
{
    public HttpJob(LogEntity logInfo, IServiceProvider serviceProvider)
        : base(logInfo, serviceProvider)
    {
    }

    public override async Task NextExecute(IJobExecutionContext context)
    {
        // 从基类加载的 JOB_CONFIG 读取配置，不再读 JobDataMap
        var config = JobConfig;
        if (config == null)
        {
            LogInfo.ErrorMsg = $"未找到任务配置: {context.JobDetail.Key.Group}.{context.JobDetail.Key.Name}";
            LogInfo.ExecutionStatus = 2;
            return;
        }

        var requestUrl = config.RequestUrl.Trim();
        if (!string.IsNullOrEmpty(requestUrl) && !requestUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            requestUrl = "http://" + requestUrl;

        var requestParameters = config.RequestParameters ?? "";
        var headersString = config.Headers;
        var headers = !string.IsNullOrWhiteSpace(headersString)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(headersString.Trim()) ?? new Dictionary<string, string>()
            : new Dictionary<string, string>();
        var requestType = (RequestTypeEnum)config.RequestType;

        LogInfo.Url         = requestUrl;
        LogInfo.RequestType = requestType.ToString();
        LogInfo.Parameters  = requestParameters;
        LogInfo.JobGroup    = context.JobDetail.Key.Group;

        var response = new HttpResponseMessage();
        var http     = HttpHelper.Instance;

        switch (requestType)
        {
            case RequestTypeEnum.Get:
                response = await http.GetAsync(requestUrl, headers);
                break;
            case RequestTypeEnum.Post:
                response = await http.PostAsync(requestUrl, requestParameters, headers);
                break;
            case RequestTypeEnum.Put:
                response = await http.PutAsync(requestUrl, requestParameters, headers);
                break;
            case RequestTypeEnum.Delete:
                response = await http.DeleteAsync(requestUrl, headers);
                break;
        }

        var result = await response.Content.ReadAsStringAsync();
        LogInfo.Result     = result;
        LogInfo.StatusCode = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            LogInfo.ErrorMsg        = result;
            LogInfo.ExecutionStatus = 2;
        }
        else
        {
            LogInfo.ExecutionStatus = 1;
        }

        // 通知逻辑已统一在 JobBase.SendNotificationsAsync 中处理（钉钉 + 邮件）
    }
}