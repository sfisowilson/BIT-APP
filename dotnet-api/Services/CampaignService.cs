using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    public interface ICampaignService
    {
        Task<IEnumerable<CampaignItem>> GetCampaignsAsync();
        Task<CampaignItem> CreateCampaignAsync(CreateCampaignDto dto);
    }

    public class CampaignService : ICampaignService
    {
        private readonly ICampaignRepository _campaignRepository;

        public CampaignService(ICampaignRepository campaignRepository)
        {
            _campaignRepository = campaignRepository;
        }

        public async Task<IEnumerable<CampaignItem>> GetCampaignsAsync()
        {
            return await _campaignRepository.GetAllAsync();
        }

        public async Task<CampaignItem> CreateCampaignAsync(CreateCampaignDto dto)
        {
            var namingRegex = new Regex(@"^[A-Z0-9]{8}_[A-Z0-9]+$");
            if (!namingRegex.IsMatch(dto.NamingStructureCode))
            {
                throw new ArgumentException("Naming structure violation! Code must match exactly: 8-character scene-code, underscore, brand identifier (e.g. UZ01EP12_COKE).");
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

            return campaign;
        }
    }
}
