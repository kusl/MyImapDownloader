namespace MyImapDownloader.Core.Paths;

public sealed class XdgDirectories(Func<string, string?> environment, string home)
{
    public static XdgDirectories Current { get; } = new(
        Environment.GetEnvironmentVariable,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public string Home => home;

    public string? DataHomeOverride => Variable("XDG_DATA_HOME");

    public string? StateHomeOverride => Variable("XDG_STATE_HOME");

    public string DataHome => DataHomeOverride ?? Path.Combine(home, ".local", "share");

    public string StateHome => StateHomeOverride ?? Path.Combine(home, ".local", "state");

    public string? Variable(string name) =>
        environment(name) is { } value && !string.IsNullOrWhiteSpace(value) ? value : null;
}
