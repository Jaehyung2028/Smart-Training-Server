// 공통 성공·메시지 응답 객체입니다. 클라이언트가 사용할 결과 형식을 정의합니다. BaseResponse를 상속하는 응답은 공통 성공 여부와 메시지도 포함합니다.
namespace Smart_Training_Server.Responses.Base
{
    public class BaseResponse
    {
        /// <summary>
        /// 성공 여부
        /// </summary>
        public bool Success
        {
            get; set;
        }

        /// <summary>
        /// 결과 메시지
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}
