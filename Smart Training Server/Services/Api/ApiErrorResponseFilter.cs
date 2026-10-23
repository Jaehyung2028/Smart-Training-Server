using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Smart_Training_Server.Responses.Base;

// Controller가 반환한 실패 결과를 공통 오류 JSON 형식으로 맞추는 MVC 결과 필터입니다.
namespace Smart_Training_Server.Services.Api
{
    public sealed class ApiErrorResponseFilter : IAlwaysRunResultFilter
    {
        // Controller 결과가 클라이언트로 나가기 직전에 실패 응답을 공통 형식으로 변환합니다. 모델 입력 오류도 여기서 맞춥니다.
        public void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.Result is not ObjectResult result || result.StatusCode is not >= 400)
            {
                return;
            }

            var status = result.StatusCode.Value;
            var error = result.Value as ApiErrorResponse;
            if (error == null)
            {
                bool validation = status == 400 && !context.ModelState.IsValid;
                error = ApiErrorResponse.Create(
                    status,
                    context.HttpContext.TraceIdentifier,
                    validation ? "요청 데이터의 형식과 필수 항목을 확인해주세요." : (result.Value as BaseResponse)?.Message,
                    validation ? "validation_failed" : null
                );
            }

            context.Result = new ObjectResult(error)
            {
                StatusCode = status
            };
        }

        // 결과 전송 이후의 후처리 지점입니다. 현재는 추가 작업이 없어 비워 둡니다.
        public void OnResultExecuted(ResultExecutedContext context)
        {
        }
    }
}
