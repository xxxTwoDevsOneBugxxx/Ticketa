using Ticketa.Core.Entities;
using Ticketa.Core.Enums;

namespace Ticketa.Core.Specifications
{
  public class ShowtimeSpecification : BaseSpecification<Showtime>
  {
    public ShowtimeSpecification(bool? archivedOnly = false)
    {
      if (archivedOnly.HasValue)
      {
        if (archivedOnly.Value)
          AddCriteria(s => s.IsArchived);
        else
          AddCriteria(s => !s.IsArchived);
      }
      AddInclude(s => s.Movie);
      AddInclude(s => s.Hall);
      AddInclude("Movie.Genres");
      ApplyNoTracking();
      ApplySplitQuery();
    }

    public ShowtimeSpecification(
        ShowtimeStatus? status,
        string? search,
        bool? archivedOnly = false,
        DateTime? fromDate = null,
        DateTime? toDate = null) : this(archivedOnly)
    {
      ApplyFilters(status, search, fromDate, toDate);
    }

    private void ApplyFilters(
        ShowtimeStatus? showtimeStatus,
        string? search,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
      if (showtimeStatus.HasValue)
        AddCriteria(s => s.Status == showtimeStatus.Value);

      if (!string.IsNullOrEmpty(search))
        AddCriteria(s => s.Movie.Title.Contains(search) || s.Hall.Name.Contains(search));

      if (fromDate.HasValue)
        AddCriteria(s => s.StartTime >= fromDate.Value);

      if (toDate.HasValue)
        AddCriteria(s => s.StartTime <= toDate.Value);
    }
  }
}
