using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Orchestra.Backend.Controllers;

// ── Transfer state kept in memory (backend-side) ─────────────────────────

public class ChunkedUploadState
{
    public string TransferId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string RemotePath { get; set; } = "";
    public long TotalSize { get; set; }
    public int TotalChunks { get; set; }
    public HashSet<int> ReceivedChunks { get; set; } = new();
    public string TempDir { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public bool Cancelled { get; set; }
    public string? ExpectedHash { get; set; } // SHA256 hex from client
}

[ApiController]
[Authorize]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, ChunkedUploadState> _uploads = new();
    private readonly string _dataDir;
    private readonly ILogger<FilesController> _logger;
    private const int CHUNK_EXPIRY_MINUTES = 60;

    public FilesController(IConfiguration configuration, ILogger<FilesController> logger)
    {
        _dataDir = configuration["MudoSoft:DataDirectory"] ?? "C:\\MudoSoft\\data";
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  LEGACY SIMPLE PUSH — kept for backward compat
    // ═══════════════════════════════════════════════════════════════════

    [HttpPost("push/{deviceId}")]
    public async Task<IActionResult> Push(string deviceId, IFormFile file, [FromForm] string remotePath)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || !IsValidDeviceId(deviceId))
            return BadRequest(new { error = "Invalid device ID" });

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided" });

        var safeFileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName.Contains(".."))
            return BadRequest(new { error = "Invalid filename" });

        var uploads = Path.Combine(_dataDir, "uploads");
        Directory.CreateDirectory(uploads);

        var filePath = Path.Combine(uploads, $"{deviceId}_{safeFileName}");
        var fullPath = Path.GetFullPath(filePath);
        if (!fullPath.StartsWith(Path.GetFullPath(uploads), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Path traversal attempt: {FileName}", file.FileName);
            return BadRequest(new { error = "Invalid file path" });
        }

        await using var stream = System.IO.File.Create(filePath);
        await file.CopyToAsync(stream);

        _logger.LogInformation("File uploaded (legacy): {FilePath}", filePath);
        return Ok(new { message = "File received", path = filePath, remotePath });
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CHUNKED UPLOAD — Init / Chunk / Complete / Cancel / Status
    // ═══════════════════════════════════════════════════════════════════

    /// POST /api/files/upload/init
    /// Body: { deviceId, fileName, remotePath, totalSize, totalChunks, expectedHash? }
    [HttpPost("upload/init")]
    public IActionResult InitUpload([FromBody] InitUploadRequest req)
    {
        if (!IsValidDeviceId(req.DeviceId))
            return BadRequest(new { error = "Invalid device ID" });

        var safeFileName = Path.GetFileName(req.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName.Contains(".."))
            return BadRequest(new { error = "Invalid filename" });

        // Expire old transfers
        PurgeExpiredTransfers();

        var transferId = Guid.NewGuid().ToString("N");
        var tempDir = Path.Combine(_dataDir, "upload_tmp", transferId);
        Directory.CreateDirectory(tempDir);

        var state = new ChunkedUploadState
        {
            TransferId = transferId,
            DeviceId = req.DeviceId,
            OriginalFileName = safeFileName,
            RemotePath = req.RemotePath,
            TotalSize = req.TotalSize,
            TotalChunks = req.TotalChunks,
            TempDir = tempDir,
            ExpectedHash = req.ExpectedHash
        };

        _uploads[transferId] = state;
        _logger.LogInformation("Upload init: {TransferId} device={DeviceId} file={File} chunks={Chunks}",
            transferId, req.DeviceId, safeFileName, req.TotalChunks);

        return Ok(new { transferId, chunkSize = 1024 * 1024 }); // 1MB chunks
    }

    /// POST /api/files/upload/chunk/{transferId}?chunkIndex=N
    [HttpPost("upload/chunk/{transferId}")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadChunk(string transferId, [FromQuery] int chunkIndex, IFormFile chunk)
    {
        if (!_uploads.TryGetValue(transferId, out var state))
            return NotFound(new { error = "Transfer not found or expired" });

        if (state.Cancelled)
            return BadRequest(new { error = "Transfer cancelled" });

        if (chunkIndex < 0 || chunkIndex >= state.TotalChunks)
            return BadRequest(new { error = $"Invalid chunk index {chunkIndex}" });

        if (chunk == null || chunk.Length == 0)
            return BadRequest(new { error = "Empty chunk" });

        var chunkPath = Path.Combine(state.TempDir, $"chunk_{chunkIndex:D6}");
        await using var stream = System.IO.File.Create(chunkPath);
        await chunk.CopyToAsync(stream);

        lock (state.ReceivedChunks)
        {
            state.ReceivedChunks.Add(chunkIndex);
        }
        state.LastActivityAt = DateTime.UtcNow;

        var received = state.ReceivedChunks.Count;
        _logger.LogDebug("Chunk {Index}/{Total} received for {TransferId}", chunkIndex + 1, state.TotalChunks, transferId);

        return Ok(new
        {
            chunkIndex,
            received,
            total = state.TotalChunks,
            percentComplete = (int)(received * 100.0 / state.TotalChunks)
        });
    }

    /// POST /api/files/upload/complete/{transferId}
    [HttpPost("upload/complete/{transferId}")]
    public async Task<IActionResult> CompleteUpload(string transferId)
    {
        if (!_uploads.TryGetValue(transferId, out var state))
            return NotFound(new { error = "Transfer not found" });

        if (state.Cancelled)
            return BadRequest(new { error = "Transfer was cancelled" });

        // Verify all chunks received
        var missing = Enumerable.Range(0, state.TotalChunks)
            .Except(state.ReceivedChunks)
            .ToList();

        if (missing.Count > 0)
            return BadRequest(new { error = $"Missing chunks: {string.Join(",", missing.Take(10))}" });

        // Assemble file
        var outputDir = Path.Combine(_dataDir, "uploads");
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, $"{state.DeviceId}_{state.OriginalFileName}");

        try
        {
            await using var outStream = System.IO.File.Create(outputPath);
            for (int i = 0; i < state.TotalChunks; i++)
            {
                var chunkPath = Path.Combine(state.TempDir, $"chunk_{i:D6}");
                await using var chunkStream = System.IO.File.OpenRead(chunkPath);
                await chunkStream.CopyToAsync(outStream);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assemble upload {TransferId}", transferId);
            return StatusCode(500, new { error = "Assembly failed" });
        }

        // Hash verification
        if (!string.IsNullOrEmpty(state.ExpectedHash))
        {
            var actualHash = await ComputeSha256Async(outputPath);
            if (!string.Equals(actualHash, state.ExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                System.IO.File.Delete(outputPath);
                _uploads.TryRemove(transferId, out _);
                CleanupTempDir(state.TempDir);
                return BadRequest(new { error = "Hash mismatch — corrupted transfer", expected = state.ExpectedHash, actual = actualHash });
            }
        }

        // Cleanup temp chunks
        CleanupTempDir(state.TempDir);
        _uploads.TryRemove(transferId, out _);

        _logger.LogInformation("Upload complete: {TransferId} → {Path}", transferId, outputPath);
        return Ok(new
        {
            message = "Upload complete",
            path = outputPath,
            remotePath = state.RemotePath,
            fileName = state.OriginalFileName,
            deviceId = state.DeviceId
        });
    }

    /// GET /api/files/upload/{transferId}/status
    [HttpGet("upload/{transferId}/status")]
    public IActionResult GetUploadStatus(string transferId)
    {
        if (!_uploads.TryGetValue(transferId, out var state))
            return NotFound(new { error = "Transfer not found" });

        var received = state.ReceivedChunks.Count;
        return Ok(new
        {
            transferId,
            received,
            total = state.TotalChunks,
            percentComplete = (int)(received * 100.0 / state.TotalChunks),
            cancelled = state.Cancelled,
            fileName = state.OriginalFileName
        });
    }

    /// DELETE /api/files/upload/{transferId}
    [HttpDelete("upload/{transferId}")]
    public IActionResult CancelUpload(string transferId)
    {
        if (!_uploads.TryGetValue(transferId, out var state))
            return NotFound(new { error = "Transfer not found" });

        state.Cancelled = true;
        CleanupTempDir(state.TempDir);
        _uploads.TryRemove(transferId, out _);

        _logger.LogInformation("Upload cancelled: {TransferId}", transferId);
        return Ok(new { message = "Transfer cancelled" });
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FILE EXPLORER — list directory on backend staging area
    // ═══════════════════════════════════════════════════════════════════

    /// GET /api/files/explorer?path=uploads
    [HttpGet("explorer")]
    public IActionResult ListDirectory([FromQuery] string path = "uploads")
    {
        var safePath = SanitizeLocalPath(path);
        if (safePath == null)
            return BadRequest(new { error = "Invalid path" });

        if (!Directory.Exists(safePath))
            return Ok(new { path = safePath, entries = Array.Empty<object>() });

        var entries = Directory.EnumerateFileSystemEntries(safePath)
            .Select(entry =>
            {
                var isDir = Directory.Exists(entry);
                var info = isDir
                    ? (object)new { name = Path.GetFileName(entry), type = "directory", size = (long?)null, modifiedAt = Directory.GetLastWriteTimeUtc(entry) }
                    : new { name = Path.GetFileName(entry), type = "file", size = (long?)new FileInfo(entry).Length, modifiedAt = System.IO.File.GetLastWriteTimeUtc(entry) };
                return info;
            })
            .ToList();

        return Ok(new { path = safePath, entries });
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private string? SanitizeLocalPath(string relativePath)
    {
        var combined = Path.GetFullPath(Path.Combine(_dataDir, relativePath));
        var root = Path.GetFullPath(_dataDir);
        return combined.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? combined : null;
    }

    private static bool IsValidDeviceId(string deviceId) =>
        System.Text.RegularExpressions.Regex.IsMatch(deviceId, @"^[a-zA-Z0-9\-_]+$");

    private static async Task<string> ComputeSha256Async(string filePath)
    {
        await using var stream = System.IO.File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void CleanupTempDir(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch { /* best-effort */ }
    }

    private static void PurgeExpiredTransfers()
    {
        var expiry = DateTime.UtcNow.AddMinutes(-CHUNK_EXPIRY_MINUTES);
        foreach (var (id, state) in _uploads)
        {
            if (state.LastActivityAt < expiry)
            {
                _uploads.TryRemove(id, out _);
                CleanupTempDir(state.TempDir);
            }
        }
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────

public class InitUploadRequest
{
    public string DeviceId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string RemotePath { get; set; } = "";
    public long TotalSize { get; set; }
    public int TotalChunks { get; set; }
    public string? ExpectedHash { get; set; }
}
