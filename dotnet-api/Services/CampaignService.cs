using System;
using System.Collections.Generic;
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
            // Per MReq 1: scene-code is 8 or 10 uppercase alphanumeric chars, underscore, brand identifier
            var namingRegex = new Regex(@"^[A-Z0-9]{8,10}_[A-Z0-9]+$");
            if (!namingRegex.IsMatch(dto.NamingStructureCode))
            {
                throw new ArgumentException("Naming structure violation! Code must match: 8- or 10-character scene-code, underscore, brand identifier (e.g. UZ01EP12_COKE or GEN23EP100_UNIL).");
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

            _email.Enqueue(_config["Smtp:FromEmail"] ?? "noreply@afrobotics.co.za",
                $"Campaign Created — {campaign.Name}",
                $"Campaign '{campaign.Name}' ({campaign.NamingStructureCode}) has been created.\n\nRegion: {campaign.TargetRegion}\nBudget: {campaign.TotalBudget:C}",
                "CampaignCreated");

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
