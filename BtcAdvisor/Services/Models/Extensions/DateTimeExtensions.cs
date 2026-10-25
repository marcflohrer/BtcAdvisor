public static class DateTimeToUnixConverter
{
    public static long ToUnixTime(this DateTime dateTime)
    {
        DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        TimeSpan elapsedTime = dateTime.ToUniversalTime() - epoch;
        return (long)elapsedTime.TotalSeconds;
    }
}
