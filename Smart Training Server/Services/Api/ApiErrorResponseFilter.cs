using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Services.Api;

public sealed class ApiErrorResponseFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not ObjectResult result || result.StatusCode is not >= 400) return;
        var status = result.StatusCode.Value;
        var error = result.Value as ApiErrorResponse;
        if (error == null)
        {
            bool validation = status == 400 && !context.ModelState.IsValid;
            error = ApiErrorResponse.Create(status, context.HttpContext.TraceIdentifier,
                validation ? "요청 데이터의 형식과 필수 항목을 확인해주세요." : (result.Value as BaseResponse)?.Message,
                validation ? "validation_failed" : null);
        }
        context.Result = new ObjectResult(error) { StatusCode = status };
    }
    public void OnResultExecuted(ResultExecutedContext context) { }
}
