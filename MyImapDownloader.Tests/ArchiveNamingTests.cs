using MyImapDownloader.Archive;

namespace MyImapDownloader.Tests;

public class ArchiveNamingTests
{
    [Test]
    [Arguments("abc@example.com", "abc@example.com")]
    [Arguments("<abc@example.com>", "_abc@example.com_")]
    [Arguments("<A.B-C_d@Mail.Example.COM>", "_a.b-c_d@mail.example.com_")]
    [Arguments("INBOX/Sub Folder", "inbox_sub folder")]
    [Arguments("a:b|c?d*e\"f\\g", "a_b_c_d_e_f_g")]
    [Arguments("  spaced  ", "  spaced  ")]
    [Arguments("<>", "__")]
    [Arguments("", "unknown")]
    public async Task NormalizeMessageId_MatchesExistingArchives(string input, string expected)
    {
        await Assert.That(ArchiveNaming.NormalizeMessageId(input)).IsEqualTo(expected);
    }

    [Test]
    public async Task NormalizeMessageId_KeepsIdsOfExactlyOneHundredCharacters()
    {
        var id = new string('x', 100);

        await Assert.That(ArchiveNaming.NormalizeMessageId(id)).IsEqualTo(id);
    }

    [Test]
    public async Task NormalizeMessageId_ShortensLongIdsWithStableHashSuffix()
    {
        var normalized = ArchiveNaming.NormalizeMessageId(new string('x', 101));

        await Assert.That(normalized).IsEqualTo(new string('x', 91) + "_c675a2e6");
        await Assert.That(normalized.Length).IsEqualTo(100);
    }

    [Test]
    public async Task NormalizeMessageId_ReplacesControlCharacters()
    {
        await Assert.That(ArchiveNaming.NormalizeMessageId("a\tb\u0001c")).IsEqualTo("a_b_c");
    }

    [Test]
    [Arguments("INBOX", "INBOX")]
    [Arguments("INBOX/Sub Folder", "INBOX_Sub_Folder")]
    [Arguments("[Gmail]/All Mail", "Gmail_All_Mail")]
    [Arguments("Entwürfe", "Entwürfe")]
    [Arguments("###", "")]
    public async Task FolderDirectory_MatchesExistingArchives(string folder, string expected)
    {
        await Assert.That(ArchiveNaming.FolderDirectory(folder)).IsEqualTo(expected);
    }

    [Test]
    public async Task Sanitize_TruncatesToMaximumLength()
    {
        await Assert.That(ArchiveNaming.Sanitize("a really long host name with spaces", 20))
            .IsEqualTo("a_really_long_host_n");
    }

    [Test]
    public async Task SafeFileId_TrimsTrailingDotsAndSpaces()
    {
        await Assert.That(ArchiveNaming.SafeFileId("abc@example.com. .")).IsEqualTo("abc@example.com");
    }

    [Test]
    public async Task FileName_CombinesUnixTimeIdAndHost()
    {
        var date = DateTimeOffset.FromUnixTimeSeconds(1702900000);

        var name = ArchiveNaming.FileName(date, "abc123@mail.example.com", "my host");

        await Assert.That(name).IsEqualTo("1702900000.abc123@mail.example.com.my_host.eml");
    }

    [Test]
    public async Task Hash_IsLowercaseSha256()
    {
        await Assert.That(ArchiveNaming.Hash("abc"))
            .IsEqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Test]
    public async Task HashFileAsync_MatchesHashOfContent()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "file.txt");
        await File.WriteAllTextAsync(path, "abc");

        await Assert.That(await ArchiveNaming.HashFileAsync(path, CancellationToken.None))
            .IsEqualTo(ArchiveNaming.Hash("abc"));
    }
}
