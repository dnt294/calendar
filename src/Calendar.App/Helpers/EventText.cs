using System.Globalization;
using Calendar.Core.Models;

namespace Calendar.App.Helpers;

/// <summary>Vietnamese display texts for events and draft problems.</summary>
internal static class EventText
{
    private static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>"Thứ Sáu, 27 tháng 11 · 9:00 – 10:00", "Cả ngày · …", or a range over several days.</summary>
    public static string When(CalendarEvent ev) => When(ev.Start, ev.End, ev.IsAllDay);

    public static string When(DateTimeOffset start, DateTimeOffset end, bool isAllDay)
    {
        var longDate = Culture.DateTimeFormat.LongDatePattern;
        if (isAllDay)
        {
            var first = DateOnly.FromDateTime(start.DateTime);
            var last = DateOnly.FromDateTime(end.DateTime).AddDays(-1);
            return last <= first
                ? $"{first.ToString(longDate, Culture)} · Cả ngày"
                : $"{first.ToString(longDate, Culture)} – {last.ToString(longDate, Culture)} · Cả ngày";
        }
        var s = start.ToLocalTime();
        var e = end.ToLocalTime();
        return s.Date == e.Date || (e.TimeOfDay == TimeSpan.Zero && e.Date == s.Date.AddDays(1))
            ? $"{s.ToString(longDate, Culture)} · {s:t} – {e:t}"
            : $"{s.ToString(longDate, Culture)} {s:t} – {e.ToString(longDate, Culture)} {e:t}";
    }

    public static string Rsvp(RsvpStatus status) => status switch
    {
        RsvpStatus.Accepted => "Đồng ý",
        RsvpStatus.Tentative => "Có thể",
        RsvpStatus.Declined => "Từ chối",
        RsvpStatus.NeedsAction => "Chưa trả lời",
        _ => "",
    };

    public static string Problem(DraftProblem problem) => problem.Kind switch
    {
        DraftProblemKind.EndBeforeStart => "Thời gian kết thúc phải sau thời gian bắt đầu.",
        DraftProblemKind.InvalidAttendeeEmail => $"Email khách mời không hợp lệ: {problem.Detail}",
        _ => problem.Kind.ToString(),
    };
}
