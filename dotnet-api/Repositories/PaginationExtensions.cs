using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.DTOs;

namespace Afrobotics.Bit.Api.Repositories
{
    /// <summary>
    /// Extension methods for IQueryable<T> to support server-side pagination and sorting.
    /// Used by services to transform EF Core queries into PaginatedResult<T> responses.
    /// </summary>
    public static class PaginationExtensions
    {
        /// <summary>
        /// Executes a paginated query: counts total matching rows, then fetches the requested page.
        /// Sorting must be applied to the query BEFORE calling this method.
        /// </summary>
        public static async Task<PaginatedResult<T>> ToPaginatedResultAsync<T>(
            this IQueryable<T> query,
            int page,
            int pageSize)
        {
            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PaginatedResult<T>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                HasPreviousPage = page > 1,
                HasNextPage = page < totalPages
            };
        }

        /// <summary>
        /// Applies dynamic sorting to an IQueryable based on a property name and direction.
        /// When sortBy is null, returns the query as-is (caller should apply default sort first).
        /// </summary>
        public static IQueryable<T> ApplySort<T>(
            this IQueryable<T> query,
            string? sortBy,
            bool descending)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return query;

            var property = typeof(T).GetProperty(sortBy,
                BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

            if (property == null)
                return query; // Unknown property — keep existing sort order

            var parameter = Expression.Parameter(typeof(T), "x");
            var propertyAccess = Expression.Property(parameter, property);
            var lambda = Expression.Lambda(propertyAccess, parameter);

            var methodName = descending ? "OrderByDescending" : "OrderBy";

            var resultExpression = Expression.Call(
                typeof(Queryable),
                methodName,
                new[] { typeof(T), property.PropertyType },
                query.Expression,
                Expression.Quote(lambda));

            return query.Provider.CreateQuery<T>(resultExpression);
        }
    }
}
