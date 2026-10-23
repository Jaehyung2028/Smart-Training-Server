using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smart_Training_Server.Hubs;
using Smart_Training_Server.Requests.Room;
using Smart_Training_Server.Responses.Base;
using Smart_Training_Server.Responses.Room;
using Smart_Training_Server.Services.Room;

// 방 조회·생성·예약·퇴장과 방장 관리 HTTP API입니다. 실제 규칙은 RoomService에 위임합니다.
namespace Smart_Training_Server._Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RoomController : ControllerBase
    {
        private readonly RoomService roomService;

        // 생성자입니다. 이 객체를 만드는 데 필요한 서비스나 설정을 전달받아 초기화합니다.
        public RoomController(RoomService roomService)
        {
            this.roomService = roomService;
        }

        [HttpGet("/api/Lobby/{lobbyId}/rooms")]
        // 지정 로비의 방 목록을 서비스에서 조회합니다. 없는 로비는 404로 응답합니다.
        public ActionResult<IReadOnlyList<RoomSummaryResponse>> GetRooms(string lobbyId)
        {
            var rooms = roomService.GetRoomsByLobby(lobbyId);
            if (rooms == null)
            {
                return NotFound(ApiErrorResponse.Create(404, HttpContext.TraceIdentifier, "로비를 찾을 수 없습니다."));
            }

            return Ok(rooms);
        }

        [HttpGet("/api/Lobby/{lobbyId}/rooms/{roomId}")]
        // 지정 로비에 속한 단일 방을 조회합니다. 찾지 못하면 404로 응답합니다.
        public ActionResult<RoomSummaryResponse> GetRoom(string lobbyId, string roomId)
        {
            var room = roomService.GetRoomByLobby(lobbyId, roomId);
            if (room == null)
            {
                return NotFound(ApiErrorResponse.Create(404, HttpContext.TraceIdentifier, "해당 로비에서 방을 찾을 수 없습니다."));
            }

            return Ok(room);
        }

        [HttpPost("create")]
        // 요청 본문과 별도로 토큰에서 얻은 사용자 ID를 전달해 방 생성을 요청하고 성공 여부를 HTTP 결과로 전달합니다.
        public ActionResult<CreateRoomResponse> CreateRoom(CreateRoomRequest request)
        {
            CreateRoomResponse response = roomService.CreateRoom(GetAuthenticatedUserId(), request);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpPost("leave")]
        // 인증된 사용자에 대해 예약 취소 또는 방 퇴장을 서비스에 요청합니다.
        public ActionResult<BaseResponse> LeaveRoom(RoomMembershipRequest request)
        {
            BaseResponse response = roomService.LeaveRoom(GetAuthenticatedUserId(), request);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpPost("join")]
        // 인증 사용자에 대해 방 입장 예약을 요청합니다. HTTP 요청 취소도 전달하여 불필요한 예약이 남지 않도록 합니다.
        public async Task<ActionResult<JoinRoomResponse>> JoinRoom(JoinRoomRequest request)
        {
            JoinRoomResponse response = await roomService.JoinRoom(GetAuthenticatedUserId(), request, HttpContext.RequestAborted);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpPost("transfer-master")]
        // 인증된 요청자의 방장 위임 요청을 서비스로 전달합니다. 실제 권한과 대상 검사는 서비스가 합니다.
        public ActionResult<RoomMasterResponse> TransferRoomMaster(RoomParticipantRequest request)
        {
            var response = roomService.TransferRoomMaster(GetAuthenticatedUserId(), request);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpPost("settings")]
        // 인증된 요청자의 이름·정원 변경을 서비스에 전달하고 결과를 반환합니다.
        public ActionResult<RoomMasterResponse> UpdateRoomSettings(UpdateRoomSettingsRequest request)
        {
            var response = roomService.UpdateRoomSettings(GetAuthenticatedUserId(), request);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpPost("entry-lock")]
        // 인증된 요청자의 신규 입장 잠금 변경을 서비스에 전달합니다.
        public ActionResult<RoomMasterResponse> SetRoomEntryLock(SetRoomEntryLockRequest request)
        {
            var response = roomService.SetRoomEntryLock(GetAuthenticatedUserId(), request);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        // 인증 Claim에서 요청자 ID를 얻습니다. 요청 본문에는 본인 ID가 없으며 토큰으로 확인한 신원만 사용합니다.
        private string GetAuthenticatedUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new InvalidOperationException("인증된 사용자 ID가 없습니다.");
        }

        [HttpPost("password")]
        // 인증된 요청자의 방 비밀번호 변경을 서비스에 전달합니다.
        public ActionResult<BaseResponse> UpdateRoomPassword(UpdateRoomPasswordRequest request)
        {
            var response = roomService.UpdateRoomPassword(GetAuthenticatedUserId(), request);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpPost("close")]
        // 인증된 방장의 방 종료 요청을 전달합니다. 실제 프로세스 통지는 비동기로 처리합니다.
        public async Task<ActionResult<BaseResponse>> CloseRoom(
            RoomMembershipRequest request,
            [FromServices] Microsoft.AspNetCore.SignalR.IHubContext<RoomHub> roomHub
        )
        {
            var response = await roomService.CloseRoomAsync(GetAuthenticatedUserId(), request, roomHub);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpPost("kick")]
        // 방장 강퇴 요청을 서비스에 전달하고 실제 Unity 서버에 비동기 통지를 요청합니다.
        public async Task<ActionResult<BaseResponse>> KickRoomUser(
            RoomParticipantRequest request,
            [FromServices] Microsoft.AspNetCore.SignalR.IHubContext<RoomHub> roomHub
        )
        {
            var response = await roomService.KickRoomUserAsync(GetAuthenticatedUserId(), request, roomHub);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }
    }
}
