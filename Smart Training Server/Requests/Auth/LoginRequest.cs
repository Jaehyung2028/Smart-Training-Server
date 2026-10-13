namespace Smart_Training_Server.Requests.Auth
{
    public class LoginRequest
    {
        /// <summary>
        /// 사용자가 입력한 닉네임
        /// </summary>
        public string UserName { get; set; } = string.Empty;
    }
}
