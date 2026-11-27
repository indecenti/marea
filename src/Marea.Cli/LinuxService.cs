using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Marea.Core;

namespace Marea.Cli;

/// <summary>Linux only: installs the agent as a systemd service for one user, so freed files come back
/// whenever a program opens them, also after a restart. The binary is copied to a root-owned path first:
/// a service with CAP_SYS_ADMIN must never run a file its user could replace.</summary>
internal static partial class LinuxService
{
    private const string UnitPath = "/etc/systemd/system/marea-agent@.service";
    private const string BinPath = "/usr/local/bin/mareactl";

    private const string Unit = """
        [Unit]
        Description=Marea agent for %i: freed model files come back when a program opens them
        Documentation=https://github.com/indecenti/marea
        After=local-fs.target network-online.target
        Wants=network-online.target
        Before=ollama.service

        [Service]
        User=%i
        ExecStart=/usr/local/bin/mareactl agent
        Restart=always
        RestartSec=2
        # fanotify permission events need CAP_SYS_ADMIN; the others let the agent refill files with their
        # original owner and write time, and check that no program still has a file open before freeing it.
        AmbientCapabilities=CAP_SYS_ADMIN CAP_DAC_OVERRIDE CAP_FOWNER CAP_LEASE
        CapabilityBoundingSet=CAP_SYS_ADMIN CAP_DAC_OVERRIDE CAP_FOWNER CAP_LEASE
        NoNewPrivileges=yes

        [Install]
        WantedBy=multi-user.target

        """;

    [DllImport("libc")]
    private static extern uint geteuid();

    [GeneratedRegex("^[a-z_][a-z0-9_.-]{0,31}$")]
    private static partial Regex UserName();

    /// <summary>Runs "service install|uninstall [--user NAME] [--yes]". Returns the exit code.</summary>
    public static int Run(string? action, string[] args)
    {
        if (action is not ("install" or "uninstall"))
        {
            Console.Error.WriteLine(Loc.T("cli.service_usage"));
            return Exit.Usage;
        }
        if (geteuid() != 0)
        {
            Console.Error.WriteLine(Loc.T("cli.service_need_root"));
            return Exit.Error;
        }
        var i = Array.IndexOf(args, "--user");
        var user = i >= 0 && i + 1 < args.Length ? args[i + 1] : Environment.GetEnvironmentVariable("SUDO_USER");
        if (user is null || user == "root" || !UserName().IsMatch(user))
        {
            Console.Error.WriteLine(Loc.T("cli.service_need_user"));
            return Exit.Usage;
        }
        return action == "install" ? Install(user) : Uninstall(user, args.Contains("--yes"));
    }

    private static int Install(string user)
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");
        if (self != BinPath)
        {
            // Replace atomically: the running service keeps its old copy until it restarts.
            File.Copy(self, BinPath + ".new", overwrite: true);
            #pragma warning disable CA1416 // this file is compiled for Linux only
            File.SetUnixFileMode(BinPath + ".new", (UnixFileMode)0b111_101_101);
            #pragma warning restore CA1416
            File.Move(BinPath + ".new", BinPath, overwrite: true);
        }
        File.WriteAllText(UnitPath, Unit);
        if (!Systemctl("daemon-reload") || !Systemctl("enable", $"marea-agent@{user}") || !Systemctl("restart", $"marea-agent@{user}"))
        {
            return Exit.Error;
        }
        Console.WriteLine(Loc.T("cli.service_installed", user));
        return Exit.Ok;
    }

    private static int Uninstall(string user, bool yes)
    {
        if (!yes)
        {
            Console.Error.WriteLine(Loc.T("cli.service_uninstall_warning"));
            return Exit.NeedsConfirmation;
        }
        if (!Systemctl("disable", "--now", $"marea-agent@{user}"))
        {
            return Exit.Error;
        }
        Console.WriteLine(Loc.T("cli.service_removed", user));
        return Exit.Ok;
    }

    private static bool Systemctl(params string[] args)
    {
        var psi = new ProcessStartInfo("systemctl") { UseShellExecute = false };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        try
        {
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Console.Error.WriteLine($"systemctl: {e.Message}");
            return false;
        }
    }
}
