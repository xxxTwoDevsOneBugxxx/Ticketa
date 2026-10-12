using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ticketa.Core.DTOs;
using Ticketa.Core.Interfaces.Services;

namespace Ticketa.Infrastructure.ExternalService
{
  public class TmdbService : ITmdbService
  {
    private readonly HttpClient _httpClient;
    private readonly ILogger<TmdbService> _logger;

    public TmdbService(HttpClient httpClient, IConfiguration configuration, ILogger<TmdbService> logger)
    {
      _httpClient = httpClient;
      _logger = logger;

      var apiKey = configuration["Tmdb:ApiKey"];
      if (string.IsNullOrWhiteSpace(apiKey))
      {
        _logger.LogWarning("TMDB API key not configured. External TMDB integrations will be unavailable.");
      }
      else
      {
        // Set the TMDB Read Access Token (v4) as a Bearer token
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
      }
    }

    public async Task<IReadOnlyList<TmdbMovieDto>> GetPopularMoviesAsync(CancellationToken ct = default)
    {
      try
      {
        var url = $"movie/now_playing?language=en-US&page=1";
        var response = await _httpClient.GetFromJsonAsync<TmdbPopularResponseDto>(url, ct);
        return response?.Results ?? [];
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, $"Error fetching now playing movies");
        return [];
      }
    }

    public async Task<TmdbMovieDto?> GetMovieByIdAsync(int tmdbId, CancellationToken ct = default)
    {
      try
      {
        var url = $"movie/{tmdbId}?language=en-US";
        return await _httpClient.GetFromJsonAsync<TmdbMovieDetailDto>(url, ct) ?? new TmdbMovieDetailDto();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, $"Error fetching movie {tmdbId} from TMDB");
        return null;
      }
    }

    public async Task<string?> GetTrailerKeyAsync(int tmdbId, CancellationToken ct = default)
    {
      try
      {

        var url = $"movie/{tmdbId}/videos?language=en-US";
        var response = await _httpClient.GetFromJsonAsync<TmdbVideoResponseDto>(url, ct);
        // Look for an official trailer on YouTube
        return response?.Results
            .Where(v => v.Site == "YouTube" && v.Type == "Trailer" && v.Official).Select(v => v.Key).FirstOrDefault()

            ?? response?.Results
            .Where(v => v.Site == "YouTube" && v.Type == "Trailer").Select(v => v.Key).FirstOrDefault();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, $"error fetching trailer for movie {tmdbId}");
        return null;
      }
    }

    public async Task<TmdbMovieDetailDto> GetMovieDetailAsync(int tmdbId, CancellationToken ct = default)
    {
      try
      {

        var url = $"movie/{tmdbId}?language=en-US";
        return await _httpClient.GetFromJsonAsync<TmdbMovieDetailDto>(url, ct) ?? new TmdbMovieDetailDto();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, $"Error fetching details for movie {tmdbId}");
        return new TmdbMovieDetailDto();
      }
    }

    public async Task<IReadOnlyList<TmdbMovieDto>> SearchMoviesAsync(string query, CancellationToken ct = default)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(query))
          return [];

        var encoded = Uri.EscapeDataString(query);

        var language = IsArabic(query) ? "ar" : "en-US";
        var url = $"search/movie?language={language}&query={encoded}&page=1";

        var response = await _httpClient.GetFromJsonAsync<TmdbPopularResponseDto>(url, ct);
        var results = response?.Results ?? [];

        if (!results.Any() && IsArabic(query))
        {
          var fallbackUrl = $"search/movie?language=en-US&query={encoded}&page=1";
          var fallbackResponse = await _httpClient.GetFromJsonAsync<TmdbPopularResponseDto>(fallbackUrl, ct);
          results = fallbackResponse?.Results ?? [];
        }

        return results;
      }
      catch (Exception ex)
      {
        var safeQuery = query.Replace("\n", " ").Replace("\r", " ");
        _logger.LogError(ex, $"error searching tmdb for query: {safeQuery}");
        return [];
      }
    }

    public async Task<TmdbCreditsDto?> GetCreditsAsync(int tmdbId, CancellationToken ct = default)
    {
      var response = await _httpClient.GetAsync($"movie/{tmdbId}/credits", ct);
      if (!response.IsSuccessStatusCode)
      {
        _logger.LogError($"Failed to fetch credits for movie {tmdbId}. Status code: {response.StatusCode}");
        return null;
      }
      return await response.Content.ReadFromJsonAsync<TmdbCreditsDto>();
    }

    private bool IsArabic(string text)
    {
      return text.Any(c => c >= 0x0600 && c <= 0x06FF);
    }
  }
}
