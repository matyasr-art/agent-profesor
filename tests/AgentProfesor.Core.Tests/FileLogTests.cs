using AgentProfesor.Core;
using Xunit;

namespace AgentProfesor.Core.Tests;

public class FileLogTests : IDisposable
{
    private readonly string _dir;

    public FileLogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"agentprofesor-log-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Writes_lines_with_level_tags()
    {
        string path;
        using (var log = new FileLog(_dir, rotationMinutes: 60))
        {
            log.Info("start");
            log.Warn("pozor");
            log.Error("chyba");
            path = log.CurrentPath!;
        }

        var content = File.ReadAllText(path);
        Assert.Contains("[INF] start", content);
        Assert.Contains("[WRN] pozor", content);
        Assert.Contains("[ERR] chyba", content);
    }

    [Fact]
    public void Error_with_exception_writes_the_stack()
    {
        string path;
        using (var log = new FileLog(_dir, rotationMinutes: 60))
        {
            try
            {
                throw new InvalidOperationException("bum");
            }
            catch (Exception ex)
            {
                log.Error("spadlo to", ex);
            }
            path = log.CurrentPath!;
        }

        var content = File.ReadAllText(path);
        Assert.Contains("[ERR] spadlo to", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("bum", content);
    }

    [Fact]
    public void Rotates_to_a_new_file_after_the_rotation_interval()
    {
        var now = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        using var log = new FileLog(_dir, rotationMinutes: 60, clock: () => now);

        log.Info("první");
        var firstPath = log.CurrentPath;

        now = now.AddMinutes(61);
        log.Info("druhý");
        var secondPath = log.CurrentPath;

        Assert.NotEqual(firstPath, secondPath);
        Assert.Equal(2, Directory.GetFiles(_dir, "agent-*.log").Length);
    }

    [Fact]
    public void Stays_in_one_file_within_the_rotation_interval()
    {
        var now = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        using var log = new FileLog(_dir, rotationMinutes: 60, clock: () => now);

        log.Info("a");
        now = now.AddMinutes(30);
        log.Info("b");

        Assert.Single(Directory.GetFiles(_dir, "agent-*.log"));
    }

    [Fact]
    public void Concurrent_writes_do_not_throw_and_all_land()
    {
        string path;
        using (var log = new FileLog(_dir, rotationMinutes: 60))
        {
            Parallel.For(0, 200, i => log.Info($"řádek {i}"));
            path = log.CurrentPath!;
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(200, lines.Count(l => l.Contains("[INF] řádek ")));
    }

    [Fact]
    public void Writing_after_dispose_does_not_open_a_new_orphan_file()
    {
        var log = new FileLog(_dir, rotationMinutes: 60);
        log.Info("před dispose");
        log.Dispose();

        // Zápis po Dispose (např. z doznívajícího background tasku) se musí tiše zahodit,
        // ne otevřít nový agent-*.log, který už nikdo nezavře.
        log.Info("po dispose");

        var files = Directory.GetFiles(_dir, "agent-*.log");
        Assert.Single(files);
        Assert.DoesNotContain("po dispose", File.ReadAllText(files[0]));
    }

    [Fact]
    public void Filename_matches_the_agent_glob_from_the_tester_readme()
    {
        using var log = new FileLog(_dir, rotationMinutes: 60);
        log.Info("x");

        var name = Path.GetFileName(log.CurrentPath!);
        Assert.StartsWith("agent-", name);
        Assert.EndsWith(".log", name);
    }
}
