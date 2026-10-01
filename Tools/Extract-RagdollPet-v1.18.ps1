param(
    [string]$OutputDirectory = (Join-Path (Get-Location).Path 'RagdollPet-v1.18'),
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.IO;

public sealed class SplitReadStream : Stream
{
    private readonly FileStream[] parts;
    private readonly long[] starts;
    private readonly long length;
    private long position;

    public SplitReadStream(string[] paths)
    {
        if (paths == null || paths.Length == 0) throw new ArgumentException("No archive parts were provided.");
        parts = new FileStream[paths.Length];
        starts = new long[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            starts[i] = length;
            parts[i] = new FileStream(paths[i], FileMode.Open, FileAccess.Read, FileShare.Read);
            length += parts[i].Length;
        }
    }

    public override bool CanRead { get { return true; } }
    public override bool CanSeek { get { return true; } }
    public override bool CanWrite { get { return false; } }
    public override long Length { get { return length; } }
    public override long Position
    {
        get { return position; }
        set { Seek(value, SeekOrigin.Begin); }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer == null) throw new ArgumentNullException("buffer");
        if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException();
        int total = 0;
        while (count > 0 && position < length)
        {
            int index = Array.BinarySearch(starts, position);
            if (index < 0) index = ~index - 1;
            if (index < 0) index = 0;
            long local = position - starts[index];
            parts[index].Position = local;
            int available = (int)Math.Min(count, parts[index].Length - local);
            if (available <= 0) { position = starts[index] + parts[index].Length; continue; }
            int read = parts[index].Read(buffer, offset, available);
            if (read <= 0) break;
            position += read;
            offset += read;
            count -= read;
            total += read;
        }
        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long next;
        switch (origin)
        {
            case SeekOrigin.Begin: next = offset; break;
            case SeekOrigin.Current: next = position + offset; break;
            case SeekOrigin.End: next = length + offset; break;
            default: throw new ArgumentOutOfRangeException("origin");
        }
        if (next < 0) throw new IOException("Cannot seek before the start of the split archive.");
        position = next;
        return position;
    }

    public override void Flush() { }
    public override void SetLength(long value) { throw new NotSupportedException(); }
    public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (FileStream part in parts)
            {
                if (part != null) part.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
'@

$workingDirectory = (Get-Location).Path
$parts = @(Get-ChildItem -LiteralPath $workingDirectory -Filter 'RagdollPet-v1.18-win-x64.zip.part*' -File |
    Sort-Object Name | ForEach-Object { $_.FullName })
if ($parts.Count -eq 0) {
    throw 'No ZIP parts found. Place every .part file beside this script and run it again.'
}

$destination = [IO.Path]::GetFullPath($OutputDirectory)
$destinationPrefix = $destination.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
New-Item -ItemType Directory -Force -Path $destination | Out-Null

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Write-Host "Found $($parts.Count) parts. Extracting directly without creating another full ZIP..." -ForegroundColor Cyan
$archiveStream = [SplitReadStream]::new([string[]]$parts)
$archive = [IO.Compression.ZipArchive]::new($archiveStream, [IO.Compression.ZipArchiveMode]::Read, $false)
try {
    $total = $archive.Entries.Count
    for ($i = 0; $i -lt $total; $i++) {
        $entry = $archive.Entries[$i]
        $relative = $entry.FullName.Replace('/', [IO.Path]::DirectorySeparatorChar)
        $target = [IO.Path]::GetFullPath((Join-Path $destination $relative))
        if (-not $target.StartsWith($destinationPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Archive contains an unsafe path; extraction stopped: $($entry.FullName)"
        }
        if ($entry.FullName.EndsWith('/')) {
            New-Item -ItemType Directory -Force -Path $target | Out-Null
        } else {
            $parent = Split-Path -Parent $target
            New-Item -ItemType Directory -Force -Path $parent | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
        if (($i + 1) % 250 -eq 0 -or $i + 1 -eq $total) {
            Write-Progress -Activity 'Extracting RagdollPet' -Status "$($i + 1) / $total files" -PercentComplete ((($i + 1) / $total) * 100)
        }
    }
} finally {
    $archive.Dispose()
    $archiveStream.Dispose()
}

Write-Host "Extraction complete: $destination" -ForegroundColor Green
if (-not $NoLaunch) {
    $petExeName = ([char]0x56E2).ToString() + ([char]0x56E2) + '2.5D' + ([char]0x684C) + ([char]0x5BA0) + '.exe'
    $petExe = Join-Path $destination $petExeName
    if (-not (Test-Path -LiteralPath $petExe)) { throw "Extraction finished but the pet executable was not found: $petExe" }
    Write-Host 'Starting the pet. Opening its chat card will start the bundled GPT-OSS 20B model.' -ForegroundColor Green
    Start-Process -FilePath $petExe -WorkingDirectory $destination
}
