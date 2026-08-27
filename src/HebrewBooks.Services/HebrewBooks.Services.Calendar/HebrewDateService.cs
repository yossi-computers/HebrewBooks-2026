using System;
using System.Globalization;

namespace HebrewBooks.Services.Calendar;

public sealed class HebrewDateService
{
	private static readonly HebrewCalendar Calendar = new HebrewCalendar();

	private static readonly CultureInfo HebrewCulture = MakeHebrewCulture();

	private static CultureInfo MakeHebrewCulture()
	{
		CultureInfo obj = (CultureInfo)CultureInfo.GetCultureInfo("he-IL").Clone();
		obj.DateTimeFormat.Calendar = Calendar;
		return obj;
	}

	public string Format(DateTime gregorian, string format = "D")
	{
		return gregorian.ToString(format, HebrewCulture);
	}

	public int HebrewYear(DateTime g)
	{
		return Calendar.GetYear(g);
	}

	public int HebrewMonth(DateTime g)
	{
		return Calendar.GetMonth(g);
	}

	public int HebrewDay(DateTime g)
	{
		return Calendar.GetDayOfMonth(g);
	}

	public DateTime ToGregorian(int hebYear, int hebMonth, int hebDay)
	{
		return Calendar.ToDateTime(hebYear, hebMonth, hebDay, 0, 0, 0, 0);
	}

	public bool IsLeapYear(int hebYear)
	{
		return Calendar.IsLeapYear(hebYear);
	}

	public int MonthsInYear(int hebYear)
	{
		return Calendar.GetMonthsInYear(hebYear);
	}

	public int DaysInMonth(int hebYear, int hebMonth)
	{
		return Calendar.GetDaysInMonth(hebYear, hebMonth);
	}

	public string MonthName(int hebYear, int hebMonth)
	{
		string[] monthNames = HebrewCulture.DateTimeFormat.MonthNames;
		if (hebMonth < 1 || hebMonth > 13)
		{
			throw new ArgumentOutOfRangeException("hebMonth");
		}
		return monthNames[hebMonth - 1];
	}
}
