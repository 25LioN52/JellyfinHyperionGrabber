using System;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.HyperionGrabber.Api;

/// <summary>
/// Administrator actions used by the configuration page.
/// </summary>
[ApiController]
[Route("HyperionGrabber")]
[Authorize(Policy = Policies.RequiresElevation)]
[Produces(MediaTypeNames.Application.Json)]
public class HyperionGrabberController : ControllerBase
{
    private readonly HyperionConnectionTester _tester;
    private readonly ISessionManager _sessionManager;
    private readonly IPlaybackStatusSource _status;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="HyperionGrabberController"/> class.
    /// </summary>
    /// <param name="tester">Runs connection tests and test patterns.</param>
    /// <param name="sessionManager">Jellyfin's sessions, for the device and user lists.</param>
    /// <param name="status">What the lights are doing.</param>
    /// <param name="libraryManager">Jellyfin's library, for the name of the playing item.</param>
    public HyperionGrabberController(
        HyperionConnectionTester tester,
        ISessionManager sessionManager,
        IPlaybackStatusSource status,
        ILibraryManager libraryManager)
    {
        _tester = tester;
        _sessionManager = sessionManager;
        _status = status;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Describes what the lights are doing, for each Hyperion server: the followed playback, the connection to
    /// Hyperion, the frame rate and the last problem. The configuration page polls it while it is open.
    /// </summary>
    /// <returns>The status.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<StatusResponse> GetStatus()
    {
        return Ok(StatusResponse.From(_status.GetStatus(), itemId => _libraryManager.GetItemById(itemId)?.Name));
    }

    /// <summary>
    /// Lists the devices and users of Jellyfin's recent sessions, to choose which playback drives the lights.
    /// </summary>
    /// <returns>Devices (most recently active first) and users.</returns>
    [HttpGet("Clients")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PlaybackClientsResponse> GetClients() => Ok(PlaybackClientsResponse.From(_sessionManager.Sessions));

    /// <summary>
    /// Connects to Hyperion, registers the priority and disconnects.
    /// </summary>
    /// <param name="request">Server to test.</param>
    /// <param name="cancellationToken">Aborted when the client disconnects.</param>
    /// <returns>The outcome; failures to connect are reported with <c>Success = false</c>.</returns>
    [HttpPost("TestConnection")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HyperionTestResponse>> TestConnection(
        [FromBody] HyperionTargetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = request.ToClientOptions();
        var errors = options.GetValidationErrors();
        if (errors.Count > 0)
        {
            return BadRequest(HyperionTestResponse.Invalid(string.Join(" ", errors)));
        }

        var result = await _tester.TestConnectionAsync(options, cancellationToken).ConfigureAwait(false);
        return Ok(HyperionTestResponse.From(result));
    }

    /// <summary>
    /// Plays the LED layout test pattern on Hyperion, then clears it.
    /// </summary>
    /// <param name="request">Server to use and optional duration.</param>
    /// <param name="cancellationToken">Aborted when the client disconnects; the pattern stops and is cleared.</param>
    /// <returns>The outcome; failures to connect are reported with <c>Success = false</c>.</returns>
    [HttpPost("TestPattern")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HyperionTestResponse>> TestPattern(
        [FromBody] HyperionTargetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = request.ToClientOptions();
        var patternOptions = new TestPatternOptions();
        if (request.DurationSeconds is { } seconds)
        {
            patternOptions = patternOptions with { Duration = TimeSpan.FromSeconds(seconds) };
        }

        var errors = options.GetValidationErrors().Concat(patternOptions.GetValidationErrors()).ToList();
        if (errors.Count > 0)
        {
            return BadRequest(HyperionTestResponse.Invalid(string.Join(" ", errors)));
        }

        var result = await _tester.SendTestPatternAsync(options, patternOptions, cancellationToken).ConfigureAwait(false);
        return Ok(HyperionTestResponse.From(result));
    }
}
