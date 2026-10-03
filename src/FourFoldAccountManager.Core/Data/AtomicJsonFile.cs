using System.Text.Json;

namespace FourFoldAccountManager.Core.Data;

/// <summary>
/// Saves user data so a crash or full disk can never leave a half-written file in place of the original.
/// </summary>
public static class AtomicJsonFile
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Writes <paramref name="value"/> to a temporary file in the same folder, flushes it to disk, then swaps it
    /// over <paramref name="path"/>. The old file stays intact unless the swap succeeds.
    /// </summary>
    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(path)}-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            // File.Replace, not File.Move: it still works while antivirus or the indexer has the old file open.
            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
