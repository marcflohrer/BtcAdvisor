using Microsoft.AspNetCore.Mvc;

namespace BtcAdvisor.Controllers;

public interface IBitcoinController
{
    Task<IActionResult> GetBitcoinInfo(CancellationToken cancellationToken);
}
