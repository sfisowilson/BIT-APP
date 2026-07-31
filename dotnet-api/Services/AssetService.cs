using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    public interface IAssetService
    {
        Task<PaginatedResult<CreativeAsset>> GetAssetsAsync(AssetFilterParams filter);
        Task<IEnumerable<CreativeAsset>> GetAssetsByCampaignAsync(string campaignId);
        Task<CreativeAsset> CreateAssetAsync(CreateAssetDto dto);
        Task<CreativeAsset> CreateAssetWithFileAsync(CreateAssetDto dto, string storageKey, string fileSize, string dimensions);
        Task<CreativeAsset?> UpdateAssetAsync(string id, UpdateAssetDto dto);
        Task<bool> AssociateAssetWithCampaignAsync(string assetId, string campaignId);
        Task<bool> UnassociateAssetAsync(string assetId);
        Task<bool> DeleteAssetAsync(string id);
    }

    public class AssetService : IAssetService
    {
        private readonly IAssetRepository _assetRepository;
        private readonly ICampaignRepository _campaignRepository;

        public AssetService(IAssetRepository assetRepository, ICampaignRepository campaignRepository)
        {
            _assetRepository = assetRepository;
            _campaignRepository = campaignRepository;
        }

        public async Task<PaginatedResult<CreativeAsset>> GetAssetsAsync(AssetFilterParams filter)
        {
            var query = _assetRepository.GetAllQueryable();

            if (!string.IsNullOrEmpty(filter.Type))
                query = query.Where(a => a.Type == filter.Type);
            if (!string.IsNullOrEmpty(filter.BrandCategory))
                query = query.Where(a => a.BrandCategory == filter.BrandCategory);
            if (!string.IsNullOrEmpty(filter.CampaignId))
                query = query.Where(a => a.CampaignId == filter.CampaignId);
            else if (filter.Unassigned == true)
                query = query.Where(a => a.CampaignId == null);
            if (!string.IsNullOrEmpty(filter.Search))
                query = query.Where(a => a.Name.Contains(filter.Search));

            if (!string.IsNullOrEmpty(filter.SortBy))
                query = query.ApplySort(filter.SortBy, filter.SortDescending);
            else
                query = query.OrderByDescending(a => a.CreatedAt);

            return await query.ToPaginatedResultAsync(filter.Page, filter.PageSize);
        }

        public async Task<IEnumerable<CreativeAsset>> GetAssetsByCampaignAsync(string campaignId)
        {
            return await _assetRepository.GetByCampaignIdAsync(campaignId);
        }

        public async Task<CreativeAsset> CreateAssetAsync(CreateAssetDto dto)
        {
            var asset = new CreativeAsset
            {
                Id = "as-" + Guid.NewGuid().ToString().Substring(0, 4),
                Name = dto.Name,
                Type = dto.Type,
                StorageKey = "s3://afrobotics-assets/" + dto.Name.Replace(" ", "_").ToLower(),
                FileSize = "0 MB",
                Dimensions = "1920x1080",
                BrandCategory = dto.BrandCategory,
                CampaignId = dto.CampaignId
            };

            await _assetRepository.AddAsync(asset);
            await _assetRepository.SaveChangesAsync();

            return asset;
        }

        public async Task<CreativeAsset> CreateAssetWithFileAsync(CreateAssetDto dto, string storageKey, string fileSize, string dimensions)
        {
            var asset = new CreativeAsset
            {
                Id = "as-" + Guid.NewGuid().ToString().Substring(0, 4),
                Name = dto.Name,
                Type = dto.Type,
                StorageKey = storageKey,
                FileSize = fileSize,
                Dimensions = dimensions,
                BrandCategory = dto.BrandCategory,
                CampaignId = dto.CampaignId
            };

            await _assetRepository.AddAsync(asset);
            await _assetRepository.SaveChangesAsync();

            return asset;
        }

        public async Task<CreativeAsset?> UpdateAssetAsync(string id, UpdateAssetDto dto)
        {
            var asset = await _assetRepository.GetByIdAsync(id);
            if (asset == null) return null;

            if (dto.Name != null) asset.Name = dto.Name;
            if (dto.Type != null) asset.Type = dto.Type;
            if (dto.BrandCategory != null) asset.BrandCategory = dto.BrandCategory;
            if (dto.CampaignId != null) asset.CampaignId = dto.CampaignId;

            await _assetRepository.SaveChangesAsync();
            return asset;
        }

        public async Task<bool> AssociateAssetWithCampaignAsync(string assetId, string campaignId)
        {
            var asset = await _assetRepository.GetByIdAsync(assetId);
            if (asset == null) return false;

            var campaign = await _campaignRepository.GetByIdAsync(campaignId);
            if (campaign == null) return false;

            asset.CampaignId = campaignId;
            await _assetRepository.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UnassociateAssetAsync(string assetId)
        {
            var asset = await _assetRepository.GetByIdAsync(assetId);
            if (asset == null) return false;

            asset.CampaignId = null;
            await _assetRepository.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteAssetAsync(string id)
        {
            var asset = await _assetRepository.GetByIdAsync(id);
            if (asset == null) return false;

            await _assetRepository.DeleteAsync(asset);
            await _assetRepository.SaveChangesAsync();
            return true;
        }
    }
}
