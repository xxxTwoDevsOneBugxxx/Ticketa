using Microsoft.EntityFrameworkCore;
using Ticketa.Core.DTOs;
using Ticketa.Core.Entities;
using Ticketa.Core.Interfaces.IRepositories;
using Ticketa.Infrastructure.Data;

namespace Ticketa.Infrastructure.Repositories
{
  public class MovieRepository(ApplicationDbContext context) : GenericRepository<Movie>(context), IMovieRepository
  {

    public async Task<List<int>> ExistingTmdbIdsAsync(IEnumerable<int> tmdbIds)
      => await _context.Movies
          .Where(m => tmdbIds.Contains(m.TmdbId))
          .Select(m => m.TmdbId)
          .ToListAsync();

    public async Task<List<TopBookedMovieDto>> GetTopBookedMoviesAsync(int count, CancellationToken ct = default)
    {
      var topMovies = await _context.BookedSeats
           .AsNoTracking()
           .Where(bs => !bs.Showtime.Movie.IsArchived)
           .GroupBy(bs => new
           {
             bs.Showtime.MovieId,
             bs.Showtime.Movie.Title,
             bs.Showtime.Movie.PosterPath,
             bs.Showtime.Movie.BackdropPath,
             bs.Showtime.Movie.Overview,
             bs.Showtime.Movie.VoteAverage,
             bs.Showtime.Movie.RuntimeMinutes
           })
           .Select(g => new TopBookedMovieDto
           {
             Id = g.Key.MovieId,
             Title = g.Key.Title,
             PosterPath = g.Key.PosterPath,
             BackdropPath = g.Key.BackdropPath,
             Overview = g.Key.Overview,
             Runtime = g.Key.RuntimeMinutes,
             VoteAverage = g.Key.VoteAverage,
             TicketsSold = g.Count(),
             TotalRevenue = g.Sum(bs => bs.Price)
           })
           .OrderByDescending(m => m.TicketsSold)
           .Take(count)
           .ToListAsync(ct);

      // Second query — genres for just these 6 movies, not all movies
      var movieIds = topMovies.Select(m => m.Id).ToList();

      var genresByMovie = await _context.Movies
          .AsNoTracking()
          .Where(m => movieIds.Contains(m.Id))
          .Select(m => new { m.Id, GenreNames = m.Genres.Select(g => g.Name).ToList() })
          .ToDictionaryAsync(x => x.Id, x => x.GenreNames, ct);

      // Merge — same "build the map, then attach" shape as genre import
      foreach (var movie in topMovies)
        movie.Genres = genresByMovie.TryGetValue(movie.Id, out var names) ? names : new List<string>();

      return topMovies;
    }

    public async Task UpdateAsync(Movie movie)
    {
      _context.Movies.Update(movie);
    }
  }
}