using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;

namespace Afrobotics.Bit.Api.Services;

public interface IInvoiceService
{
    Task<InvoiceSummaryDto> GenerateCampaignInvoiceAsync(string campaignId);
}
