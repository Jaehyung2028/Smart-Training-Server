// 공통 API 오류 응답 객체입니다. 클라이언트가 사용할 결과 형식을 정의합니다. BaseResponse를 상속하는 응답은 공통 성공 여부와 메시지도 포함합니다.
namespace Smart_Training_Server.Responses.Base
{
    public sealed class ApiErrorResponse : BaseResponse
    {
        // 오류 종류를 코드로 구분할 때 사용합니다.
        public string ErrorCode { get; init; } = string.Empty;

        // 동일 요청의 서버 로그를 찾는 추적 ID입니다.
        public string TraceId { get; init; } = string.Empty;

        public static ApiErrorResponse Create(int status, string traceId, string? message = null, string? code = null)
        {
            return new()
            {
                Success = false,
                ErrorCode = code ?? status switch
                {
                    400 => "bad_request",
                    401 => "unauthorized",
                    403 => "forbidden",
                    404 => "not_found",
                    405 => "method_not_allowed",
                    415 => "unsupported_media_type",
                    _ => status >= 500 ? "internal_error" : "request_failed"
                },
                TraceId = traceId,
                Message = message ?? status switch
                {
                    400 => "요청 내용을 확인해주세요.",
                    401 => "로그인이 필요합니다. 유효한 Bearer 토큰을 보내주세요.",
                    403 => "요청 권한이 없습니다.",
                    404 => "요청한 대상을 찾을 수 없습니다.",
                    405 => "지원하지 않는 요청 방식입니다.",
                    415 => "JSON 형식으로 요청해주세요.",
                    _ => status >= 500 ? "서버 오류가 발생했습니다. 잠시 후 다시 시도해주세요." : "요청을 처리하지 못했습니다."
                }
            };
        }
    }
}
