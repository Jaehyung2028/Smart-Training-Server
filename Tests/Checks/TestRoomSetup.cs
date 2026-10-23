using System.Reflection;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Room;
using Smart_Training_Server.Services.Room;

// 개별 방 기능 검사의 준비용입니다. 생성 API의 초기 예약 없이 방 등록·입력 검증 부분만 실행합니다.
// 생성과 예약을 함께 검증할 때는 이 fixture 대신 공개 CreateRoom을 직접 호출합니다.
internal static class TestRoomSetup
{
    public static CreateRoomResponse Create(RoomService rooms, string userId, CreateRoomRequest request)
    {
        return (CreateRoomResponse)typeof(RoomService)
            .GetMethod("CreateRoomCore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(rooms, new object[] { userId, request })!;
    }
}
