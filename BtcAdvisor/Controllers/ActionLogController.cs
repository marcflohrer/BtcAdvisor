using Microsoft.AspNetCore.Mvc;
using BtcAdvisor.Repository;
using BtcAdvisor.Services;

namespace BtcAdvisor.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ActionLogController(
    ISellActionLogRepository sellActionLogRepository,
    IBuyActionLogRepository buyActionLogRepository,
    IActionLogService actionLogService) : ControllerBase
{
    [HttpGet ("sellactions")]
    public async Task<IActionResult> GetSellActionLogs ([FromQuery] DateTime startHistory, [FromQuery] DateTime endHistory, [FromQuery] bool? isLongTimeframe, [FromQuery] bool acceptableOnly = false, [FromQuery] bool validatedOnly = true, CancellationToken cancellationToken = default)
    {
        var result = await sellActionLogRepository.GetSellActionLogs(startHistory, endHistory, isLongTimeframe, cancellationToken, acceptableOnly, validatedOnly);
        return Ok (result);
    }

    [HttpGet ("buyactions")]
    public async Task<IActionResult> GetBuyActionLogs ([FromQuery] DateTime startHistory, [FromQuery] DateTime endHistory, [FromQuery] bool? isLongTimeframe, [FromQuery] bool acceptableOnly = false, [FromQuery] bool validatedOnly = true, CancellationToken cancellationToken = default)
    {
        var result = await buyActionLogRepository.GetBuyActionLogs(startHistory, endHistory, isLongTimeframe, cancellationToken, acceptableOnly, validatedOnly);
        return Ok (result);
    }       

    [HttpGet ("urgentactions")]
    public async Task<IActionResult> GetFirePanicActionLogs (CancellationToken cancellationToken)
    {
        var result = await actionLogService.GetActionLogs(DateTime.UtcNow.ToUniversalTime(), cancellationToken);
        return Ok (result);
    }

    [HttpGet ("firesellactions")]
    public async Task<IActionResult> GetFireSellActionLogs ([FromQuery] DateTime startHistory, [FromQuery] DateTime endHistory, [FromQuery] bool? isLongTimeframe, CancellationToken cancellationToken)
    {
        var result = await sellActionLogRepository.GetFireSellActionLogs(startHistory, endHistory, isLongTimeframe, cancellationToken);
        return Ok (result);
    }

    [HttpGet ("relabelfiresellactions")]
    public async Task<IActionResult> RelabelFireSellActionLogs ([FromQuery] DateTime startHistory, [FromQuery] DateTime endHistory, CancellationToken cancellationToken)
    {
        var result = await sellActionLogRepository.RelabelFireSellToSellActionLogs(startHistory, endHistory, cancellationToken);
        return Ok (result);
    }
}