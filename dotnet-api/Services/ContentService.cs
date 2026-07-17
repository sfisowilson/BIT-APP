using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    public interface IContentService
    {
        Task<IEnumerable<ContentItem>> GetContentAsync();
        Task<ContentItem> IngestVideoAsync(IngestVideoDto dto);
        Task<IEnumerable<SceneItem>> GetScenesAsync(string contentId);
    }

    public class ContentService : IContentService
    {
        private readonly IContentRepository _contentRepository;

        public ContentService(IContentRepository contentRepository)
        {
            _contentRepository = contentRepository;
        }

        public async Task<IEnumerable<ContentItem>> GetContentAsync()
        {
            return await _contentRepository.GetAllAsync();
        }

        public async Task<ContentItem> IngestVideoAsync(IngestVideoDto dto)
        {
            if (string.IsNullOrEmpty(dto.Title) || string.IsNullOrEmpty(dto.StorageKey))
            {
                throw new ArgumentException("Missing mandatory parameters: Title and StorageKey are required.");
            }

            var content = new ContentItem
            {
                Id = "v-" + Guid.NewGuid().ToString().Substring(0, 4),
                Title = dto.Title,
                Duration = "00:05:00", // Fixed duration for simulation
                Resolution = "1920x1080 (1080p)",
                FrameRate = 50,
                SourceChannel = dto.SourceChannel ?? "Manual Upload",
                StorageKey = dto.StorageKey,
                IngestionStatus = "Staging",
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
    }
}
