using System;
using System.Globalization;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.CalendarDate;

/// <summary>Stores the native calendar date independently of approximate clock drift.</summary>
internal readonly record struct CalendarDateReading(int Day, EnumMonth Month)
{
    #region Public API
    /// <summary>Reads variable-length game months using zero-based day-of-year and native month names.</summary>
    public static CalendarDateReading? Read(IGameCalendar calendar)
    {
        if (calendar.DaysPerMonth <= 0 || calendar.DayOfYear < 0 || !Enum.IsDefined(calendar.MonthName)) return null;
        return new(calendar.DayOfYear % calendar.DaysPerMonth + 1, calendar.MonthName);
    }

    /// <summary>Uses the native localized month name, limited to three text elements rather than a numeric month.</summary>
    public string MonthAbbreviation(string locale)
    {
        string name = Lang.GetL(locale, "month-" + Month);
        if (name == "month-" + Month) name = Month.ToString();
        int[] elements = StringInfo.ParseCombiningCharacters(name);
        if (elements.Length > 3) name = name[..elements[3]];
        CultureInfo culture;
        try { culture = CultureInfo.GetCultureInfo(locale); }
        catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }
        return name.ToUpper(culture);
    }
    #endregion
}
