using Microsoft.EntityFrameworkCore;
using Ticketa.Core.Specifications;

namespace Ticketa.Infrastructure.Specification
{
  public static class SpecificationEvaluator<T> where T : class
  {
    public static IQueryable<T> GetQuery(IQueryable<T> query, BaseSpecification<T> spec)
    {
      if (spec.AsNoTracking)
        query = query.AsNoTracking();

      if (spec.AsSplitQuery)
        query = query.AsSplitQuery();

      foreach (var criteria in spec.CriteriaList)
      {
        query = query.Where(criteria);
      }

      query = spec.Includes.Aggregate(query, (current, include) => current.Include(include));
      query = spec.IncludeStrings.Aggregate(query, (current, include) => current.Include(include));

      if (spec.OrderByDesc is not null)
        query = query.OrderByDescending(spec.OrderByDesc);
      else if (spec.OrderBy is not null)
        query = query.OrderBy(spec.OrderBy);

      if (spec.IsPagingEnabled)
        query = query.Skip(spec.Skip).Take(spec.Take);

      return query;
    }
  }
}
