using Microsoft.AspNetCore.Mvc;
using Smart_Training_Server.Models;
using Smart_Training_Server.Requests.Lobby;
using Smart_Training_Server.Responses.Lobby;
using Smart_Training_Server.Services.Lobby;

namespace Smart_Training_Server._Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class LobbyController : ControllerBase
    {
        private readonly LobbyService lobbyService;
        private readonly Smart_Training_Server.Services.Room.RoomService roomService;

        public LobbyController(LobbyService lobbyService, Smart_Training_Server.Services.Room.RoomService roomService)
        {
            this.lobbyService = lobbyService;
            this.roomService = roomService;
        }

        [HttpPost("creat")]
        [HttpPost("create")]
        public ActionResult<CreateLobbyResponse> CreateLobby(CreateLobbyRequest request)
        {
            CreateLobbyResponse response = lobbyService.CreateLobby(request);

            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpPost("join")]
        public async Task<ActionResult<JoinLobbyResponse>> JoinLobby(JoinLobbyRequest request)
        {
            request.UserId = GetAuthenticatedUserId();
            JoinLobbyResponse response = await lobbyService.JoinLobby(request, HttpContext.RequestAborted);

            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpPost("leave")]
        public ActionResult<LeaveLobbyResponse> LeaveLobby(LeaveLobbyRequest request)
        {
            request.UserId = GetAuthenticatedUserId();
            LeaveLobbyResponse response = lobbyService.LeaveLobby(request);

            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            return Ok(lobbyService.GetLobbySummaries());
        }

        [HttpGet("{lobbyId}")]
        public IActionResult GetLobby(string lobbyId)
        {
            var lobby = lobbyService.GetLobbySummary(lobbyId);
            return lobby == null
                ? NotFound(Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(404, HttpContext.TraceIdentifier, "로비를 찾을 수 없습니다."))
                : Ok(lobby);
        }

        [HttpGet("{lobbyId}/contents")]
        public IActionResult GetContents(string lobbyId)
        {
            var contents = lobbyService.GetContentsByLobby(lobbyId);
            return contents == null
                ? NotFound(Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(404, HttpContext.TraceIdentifier, "로비를 찾을 수 없습니다."))
                : Ok(contents);
        }

        [HttpGet("{lobbyId}/participants")]
        public IActionResult GetParticipants(string lobbyId)
        {
            var result = roomService.GetLobbyParticipants(lobbyId, GetAuthenticatedUserId());
            if (result.StatusCode == 200) return Ok(result.Participants);
            var message = result.StatusCode == 404 ? "로비를 찾을 수 없습니다." : "현재 입장한 로비의 참가자만 조회할 수 있습니다.";
            return StatusCode(result.StatusCode, Smart_Training_Server.Responses.Base.ApiErrorResponse.Create(result.StatusCode, HttpContext.TraceIdentifier, message));
        }

        private string GetAuthenticatedUserId() =>
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("인증된 사용자 ID가 없습니다.");
    }
}
