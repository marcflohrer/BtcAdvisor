using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Repository;

public interface IActionLogRepository
{
    Task DeleteActionLogs(DateTime start, DateTime end, CancellationToken cancellationToken);
    Task SaveActionLog(ActionLog actionLog);

    Task<IReadOnlyCollection<ActionLog>> GetActionLogs(DateTime currentDateTime, CancellationToken cancellationToken);

    Task<ActionLog?> GetLastActionLog(string actionName, CancellationToken cancellationToken);
}