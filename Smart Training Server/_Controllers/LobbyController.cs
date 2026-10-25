using Smart_Training_Server.Services.Room;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Responses.Base;
using Smart_Training_Server.Responses.Lobby;
using Smart_Training_Server.Services.Lobby;

// 로비 목록·콘텐츠·참가자 조회와 로비 입장·퇴장 HTTP API입니다. 사용자 ID는 인증 결과에서 얻습니다.
namespace Smart_Training_Server._Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class LobbyController : ControllerBase
    {
        private readonly LobbyService lobbyService;

        private readonly RoomService roomService;

        // 생성자입니다. 이 객체를 만드는 데 필요한 서비스나 설정을 전달받아 초기화합니다.
        public LobbyController(LobbyService lobbyService, RoomService roomService)
        {
            this.lobbyService = lobbyService;
            this.roomService = roomService;
        }

        [HttpPost("join")]
        // 인증 사용자 ID로 입장을 요청하고 HTTP 취소 신호도 서비스에 전달합니다. 소속 검사는 서비스가 처리합니다.
        public async Task<ActionResult<JoinLobbyResponse>> JoinLobby(LobbyMembershipRequest request)
        {
            JoinLobbyResponse response = await lobbyService.JoinLobby(GetAuthenticatedUserId(), request, HttpContext.RequestAborted);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpPost("leave")]
        // 토큰에서 얻은 사용자 ID와 요청 데이터를 분리해 로비 퇴장을 요청하고 결과를 HTTP 응답으로 전달합니다.
        public ActionResult<BaseResponse> LeaveLobby(LobbyMembershipRequest request)
        {
            BaseResponse response = lobbyService.LeaveLobby(GetAuthenticatedUserId(), request);
            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpGet]
        // 로비 요약 목록을 HTTP 200으로 반환합니다. 사용자·방 개수와 운영 설정을 포함합니다.
        public IActionResult GetAll()
        {
            return Ok(lobbyService.GetLobbySummaries());
        }

        [HttpGet("{lobbyId}")]
        // 로비 하나의 공개 요약을 반환하고 없으면 공통 404 오류로 응답합니다.
        public IActionResult GetLobby(string lobbyId)
        {
            var lobby = lobbyService.GetLobbySummary(lobbyId);
            return lobby == null ? NotFound(ApiErrorResponse.Create(404, HttpContext.TraceIdentifier, "로비를 찾을 수 없습니다.")) : Ok(lobby);
        }

        [HttpGet("{lobbyId}/contents")]
        // 로비의 공개 콘텐츠 트리를 반환하고 로비가 없으면 404로 응답합니다.
        public IActionResult GetContents(string lobbyId)
        {
            var contents = lobbyService.GetContentsByLobby(lobbyId);
            return contents == null ? NotFound(ApiErrorResponse.Create(404, HttpContext.TraceIdentifier, "로비를 찾을 수 없습니다.")) : Ok(contents);
        }

        [HttpGet("{lobbyId}/participants")]
        // 인증 사용자가 조회할 수 있는 로비 참가자 목록을 반환합니다. 권한 부족과 없는 로비를 구분합니다.
        public IActionResult GetParticipants(string lobbyId)
        {
            var result = roomService.GetLobbyParticipants(lobbyId, GetAuthenticatedUserId());
            if (result.StatusCode == 200)
            {
                return Ok(result.Participants);
            }

            var message = result.StatusCode == 404 ? "로비를 찾을 수 없습니다." : "현재 입장한 로비의 참가자만 조회할 수 있습니다.";
            return StatusCode(result.StatusCode, ApiErrorResponse.Create(result.StatusCode, HttpContext.TraceIdentifier, message));
        }

        // 클라이언트는 본인 ID를 보내지 않습니다. 인증 미들웨어가 토큰으로 확인해 기록한 사용자 ID를 얻습니다.
        private string GetAuthenticatedUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new InvalidOperationException("인증된 사용자 ID가 없습니다.");
        }
    }
}
