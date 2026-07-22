import React from 'react';
import { ChevronLeft, ChevronRight } from 'lucide-react';

interface PaginationProps {
  page: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
  onPageChange: (page: number) => void;
}

/**
 * Reusable pagination control with Previous/Next buttons and numbered pages.
 * Shows ellipsis for large page ranges.
 */
export const Pagination: React.FC<PaginationProps> = ({
  page,
  totalPages,
  hasPreviousPage,
  hasNextPage,
  onPageChange,
}) => {
  if (totalPages <= 1) return null;

  const getPageNumbers = (): (number | 'ellipsis')[] => {
    const pages: (number | 'ellipsis')[] = [];
    const maxVisible = 5;

    if (totalPages <= maxVisible + 2) {
      for (let i = 1; i <= totalPages; i++) pages.push(i);
    } else {
      pages.push(1);
      let start = Math.max(2, page - 1);
      let end = Math.min(totalPages - 1, page + 1);

      if (page <= 3) {
        start = 2;
        end = Math.min(maxVisible, totalPages - 1);
      } else if (page >= totalPages - 2) {
        start = Math.max(2, totalPages - maxVisible + 1);
        end = totalPages - 1;
      }

      if (start > 2) pages.push('ellipsis');
      for (let i = start; i <= end; i++) pages.push(i);
      if (end < totalPages - 1) pages.push('ellipsis');
      pages.push(totalPages);
    }
    return pages;
  };

  const pageNumbers = getPageNumbers();

  return (
    <div className="flex items-center justify-center gap-1 mt-4">
      <button
        onClick={() => onPageChange(page - 1)}
        disabled={!hasPreviousPage}
        className={`p-1.5 rounded-lg border transition-colors ${
          hasPreviousPage
            ? 'border-slate-200 hover:bg-slate-100 text-slate-600 cursor-pointer'
            : 'border-slate-100 text-slate-300 cursor-not-allowed'
        }`}
        aria-label="Previous page"
      >
        <ChevronLeft className="h-4 w-4" />
      </button>

      {pageNumbers.map((p, idx) =>
        p === 'ellipsis' ? (
          <span key={`ellipsis-${idx}`} className="px-1.5 text-slate-400 text-xs">
            …
          </span>
        ) : (
          <button
            key={p}
            onClick={() => onPageChange(p)}
            className={`min-w-[2rem] h-7 px-1.5 rounded-lg text-xs font-semibold transition-colors cursor-pointer ${
              p === page
                ? 'bg-blue-600 text-white shadow-sm'
                : 'text-slate-600 hover:bg-slate-100 border border-slate-200'
            }`}
          >
            {p}
          </button>
        )
      )}

      <button
        onClick={() => onPageChange(page + 1)}
        disabled={!hasNextPage}
        className={`p-1.5 rounded-lg border transition-colors ${
          hasNextPage
            ? 'border-slate-200 hover:bg-slate-100 text-slate-600 cursor-pointer'
            : 'border-slate-100 text-slate-300 cursor-not-allowed'
        }`}
        aria-label="Next page"
      >
        <ChevronRight className="h-4 w-4" />
      </button>
    </div>
  );
};
