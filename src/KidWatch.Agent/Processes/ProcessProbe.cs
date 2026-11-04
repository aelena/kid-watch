using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace KidWatch.Agent.Processes;

/// <summary>A process seen at a moment in time.</summary>
public sealed record RunningProcess(int Pid, string Name);

/// <summary>Enumerates and terminates processes. Faked in tests.</summary>
public interface IProcessProbe
{
    IReadOnlyList<RunningProcess> Snapshot();
    void KillTree(string processName);
}

/// <summary>
/// Real implementation over <see cref="System.Diagnostics.Process"/>. Enumeration
/// and kills are best-effort: a process can exit between listing and acting on it,
/// so every call is guarded.
/// </summary>
public sealed class WindowsProcessProbe(ILogger<WindowsProcessProbe> logger) : IProcessProbe
{
    public IReadOnlyList<RunningProcess> Snapshot()
    {
        var list = new List<RunningProcess>();
        foreach (var p in Process.GetProcesses())
        {
            try { list.Add(new RunningProcess(p.Id, p.ProcessName)); }
            catch { /* exited between enumeration and read */ }
            finally { p.Dispose(); }
        }
        return list;
    }

    public void KillTree(string processName)
    {
        var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "could not kill {Name} (pid {Pid})", name, p.Id);
            }
            finally
            {
                p.Dispose();
            }
        }
    }
}
