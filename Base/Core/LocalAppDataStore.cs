#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Base.Core
{
    public sealed class LocalAppDataStore
    {
        private static readonly object initLock = new();
        private static LocalAppDataStore instance;
        public static LocalAppDataStore Instance =>
            instance ?? throw new InvalidOperationException("LocalAppDataStore is not initialized. Call LocalAppDataStore.Init(...) first.");

        private readonly string rootDir;
        private readonly string kvFilePath;
        private readonly JsonSerializerOptions jsonOptions;
        private readonly SemaphoreSlim gate = new(1, 1);

        private Dictionary<string, JsonElement>? cache;

        public static LocalAppDataStore Init(string companyName, string appName, string? environment = null, string kvFileName = "settings.json")
        {
            lock (initLock)
            {
                instance ??= new LocalAppDataStore(companyName, appName, environment, kvFileName);
                return instance;
            }
        }

        public LocalAppDataStore(string companyName, string appName, string? environment = null, string kvFileName = "settings.json")
        {
            if (string.IsNullOrWhiteSpace(companyName)) throw new ArgumentException("companyName is required.", nameof(companyName));
            if (string.IsNullOrWhiteSpace(appName)) throw new ArgumentException("appName is required.", nameof(appName));
            if (string.IsNullOrWhiteSpace(kvFileName)) throw new ArgumentException("kvFileName is required.", nameof(kvFileName));

            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            rootDir = Path.Combine(baseDir, companyName.Trim(), appName.Trim());
            if (!string.IsNullOrWhiteSpace(environment))
                rootDir = Path.Combine(rootDir, environment.Trim());

            Directory.CreateDirectory(rootDir);

            kvFilePath = Path.Combine(rootDir, kvFileName);

            jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = false
            };
        }

        public string RootDirectory => rootDir;

        // ----------------------------
        // Key/Value JSON Settings
        // ----------------------------

        public T? Get<T>(string key, T? defaultValue = default)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required.", nameof(key));
            gate.Wait();
            try
            {
                EnsureLoaded_NoLock();

                if (cache!.TryGetValue(key, out var elem))
                {
                    try
                    {
                        return elem.Deserialize<T>(jsonOptions);
                    }
                    catch
                    {
                        return defaultValue;
                    }
                }

                return defaultValue;
            }
            finally
            {
                gate.Release();
            }
        }

        public bool TryGet<T>(string key, out T? value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required.", nameof(key));
            gate.Wait();
            try
            {
                EnsureLoaded_NoLock();

                if (cache!.TryGetValue(key, out var elem))
                {
                    try
                    {
                        value = elem.Deserialize<T>(jsonOptions);
                        return true;
                    }
                    catch
                    {
                        value = default;
                        return false;
                    }
                }

                value = default;
                return false;
            }
            finally
            {
                gate.Release();
            }
        }

        public void Set<T>(string key, T value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required.", nameof(key));

            gate.Wait();
            try
            {
                EnsureLoaded_NoLock();
                cache![key] = JsonSerializer.SerializeToElement(value, jsonOptions);
                Save_NoLock();
            }
            finally
            {
                gate.Release();
            }
        }

        public bool Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required.", nameof(key));

            gate.Wait();
            try
            {
                EnsureLoaded_NoLock();
                var removed = cache!.Remove(key);
                if (removed) Save_NoLock();
                return removed;
            }
            finally
            {
                gate.Release();
            }
        }

        public IReadOnlyCollection<string> Keys()
        {
            gate.Wait();
            try
            {
                EnsureLoaded_NoLock();
                return cache!.Keys.ToArray();
            }
            finally
            {
                gate.Release();
            }
        }

        public void Clear()
        {
            gate.Wait();
            try
            {
                cache = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                Save_NoLock();
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<T?> GetAsync<T>(string key, T? defaultValue = default, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required.", nameof(key));
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await EnsureLoadedAsync_NoLock(ct).ConfigureAwait(false);

                if (cache!.TryGetValue(key, out var elem))
                {
                    try
                    {
                        return elem.Deserialize<T>(jsonOptions);
                    }
                    catch
                    {
                        return defaultValue;
                    }
                }

                return defaultValue;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task SetAsync<T>(string key, T value, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required.", nameof(key));

            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await EnsureLoadedAsync_NoLock(ct).ConfigureAwait(false);
                cache![key] = JsonSerializer.SerializeToElement(value, jsonOptions);
                await SaveAsync_NoLock(ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<bool> RemoveAsync(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key is required.", nameof(key));

            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await EnsureLoadedAsync_NoLock(ct).ConfigureAwait(false);
                var removed = cache!.Remove(key);
                if (removed) await SaveAsync_NoLock(ct).ConfigureAwait(false);
                return removed;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task ClearAsync(CancellationToken ct = default)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                cache = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                await SaveAsync_NoLock(ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        // ----------------------------
        // Arbitrary File Storage
        // ----------------------------

        public string GetPath(params string[] relativeParts)
        {
            if (relativeParts is null || relativeParts.Length == 0)
                throw new ArgumentException("relativeParts must contain at least one item.", nameof(relativeParts));

            var sanitized = relativeParts.Select(SanitizeRelativePart).ToArray();
            var combined = sanitized.Aggregate(rootDir, Path.Combine);
            return combined;
        }

        public void EnsureDirectory(params string[] relativeParts)
        {
            var dir = GetPath(relativeParts);
            Directory.CreateDirectory(dir);
        }

        public byte[] ReadBytes(params string[] relativeParts)
        {
            var path = GetPath(relativeParts);
            return File.ReadAllBytes(path);
        }

        public async Task<byte[]> ReadBytesAsync(string[] relativeParts, CancellationToken ct = default)
        {
            var path = GetPath(relativeParts);
            return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }

        public string ReadText(params string[] relativeParts)
        {
            var path = GetPath(relativeParts);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        public async Task<string> ReadTextAsync(string[] relativeParts, CancellationToken ct = default)
        {
            var path = GetPath(relativeParts);
            return await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
        }

        public void WriteBytes(byte[] data, params string[] relativeParts)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            var path = GetPath(relativeParts);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicWriteBytes(path, data);
        }

        public async Task WriteBytesAsync(byte[] data, string[] relativeParts, CancellationToken ct = default)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            var path = GetPath(relativeParts);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await AtomicWriteBytesAsync(path, data, ct).ConfigureAwait(false);
        }

        public void WriteText(string text, params string[] relativeParts)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            var path = GetPath(relativeParts);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AtomicWriteText(path, text);
        }

        public async Task WriteTextAsync(string text, string[] relativeParts, CancellationToken ct = default)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));
            var path = GetPath(relativeParts);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await AtomicWriteTextAsync(path, text, ct).ConfigureAwait(false);
        }

        public bool Exists(params string[] relativeParts)
        {
            var path = GetPath(relativeParts);
            return File.Exists(path) || Directory.Exists(path);
        }

        public void Delete(params string[] relativeParts)
        {
            var path = GetPath(relativeParts);
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }

        // ----------------------------
        // Internals
        // ----------------------------

        private void EnsureLoaded_NoLock()
        {
            if (cache is not null) return;

            cache = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            if (!File.Exists(kvFilePath)) return;

            try
            {
                using var fs = File.OpenRead(kvFilePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(fs, jsonOptions);
                if (dict is not null)
                    cache = new Dictionary<string, JsonElement>(dict, StringComparer.Ordinal);
            }
            catch
            {
                // If settings.json is corrupted, start fresh (don't throw).
                cache = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            }
        }

        private async Task EnsureLoadedAsync_NoLock(CancellationToken ct)
        {
            if (cache is not null) return;

            cache = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            if (!File.Exists(kvFilePath)) return;

            try
            {
                await using var fs = File.Open(kvFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var dict = await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(fs, jsonOptions, ct).ConfigureAwait(false);
                if (dict is not null)
                    cache = new Dictionary<string, JsonElement>(dict, StringComparer.Ordinal);
            }
            catch
            {
                cache = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            }
        }

        private void Save_NoLock()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(kvFilePath)!);
            var json = JsonSerializer.Serialize(cache!, jsonOptions);
            AtomicWriteText(kvFilePath, json);
        }

        private async Task SaveAsync_NoLock(CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(kvFilePath)!);
            var json = JsonSerializer.Serialize(cache!, jsonOptions);
            await AtomicWriteTextAsync(kvFilePath, json, ct).ConfigureAwait(false);
        }

        private static string SanitizeRelativePart(string part)
        {
            if (string.IsNullOrWhiteSpace(part))
                throw new ArgumentException("Path part is empty.", nameof(part));

            var p = part.Replace('\\', Path.DirectorySeparatorChar)
                        .Replace('/', Path.DirectorySeparatorChar);

            // Disallow rooted paths and parent traversal.
            if (Path.IsPathRooted(p))
                throw new ArgumentException("Rooted paths are not allowed.", nameof(part));

            var segments = p.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(s => s == "." || s == ".."))
                throw new ArgumentException("Path traversal is not allowed.", nameof(part));

            // Remove invalid filename chars per segment (best-effort).
            var invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];
                var cleaned = new string(seg.Where(ch => !invalid.Contains(ch)).ToArray());
                if (string.IsNullOrWhiteSpace(cleaned))
                    throw new ArgumentException("Path part became empty after sanitization.", nameof(part));
                segments[i] = cleaned;
            }

            return string.Join(Path.DirectorySeparatorChar, segments);
        }

        private static void AtomicWriteText(string path, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            AtomicWriteBytes(path, bytes);
        }

        private static async Task AtomicWriteTextAsync(string path, string text, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await AtomicWriteBytesAsync(path, bytes, ct).ConfigureAwait(false);
        }

        private static void AtomicWriteBytes(string path, byte[] data)
        {
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);

            var tmp = Path.Combine(dir, Path.GetFileName(path) + ".tmp");
            File.WriteAllBytes(tmp, data);

            // Replace is atomic on NTFS when target exists; if not, move.
            if (File.Exists(path))
            {
                var bak = Path.Combine(dir, Path.GetFileName(path) + ".bak");
                try
                {
                    File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
                    TryDelete(bak);
                }
                catch
                {
                    // Fallback
                    File.Delete(path);
                    File.Move(tmp, path);
                }
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        private static async Task AtomicWriteBytesAsync(string path, byte[] data, CancellationToken ct)
        {
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);

            var tmp = Path.Combine(dir, Path.GetFileName(path) + ".tmp");

            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await fs.WriteAsync(data, 0, data.Length, ct).ConfigureAwait(false);
                await fs.FlushAsync(ct).ConfigureAwait(false);
            }

            if (File.Exists(path))
            {
                var bak = Path.Combine(dir, Path.GetFileName(path) + ".bak");
                try
                {
                    File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
                    TryDelete(bak);
                }
                catch
                {
                    File.Delete(path);
                    File.Move(tmp, path);
                }
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
