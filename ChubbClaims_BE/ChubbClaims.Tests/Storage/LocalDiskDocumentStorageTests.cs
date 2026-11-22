using ChubbClaims.Service.Exceptions;
using ChubbClaims.Service.Storage;
using Shouldly;

namespace ChubbClaims.Tests.Storage;

public sealed class LocalDiskDocumentStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chubbclaims-tests", Guid.NewGuid().ToString("N"));
    private readonly LocalDiskDocumentStorage _storage;

    public LocalDiskDocumentStorageTests() => _storage = new LocalDiskDocumentStorage(new DocumentStorageSettings { LocalPath = _root });

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Stores_and_reads_back_the_same_bytes()
    {
        var key = DocumentKeys.For(Guid.NewGuid(), "image/png");
        byte[] bytes = [1, 2, 3, 4];

        await _storage.SaveAsync(key, new MemoryStream(bytes), "image/png", CancellationToken.None);
        await using var read = await _storage.OpenReadAsync(key, CancellationToken.None);

        using var copy = new MemoryStream();
        await read!.CopyToAsync(copy);
        copy.ToArray().ShouldBe(bytes);
    }

    [Fact]
    public async Task Never_overwrites_a_stored_file()
    {
        var key = DocumentKeys.For(Guid.NewGuid(), "image/png");
        await _storage.SaveAsync(key, new MemoryStream([1]), "image/png", CancellationToken.None);

        await Should.ThrowAsync<DocumentStorageException>(() =>
            _storage.SaveAsync(key, new MemoryStream([2]), "image/png", CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_file_reads_as_null_and_delete_is_safe_to_repeat()
    {
        var key = DocumentKeys.For(Guid.NewGuid(), "application/pdf");

        (await _storage.OpenReadAsync(key, CancellationToken.None)).ShouldBeNull();
        await _storage.DeleteAsync(key, CancellationToken.None);
        await _storage.DeleteAsync(key, CancellationToken.None);
    }

    [Fact]
    public async Task A_key_that_tries_to_leave_the_folder_is_refused_before_any_file_access()
    {
        await Should.ThrowAsync<ArgumentException>(() => _storage.OpenReadAsync("../../appsettings.json", CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() =>
            _storage.SaveAsync("claims/../../evil.jpg", new MemoryStream([1]), "image/jpeg", CancellationToken.None));
    }
}
