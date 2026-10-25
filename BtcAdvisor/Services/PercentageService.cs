public static class PercentageService
{
    public static double? GetPercentage(double? current, double? maximum)
    {
        if (current is null)
        {
            return null;
        }
        if(maximum is null || maximum == 0)
        {
            return null;
        }
        if (!current.HasValue || !maximum.HasValue)
        {
            return null;
        }
        return (current.Value / maximum.Value) * 100;
    }
}
