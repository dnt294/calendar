using Calendar.Core.Models;

namespace Calendar.App.Services;

/// <summary>
/// Fake events to exercise the grid before a real provider exists.
/// TODO(2.1): remove once the Google provider fills the store.
/// </summary>
internal static class SampleData
{
    private const string AccountId = "sample:1";

    public static CalendarInfo[] Calendars { get; } =
    [
        new(AccountId, "personal", "Cá nhân", "#0078D4", IsPrimary: true, CanEdit: true),
        new(AccountId, "work", "Công việc", "#C239B3", IsPrimary: false, CanEdit: true),
        new(AccountId, "holidays", "Ngày lễ", "#107C10", IsPrimary: false, CanEdit: false),
    ];

    public static IReadOnlyList<CalendarEvent> ForWeek(DateOnly monday)
    {
        var list = new List<CalendarEvent>();
        var zone = TimeZoneInfo.Local;

        DateTimeOffset At(int dayOffset, int hour, int minute = 0)
        {
            var local = monday.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, minute));
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }

        CalendarEvent Timed(string id, string cal, string title, DateTimeOffset start, TimeSpan duration,
            string? location = null) => new()
        {
            AccountId = AccountId, CalendarId = cal, Id = id, Title = title,
            Start = start, End = start + duration, Location = location,
        };

        CalendarEvent AllDay(string id, string cal, string title, int first, int last)
        {
            var (s, e) = CalendarEvent.AllDaySpan(monday.AddDays(first), monday.AddDays(last));
            return new CalendarEvent
            {
                AccountId = AccountId, CalendarId = cal, Id = id, Title = title, Start = s, End = e, IsAllDay = true,
            };
        }

        for (var d = 0; d < 5; d++)
            list.Add(Timed($"standup-{d}", "work", "Standup", At(d, 9), TimeSpan.FromMinutes(15), "Google Meet") with
            {
                SeriesId = "standup",
            });

        // Overlaps: 2 and 3 columns, nested, adjacent.
        list.Add(Timed("1on1", "work", "1:1 với sếp", At(1, 10), TimeSpan.FromMinutes(60)));
        list.Add(Timed("review", "work", "Review thiết kế", At(1, 10, 30), TimeSpan.FromMinutes(60), "Phòng 3"));
        list.Add(Timed("workshop", "work", "Workshop cả buổi sáng", At(2, 9, 30), TimeSpan.FromHours(3)));
        list.Add(Timed("call-a", "personal", "Gọi điện", At(2, 10), TimeSpan.FromMinutes(30)));
        list.Add(Timed("call-b", "work", "Sync nhanh", At(2, 10, 15), TimeSpan.FromMinutes(45)));
        list.Add(Timed("lunch", "personal", "Ăn trưa", At(2, 12, 30), TimeSpan.FromMinutes(60)));
        list.Add(Timed("after-lunch", "work", "Planning", At(2, 13, 30), TimeSpan.FromMinutes(60)));

        list.Add(Timed("ping", "work", "Nhắc nộp báo cáo", At(3, 15), TimeSpan.FromMinutes(5)));
        list.Add(Timed("gym", "personal", "Gym", At(3, 18), TimeSpan.FromMinutes(90), "California Fitness"));
        list.Add(Timed("late", "personal", "Xem phim", At(4, 22, 30), TimeSpan.FromHours(3)));
        list.Add(Timed("weekend", "personal", "Đi chợ", At(5, 9), TimeSpan.FromMinutes(90)));

        list.Add(AllDay("holiday", "holidays", "Ngày Nhà giáo", -4, -4));
        list.Add(AllDay("trip", "personal", "Công tác Đà Nẵng", 3, 5));
        list.Add(AllDay("deadline", "work", "Deadline sprint", 4, 4));
        list.Add(AllDay("vacation", "personal", "Nghỉ phép", 6, 9));

        return list;
    }
}
