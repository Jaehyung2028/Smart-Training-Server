using Smart_Training_Server.Responses.Base;

namespace Smart_Training_Server.Responses.Room
{
    // 구독 이전에 생성된 메시지는 재입장 후 표시하지 않도록 기준 번호를 함께 전달합니다.
    public sealed class RoomChatSubscriptionResponse : BaseResponse
    {
        public long ChatSequence { get; set; }
    }
}
