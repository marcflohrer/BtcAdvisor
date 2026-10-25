namespace BtcAdvisor.Services;

public class TelegramBotOptions
{
    public string TelegramBotSecret { get; set; } = string.Empty;
    public IReadOnlyCollection<long> ChatIds { get; set; } = new List<long>();
}
