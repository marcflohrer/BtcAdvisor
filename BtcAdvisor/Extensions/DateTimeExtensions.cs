namespace BtcAdvisor.Extensions;

public static class DateTimeExtensions
{
    public static DateTime ToUtcDateTime(this DateTime dateTime)
    {
         var utcTradingDate = dateTime.Kind == DateTimeKind.Unspecified
                                ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc) // annehmen, dass es UTC ist, wenn nicht spezifiziert
                                : dateTime.ToUniversalTime(); // Konvertiere zu UTC, wenn lokal
        // Konvertiere DateTime zu UTC, wenn es Local ist
        if (utcTradingDate.Kind == DateTimeKind.Local)
        {
            utcTradingDate = utcTradingDate.ToUniversalTime();
        }
        return utcTradingDate;
    }

    public static DateTime ToCetDateTime(this DateTime utcDateTime)
    {
        TimeZoneInfo cetZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        // Das DateTime-Objekt von UTC in CET konvertieren
        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, cetZone);
    }

    public static string GetBerlinDateTime(this DateTime dateTime)
    {
        // Definiere die Zeitzone für Berlin
        TimeZoneInfo berlinZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        // Hole die aktuelle Uhrzeit in der Berliner Zeitzone
        var berlinTime = TimeZoneInfo.ConvertTime(dateTime, berlinZone);
        var berlinTimeString = berlinTime.ToString("yyyy-MM-dd HH:mm");
        return berlinTimeString;
    }
}