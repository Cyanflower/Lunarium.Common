using System.Text;

namespace Lunarium.Common.Caching;

/// <summary>
/// JSON 文件的原子写与损坏留档助手，供 <see cref="JsonFileHandler"/> 与 <see cref="JsonDocumentFileHandler{T}"/> 共用。
/// </summary>
internal static class JsonFileIO
{
    /// <summary>
    /// 以 UTF-8 编码把文本原子写入文件（先写同目录临时文件，再改名覆盖目标文件）。
    /// </summary>
    /// <param name="filePath">目标文件路径。</param>
    /// <param name="content">要写入的文本内容。</param>
    /// <param name="ct">用于取消写入操作的取消令牌。</param>
    public static Task WriteAllTextAsync(string filePath, string content, CancellationToken ct) =>
        WriteAllBytesAsync(filePath, Encoding.UTF8.GetBytes(content), ct);

    /// <summary>
    /// 把 UTF-8 字节原子写入文件（先写同目录临时文件，再改名覆盖目标文件），
    /// 保证目标文件永远是完整内容，不会出现半截写入。
    /// </summary>
    /// <param name="filePath">目标文件路径。</param>
    /// <param name="utf8Json">要写入的 UTF-8 字节内容。</param>
    /// <param name="ct">用于取消写入操作的取消令牌。</param>
    public static async Task WriteAllBytesAsync(string filePath, byte[] utf8Json, CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory); // 幂等, 已存在时为无操作

        string tempPath = filePath + ".tmp";
        await File.WriteAllBytesAsync(tempPath, utf8Json, ct).ConfigureAwait(false);
        File.Move(tempPath, filePath, overwrite: true);
    }

    /// <summary>
    /// 把损坏文件改名留档，便于事后排查；文件不存在时返回 <see langword="null"/>。
    /// </summary>
    /// <param name="filePath">要留档的文件路径。</param>
    /// <returns>留档后的路径；文件不存在时为 <see langword="null"/>。</returns>
    public static string? ArchiveCorrupt(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        string archivedPath = $"{filePath}.corrupt-{DateTime.Now:yyyyMMddHHmmss}";
        File.Move(filePath, archivedPath, overwrite: true);
        return archivedPath;
    }
}
