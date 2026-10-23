// appsettings의 Operations 설정입니다. 운영 페이지 자동 열기와 운영 이력 파일 저장 여부를 결정합니다.
namespace Smart_Training_Server.Options
{
    public sealed class OperationsOptions
    {
        // API 시작 후 운영 페이지를 브라우저로 자동으로 열지 결정합니다.
        public bool OpenBrowserOnStart { get; set; } = true;

        // 운영 기록과 운영 모드의 파일 저장을 사용할지 결정합니다.
        public bool PersistHistory { get; set; } = true;

        // 운영 이력 JSON 저장 경로입니다.
        public string HistoryPath { get; set; } = "Data/operations-history.json";
    }
}
