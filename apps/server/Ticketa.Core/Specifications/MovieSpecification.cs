using Ticketa.Core.Entities;
using Ticketa.Core.Enums;

namespace Ticketa.Core.Specifications
{
  public class MovieSpecification : BaseSpecification<Movie>
  {
    public MovieSpecification(bool? archivedOnly = false)
    {
      if (archivedOnly.HasValue)
      {
        if (archivedOnly.Value)
          AddCriteria(m => m.IsArchived);
        else
          AddCriteria(m => !m.IsArchived);
      }
    }

    public MovieSpecification(MovieStatus? status, string? search, bool? archivedOnly = false) : this(archivedOnly)
    {
      ApplyFilters(status, search);
    }

    public MovieSpecification(MovieStatus? status, string? search, bool includeGenres, bool includeCast, bool? archivedOnly = false) : this(status, search, archivedOnly)
    {
      if (includeGenres)
        AddInclude(m => m.Genres);
    }

    public MovieSpecification(int id, bool includeGenres, bool includeCast = false) : this()
    {
      AddCriteria(m => m.Id == id);
      ApplyNoTracking();
      if (includeGenres)
        AddInclude(m => m.Genres);
      if (includeCast)
        AddInclude(m => m.Cast);
    }

    public MovieSpecification(MovieStatus? status, string? search, int orderColumn, string orderDir, int skip, int take, bool? archivedOnly = false, bool orderByStatus = false) : this(status, search, archivedOnly)
    {
      if (orderByStatus)
        AddOrderBy(m => m.Status);
      else
        ApplyOrdering(orderColumn, orderDir);
      ApplyPaging(skip, take);
      ApplyNoTracking();
    }

    public MovieSpecification(MovieStatus? status, string? search, bool includeGenres, bool includeCast, int skip, int take, bool? archivedOnly = false)
        : this(status, search, includeGenres, includeCast, archivedOnly)
    {
      AddOrderByDesc(m => m.ReleaseDate);
      ApplyPaging(skip, take);
      ApplyNoTracking();
    }

    private void ApplyFilters(MovieStatus? status, string? search)
    {
      ApplyNoTracking();
      if (status.HasValue && !string.IsNullOrEmpty(search))
        AddCriteria(m => m.Status == status.Value && m.Title.Contains(search));
      else if (status.HasValue)
        AddCriteria(m => m.Status == status.Value);
      else if (!string.IsNullOrEmpty(search))
        AddCriteria(m => m.Title.Contains(search));
    }

    private void ApplyOrdering(int orderColumn, string orderDir)
    {
      var isDesc = orderDir.Equals("desc", StringComparison.OrdinalIgnoreCase);

      switch (orderColumn)
      {
        case 3: // VoteAverage column
          if (isDesc) AddOrderByDesc(m => m.VoteAverage);
          else AddOrderBy(m => m.VoteAverage);
          break;
        case 4: // ReleaseDate column
          if (isDesc) AddOrderByDesc(m => m.ReleaseDate);
          else AddOrderBy(m => m.ReleaseDate);
          break;
        case 5: // ImportedAt column
          if (isDesc) AddOrderByDesc(m => m.ImportedAt);
          else AddOrderBy(m => m.ImportedAt);
          break;
        case 6: // Runtime column
          if (isDesc) AddOrderByDesc(m => m.RuntimeMinutes);
          else AddOrderBy(m => m.RuntimeMinutes);
          break;
        default:
          AddOrderByDesc(m => m.ImportedAt);
          break;
      }
    }
  }
}