import React, { useState, useEffect } from 'react';
import { FileText, Loader2, AlertTriangle, RefreshCw } from 'lucide-react';
import { fetchCampaignInvoice } from '../apiClient';
import type { InvoiceSummary } from '../types';

interface InvoicePanelProps {
  campaignId: string;
}

/**
 * Real, backend-calculated campaign invoice — GET /api/campaigns/{id}/invoice. Computed as
 * exposure seconds × viability multiplier + render processing costs + VAT, one line item per
 * Finished render (see dotnet-api/Services/InvoiceService.cs). Never modifies any pipeline state —
 * purely a read/report view.
 */
export const InvoicePanel: React.FC<InvoicePanelProps> = ({ campaignId }) => {
  const [invoice, setInvoice] = useState<InvoiceSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadInvoice = async () => {
    setLoading(true);
    setError('');
    try {
      const data = await fetchCampaignInvoice(campaignId);
      setInvoice(data);
    } catch (err: any) {
      setError(err.message || 'Failed to load invoice.');
      setInvoice(null);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { loadInvoice(); }, [campaignId]); // eslint-disable-line react-hooks/exhaustive-deps

  const formatCurrency = (amount: number, currency: string) =>
    `${currency} ${amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

  if (loading) {
    return (
      <div className="bg-white border border-slate-200 rounded-2xl p-10 shadow-sm flex flex-col items-center justify-center text-slate-400">
        <Loader2 className="h-6 w-6 animate-spin mb-2" />
        <span className="text-xs font-mono">Calculating invoice…</span>
      </div>
    );
  }

  if (error || !invoice) {
    return (
      <div className="bg-white border border-slate-200 rounded-2xl p-10 shadow-sm flex flex-col items-center justify-center text-center">
        <AlertTriangle className="h-6 w-6 text-red-400 mb-2" />
        <p className="text-xs text-red-600 font-semibold mb-3">{error || 'Failed to load invoice.'}</p>
        <button
          type="button"
          onClick={loadInvoice}
          className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-slate-100 hover:bg-slate-200 text-slate-600 font-semibold text-[11px] rounded-lg cursor-pointer transition-colors"
        >
          <RefreshCw className="h-3 w-3" /> Retry
        </button>
      </div>
    );
  }

  return (
    <div className="bg-white border border-slate-200 rounded-2xl p-6 shadow-sm">
      <div className="flex items-start justify-between border-b border-slate-100 pb-4 mb-4">
        <div className="flex items-center gap-2.5">
          <div className="h-9 w-9 rounded-lg bg-blue-50 flex items-center justify-center text-blue-600 shrink-0">
            <FileText className="h-4 w-4" />
          </div>
          <div>
            <h3 className="text-sm font-bold text-slate-800 font-display">{invoice.invoiceNumber}</h3>
            <p className="text-[11px] text-slate-400">
              {invoice.clientName} · {new Date(invoice.invoiceDate).toLocaleDateString()}
            </p>
          </div>
        </div>
        <button
          type="button"
          onClick={loadInvoice}
          title="Recalculate"
          className="inline-flex items-center gap-1 px-2 py-1 text-[10px] font-bold text-slate-500 hover:text-slate-700 cursor-pointer transition-colors"
        >
          <RefreshCw className="h-3 w-3" /> Refresh
        </button>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full text-xs">
          <thead>
            <tr className="text-left text-[10px] uppercase tracking-wider text-slate-400 font-mono">
              <th className="pb-2 font-medium">Description</th>
              <th className="pb-2 font-medium text-right">Duration</th>
              <th className="pb-2 font-medium text-right">Viability</th>
              <th className="pb-2 font-medium text-right">Rate</th>
              <th className="pb-2 font-medium text-right">Amount</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {invoice.lineItems.map(item => (
              <tr key={item.id}>
                <td className="py-2 text-slate-700 font-medium">{item.description}</td>
                <td className="py-2 text-right text-slate-500 font-mono">{item.durationSeconds.toFixed(1)}s</td>
                <td className="py-2 text-right text-slate-500 font-mono">{(item.viabilityScore * 100).toFixed(0)}%</td>
                <td className="py-2 text-right text-slate-500 font-mono">{formatCurrency(item.unitRate, invoice.currency)}</td>
                <td className="py-2 text-right text-slate-800 font-bold font-mono">{formatCurrency(item.amount, invoice.currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="mt-4 pt-4 border-t border-slate-100 space-y-1.5 max-w-xs ml-auto">
        <div className="flex justify-between text-xs text-slate-500">
          <span>Subtotal</span>
          <span className="font-mono">{formatCurrency(invoice.subtotal, invoice.currency)}</span>
        </div>
        <div className="flex justify-between text-xs text-slate-500">
          <span>Render Processing Fees</span>
          <span className="font-mono">{formatCurrency(invoice.renderProcessingFees, invoice.currency)}</span>
        </div>
        <div className="flex justify-between text-xs text-slate-500">
          <span>VAT / Tax</span>
          <span className="font-mono">{formatCurrency(invoice.taxAmount, invoice.currency)}</span>
        </div>
        <div className="flex justify-between text-sm font-bold text-slate-800 pt-1.5 border-t border-slate-100">
          <span>Total</span>
          <span className="font-mono">{formatCurrency(invoice.totalAmount, invoice.currency)}</span>
        </div>
      </div>
    </div>
  );
};
