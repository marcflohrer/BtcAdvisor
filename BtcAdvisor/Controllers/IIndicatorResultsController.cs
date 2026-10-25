using Microsoft.AspNetCore.Mvc;

namespace BtcAdvisor.Controllers;

public interface IIndicatorResultsController
{
    /// <summary>
    /// Gets the indicator results.
    /// </summary>
    /// <param name="startHistory" example="2024-09-13T15:00:00+02:00"></param>
    /// <param name="timeSpan" example="1h"></param>
    /// <param name="indicator" example="sma"></param>
    /// <param name="period" example="200"></param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The action result.</returns>
    Task<IActionResult> GetIndicatorResults ([FromQuery] DateTime startHistory, [FromQuery] string timeSpan, [FromQuery] string indicator, int? period, CancellationToken cancellationToken);
}
