using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public class ActionLogService(IActionLogRepository actionLogRepository) : IActionLogService
{
    public async Task DeleteActionLogs(DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        await actionLogRepository.DeleteActionLogs(start, end, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ActionLog>> GetActionLogs(DateTime currentDateTime, CancellationToken cancellationToken)
    {
        return await actionLogRepository.GetActionLogs(currentDateTime, cancellationToken);
    }

    public async Task SaveAsync(ActionLog action)
    {
        // validate action log
        if(action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }
        if(action.Type == null)
        {
            throw new ArgumentNullException(nameof(action.Type));
        }
        // check if this action log is the same as the last stored one in the database
        var lastAction = await actionLogRepository.GetLastActionLog(action!.Type!, CancellationToken.None);
        if(lastAction != null
            && lastAction.TradeAdvice == action.TradeAdvice
            && lastAction.IsLongTimeframe == action.IsLongTimeframe)
        {
            return;
        }

        // save to database
        await actionLogRepository.SaveActionLog(action);
    }
}