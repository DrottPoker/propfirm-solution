using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Prop.Api.Firms;

/// <summary>
/// A logo the firm uploaded for its portal (ADR 0023): PNG, JPEG, WebP or SVG, known from its content, with the
/// SHA-256 of the content. SVG may not hold scripts or anything that loads more, and is served so that it could
/// not run any either.
/// </summary>
internal sealed partial record FirmLogo(string ContentType, byte[] Content, byte[] Sha256)
{
    public const int MaxBytes = 1024 * 1024;

    /// <summary>Where the portal serves the logo. The content's hash is part of it, so a new logo gets a new address.</summary>
    public static string UrlOf(byte[] sha256) => $"/api/portal/logo/{Convert.ToHexStringLower(sha256)}";

    /// <summary>The logo with the content, or why the content cannot be one.</summary>
    public static (FirmLogo? Logo, string? Problem) From(byte[] content)
    {
        if (content.Length == 0)
        {
            return (null, "Choose an image file.");
        }

        if (content.Length > MaxBytes)
        {
            return (null, "A logo can be at most 1 MB.");
        }

        var contentType = RasterTypeOf(content) ?? (IsSvg(content) ? "image/svg+xml" : null);
        if (contentType is null)
        {
            return (null, "The logo must be a PNG, JPEG, WebP or SVG image.");
        }

        if (contentType == "image/svg+xml" && SvgProblem(content) is { } problem)
        {
            return (null, problem);
        }

        return (new FirmLogo(contentType, content, SHA256.HashData(content)), null);
    }

    private static string? RasterTypeOf(ReadOnlySpan<byte> content) =>
        content.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) ? "image/png"
        : content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]) ? "image/jpeg"
        : content.Length >= 12 && content.StartsWith("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8) ? "image/webp"
        : null;

    // An SVG file is XML text whose first element is svg, after an optional declaration and comments.
    private static bool IsSvg(byte[] content)
    {
        var text = Text(content).TrimStart();
        return (text.StartsWith("<?xml", StringComparison.Ordinal) || text.StartsWith("<svg", StringComparison.Ordinal) || text.StartsWith("<!--", StringComparison.Ordinal))
            && text.Contains("<svg", StringComparison.Ordinal);
    }

    // What a logo has no use for, and what could run or load something when the file is opened on its own.
    private static string? SvgProblem(byte[] content) =>
        UnsafeSvg().IsMatch(Text(content)) ? "The SVG logo may not hold scripts, event handlers, embedded pages or entities." : null;

    private static string Text(byte[] content) => Encoding.UTF8.GetString(content).TrimStart('﻿');

    [GeneratedRegex(@"<\s*(script|foreignObject|iframe|embed|object)\b|<!(ENTITY|DOCTYPE)|javascript\s*:|\son[a-z]+\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex UnsafeSvg();
}
