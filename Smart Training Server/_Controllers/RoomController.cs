using Microsoft.AspNetCore.Mvc;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Room;
using Smart_Training_Server.Services.Room;

namespace Smart_Training_Server._Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class RoomController : ControllerBase
    {
        private readonly RoomService roomService;

        public RoomController(RoomService roomService)
        {
            this.roomService = roomService;
        }

        [HttpGet("/api/Lobby/{lobbyId}/rooms")]
        public ActionResult<IReadOnlyList<RoomSummaryResponse>> GetRooms(string lobbyId)
        {
            var rooms = roomService.GetRoomsByLobby(lobbyId);
            if (rooms == null)
                return NotFound(Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(404, HttpContext.TraceIdentifier, "로비를 찾을 수 없습니다."));
            return Ok(rooms);
        }

        [HttpGet("/api/Lobby/{lobbyId}/rooms/{roomId}")]
        public ActionResult<RoomSummaryResponse> GetRoom(string lobbyId, string roomId)
        {
            var room = roomService.GetRoomByLobby(lobbyId, roomId);
            if (room == null)
                return NotFound(Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(404, HttpContext.TraceIdentifier, "해당 로비에서 방을 찾을 수 없습니다."));
            return Ok(room);
        }

        [HttpPost("creat")]
        [HttpPost("create")]
        public ActionResult<CreateRoomResponse> CreateRoom(
            CreateRoomRequest request)
        {
            request.UserId = GetAuthenticatedUserId();
            CreateRoomResponse response =
                roomService.CreateRoom(request);

            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpPost("leave")]
        public ActionResult<LeaveRoomResponse> LeaveRoom(LeaveRoomRequest request)
        {
            request.UserId = GetAuthenticatedUserId();
            LeaveRoomResponse response = roomService.LeaveRoom(request);

            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpPost("join")]
        public async Task<ActionResult<JoinRoomResponse>> JoinRoom(
    JoinRoomRequest request)
        {
            request.UserId = GetAuthenticatedUserId();
            JoinRoomResponse response =
                await roomService.JoinRoom(request, HttpContext.RequestAborted);

            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpPost("transfer-master")]
        public ActionResult<RoomMasterResponse> TransferRoomMaster(TransferRoomMasterRequest request)
        {
            var response = roomService.TransferRoomMaster(GetAuthenticatedUserId(), request);
            if (!response.Success) return BadRequest(response);
            return Ok(response);
        }

        [HttpPost("settings")]
        public ActionResult<RoomMasterResponse> UpdateRoomSettings(UpdateRoomSettingsRequest request)
        {
            var response = roomService.UpdateRoomSettings(GetAuthenticatedUserId(), request);
            if (!response.Success) return BadRequest(response);
            return Ok(response);
        }

        [HttpPost("entry-lock")]
        public ActionResult<RoomMasterResponse> SetRoomEntryLock(SetRoomEntryLockRequest request)
        {
            var response = roomService.SetRoomEntryLock(GetAuthenticatedUserId(), request);
            if (!response.Success) return BadRequest(response);
            return Ok(response);
        }

        private string GetAuthenticatedUserId() =>
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("인증된 사용자 ID가 없습니다.");

        [HttpPost("password")]
        public ActionResult<Smart_Training_Server.Responses.Base.BaseResponse> UpdateRoomPassword(UpdateRoomPasswordRequest request)
        {
            var response = roomService.UpdateRoomPassword(GetAuthenticatedUserId(), request);
            if (!response.Success) return BadRequest(response);
            return Ok(response);
        }

        [HttpPost("close")]
        public async Task<ActionResult<Smart_Training_Server.Responses.Base.BaseResponse>> CloseRoom(
            CloseRoomRequest request,
            [FromServices] Microsoft.AspNetCore.SignalR.IHubContext<Smart_Training_Server.Hubs.RoomHub> roomHub)
        {
            var response = await roomService.CloseRoomAsync(GetAuthenticatedUserId(), request, roomHub);
            if (!response.Success) return BadRequest(response);
            return Ok(response);
        }

        [HttpPost("kick")]
        public async Task<ActionResult<Smart_Training_Server.Responses.Base.BaseResponse>> KickRoomUser(
            KickRoomUserRequest request,
            [FromServices] Microsoft.AspNetCore.SignalR.IHubContext<Smart_Training_Server.Hubs.RoomHub> roomHub)
        {
            var response = await roomService.KickRoomUserAsync(GetAuthenticatedUserId(), request, roomHub);
            if (!response.Success) return BadRequest(response);
            return Ok(response);
        }

    }
}
