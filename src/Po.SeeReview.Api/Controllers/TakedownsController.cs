using System.Collections.Generic;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using Po.SeeReview.Core.Interfaces;
using Po.SeeReview.Shared.Dtos;

namespace Po.SeeReview.Api.Controllers;

/// <summary>
/// Handles content takedown requests from restaurant owners and authorized representatives.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class TakedownsController : ControllerBase
{
    private readonly IComicRepository _comicRepository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILeaderboardRepository _leaderboardRepository;
    private readonly ILogger<TakedownsController> _logger;
    private readonly TelemetryClient _telemetryClient;

    public TakedownsController(
        IComicRepository comicRepository,
        IBlobStorageService blobStorageService,
        ILeaderboardRepository leaderboardRepository,
        ILogger<TakedownsController> logger,
        TelemetryClient telemetryClient)
    {
        _comicRepository = comicRepository ?? throw new ArgumentNullException(nameof(comicRepository));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _leaderboardRepository = leaderboardRepository ?? throw new ArgumentNullException(nameof(leaderboardRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
    }

    /// <summary>
    /// Submits a takedown request for a restaurant's comic.
    /// Removes cached comic assets immediately and records the request for follow-up.
    /// </summary>
    /// <response code="202">Takedown request accepted and queued for review.</response>
    /// <response code="400">Invalid request payload.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitAsync([FromBody] TakedownRequestDto request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        _logger.LogInformation("Received takedown request for {PlaceId} from {Requester}", request.PlaceId, request.RequesterName);

        _telemetryClient.TrackEvent("TakedownRequestReceived", new Dictionary<string, string>
        {
            ["PlaceId"] = request.PlaceId,
            ["Requester"] = request.RequesterName,
            ["Email"] = request.ContactEmail,
            ["Region"] = request.Region
        });

        var existingComic = await _comicRepository.GetByPlaceIdAsync(request.PlaceId);
        if (existingComic != null)
        {
            await _comicRepository.DeleteAsync(request.PlaceId);

            if (!string.IsNullOrWhiteSpace(existingComic.Id))
            {
                await _blobStorageService.DeleteComicImageAsync(existingComic.Id);
            }

            await _leaderboardRepository.DeleteAsync(request.PlaceId, request.Region);

            _logger.LogInformation("Removed cached comic and leaderboard entry for {PlaceId}", request.PlaceId);
        }
        else
        {
            _logger.LogInformation("No cached comic found for {PlaceId} during takedown", request.PlaceId);
        }

        var response = new
        {
            message = "Your takedown request was received. Our team will follow up via email within 2 business days.",
            requestId = Guid.NewGuid()
        };

        return Accepted(response);
    }
}
