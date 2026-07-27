using System;
using System.Collections.Generic;

namespace Afrobotics.Bit.Api.DTOs;

public class InvoiceLineItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SurfaceType { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public double ViabilityScore { get; set; }
    public decimal UnitRate { get; set; }
    public decimal Amount { get; set; }
}

public class InvoiceSummaryDto
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public string CampaignId { get; set; } = string.Empty;
    public string CampaignName { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public List<InvoiceLineItemDto> LineItems { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal RenderProcessingFees { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "ZAR";
}
