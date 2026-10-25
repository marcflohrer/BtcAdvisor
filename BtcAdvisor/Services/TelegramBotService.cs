using Telegram.Bot;
using Microsoft.Extensions.Options;

namespace BtcAdvisor.Services;

public class TelegramBotService : ITelegramBotService
{
    private readonly TelegramBotClient? _botClient;
    private readonly IReadOnlyCollection<long> _chatIds;
    private readonly bool _isEnabled;

    public TelegramBotService(IOptions<TelegramBotOptions> options)
    {
        _chatIds = options.Value.ChatIds;

        var token = options.Value.TelegramBotSecret;
        if (string.IsNullOrWhiteSpace(token))
        {
            _isEnabled = false;
            Console.WriteLine("WARN: Telegram bot token is empty. Telegram notifications are disabled.");
            return;
        }

        try
        {
            _botClient = new TelegramBotClient(token);
            _isEnabled = true;
        }
        catch (Exception ex)
        {
            _isEnabled = false;
            Console.WriteLine($"WARN: Telegram bot initialization failed ({ex.Message}). Telegram notifications are disabled.");
        }
    }

    public async Task SendMessageAsync(string message)
    {
        if (!_isEnabled || _botClient is null || _chatIds.Count == 0)
        {
            return;
        }

        foreach (var chatId in _chatIds)
        {
            try
            {
                await _botClient.SendMessage(chatId, message);
            }
            catch(Exception ex)
            {
                Console.WriteLine($"ERROR: Sending telegram message failed for chatId {chatId}. {ex.Message}");
            }
        }
    }
}
