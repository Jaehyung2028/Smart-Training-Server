// 공통 성공·메시지 응답 객체입니다.
namespace Smart_Training_Server.Responses.Base
{
    public class BaseResponse
    {
        /// <summary>
        /// 성공 여부
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 결과 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}
