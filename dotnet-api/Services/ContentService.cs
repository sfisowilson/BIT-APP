using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    public interface IContentService
    {
        Task<IEnumerable<ContentItem>> GetContentAsync(string? campaignId = null);
        Task<ContentItem> IngestVideoAsync(IngestVideoDto dto);
        Task<IEnumerable<SceneItem>> GetScenesAsync(string contentId);
        Task<bool> DeleteContentAsync(string id);
    }

    public class ContentService : IContentService
    {
        private static readonly string[] AcceptedFormats = { ".mp4", ".mov", ".mxf", ".avi" };
        private static readonly Regex DurationRegex = new(@"^(\d{2}):([0-5]\d):([0-5]\d)$", RegexOptions.Compiled);
        private static readonly int[] AcceptedFrameRates = { 24, 25, 30, 50, 60 };

        private readonly IContentRepository _contentRepository;

        public ContentService(IContentRepository contentRepository)
        {
            _contentRepository = contentRepository;
        }

        public async Task<IEnumerable<ContentItem>> GetContentAsync(string? campaignId = null)
        {
            var all = await _contentRepository.GetAllAsync();
            if (!string.IsNullOrEmpty(campaignId))
                return all.Where(c => c.CampaignId == campaignId);
            return all;
        }

        public async Task<ContentItem> IngestVideoAsync(IngestVideoDto dto)
        {
            // MReq 1: Validate title
            if (string.IsNullOrWhiteSpace(dto.Title))
                throw new ArgumentException("Title is required.");

            // MReq 1: Validate duration as perfect HH:MM:SS
            if (!DurationRegex.IsMatch(dto.Duration))
                throw new ArgumentException("Duration must be in HH:MM:SS format (e.g. 00:05:00).");

            // MReq 1: Validate frame rate is a broadcast standard
            if (!AcceptedFrameRates.Contains(dto.FrameRate))
                throw new ArgumentException($"Frame rate must be a broadcast standard: {string.Join(", ", AcceptedFrameRates)} FPS.");

            // Generate storage key from title if not provided
            var storageKey = !string.IsNullOrWhiteSpace(dto.StorageKey)
                ? dto.StorageKey
                : $"s3://afrobotics-raw-ingest/{dto.Title.Replace(" ", "_").ToLower()}_{DateTime.UtcNow:yyyyMMdd}.mov";

            var content = new ContentItem
            {
                Id = "v-" + Guid.NewGuid().ToString().Substring(0, 4),
                Title = dto.Title.Trim(),
                Duration = dto.Duration,
                Resolution = dto.Resolution,
                FrameRate = dto.FrameRate,
                SourceChannel = dto.SourceChannel,
                StorageKey = storageKey,
                IngestionStatus = "Staging",
                CampaignId = dto.CampaignId,
                CreatedAt = DateTime.UtcNow
            };

            await _contentRepository.AddAsync(content);
            await _contentRepository.SaveChangesAsync();

            return content;
        }

        public async Task<IEnumerable<SceneItem>> GetScenesAsync(string contentId)
        {
            return await _contentRepository.GetScenesByContentIdAsync(contentId);
        }

        public async Task<bool> DeleteContentAsync(string id)
        {
            var content = await _contentRepository.GetByIdAsync(id);
            if (content == null) return false;

            await _contentRepository.DeleteAsync(content);
            await _contentRepository.SaveChangesAsync();
            return true;
        }
    }
}
