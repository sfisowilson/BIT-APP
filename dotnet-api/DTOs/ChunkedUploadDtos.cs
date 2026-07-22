using System;
using System.Collections.Generic;

namespace Afrobotics.Bit.Api.DTOs
{
    /// <summary>Initializes a chunked upload session for large broadcast files.</summary>
    public class ChunkedUploadInitDto
    {
        public string FileName { get; set; } = string.Empty;
        public int TotalChunks { get; set; }
        public long ChunkSize { get; set; }
        public long TotalSize { get; set; }
    }

    /// <summary>Completes a chunked upload and registers the video.</summary>
    public class ChunkedUploadCompleteDto
    {
        public string UploadId { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? SourceChannel { get; set; }
        public string? CampaignId { get; set; }
    }
}

namespace Afrobotics.Bit.Api.Models
{
    /// <summary>Tracks the state of an in-progress chunked upload.</summary>
    public class ChunkedUploadSession
    {
        public string UploadId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int TotalChunks { get; set; }
        public long ChunkSize { get; set; }
        public long TotalSize { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivity { get; set; }
        public HashSet<int> UploadedChunks { get; set; } = new();
    }
}
