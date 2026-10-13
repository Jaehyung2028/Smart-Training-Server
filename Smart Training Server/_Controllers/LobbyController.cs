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

        public LobbyController(LobbyService lobbyService)
        {
            this.lobbyService = lobbyService;
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
            JoinLobbyResponse response = await lobbyService.JoinLobby(request);

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

        private string GetAuthenticatedUserId() =>
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("인증된 사용자 ID가 없습니다.");
    }
}
