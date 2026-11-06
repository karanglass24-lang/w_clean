using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;

namespace WClean;

public enum ServiceStartMode { Auto = 2, Manual = 3, Disabled = 4 }

public sealed class ServiceEntry : ObservableObject
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string ImagePath { get; init; } = "";
    public string Company { get; init; } = "";
    public bool IsMicrosoft { get; init; }
    public bool Delayed { get; init; }

    private ServiceStartMode _mode;
    public ServiceStartMode Mode
    {
        get => _mode;
        set { if (Set(ref _mode, value)) Raise(nameof(ModeText)); }
    }

    private bool _running;
    public bool Running
    {
        get => _running;
        set { if (Set(ref _running, value)) Raise(nameof(StateText)); }
    }

    public string ModeText => Mode switch
    {
        ServiceStartMode.Auto => Delayed ? "자동 (지연)" : "자동",
        ServiceStartMode.Manual => "수동",
        _ => "사용 안 함",
    };
    public string StateText => Running ? "실행 중" : "중지됨";
}

/// <summary>Lists Windows services that start automatically and lets the user change how they start.</summary>
public sealed class WindowsServiceManager
{
    private const string ServicesPath = @"SYSTEM\CurrentControlSet\Services";

    public List<ServiceEntry> Load()
    {
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var sc in ServiceController.GetServices())
            {
                using (sc) if (sc.Status == ServiceControllerStatus.Running) running.Add(sc.ServiceName);
            }
        }
        catch { }

        var list = new List<ServiceEntry>();
        var companyCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var root = new RegLocation(RegistryHive.LocalMachine, RegistryView.Registry64, ServicesPath).Open();
        if (root is null) return list;

        foreach (var name in root.GetSubKeyNames())
        {
            try
            {
                using var k = root.OpenSubKey(name);
                if (k is null) continue;
                if (k.GetValue("Type") is not int type || (type & 0x30) == 0) continue;   // own-process / shared-process only
                if (k.GetValue("Start") is not int start || start < 2 || start > 4) continue;   // skip boot/system drivers
                var image = k.GetValue("ImagePath") as string ?? "";
                var display = k.GetValue("DisplayName") as string ?? name;
                if (display.StartsWith('@')) display = name;   // unresolved resource string

                var exe = PathHelper.ExtractExecutable(image);
                if (!companyCache.TryGetValue(exe ?? "", out var company))
                {
                    company = "";
                    try
                    {
                        if (exe is not null && File.Exists(exe))
                            company = FileVersionInfo.GetVersionInfo(exe).CompanyName ?? "";
                    }
                    catch { }
                    companyCache[exe ?? ""] = company;
                }

                var desc = k.GetValue("Description") as string ?? "";
                list.Add(new ServiceEntry
                {
                    Name = name,
                    DisplayName = display,
                    Description = desc.StartsWith('@') ? "" : desc,
                    ImagePath = image,
                    Company = company,
                    IsMicrosoft = company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase),
                    Delayed = k.GetValue("DelayedAutostart") is int d && d == 1,
                    Mode = (ServiceStartMode)start,
                    Running = running.Contains(name),
                });
            }
            catch { }
        }
        return list.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void SetStartMode(ServiceEntry s, ServiceStartMode mode)
    {
        var arg = mode switch
        {
            ServiceStartMode.Auto => "auto",
            ServiceStartMode.Manual => "demand",
            _ => "disabled",
        };
        Sc($"config \"{s.Name}\" start= {arg}");
        s.Mode = mode;
    }

    public void Stop(ServiceEntry s)
    {
        using var sc = new ServiceController(s.Name);
        if (sc.Status == ServiceControllerStatus.Running && sc.CanStop)
        {
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
        }
        s.Running = false;
    }

    public void Start(ServiceEntry s)
    {
        using var sc = new ServiceController(s.Name);
        if (sc.Status != ServiceControllerStatus.Running)
        {
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
        }
        s.Running = true;
    }

    private static void Sc(string args)
    {
        var psi = new ProcessStartInfo("sc.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException(output.Trim().Length > 0 ? output.Trim() : $"sc.exe 오류 {p.ExitCode}");
    }
}
