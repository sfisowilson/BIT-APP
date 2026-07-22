import { useState, useEffect, useCallback, useRef } from 'react';
import { fetchPaginated } from '../apiClient';

interface PaginatedDataState<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
  loading: boolean;
  error: string | null;
}

interface UsePaginatedDataOptions {
  /** Polling interval in ms. 0 or undefined = no polling. */
  refreshInterval?: number;
  /** Default page size. */
  defaultPageSize?: number;
}

interface UsePaginatedDataReturn<T> {
  data: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
  loading: boolean;
  error: string | null;
  setPage: (page: number) => void;
  setFilters: (filters: Record<string, unknown>) => void;
  refresh: () => void;
}

/**
 * Generic hook for paginated data fetching from BIT API endpoints.
 *
 * Usage:
 *   const { data, loading, page, totalPages, setPage, setFilters } =
 *     usePaginatedData<ContentItem>('/api/content', { ingestionStatus: 'Completed' });
 */
export function usePaginatedData<T>(
  endpoint: string,
  initialFilters: Record<string, unknown> = {},
  options: UsePaginatedDataOptions = {},
): UsePaginatedDataReturn<T> {
  const { refreshInterval = 0, defaultPageSize = 20 } = options;

  const [state, setState] = useState<PaginatedDataState<T>>({
    items: [],
    totalCount: 0,
    page: 1,
    pageSize: defaultPageSize,
    totalPages: 0,
    hasPreviousPage: false,
    hasNextPage: false,
    loading: true,
    error: null,
  });

  const filtersRef = useRef<Record<string, unknown>>(initialFilters);
  const pageRef = useRef(1);
  const abortRef = useRef<AbortController | null>(null);

  const fetchData = useCallback(async (page: number, filters: Record<string, unknown>) => {
    // Abort any in-flight request to prevent race conditions
    if (abortRef.current) {
      abortRef.current.abort();
    }
    abortRef.current = new AbortController();

    setState(prev => ({ ...prev, loading: true, error: null }));

    try {
      const params = { ...filters, page, pageSize: defaultPageSize };
      const result = await fetchPaginated<T>(endpoint, params);

      setState({
        items: result.items,
        totalCount: result.totalCount,
        page: result.page,
        pageSize: result.pageSize,
        totalPages: result.totalPages,
        hasPreviousPage: result.hasPreviousPage,
        hasNextPage: result.hasNextPage,
        loading: false,
        error: null,
      });
    } catch (err: any) {
      if (err.name === 'AbortError') return; // Ignore aborted requests
      setState(prev => ({
        ...prev,
        loading: false,
        error: err.message || 'Failed to fetch data.',
      }));
    }
  }, [endpoint, defaultPageSize]);

  // Initial fetch + refetch when filters or page change
  useEffect(() => {
    fetchData(pageRef.current, filtersRef.current);
  }, [fetchData]);

  // Optional polling
  useEffect(() => {
    if (refreshInterval <= 0) return;
    const timer = setInterval(() => {
      fetchData(pageRef.current, filtersRef.current);
    }, refreshInterval);
    return () => clearInterval(timer);
  }, [refreshInterval, fetchData]);

  const setPage = useCallback((newPage: number) => {
    pageRef.current = newPage;
    fetchData(newPage, filtersRef.current);
  }, [fetchData]);

  const setFilters = useCallback((newFilters: Record<string, unknown>) => {
    filtersRef.current = { ...filtersRef.current, ...newFilters };
    pageRef.current = 1; // Reset to first page on filter change
    fetchData(1, filtersRef.current);
  }, [fetchData]);

  const refresh = useCallback(() => {
    fetchData(pageRef.current, filtersRef.current);
  }, [fetchData]);

  return {
    data: state.items,
    totalCount: state.totalCount,
    page: state.page,
    pageSize: state.pageSize,
    totalPages: state.totalPages,
    hasPreviousPage: state.hasPreviousPage,
    hasNextPage: state.hasNextPage,
    loading: state.loading,
    error: state.error,
    setPage,
    setFilters,
    refresh,
  };
}
