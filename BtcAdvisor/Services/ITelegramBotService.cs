namespace BtcAdvisor.Services;

public interface ITelegramBotService
{
    public Task SendMessageAsync(string message);
}