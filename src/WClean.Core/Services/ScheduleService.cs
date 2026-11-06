using Microsoft.Win32.TaskScheduler;

namespace WClean;

public enum ScheduleFrequency { Daily, Weekly, Monthly }

/// <summary>Registers a Task Scheduler job that runs <c>WClean.exe --auto-clean</c>.</summary>
public sealed class ScheduleService
{
    public const string TaskName = "WClean AutoClean";
    public const string AutoCleanArg = "--auto-clean";

    public ScheduleFrequency? GetCurrent()
    {
        try
        {
            using var ts = new TaskService();
            var t = ts.GetTask(TaskName);
            var trig = t?.Definition.Triggers.FirstOrDefault();
            return trig switch
            {
                DailyTrigger => ScheduleFrequency.Daily,
                WeeklyTrigger => ScheduleFrequency.Weekly,
                MonthlyTrigger => ScheduleFrequency.Monthly,
                _ => null,
            };
        }
        catch { return null; }
    }

    public void Enable(ScheduleFrequency freq, string exePath)
    {
        using var ts = new TaskService();
        var td = ts.NewTask();
        td.RegistrationInfo.Description = "W Clean: 임시 파일과 캐시를 자동으로 정리합니다.";
        td.Principal.RunLevel = TaskRunLevel.Highest;
        td.Settings.StartWhenAvailable = true;           // run later if the PC was off
        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.StopIfGoingOnBatteries = false;
        td.Settings.ExecutionTimeLimit = TimeSpan.FromHours(1);

        var start = DateTime.Today.AddHours(12);
        Trigger trigger = freq switch
        {
            ScheduleFrequency.Daily => new DailyTrigger { StartBoundary = start },
            ScheduleFrequency.Weekly => new WeeklyTrigger { StartBoundary = start, DaysOfWeek = DaysOfTheWeek.Monday },
            _ => new MonthlyTrigger { StartBoundary = start, DaysOfMonth = new[] { 1 } },
        };
        td.Triggers.Add(trigger);
        td.Actions.Add(new ExecAction(exePath, AutoCleanArg, Path.GetDirectoryName(exePath)));
        ts.RootFolder.RegisterTaskDefinition(TaskName, td);
    }

    public void Disable()
    {
        using var ts = new TaskService();
        if (ts.GetTask(TaskName) is not null) ts.RootFolder.DeleteTask(TaskName, false);
    }

    /// <summary>What the scheduled run does: cleans the categories that are checked by default, except the recycle bin.</summary>
    public static (long Freed, long Deleted) RunAutoClean()
    {
        var svc = new JunkService();
        var cats = svc.CreateCategories().Where(c => c.IsChecked && !c.IsRecycleBin).ToList();   // never empties the recycle bin unattended
        var r = svc.CleanAsync(cats).GetAwaiter().GetResult();
        if (r.FreedBytes > 0 || r.Deleted > 0) HistoryService.Add("자동 정리", r.FreedBytes, r.Deleted);
        return (r.FreedBytes, r.Deleted);
    }
}
