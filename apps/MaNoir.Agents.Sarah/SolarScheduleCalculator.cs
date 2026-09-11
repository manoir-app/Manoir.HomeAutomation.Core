using MaNoir.Core.Contracts.Models.Locations;
using System;

namespace MaNoir.Agents.Sarah;

internal static class SolarScheduleCalculator
{
    public static DateTimeOffset CalculateSunrise(GeoCoordinates coordinates, DateTime date, TimeZoneInfo timeZone)
    {
        return Calculate(coordinates, date, timeZone, sunrise: true);
    }

    public static DateTimeOffset CalculateSunset(GeoCoordinates coordinates, DateTime date, TimeZoneInfo timeZone)
    {
        return Calculate(coordinates, date, timeZone, sunrise: false);
    }

    private static DateTimeOffset Calculate(GeoCoordinates coordinates, DateTime date, TimeZoneInfo timeZone, bool sunrise)
    {
        double longitude = (double)coordinates.Longitude;
        double latitudeRadians = (double)coordinates.Latitude * Math.PI / 180D;
        int dayOfYear = date.DayOfYear;
        double declination = Math.Asin(-0.39795D * Math.Cos(2D * Math.PI * (dayOfYear + 10D) / 365D));
        double sinePosition = Math.Sin(latitudeRadians) * Math.Sin(declination);
        double cosinePosition = Math.Cos(latitudeRadians) * Math.Cos(declination);
        double tangentPosition = Math.Clamp(sinePosition / cosinePosition, -1D, 1D);
        double timezoneLongitude = longitude > 0D ? Math.Ceiling(longitude / 15D) * 15D : Math.Floor(longitude / 15D) * 15D;
        double equationOfTime = 7.95204D * Math.Sin(0.01768D * dayOfYear + 3.03217D)
            + 9.98906D * Math.Sin(0.03383D * dayOfYear + 3.46870D)
            + (longitude - timezoneLongitude) * 4D;
        DateTime localDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        equationOfTime -= timeZone.IsDaylightSavingTime(localDate) ? 60D : 0D;

        double minutes = sunrise
            ? 720D - 720D / Math.PI * Math.Acos(-tangentPosition) - equationOfTime
            : 720D + 720D / Math.PI * Math.Acos(-tangentPosition) - equationOfTime;
        int roundedMinutes = (int)minutes;
        if (sunrise && roundedMinutes < 0)
            roundedMinutes += 1440;
        if (!sunrise && roundedMinutes > 1439)
            roundedMinutes -= 1439;

        DateTime localTime = localDate.AddMinutes(roundedMinutes);
        return new DateTimeOffset(localTime, timeZone.GetUtcOffset(localTime));
    }
}