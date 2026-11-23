using FluentAssertions;
using HabitGrid.Data.V3;
using Xunit;

namespace HabitGrid.Data.Tests.V3;

public sealed class BackupMirrorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "habitgrid-mirror-" + Guid.NewGuid().ToString("N"));
    private string Local => Path.Combine(_root, "local");
    private string Cloud => Path.Combine(_root, "OneDrive", "HabitGrid");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private BackupManager Create(V3Fixture f, int retention = 3) =>
        new(new SnapshotBackupService(f.Database, f.Clock, f.Bus, includeJournal: false), Local, retention);

    private static DateTime Day(int offset) => new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc).AddDays(offset);

    [Fact]
    public async Task EveryAutomaticBackup_IsMirroredToTheCloudFolder()
    {
        using var f = new V3Fixture();
        var manager = Create(f);
        manager.MirrorTarget = () => Task.FromResult<string?>(Cloud);

        await manager.RunIfDueAsync(Day(0));

        manager.LastMirror!.Succeeded.Should().BeTrue();
        BackupManager.ListIn(Cloud).Should().ContainSingle();
        BackupManager.ListIn(Cloud)[0].SizeBytes.Should().Be(manager.List()[0].SizeBytes, "bản sao giống hệt");
        Directory.GetFiles(Cloud, "*.tmp").Should().BeEmpty("không để file tạm cho dịch vụ cloud đồng bộ");
    }

    [Fact]
    public async Task NoTarget_MeansNoMirror_AndLocalBackupStillWorks()
    {
        using var f = new V3Fixture();
        var manager = Create(f);
        manager.MirrorTarget = () => Task.FromResult<string?>(null);

        var file = await manager.RunIfDueAsync(Day(0));

        file.Should().NotBeNull();
        manager.LastMirror.Should().BeNull();
        Directory.Exists(Cloud).Should().BeFalse();
    }

    [Fact]
    public async Task CloudFolderIsPruned_ButOnlyAutomaticBackups_NeverTheUsersOtherFiles()
    {
        using var f = new V3Fixture();
        var manager = Create(f, retention: 2);
        manager.MirrorTarget = () => Task.FromResult<string?>(Cloud);
        Directory.CreateDirectory(Cloud);
        var photo = Path.Combine(Cloud, "ảnh-gia-đình.jpg");
        await File.WriteAllTextAsync(photo, "keep me");

        for (var i = 0; i < 5; i++)
        {
            await manager.RunIfDueAsync(Day(i));
        }

        BackupManager.ListIn(Cloud).Where(b => b.IsAutomatic).Should().HaveCount(2);
        File.Exists(photo).Should().BeTrue("file của người dùng không bao giờ bị xóa");
    }

    [Fact]
    public async Task ManualBackups_AreMirrored_ButTheBeforeRestoreSafetyCopyIsNot()
    {
        using var f = new V3Fixture();
        await f.Habits.InsertAsync(V3Fixture.NewHabit("A"));
        var manager = Create(f);
        manager.MirrorTarget = () => Task.FromResult<string?>(Cloud);

        var manual = await manager.BackupNowAsync(Day(0));
        BackupManager.ListIn(Cloud).Should().ContainSingle();

        await manager.RestoreAsync(manual.Path, Day(1));

        BackupManager.ListIn(Cloud).Should().ContainSingle("bản an toàn trước khi khôi phục chỉ nằm cục bộ");
        manager.List().Should().HaveCount(2);
    }

    [Fact]
    public async Task AnUnusableTarget_RecordsTheFailure_WithoutBreakingTheLocalBackup()
    {
        using var f = new V3Fixture();
        var manager = Create(f);
        var blocker = Path.Combine(_root, "afile");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(blocker, "x");
        manager.MirrorTarget = () => Task.FromResult<string?>(Path.Combine(blocker, "sub"));

        var file = await manager.RunIfDueAsync(Day(0));

        file.Should().NotBeNull("sao lưu cục bộ vẫn thành công");
        manager.LastMirror!.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ATargetThatThrows_IsContained()
    {
        using var f = new V3Fixture();
        var manager = Create(f);
        manager.MirrorTarget = () => throw new InvalidOperationException("settings down");

        (await manager.RunIfDueAsync(Day(0))).Should().NotBeNull();
        manager.LastMirror!.Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("   ", "empty")]
    public void Validate_RejectsEmptyTargets(string? target, string reason) =>
        BackupMirror.Validate(target, Local).Should().Be(reason);

    [Fact]
    public void Validate_RejectsTheLocalFolder_AcceptsAWritableOne_AndCreatesIt()
    {
        BackupMirror.Validate(Local, Local).Should().Be("same-as-local");
        BackupMirror.Validate(Local + Path.DirectorySeparatorChar, Local).Should().Be("same-as-local");
        BackupMirror.Validate(Cloud, Local).Should().BeNull();
        Directory.Exists(Cloud).Should().BeTrue();
        Directory.GetFiles(Cloud).Should().BeEmpty("file thử ghi đã được xóa");
    }

    [Fact]
    public void DetectCloudFolders_ReturnsOnlyExistingOnes_WithoutDuplicates()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Users\Linh\OneDrive", @"C:\Users\Linh\Dropbox" };
        string? Env(string name) => name switch
        {
            "USERPROFILE" => @"C:\Users\Linh",
            "OneDrive" => @"C:\Users\Linh\OneDrive",
            "OneDriveConsumer" => @"C:\Users\Linh\OneDrive",
            _ => null
        };

        var found = BackupMirror.DetectCloudFolders(Env, existing.Contains);

        found.Select(c => c.Name).Should().Equal("OneDrive", "Dropbox");
        found[0].Path.Should().Be(@"C:\Users\Linh\OneDrive\HabitGrid");
    }
}
