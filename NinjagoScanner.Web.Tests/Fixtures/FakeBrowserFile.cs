using Microsoft.AspNetCore.Components.Forms;

namespace NinjagoScanner.Web.Tests.Fixtures;

/// <summary>
/// Hand-written <see cref="IBrowserFile"/> for tests. Note that Blazor's
/// <c>RequestImageFileAsync</c> is an extension method that throws for any <see cref="IBrowserFile"/>
/// other than its own internal type, so the browser-side resize can't be faked on this class; the
/// code under test takes it as a delegate (<c>PhotoUploadPreparer.ResizeImage</c>) instead.
/// </summary>
internal sealed class FakeBrowserFile : IBrowserFile
{
    private readonly byte[] content;

    public FakeBrowserFile(string name, long size, string contentType = "image/jpeg", byte[]? content = null)
    {
        Name = name;
        Size = size;
        ContentType = contentType;
        this.content = content ?? [];
    }

    public string Name { get; }

    public DateTimeOffset LastModified { get; init; } = DateTimeOffset.UnixEpoch;

    public long Size { get; }

    public string ContentType { get; }

    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
    {
        if (Size > maxAllowedSize)
        {
            throw new IOException($"Supplied file with size {Size} bytes exceeds the maximum of {maxAllowedSize} bytes.");
        }

        return new MemoryStream(content);
    }
}
