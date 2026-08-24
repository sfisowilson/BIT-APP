using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    public interface ICampaignService
    {
        Task<PaginatedResult<CampaignItem>> GetCampaignsAsync(CampaignFilterParams filter);
        Task<CampaignItem?> GetCampaignByIdAsync(string id);
        Task<CampaignItem> CreateCampaignAsync(CreateCampaignDto dto);
        Task<CampaignItem?> UpdateCampaignAsync(string id, UpdateCampaignDto dto);
        Task<bool> DeleteCampaignAsync(string id);
    }

    public class CampaignService : ICampaignService
    {
        private readonly ICampaignRepository _campaignRepository;
        private readonly IEmailService _email;
        private readonly IConfiguration _config;

        public CampaignService(ICampaignRepository campaignRepository, IEmailService email, IConfiguration config)
        {
            _campaignRepository = campaignRepository;
            _email = email;
            _config = config;
        }

        public async Task<PaginatedResult<CampaignItem>> GetCampaignsAsync(CampaignFilterParams filter)
        {
            var query = _campaignRepository.GetAllQueryable();

            if (!string.IsNullOrEmpty(filter.Status))
                query = query.Where(c => c.Status == filter.Status);
            if (!string.IsNullOrEmpty(filter.Search))
                query = query.Where(c => c.Name.Contains(filter.Search));

            if (!string.IsNullOrEmpty(filter.SortBy))
                query = query.ApplySort(filter.SortBy, filter.SortDescending);
            else
                query = query.OrderByDescending(c => c.CreatedAt);

            return await query.ToPaginatedResultAsync(filter.Page, filter.PageSize);
        }

        public async Task<CampaignItem?> GetCampaignByIdAsync(string id)
        {
            return await _campaignRepository.GetByIdAsync(id);
        }

        public async Task<CampaignItem> CreateCampaignAsync(CreateCampaignDto dto)
        {
            // Naming code, region, and budget are optional now (deprioritized per client request) —
            // only validate the naming code's format if one was actually supplied, matching how
            // UpdateCampaignAsync already treats it as optional-but-validated-when-present.
            if (!string.IsNullOrEmpty(dto.NamingStructureCode))
            {
                var namingRegex = new Regex(@"^[A-Z0-9]{8,10}_[A-Z0-9]+$");
                if (!namingRegex.IsMatch(dto.NamingStructureCode))
                {
                    throw new ArgumentException("Naming structure violation! Code must match: 8- or 10-character scene-code, underscore, brand identifier (e.g. UZ01EP12_COKE or GEN23EP100_UNIL).");
                }
            }

            var campaign = new CampaignItem
            {
                Id = "c-" + Guid.NewGuid().ToString().Substring(0, 4),
                Name = dto.Name,
                NamingStructureCode = dto.NamingStructureCode,
                ScheduleStart = DateTime.UtcNow,
                ScheduleEnd = DateTime.UtcNow.AddMonths(2),
                TargetRegion = dto.TargetRegion,
                TotalBudget = dto.TotalBudget,
                Status = "Draft",
                CreatedAt = DateTime.UtcNow
            };

            await _campaignRepository.AddAsync(campaign);
            await _campaignRepository.SaveChangesAsync();

            var detailsLine = string.Join(" | ", new[]
            {
                !string.IsNullOrEmpty(campaign.NamingStructureCode) ? $"Code: {campaign.NamingStructureCode}" : null,
                !string.IsNullOrEmpty(campaign.TargetRegion) ? $"Region: {campaign.TargetRegion}" : null,
                campaign.TotalBudget.HasValue ? $"Budget: {campaign.TotalBudget:C}" : null,
            }.Where(s => s != null));

            _email.Enqueue(_config?["Smtp:FromEmail"] ?? "noreply@afrobotics.co.za",
                $"Campaign Created — {campaign.Name}",
                $"Campaign '{campaign.Name}' has been created." + (detailsLine.Length > 0 ? $"\n\n{detailsLine}" : ""),
                "CampaignCreated");

            return campaign;
        }

        public async Task<CampaignItem?> UpdateCampaignAsync(string id, UpdateCampaignDto dto)
        {
            var campaign = await _campaignRepository.GetByIdAsync(id);
            if (campaign == null) return null;

            // Only update fields that are provided (non-null for value types: check HasValue)
            if (dto.Name != null)
                campaign.Name = dto.Name;

            if (dto.NamingStructureCode != null)
            {
                var namingRegex = new Regex(@"^[A-Z0-9]{8,10}_[A-Z0-9]+$");
                if (!namingRegex.IsMatch(dto.NamingStructureCode))
                {
                    throw new ArgumentException("Naming structure violation! Code must match: 8- or 10-character scene-code, underscore, brand identifier (e.g. UZ01EP12_COKE or GEN23EP100_UNIL).");
                }
                campaign.NamingStructureCode = dto.NamingStructureCode;
            }

            if (dto.TargetRegion != null)
                campaign.TargetRegion = dto.TargetRegion;

            if (dto.TotalBudget.HasValue)
                campaign.TotalBudget = dto.TotalBudget.Value;

            if (dto.Status != null)
            {
                var validStatuses = new[] { "Draft", "Active", "Completed", "Paused" };
                if (!validStatuses.Contains(dto.Status))
                {
                    throw new ArgumentException($"Invalid status '{dto.Status}'. Must be one of: {string.Join(", ", validStatuses)}");
                }
                campaign.Status = dto.Status;
            }

            await _campaignRepository.UpdateAsync(campaign);
            await _campaignRepository.SaveChangesAsync();

            return campaign;
        }

        public async Task<bool> DeleteCampaignAsync(string id)
        {
            var campaign = await _campaignRepository.GetByIdAsync(id);
            if (campaign == null) return false;

            await _campaignRepository.DeleteAsync(campaign);
            await _campaignRepository.SaveChangesAsync();
            return true;
        }
    }
}
