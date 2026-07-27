using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Api.Controllers;

[ApiController]
[Route("api/campaigns")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _invoiceService;

    public InvoicesController(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    [HttpGet("{campaignId}/invoice")]
    public async Task<ActionResult<InvoiceSummaryDto>> GetCampaignInvoice(string campaignId)
    {
        try
        {
            var invoice = await _invoiceService.GenerateCampaignInvoiceAsync(campaignId);
            return Ok(invoice);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
