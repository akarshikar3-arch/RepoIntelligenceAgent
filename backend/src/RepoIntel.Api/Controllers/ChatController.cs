using Microsoft.AspNetCore.Mvc;
using RepoIntel.Application.Abstractions;
using RepoIntel.Contracts;

namespace RepoIntel.Api.Controllers;

[ApiController]
[Route("api/sessions/{sessionId}/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly IChatOrchestrator _orchestrator;
    private readonly IChatHistoryStore _history;

    public ChatController(IChatOrchestrator orchestrator, IChatHistoryStore history)
    {
        _orchestrator = orchestrator;
        _history = history;
    }

    /// <summary>Ask a question grounded in the indexed repo/snippet for this session.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChatResponse>> Ask(
        string sessionId,
        [FromBody] ChatRequest request,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new ProblemDetails { Title = "Question is required.", Status = 400 });

        try
        {
            var response = await _orchestrator.AskAsync(sessionId, request, ct);
            return Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ProblemDetails { Title = "Session not found.", Status = 404 });
        }
    }

    [HttpGet("history")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatTurnDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChatTurnDto>>> GetHistory(string sessionId, CancellationToken ct)
        => Ok(await _history.GetAsync(sessionId, ct));

    [HttpDelete("history")]
    public async Task<IActionResult> ClearHistory(string sessionId, CancellationToken ct)
    {
        await _history.ClearAsync(sessionId, ct);
        return NoContent();
    }
}
