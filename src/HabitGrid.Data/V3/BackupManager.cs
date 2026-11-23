using System.Globalization;

namespace HabitGrid.Data.V3;

/// <summary>Một bản sao lưu trên đĩa.</summary>
public sealed record BackupFile(string Path, DateTime CreatedAtUtc, long SizeBytes, bool IsAutomatic);

/// <summary>
/// Quản lý sao lưu trên máy (F5): tự sao lưu mỗi ngày vào thư mục cục bộ, giữ N bản mới nhất, liệt kê và khôi phục.
/// Tên file <c>habitgrid-auto-yyyyMMdd-HHmmss.hgbackup</c> (tự động) hoặc <c>habitgrid-yyyyMMdd-HHmmss.hgbackup</c> (tay).
/// Chỉ bản tự động bị dọn theo hạn giữ; bản người dùng tự tạo không bao giờ bị xóa.
/// </summary>
public sealed class BackupManager
{
    private const string AutoPrefix = "habitgrid-auto-";
    private const string ManualPrefix = "habitgrid-";
    private const string Pattern = "yyyyMMdd-HHmmss";

    private readonly SnapshotBackupService _service;
    private readonly string _directory;
    private readonly int _retention;

    public BackupManager(SnapshotBackupService service, string directory, int retention = SnapshotBackupService.DefaultRetention)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(retention, 1);
        _directory = directory;
        _retention = retention;
    }

    public string Directory => _directory;

    /// <summary>
    /// Thư mục nhân bản (cloud) hiện tại, hoặc null nếu tắt/không có quyền. Gọi mỗi lần sao lưu nên đổi cài đặt có hiệu lực ngay.
    /// Lỗi nhân bản không làm hỏng bản sao lưu cục bộ; xem <see cref="LastMirror"/>.
    /// </summary>
    public Func<Task<string?>>? MirrorTarget { get; set; }

    /// <summary>Kết quả nhân bản gần nhất (null nếu chưa có lần nào).</summary>
    public MirrorResult? LastMirror { get; private set; }

    /// <summary>Danh sách bản sao lưu trong thư mục, mới nhất trước.</summary>
    public IReadOnlyList<BackupFile> List() => ListIn(_directory);

    public static IReadOnlyList<BackupFile> ListIn(string directory)
    {
        if (!System.IO.Directory.Exists(directory))
        {
            return [];
        }

        var list = new List<BackupFile>();

        foreach (var path in System.IO.Directory.EnumerateFiles(directory, "habitgrid-*" + SnapshotBackupService.Extension))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var auto = name.StartsWith(AutoPrefix, StringComparison.Ordinal);
            var stamp = name[(auto ? AutoPrefix.Length : ManualPrefix.Length)..];

            // Bỏ hậu tố như "-before-restore"; chỉ phần ngày giờ đầu tiên có nghĩa.
            if (stamp.Length > Pattern.Length)
            {
                stamp = stamp[..Pattern.Length];
            }

            if (!DateTime.TryParseExact(stamp, Pattern, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var created))
            {
                continue;
            }

            list.Add(new BackupFile(path, created, new FileInfo(path).Length, auto));
        }

        return list.OrderByDescending(b => b.CreatedAtUtc).ToList();
    }

    /// <summary>Tạo bản sao lưu do người dùng yêu cầu vào thư mục cục bộ.</summary>
    public async Task<BackupFile> BackupNowAsync(DateTime? nowUtc = null, CancellationToken ct = default) =>
        await CreateAsync(manual: true, nowUtc ?? DateTime.UtcNow, ct);

    /// <summary>
    /// Tự sao lưu nếu hôm nay (theo giờ địa phương) chưa có bản tự động nào. Trả về bản mới tạo, hoặc null nếu không cần.
    /// Dọn bản tự động cũ hơn hạn giữ. Lỗi ghi file (ổ đầy, thư mục bị khóa) thì trả null chứ không làm hỏng app.
    /// </summary>
    public async Task<BackupFile?> RunIfDueAsync(DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now.ToLocalTime());

        if (List().Any(b => b.IsAutomatic && DateOnly.FromDateTime(b.CreatedAtUtc.ToLocalTime()) == today))
        {
            return null;
        }

        try
        {
            var file = await CreateAsync(manual: false, now, ct);
            Prune();
            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Khôi phục từ một file: trước khi thay thế, tự sao lưu hiện trạng để người dùng quay lại được nếu chọn nhầm.</summary>
    public async Task<BackupManifest> RestoreAsync(string path, DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var stamp = (nowUtc ?? DateTime.UtcNow).ToString(Pattern, CultureInfo.InvariantCulture);
        var name = $"{ManualPrefix}{stamp}-before-restore-{Guid.NewGuid():N}";
        var safetyPath = System.IO.Path.Combine(_directory, name + SnapshotBackupService.Extension);
        return await _service.RestoreAsync(path, safetyPath, ct);
    }

    private async Task<BackupFile> CreateAsync(bool manual, DateTime nowUtc, CancellationToken ct)
    {
        var name = $"{(manual ? ManualPrefix : AutoPrefix)}{nowUtc.ToString(Pattern, CultureInfo.InvariantCulture)}";
        var path = System.IO.Path.Combine(_directory, name + SnapshotBackupService.Extension);
        await _service.ExportAsync(path, ct);
        await MirrorAsync(path, ct);
        return new BackupFile(path, nowUtc, new FileInfo(path).Length, !manual);
    }

    /// <summary>Nhân bản ngay bản sao lưu mới nhất sang <paramref name="target"/> (dùng khi người dùng vừa chọn thư mục cloud).</summary>
    public async Task<MirrorResult?> MirrorLatestAsync(string target, CancellationToken ct = default)
    {
        if (List().FirstOrDefault() is not { } latest)
        {
            return null;
        }

        LastMirror = await BackupMirror.CopyAsync(latest.Path, target, _retention, ct);
        return LastMirror;
    }

    /// <summary>Chỉ nhân bản bản sao lưu thường (tay hoặc tự động), không nhân bản bản an toàn "trước khi khôi phục".</summary>
    private async Task MirrorAsync(string path, CancellationToken ct)
    {
        if (MirrorTarget is null)
        {
            return;
        }

        try
        {
            var target = await MirrorTarget();

            if (target is null)
            {
                return;
            }

            LastMirror = BackupMirror.Validate(target, _directory) is null
                ? await BackupMirror.CopyAsync(path, target, _retention, ct)
                : new MirrorResult(false, null, "target-unavailable");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastMirror = new MirrorResult(false, null, ex.Message);
        }
    }

    private void Prune()
    {
        foreach (var old in List().Where(b => b.IsAutomatic).Skip(_retention))
        {
            try
            {
                File.Delete(old.Path);
            }
            catch (IOException)
            {
                // Bản cũ đang bị khóa: lần sau dọn tiếp.
            }
        }
    }
}
