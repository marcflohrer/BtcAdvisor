using BtcAdvisor.Controllers;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public interface IActionLogService
{
    /// <summary>
    /// Deletes the firesell and panic buy action logs within the specified date range.
    /// </summary>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>Task representing the asynchronous operation.</returns>
    Task DeleteActionLogs(DateTime start, DateTime end, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the firesell and panic buy action logs within the specified date range converts them to action logs and sorts them by trading date.
    /// </summary>
    /// <param name="currentDateTime"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<IReadOnlyCollection<ActionLog>> GetActionLogs(DateTime currentDateTime, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the action log if it is not a duplicate.
    /// </summary>
    /// <param name="action"></param>
    /// <returns>Task representing the asynchronous operation.</returns>
    Task SaveAsync(ActionLog action);
}
