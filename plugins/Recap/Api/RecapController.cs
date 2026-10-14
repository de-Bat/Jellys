using System.Net.Mime;
using Jellyfin.Plugin.Recap.Llm;
using Jellyfin.Plugin.Recap.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Recap.Api;

[ApiController]
[Authorize]
[Route("Recap")]
[Produces(MediaTypeNames.Application.Json)]
public class RecapController : ControllerBase
{
    private readonly ILibraryManager _library;
    private readonly IUserManager _users;
    private readonly RecapService _recap;

    public RecapController(ILibraryManager library, IUserManager users, RecapService recap)
    {
        _library = library;
        _users = users;
        _recap = recap;
    }

    /// <summary>Recap of an episode from its start up to <paramref name="positionTicks"/>.</summary>
    [HttpGet("Items/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<RecapResponse>> GetRecap(
        [FromRoute] Guid itemId,
        [FromQuery] long positionTicks,
        [FromQuery] string? language,
        CancellationToken cancellationToken)
    {
        var item = _library.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        var userClaim = User.FindFirst("Jellyfin-UserId")?.Value;
        if (Guid.TryParse(userClaim, out var userId))
        {
            var user = _users.GetUserById(userId);
            if (user is null || !item.IsVisible(user))
            {
                return NotFound();
            }
        }

        if (positionTicks <= 0)
        {
            return NotFound(new { message = "Nothing watched yet." });
        }

        try
        {
            var result = await _recap.GetRecapAsync(item, positionTicks, language, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                return NotFound(new { message = "No text subtitles available for this item." });
            }

            return new RecapResponse(result.Text, result.Language, result.Model, result.Cached);
        }
        catch (LlmException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "LLM request timed out." });
        }
    }
}

public record RecapResponse(string Text, string Language, string Model, bool Cached);
