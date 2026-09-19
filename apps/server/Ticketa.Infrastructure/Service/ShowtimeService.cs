using Microsoft.EntityFrameworkCore;
using Ticketa.Core.DTOs;
using Ticketa.Core.Entities;
using Ticketa.Core.Enums;
using Ticketa.Core.Helpers;
using Ticketa.Core.Interfaces;
using Ticketa.Core.Interfaces.IServices;
using Ticketa.Core.Specifications;
using Ticketa.Infrastructure.Data;

namespace Ticketa.Infrastructure.Service
{
  public class ShowtimeService(IUnitOfWork uow, TimeConversions timeConversions, ApplicationDbContext context) : IShowtimeService
  {
    private const int BufferMinutes = 15;

    private readonly IUnitOfWork _uow = uow;
    private readonly TimeConversions _timeConversions = timeConversions;

    // ── DataTables ────────────────────────────────────────────────

    public async Task<IEnumerable<MovieShowtimeDto>> GetAllAsync(
        string? search,
        string? segmentedFilter)
    {
      return await GetAllDataAsync(search, segmentedFilter, archivedOnly: false);
    }

    private async Task<IEnumerable<MovieShowtimeDto>> GetAllDataAsync(
        string? search,
        string? segmentedFilter,
        bool archivedOnly)
    {
      ShowtimeStatus? status = segmentedFilter?.ToLower() switch
      {
        "scheduled" => ShowtimeStatus.Scheduled,
        "soldout" => ShowtimeStatus.SoldOut,
        "completed" => ShowtimeStatus.Completed,
        _ => null
      };

      var query = string.IsNullOrWhiteSpace(search) ? null : search;

      var showtimes = await _uow.Showtimes.GetAllWithSpecAsync(
          new ShowtimeSpecification(status, query, archivedOnly: archivedOnly));

      return showtimes
          .GroupBy(s => s.MovieId)
          .Select(g =>
          {
            var movie = g.First().Movie;
            return new MovieShowtimeDto
            {
              MovieId = movie.Id,
              TmdbId = movie.TmdbId,
              Title = movie.Title,
              PosterPath = movie.PosterPath,
              trailerKey = movie.TrailerKey,
              Rate = movie.VoteAverage,
              Runtime = movie.RuntimeMinutes,
              Genres = movie.Genres.Select(genre => genre.Name).ToList(),
              Showtimes = g.Select(s => new ShowtimeListItemDto
              {
                Id = s.Id,
                HallName = s.Hall.Name,
                VisibleSeatCount = HallTypeHelper.GetTemplate(s.Hall.Type).VisibleSeatCount,
                StartTime = _timeConversions.EnsureUtcKind(s.StartTime),
                EndTime = _timeConversions.EnsureUtcKind(s.EndTime),
                Price = s.Price,
                Status = s.Status,
                HallId = s.HallId,
                IsArchived = s.IsArchived,
                ArchivedAt = s.ArchivedAt.HasValue ? _timeConversions.EnsureUtcKind(s.ArchivedAt.Value) : null
              }).OrderBy(s => s.StartTime).ToList()
            };
          })
          .OrderBy(m => m.Title);
    }

    public async Task<object> GetAllAsync(DataTableRequestsDto request, string? search, string? segmentedFilter)
    {
      return await GetAllDataTableAsync(request, search, segmentedFilter);
    }

    private async Task<object> GetAllDataTableAsync(DataTableRequestsDto request, string? search, string? segmentedFilter)
    {
      ShowtimeStatus? status = segmentedFilter?.ToLower() switch
      {
        "scheduled" => ShowtimeStatus.Scheduled,
        "soldout" => ShowtimeStatus.SoldOut,
        "completed" => ShowtimeStatus.Completed,
        _ => null
      };

      var searchValue = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
      var hasFilter = status.HasValue || !string.IsNullOrEmpty(searchValue);

      // ── 1. Count queries with AsNoTracking ────────────────────────
      var baseQuery = context.Showtimes
          .AsNoTracking()
          .Where(s => !s.IsArchived);

      IQueryable<Showtime> filteredQuery = baseQuery;

      if (status.HasValue)
        filteredQuery = filteredQuery.Where(s => s.Status == status.Value);

      if (!string.IsNullOrEmpty(searchValue))
      {
        filteredQuery = filteredQuery.Where(s =>
            s.Movie.Title.Contains(searchValue) || s.Hall.Name.Contains(searchValue));
      }

      int totalMovies;
      int filteredCount;

      if (!hasFilter)
      {
        totalMovies = await baseQuery
            .Select(s => s.MovieId)
            .Distinct()
            .CountAsync();
        filteredCount = totalMovies;
      }
      else
      {
        filteredCount = await filteredQuery
            .Select(s => s.MovieId)
            .Distinct()
            .CountAsync();

        totalMovies = await baseQuery
            .Select(s => s.MovieId)
            .Distinct()
            .CountAsync();
      }

      if (filteredCount == 0)
      {
        return new
        {
          draw = request.Draw,
          recordsTotal = totalMovies,
          recordsFiltered = 0,
          data = Array.Empty<MovieShowtimeDto>()
        };
      }

      // ── 2. Database-level alphabetical paging for distinct movie IDs ──────────
      var pagedMovieIds = await filteredQuery
          .Select(s => new { s.MovieId, s.Movie.Title })
          .Distinct()
          .OrderBy(x => x.Title)
          .Skip(request.Start)
          .Take(request.Length)
          .Select(x => x.MovieId)
          .ToListAsync();

      // ── 3. Load ONLY filtered showtimes for the paged movies ───────
      var showtimesQuery = context.Showtimes
          .AsNoTracking()
          .Include(s => s.Movie)
              .ThenInclude(m => m.Genres)
          .Include(s => s.Hall)
          .Where(s => !s.IsArchived && pagedMovieIds.Contains(s.MovieId));

      if (status.HasValue)
        showtimesQuery = showtimesQuery.Where(s => s.Status == status.Value);

      if (!string.IsNullOrEmpty(searchValue))
      {
        showtimesQuery = showtimesQuery.Where(s =>
            s.Movie.Title.Contains(searchValue) || s.Hall.Name.Contains(searchValue));
      }

      var showtimes = await showtimesQuery.ToListAsync();

      // ── 4. Group by MovieId in C# (small paged set) ───────────────
      var movieGroups = showtimes
          .GroupBy(s => s.MovieId)
          .Select(g =>
          {
            var movie = g.First().Movie;
            return new MovieShowtimeDto
            {
              MovieId = movie.Id,
              TmdbId = movie.TmdbId,
              Title = movie.Title,
              PosterPath = movie.PosterPath,
              trailerKey = movie.TrailerKey,
              Rate = movie.VoteAverage,
              Runtime = movie.RuntimeMinutes,
              Genres = movie.Genres.Select(genre => genre.Name).ToList(),
              Showtimes = g.Select(s => new ShowtimeListItemDto
              {
                Id = s.Id,
                HallName = s.Hall.Name,
                VisibleSeatCount = HallTypeHelper.GetTemplate(s.Hall.Type).VisibleSeatCount,
                StartTime = _timeConversions.EnsureUtcKind(s.StartTime),
                EndTime = _timeConversions.EnsureUtcKind(s.EndTime),
                Price = s.Price,
                Status = s.Status,
                HallId = s.HallId,
                IsArchived = s.IsArchived,
                ArchivedAt = s.ArchivedAt.HasValue ? _timeConversions.EnsureUtcKind(s.ArchivedAt.Value) : null
              }).OrderBy(s => s.StartTime).ToList()
            };
          })
          .OrderBy(m => m.Title)
          .ToList();

      return new
      {
        draw = request.Draw,
        recordsTotal = totalMovies,
        recordsFiltered = filteredCount,
        data = movieGroups
      };
    }

    // ── Create & Update ───────────────────────────────────────────

    public async Task<string?> CreateAsync(ShowtimeUpsertDto dto)
    {
      var utcStart = _timeConversions.ConvertToUtc(dto.StartTime);

      if (utcStart < DateTime.UtcNow)
        return "A showtime cannot be scheduled in the past.";

      if (utcStart < DateTime.UtcNow.AddHours(5))
        return "A showtime must be scheduled at least 5 hours from now.";

      var movie = await _uow.Movies.GetAsync(m => m.Id == dto.MovieId);
      if (movie is null) return "Movie not found.";

      var hall = await _uow.Halls.GetAsync(h => h.Id == dto.HallId);
      if (hall is null) return "Hall not found.";

      var endTime = utcStart.AddMinutes((movie.RuntimeMinutes > 0 ? movie.RuntimeMinutes : 120) + BufferMinutes);

      if (await _uow.Showtimes.HasConflictAsync(dto.HallId, utcStart, endTime))
        return $"{hall.Name} already has a showtime during that slot.";

      await _uow.Showtimes.CreateAsync(new Showtime
      {
        MovieId = dto.MovieId,
        HallId = dto.HallId,
        StartTime = utcStart,
        EndTime = endTime,
        Price = dto.Price,
        Status = ShowtimeStatus.Scheduled,
      });

      await _uow.SaveAsync();
      return null;
    }

    public async Task<Showtime?> GetByIdAsync(int id)
    {
      var spec = new ShowtimeByIdSpecification(id);
      var showtimes = await _uow.Showtimes.GetAllWithSpecAsync(spec);
      return showtimes.FirstOrDefault();
    }

    public async Task<ShowtimeUpsertDto?> GetForUpsertAsync(int id)
    {
      var showtime = await GetByIdAsync(id);
      if (showtime == null) return null;

      return new ShowtimeUpsertDto
      {
        Id = showtime.Id,
        MovieId = showtime.MovieId,
        HallId = showtime.HallId,
        StartTime = _timeConversions.ConvertFromUtc(showtime.StartTime),
        Price = showtime.Price
      };
    }

    public async Task<string?> UpdateAsync(ShowtimeUpsertDto dto)
    {
      var utcStart = _timeConversions.ConvertToUtc(dto.StartTime);

      if (utcStart < DateTime.UtcNow)
        return "A showtime cannot be scheduled in the past.";

      if (utcStart < DateTime.UtcNow.AddHours(5))
        return "A showtime must be scheduled at least 5 hours from now.";

      var movie = await _uow.Movies.GetAsync(m => m.Id == dto.MovieId);
      if (movie is null) return "Movie not found.";

      var hall = await _uow.Halls.GetAsync(h => h.Id == dto.HallId);
      if (hall is null) return "Hall not found.";

      var showtime = await _uow.Showtimes.GetAsync(s => s.Id == dto.Id);
      if (showtime is null) return "Showtime not found.";

      if (showtime.StartTime <= DateTime.UtcNow.AddHours(5))
        return "A showtime cannot be edited less than 5 hours before it starts.";

      if (showtime.Status == ShowtimeStatus.Completed)
        return "The showtime is already completed";

      var endTime = utcStart.AddMinutes((movie.RuntimeMinutes > 0 ? movie.RuntimeMinutes : 120) + BufferMinutes);

      if (await _uow.Showtimes.HasConflictAsync(dto.HallId, utcStart, endTime, dto.Id))
        return $"{hall.Name} already has a showtime during that slot.";

      showtime.MovieId = dto.MovieId;
      showtime.HallId = dto.HallId;
      showtime.StartTime = utcStart;
      showtime.EndTime = endTime;
      showtime.Price = dto.Price;

      await _uow.Showtimes.UpdateAsync(showtime);
      await _uow.SaveAsync();
      return null;
    }

    // ── Halls dropdown ────────────────────────────────────────────

    public async Task<IEnumerable<HallDto>> GetHallsAsync()
    {
      var halls = await _uow.Halls.GetAllAsync();
      return halls.Select(h => new HallDto
      {
        Id = h.Id,
        Name = h.Name,
        Type = h.Type,
        TotalRows = h.TotalRows,
        SeatsPerRow = h.SeatsPerRow,
      });
    }

    public async Task<IEnumerable<MovieShowtimeDto>> GetScheduledGroupedAsync(CancellationToken ct = default)
    {
      return await GetAllAsync(search: null, segmentedFilter: "scheduled");
    }

    public async Task<ShowtimeSeatDto?> GetSeatMapAsync(
    int showtimeId, CancellationToken ct = default)
    {
      var spec = new ShowtimeByIdSpecification(showtimeId);
      var showtime = await _uow.Showtimes.GetEntityWithSpecAsync(spec);
      if (showtime is null) return null;


      var template = HallTypeHelper.GetTemplate(showtime.Hall.Type);
      var bookedSeats = await _uow.BookedSeats.GetByShowtimeIdAsync(showtimeId, ct);

      var categoryPrices = template.RowCategoryMap.Values.Distinct().ToDictionary(
          cat => cat.ToString(),
          cat => showtime.Price * (cat switch
          {
            SeatCategory.VIP => 1.5m,
            SeatCategory.Premium => 1.2m,
            _ => 1.0m
          })
        );

      return new ShowtimeSeatDto
      {
        ShowtimeId = showtime.Id,
        MovieId = showtime.MovieId,
        MovieTitle = showtime.Movie.Title,
        MoviePosterPath = showtime.Movie.PosterPath,
        HallName = showtime.Hall.Name,
        HallType = showtime.Hall.Type.ToString(),
        StartsAt = _timeConversions.EnsureUtcKind(showtime.StartTime),
        BasePrice = showtime.Price,
        Rows = template.Rows,
        SeatsPerRow = template.SeatsPerRow,
        RowCategoryMap = template.RowCategoryMap
                                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString()),
        CategoryPrices = categoryPrices,
        BookedSeats = bookedSeats
                                .Select(b => new SeatDto { Row = b.Row, SeatNumber = b.SeatNumber })
                                .ToList()
      };
    }

    public async Task<string?> DeleteAsync(int id)
    {
      var showtime = await _uow.Showtimes.GetAsync(s => s.Id == id);

      if (showtime is null)
        return "Showtime not found.";

      var hasBookings = await _uow.Bookings.AnyForShowtimeAsync(id);
      if (hasBookings)
        return "Can't remove this showtime — it has bookings or payments.";

      _uow.Showtimes.Delete(showtime);
      await _uow.SaveAsync();
      return null;
    }

    public async Task<IEnumerable<HallTimelineDto>> GetByDateAsync(DateOnly date, CancellationToken ct = default)
    {
      var dayStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
      var dayEnd = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

      var showtimes = await _uow.Showtimes.GetAllWithSpecAsync(
          new ShowtimeByDateSpecification(dayStart, dayEnd), ct);

      var showtimeIds = showtimes.Select(s => s.Id).ToList();
      var bookingShowtimeIds = showtimeIds.Count > 0
          ? await context.BookedSeats
              .AsNoTracking()
              .Where(bs => showtimeIds.Contains(bs.ShowtimeId))
              .Select(bs => bs.ShowtimeId)
              .Distinct()
              .ToListAsync(ct)
          : [];

      return showtimes
          .GroupBy(s => s.Hall)
          .Select(g => new HallTimelineDto
          {
            HallId = g.Key.Id,
            HallName = g.Key.Name,
            HallType = g.Key.Type.ToString(),
            Showtimes = g.Select(s => new TimelineShowtimeDto
            {
              Id = s.Id,
              MovieId = s.MovieId,
              MovieTitle = s.Movie.Title,
              RuntimeMinutes = s.Movie.RuntimeMinutes,
              StartTime = _timeConversions.EnsureUtcKind(s.StartTime),
              EndTime = _timeConversions.EnsureUtcKind(s.EndTime),
              Price = s.Price,
              Status = (int)s.Status,
              PosterPath = s.Movie.PosterPath,
              TrailerKey = s.Movie.TrailerKey,
              TmdbId = s.Movie.TmdbId,
              IsArchived = s.IsArchived,
              HasBookings = bookingShowtimeIds.Contains(s.Id)
            }).OrderBy(s => s.StartTime).ToList()
          })
          .OrderBy(h => h.HallName);
    }

    public async Task<ShowtimeBatchResultDto> SaveBatchAsync(ShowtimeBatchSaveDto dto, CancellationToken ct = default)
    {
      var result = new ShowtimeBatchResultDto { Success = true };

      foreach (var change in dto.Changes)
      {
        try
        {
          switch (change.Action)
          {
            case "create":
              {
                if (!change.MovieId.HasValue || !change.HallId.HasValue || string.IsNullOrEmpty(change.StartTime))
                {
                  result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, Message = "Movie, Hall, and StartTime are required." });
                  continue;
                }

                var startLocal = DateTime.Parse(change.StartTime);
                var utcStart = _timeConversions.ConvertToUtc(startLocal);

                if (utcStart < DateTime.UtcNow)
                {
                  result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, Message = "A showtime cannot be scheduled in the past." });
                  continue;
                }

                if (utcStart < DateTime.UtcNow.AddHours(5))
                {
                  result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, Message = "A showtime must be scheduled at least 5 hours from now." });
                  continue;
                }

                var movie = await _uow.Movies.GetAsync(m => m.Id == change.MovieId.Value);
                if (movie is null)
                {
                  result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, Message = "Movie not found." });
                  continue;
                }

                var hall = await _uow.Halls.GetAsync(h => h.Id == change.HallId.Value);
                if (hall is null)
                {
                  result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, Message = "Hall not found." });
                  continue;
                }

                var endTime = utcStart.AddMinutes((movie.RuntimeMinutes > 0 ? movie.RuntimeMinutes : 120) + BufferMinutes);

                if (await _uow.Showtimes.HasConflictAsync(change.HallId.Value, utcStart, endTime))
                {
                  result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, Message = $"{hall.Name} already has a showtime during that slot." });
                  continue;
                }

                await _uow.Showtimes.CreateAsync(new Showtime
                {
                  MovieId = change.MovieId.Value,
                  HallId = change.HallId.Value,
                  StartTime = utcStart,
                  EndTime = endTime,
                  Price = change.Price ?? 10.00m,
                  Status = ShowtimeStatus.Scheduled,
                });

                break;
              }

            case "update":
              {
                if (!change.ShowtimeId.HasValue)
                {
                  result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, Message = "ShowtimeId is required for update." });
                  continue;
                }

                var showtime = await _uow.Showtimes.GetAsync(s => s.Id == change.ShowtimeId.Value);
                if (showtime is null)
                {
                  result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, ShowtimeId = change.ShowtimeId, Message = "Showtime not found." });
                  continue;
                }

                if (!string.IsNullOrEmpty(change.StartTime))
                {
                  var startLocal = DateTime.Parse(change.StartTime);
                  var utcStart = _timeConversions.ConvertToUtc(startLocal);

                  if (utcStart < DateTime.UtcNow)
                  {
                    result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, ShowtimeId = change.ShowtimeId, Message = "A showtime cannot be moved to the past." });
                    continue;
                  }

                  if (utcStart < DateTime.UtcNow.AddHours(5))
                  {
                    result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, ShowtimeId = change.ShowtimeId, Message = "A showtime must be at least 5 hours from now." });
                    continue;
                  }

                  if (showtime.Status == ShowtimeStatus.Completed)
                  {
                    result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, ShowtimeId = change.ShowtimeId, Message = "Completed showtime cannot be moved." });
                    continue;
                  }

                  var movie = await _uow.Movies.GetAsync(m => m.Id == showtime.MovieId);
                  var runtime = movie?.RuntimeMinutes ?? 120;
                  var newEnd = utcStart.AddMinutes(runtime + BufferMinutes);

                  if (await _uow.Showtimes.HasConflictAsync(showtime.HallId, utcStart, newEnd, showtime.Id))
                  {
                    var hall = await _uow.Halls.GetAsync(h => h.Id == showtime.HallId);
                    result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, ShowtimeId = change.ShowtimeId, Message = $"{hall?.Name ?? "Hall"} already has a showtime during that slot." });
                    continue;
                  }

                  showtime.StartTime = utcStart;
                  showtime.EndTime = newEnd;
                }

                if (change.Price.HasValue)
                  showtime.Price = change.Price.Value;

                if (showtime.Status == ShowtimeStatus.SoldOut)
                  showtime.Status = ShowtimeStatus.Scheduled;

                await _uow.Showtimes.UpdateAsync(showtime);
                break;
              }

            case "delete":
              {
                if (!change.ShowtimeId.HasValue)
                {
                  result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, Message = "ShowtimeId is required for delete." });
                  continue;
                }

                var showtime = await _uow.Showtimes.GetAsync(s => s.Id == change.ShowtimeId.Value);
                if (showtime is null)
                {
                  result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, ShowtimeId = change.ShowtimeId, Message = "Showtime not found." });
                  continue;
                }

                var hasBookings = await _uow.Bookings.AnyForShowtimeAsync(change.ShowtimeId.Value);
                if (hasBookings)
                {
                  result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, ShowtimeId = change.ShowtimeId, Message = "Cannot delete — it has bookings or payments." });
                  continue;
                }

                _uow.Showtimes.Delete(showtime);
                break;
              }
          }
        }
        catch (Exception ex)
        {
          result.Errors.Add(new ShowtimeBatchErrorDto { ClientId = change.ClientId, ShowtimeId = change.ShowtimeId, Message = ex.Message });
        }
      }

      if (result.Errors.Count == 0)
      {
        await _uow.SaveAsync();
        result.Success = true;
      }
      else
      {
        await _uow.SaveAsync();
        result.Success = dto.Changes.Count > result.Errors.Count;
      }

      return result;
    }
  }
}