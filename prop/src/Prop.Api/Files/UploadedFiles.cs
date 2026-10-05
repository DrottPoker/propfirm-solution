using Microsoft.AspNetCore.Http.HttpResults;

namespace Prop.Api.Files;

/// <summary>What the platform accepts of files people upload, such as a firm's documents and the attachments of support tickets.</summary>
internal static class UploadedFiles
{
    public const int MaxFileNameLength = 200;

    /// <summary>The PDF, PNG or JPEG content type of the file, known from its first bytes. Null for anything else.</summary>
    public static string? ContentTypeOf(ReadOnlySpan<byte> content) =>
        content.StartsWith("%PDF-"u8) ? "application/pdf"
        : content.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) ? "image/png"
        : content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]) ? "image/jpeg"
        : null;

    /// <summary>The name without folders or control characters, shortened to <see cref="MaxFileNameLength"/>, or <paramref name="fallback"/> when nothing is left.</summary>
    public static string CleanFileName(string? fileName, string fallback)
    {
        var name = new string(Path.GetFileName((fileName ?? "").Replace('\\', '/')).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length > MaxFileNameLength)
        {
            var extension = Path.GetExtension(name);
            name = extension.Length < 20 ? name[..(MaxFileNameLength - extension.Length)] + extension : name[..MaxFileNameLength];
        }

        return name.Length == 0 ? fallback : name;
    }

    /// <summary>
    /// A stored file as a download, never shown as a page, so a file cannot run as a page on the portal's address. An
    /// image can still be shown in an img element.
    /// </summary>
    public static FileContentHttpResult Download(HttpContext context, byte[] content, string contentType, string fileName)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.CacheControl = "private, no-store";
        headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        return TypedResults.File(content, contentType, fileName);
    }
}
