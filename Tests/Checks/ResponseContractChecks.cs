using Smart_Training_Server.Responses.Room;
using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Protocol;
using Smart_Training_Server.Hubs;
using Smart_Training_Server.Responses.Base;
using Smart_Training_Server.Responses.Health;
using Smart_Training_Server.Responses.Lobby;

internal static class ResponseContractChecks
{
    public static void Run()
    {
        void Check(bool value, string name)
        {
            if (!value)
            {
                throw new Exception(name);
            }

            Console.WriteLine("PASS: " + name);
        }

        // Unity가 읽는 실제 SignalR completion 메시지의 success·message 이름을 확인합니다.
        var buffer = new ArrayBufferWriter<byte>();
        var protocol = new JsonHubProtocol();
        protocol.WriteMessage(CompletionMessage.WithResult("1", new BaseResponse { Success = true }), buffer);
        using var completion = JsonDocument.Parse(Encoding.UTF8.GetString(buffer.WrittenSpan).TrimEnd((char)0x1e));
        var result = completion.RootElement.GetProperty("result");
        Check(result.GetProperty("success").GetBoolean() && result.GetProperty("message").GetString() == "", "typed Hub response preserves Unity success and message fields");

        var hubType = typeof(LobbyUpdatesHub);
        Check(hubType.GetMethod("Subscribe")!.ReturnType == typeof(Task<BaseResponse>) && hubType.GetMethod("SubscribeRoomChat")!.ReturnType == typeof(Task<RoomChatSubscriptionResponse>) && hubType.GetMethod("SendChat")!.ReturnType == typeof(Task<BaseResponse>) && hubType.GetMethod("SendRoomChat")!.ReturnType == typeof(Task<BaseResponse>), "all lobby Hub result methods expose typed responses");

        var chat = new ChatMessageResponse
        {
            Id = "message",
            LobbyId = "lobby",
            UserId = "user",
            Nickname = "nickname",
            Text = "hello",
            SentAt = DateTimeOffset.UtcNow
        };
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(chat)))
        {
            var data = json.RootElement;
            Check(data.EnumerateObject().Count() == 6 && !data.TryGetProperty("roomId", out _) && data.GetProperty("nickname").GetString() == "nickname" && data.GetProperty("text").GetString() == "hello", "typed lobby chat retains previous JSON fields without roomId");
        }

        chat.RoomId = "room";
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(chat)))
        {
            Check(json.RootElement.EnumerateObject().Count() == 7 && json.RootElement.GetProperty("roomId").GetString() == "room", "typed room chat retains roomId");
        }

        Check(JsonSerializer.Serialize(new HealthResponse(), new JsonSerializerOptions(JsonSerializerDefaults.Web)) == "{\"status\":\"ok\"}", "typed health response preserves status JSON");
    }
}
