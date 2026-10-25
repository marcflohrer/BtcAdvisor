using Microsoft.AspNetCore.Mvc;
using BtcAdvisor.Repository;

namespace BtcAdvisor.Controllers;

public class IndicatorResultsController(
    IBitcoinRepository bitcoinRepository) : ControllerBase, IIndicatorResultsController
{
    [HttpGet ("indicatorresults")]
    public async Task<IActionResult> GetIndicatorResults([FromQuery] DateTime startHistory, [FromQuery] string timeSpan, string indicator, int? period, CancellationToken cancellationToken)
    {
        return Ok(await bitcoinRepository.GetIndicatorStatistics (startHistory, timeSpan, indicator, period, cancellationToken));
    }
}
