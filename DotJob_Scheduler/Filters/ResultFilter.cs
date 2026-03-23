using DotJob_Core.Systems;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Model;

namespace Job_Scheduler.Filters;

/// <summary>
/// 参数校验，响应组装，异常处理
/// </summary>
public class ResultFilter : ActionFilterAttribute, IExceptionFilter
{
    /// <summary>
    /// 响应处理：将所有接口响应统一包装成 ApiResponse 格式。
    /// ObjectResult（有返回值）→ Data 填充返回值；
    /// EmptyResult（void 接口）→ Data 为 null，同样返回 code:0 成功。
    /// </summary>
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        if (!context.ModelState.IsValid) return;

        ApiResponse api;

        if (context.Result is ObjectResult objectResult)
        {
            // 已经是 ApiResponse 则不重复包装
            if (objectResult.Value is ApiResponse) return;
            api = new ApiResponse { Code = 0, Message = "成功", Data = objectResult.Value };
        }
        else if (context.Result is EmptyResult)
        {
            // void 接口：返回统一成功响应
            api = new ApiResponse { Code = 0, Message = "成功" };
        }
        else
        {
            return;
        }

        context.Result = new OkObjectResult(api);
        base.OnResultExecuting(context);
    }

    /// <summary>
    /// 参数完整判断
    /// </summary>
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;
        var result = new ApiResponse();

        foreach (var error in context.ModelState.Values.SelectMany(item => item.Errors))
        {
            result.Success = false;
            result.Code = -1;
            result.Message = error.ErrorMessage;
            break;
        }

        context.Result = new JsonResult(result);
    }

    /// <summary>
    /// 异常处理：UnauthorizedAccessException 返回 401，其他异常返回 500
    /// </summary>
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is UnauthorizedAccessException)
        {
            context.Result = new JsonResult(new ApiResponse
            {
                Success = false,
                Code = 401,
                Message = context.Exception.Message
            })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
        }
        else
        {
            context.Result = new JsonResult(new ApiResponse
            {
                Success = false,
                Code = 500,
                Message = context.Exception.Message
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }

        context.ExceptionHandled = true;
    }
}