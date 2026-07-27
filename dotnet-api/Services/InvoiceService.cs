using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Data;
using Afrobotics.Bit.Api.DTOs;

namespace Afrobotics.Bit.Api.Services;

public class InvoiceService : IInvoiceService
{
    private readonly PostgresDbContext _context;

    public InvoiceService(PostgresDbContext context)
    {
        _context = context;
    }

    public async Task<InvoiceSummaryDto> GenerateCampaignInvoiceAsync(string campaignId)
    {
        var campaign = await _context.Campaigns.FindAsync(campaignId);
        if (campaign == null)
        {
            throw new KeyNotFoundException($"Campaign '{campaignId}' not found.");
        }

        var renders = await _context.Renders
            .Where(r => r.CampaignId == campaignId && r.RenderStatus == "Finished")
            .ToListAsync();

        var adSlots = await _context.AdSlots
            .Where(a => a.CampaignId == campaignId)
            .ToListAsync();

        var lineItems = new List<InvoiceLineItemDto>();
        decimal subtotal = 0m;

        int itemIdx = 1;
        foreach (var render in renders)
        {
            var surface = await _context.SurfaceItems.FindAsync(render.SurfaceId);
            var scene = surface != null ? await _context.SceneItems.FindAsync(surface.SceneId) : null;
            
            double duration = scene?.DurationSeconds ?? 5.0;
            double viability = surface?.ViabilityScore ?? 0.85;
            decimal baseRatePerSec = 150.00m; // ZAR 150 per second base placement rate
            decimal amount = (decimal)(duration * viability) * baseRatePerSec;

            var lineItem = new InvoiceLineItemDto
            {
                Id = $"inv-item-{itemIdx++}",
                Description = $"Virtual Insertion: {surface?.SurfaceType ?? "Surface"} (Scene #{scene?.SceneIndex ?? 1})",
                SurfaceType = surface?.SurfaceType ?? "Surface Placement",
                DurationSeconds = Math.Round(duration, 2),
                ViabilityScore = Math.Round(viability, 2),
                UnitRate = baseRatePerSec,
                Amount = Math.Round(amount, 2)
            };

            lineItems.Add(lineItem);
            subtotal += lineItem.Amount;
        }

        // Add default line item if no completed renders yet so campaign preview invoice is valid
        if (lineItems.Count == 0)
        {
            lineItems.Add(new InvoiceLineItemDto
            {
                Id = "inv-item-1",
                Description = $"Campaign Setup & Surface Booking — {campaign.Name}",
                SurfaceType = "Campaign Inventory Slot",
                DurationSeconds = 10.0,
                ViabilityScore = 0.90,
                UnitRate = 150.00m,
                Amount = 1350.00m
            });
            subtotal = 1350.00m;
        }

        decimal renderFees = renders.Count * 250.00m; // ZAR 250 per GPU render job
        decimal taxRate = 0.15m; // 15% VAT
        decimal taxAmount = Math.Round((subtotal + renderFees) * taxRate, 2);
        decimal totalAmount = subtotal + renderFees + taxAmount;

        return new InvoiceSummaryDto
        {
            InvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMM}-{campaignId[..Math.Min(4, campaignId.Length)]}",
            CampaignId = campaign.Id,
            CampaignName = campaign.Name,
            ClientName = !string.IsNullOrEmpty(campaign.TargetRegion) ? $"{campaign.TargetRegion} Advertiser" : "SADC Media Advertiser",
            InvoiceDate = DateTime.UtcNow,
            LineItems = lineItems,
            Subtotal = Math.Round(subtotal, 2),
            RenderProcessingFees = Math.Round(renderFees, 2),
            TaxAmount = taxAmount,
            TotalAmount = Math.Round(totalAmount, 2),
            Currency = "ZAR"
        };
    }
}
