namespace Smart_Training_Server.Enum
{
    public enum ServerState
    {
        Creating,     // 생성 중
        Starting,     // 프로세스 실행 중
        Ready,        // 접속 가능
        Running,      // 사용 중
        Stopping,     // 종료 중
        Stopped,      // 종료됨
        Failed        // 실행 실패
    }
}
