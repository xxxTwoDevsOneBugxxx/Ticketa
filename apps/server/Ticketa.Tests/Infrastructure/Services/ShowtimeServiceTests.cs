using System.Linq.Expressions;
using Microsoft.Extensions.Configuration;
using Moq;
using Ticketa.Core.DTOs;
using Ticketa.Core.Entities;
using Ticketa.Core.Enums;
using Ticketa.Core.Helpers;
using Ticketa.Core.Interfaces;
using Ticketa.Core.Interfaces.IRepositories;
using Ticketa.Core.Specifications;
using Ticketa.Infrastructure.Service;
using Ticketa.Tests.TestBuilders;
using Xunit;

namespace Ticketa.Tests.Infrastructure.Services
{
  public class ShowtimeServiceTests
  {
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly Mock<IShowtimeRepository> _mockShowtimeRepo;
    private readonly Mock<IMovieRepository> _mockMovieRepo;
    private readonly Mock<IHallRepository> _mockHallRepo;
    private readonly Mock<IBookingRepository> _mockBookingRepo;
    private readonly Mock<IBookedSeatRepository> _mockBookedSeatRepo;
    private readonly TimeConversions _timeConversions;
    private readonly ShowtimeService _sut;

    private const int DefaultShowtimeId = 1;
    private const int DefaultMovieId = 10;
    private const int DefaultHallId = 2;
    private const decimal DefaultPrice = 100m;

    public ShowtimeServiceTests()
    {
      _mockUow = new Mock<IUnitOfWork>();
      _mockShowtimeRepo = new Mock<IShowtimeRepository>();
      _mockMovieRepo = new Mock<IMovieRepository>();
      _mockHallRepo = new Mock<IHallRepository>();
      _mockBookingRepo = new Mock<IBookingRepository>();
      _mockBookedSeatRepo = new Mock<IBookedSeatRepository>();

      _mockUow.Setup(u => u.Showtimes).Returns(_mockShowtimeRepo.Object);
      _mockUow.Setup(u => u.Movies).Returns(_mockMovieRepo.Object);
      _mockUow.Setup(u => u.Halls).Returns(_mockHallRepo.Object);
      _mockUow.Setup(u => u.Bookings).Returns(_mockBookingRepo.Object);
      _mockUow.Setup(u => u.BookedSeats).Returns(_mockBookedSeatRepo.Object);

      var inMemoryConfig = new Dictionary<string, string?>
      {
        { "AppTimeZone", "UTC" }
      };
      var configuration = new ConfigurationBuilder()
          .AddInMemoryCollection(inMemoryConfig)
          .Build();
      _timeConversions = new TimeConversions(configuration);

      _sut = new ShowtimeService(_mockUow.Object, _timeConversions, null!);
    }

    #region GetAllAsync & GetHallsAsync Tests

    [Fact]
    public async Task GetAllAsync_WhenCalled_ReturnsGroupedMovieShowtimes()
    {
      // Arrange
      var movie = new Movie { Id = 1, Title = "Avatar", RuntimeMinutes = 160, Genres = [new Genre { Name = "Sci-Fi" }] };
      var hall = new Hall { Id = 1, Name = "IMAX 1", Type = HallType.IMAX };
      var showtime = new Showtime
      {
        Id = 1,
        MovieId = 1,
        HallId = 1,
        Movie = movie,
        Hall = hall,
        StartTime = DateTime.UtcNow.AddHours(2),
        EndTime = DateTime.UtcNow.AddHours(4),
        Price = 150m,
        Status = ShowtimeStatus.Scheduled
      };

      _mockShowtimeRepo
          .Setup(r => r.GetAllWithSpecAsync(It.IsAny<ShowtimeSpecification>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync([showtime]);

      // Act
      var result = (await _sut.GetAllAsync("Avatar", "scheduled")).ToList();

      // Assert
      Assert.Single(result);
      Assert.Equal("Avatar", result[0].Title);
      Assert.Single(result[0].Showtimes);
      Assert.Equal("IMAX 1", result[0].Showtimes[0].HallName);
    }

    [Fact]
    public async Task GetAllAsync_WhenMultipleShowtimesForSameMovieWithDistinctInstances_GroupsUnderSingleMovie()
    {
      // Arrange
      var hall1 = new Hall { Id = 1, Name = "IMAX 1", Type = HallType.IMAX };
      var hall2 = new Hall { Id = 2, Name = "Standard 1", Type = HallType.Standard };

      // Distinct Movie instances simulating AsNoTracking behavior
      var movie1 = new Movie { Id = 1, Title = "Avatar", RuntimeMinutes = 160, Genres = [new Genre { Name = "Sci-Fi" }] };
      var movie2 = new Movie { Id = 1, Title = "Avatar", RuntimeMinutes = 160, Genres = [new Genre { Name = "Sci-Fi" }] };

      var showtime1 = new Showtime
      {
        Id = 1,
        MovieId = 1,
        HallId = 1,
        Movie = movie1,
        Hall = hall1,
        StartTime = DateTime.UtcNow.AddHours(2),
        EndTime = DateTime.UtcNow.AddHours(4),
        Price = 150m,
        Status = ShowtimeStatus.Scheduled
      };

      var showtime2 = new Showtime
      {
        Id = 2,
        MovieId = 1,
        HallId = 2,
        Movie = movie2,
        Hall = hall2,
        StartTime = DateTime.UtcNow.AddHours(5),
        EndTime = DateTime.UtcNow.AddHours(7),
        Price = 100m,
        Status = ShowtimeStatus.Scheduled
      };

      _mockShowtimeRepo
          .Setup(r => r.GetAllWithSpecAsync(It.IsAny<ShowtimeSpecification>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync([showtime1, showtime2]);

      // Act
      var result = (await _sut.GetAllAsync("Avatar", "scheduled")).ToList();

      // Assert
      Assert.Single(result);
      Assert.Equal("Avatar", result[0].Title);
      Assert.Equal(2, result[0].Showtimes.Count);
    }

    [Fact]
    public async Task GetHallsAsync_ReturnsMappedHalls()
    {
      // Arrange
      var halls = new List<Hall>
      {
        new() { Id = 1, Name = "Hall A", Type = HallType.Standard, TotalRows = 10, SeatsPerRow = 12 }
      };

      _mockHallRepo
          .Setup(r => r.GetAllAsync())
          .ReturnsAsync(halls);

      // Act
      var result = (await _sut.GetHallsAsync()).ToList();

      // Assert
      Assert.Single(result);
      Assert.Equal("Hall A", result[0].Name);
      Assert.Equal(120, result[0].TotalSeats);
    }

    #endregion

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_WhenStartTimeInPast_ReturnsPastError()
    {
      // Arrange
      var dto = new ShowtimeUpsertDto
      {
        MovieId = DefaultMovieId,
        HallId = DefaultHallId,
        StartTime = DateTime.UtcNow.AddHours(-1),
        Price = DefaultPrice
      };

      // Act
      var error = await _sut.CreateAsync(dto);

      // Assert
      Assert.Equal("A showtime cannot be scheduled in the past.", error);
      _mockShowtimeRepo.Verify(r => r.CreateAsync(It.IsAny<Showtime>()), Times.Never);
      _mockUow.Verify(u => u.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhenStartTimeLessThan5HoursFromNow_Returns5HourAdvanceError()
    {
      // Arrange: 2 hours into future (< 5 hours)
      var dto = new ShowtimeUpsertDto
      {
        MovieId = DefaultMovieId,
        HallId = DefaultHallId,
        StartTime = DateTime.UtcNow.AddHours(2),
        Price = DefaultPrice
      };

      // Act
      var error = await _sut.CreateAsync(dto);

      // Assert
      Assert.Equal("A showtime must be scheduled at least 5 hours from now.", error);
      _mockShowtimeRepo.Verify(r => r.CreateAsync(It.IsAny<Showtime>()), Times.Never);
      _mockUow.Verify(u => u.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhenMovieNotFound_ReturnsMovieNotFoundError()
    {
      // Arrange
      var dto = new ShowtimeUpsertDto
      {
        MovieId = DefaultMovieId,
        HallId = DefaultHallId,
        StartTime = DateTime.UtcNow.AddHours(6),
        Price = DefaultPrice
      };

      _mockMovieRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Movie, bool>>>()))
          .ReturnsAsync((Movie?)null);

      // Act
      var error = await _sut.CreateAsync(dto);

      // Assert
      Assert.Equal("Movie not found.", error);
      _mockShowtimeRepo.Verify(r => r.CreateAsync(It.IsAny<Showtime>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhenHallNotFound_ReturnsHallNotFoundError()
    {
      // Arrange
      var dto = new ShowtimeUpsertDto
      {
        MovieId = DefaultMovieId,
        HallId = DefaultHallId,
        StartTime = DateTime.UtcNow.AddHours(6),
        Price = DefaultPrice
      };

      var movie = new Movie { Id = DefaultMovieId, Title = "Inception", RuntimeMinutes = 120 };
      var hall = new Hall { Id = DefaultHallId, Name = "IMAX Hall", Type = HallType.IMAX };

      _mockMovieRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Movie, bool>>>()))
          .ReturnsAsync(movie);

      _mockHallRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Hall, bool>>>()))
          .ReturnsAsync((Hall?)null);

      // Act
      var error = await _sut.CreateAsync(dto);

      // Assert
      Assert.Equal("Hall not found.", error);
      _mockShowtimeRepo.Verify(r => r.CreateAsync(It.IsAny<Showtime>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhenTurnaroundBufferHasConflict_ReturnsConflictError()
    {
      // Arrange
      var startTime = DateTime.UtcNow.AddHours(6);
      var dto = new ShowtimeUpsertDto
      {
        MovieId = DefaultMovieId,
        HallId = DefaultHallId,
        StartTime = startTime,
        Price = DefaultPrice
      };

      var movie = new Movie { Id = DefaultMovieId, Title = "Inception", RuntimeMinutes = 120 };
      var hall = new Hall { Id = DefaultHallId, Name = "IMAX Hall", Type = HallType.IMAX };

      _mockMovieRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Movie, bool>>>()))
          .ReturnsAsync(movie);

      _mockHallRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Hall, bool>>>()))
          .ReturnsAsync(hall);

      _mockShowtimeRepo
          .Setup(r => r.HasConflictAsync(DefaultHallId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), null))
          .ReturnsAsync(true);

      // Act
      var error = await _sut.CreateAsync(dto);

      // Assert
      Assert.Equal("IMAX Hall already has a showtime during that slot.", error);
      _mockShowtimeRepo.Verify(r => r.CreateAsync(It.IsAny<Showtime>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhenValid_CalculatesEndTimeWith15MinBufferAndCreatesScheduledShowtime()
    {
      // Arrange: Movie runtime = 120 mins. Buffer = 15 mins -> EndTime = StartTime + 135 mins
      var startTime = DateTime.UtcNow.AddHours(6);
      var dto = new ShowtimeUpsertDto
      {
        MovieId = DefaultMovieId,
        HallId = DefaultHallId,
        StartTime = startTime,
        Price = DefaultPrice
      };

      var movie = new Movie { Id = DefaultMovieId, Title = "Inception", RuntimeMinutes = 120 };
      var hall = new Hall { Id = DefaultHallId, Name = "IMAX Hall", Type = HallType.IMAX };

      _mockMovieRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Movie, bool>>>()))
          .ReturnsAsync(movie);

      _mockHallRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Hall, bool>>>()))
          .ReturnsAsync(hall);

      _mockShowtimeRepo
          .Setup(r => r.HasConflictAsync(DefaultHallId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), null))
          .ReturnsAsync(false);

      Showtime? capturedShowtime = null;
      _mockShowtimeRepo
          .Setup(r => r.CreateAsync(It.IsAny<Showtime>()))
          .Callback<Showtime>(s => capturedShowtime = s)
          .Returns(Task.CompletedTask);

      // Act
      var error = await _sut.CreateAsync(dto);

      // Assert
      Assert.Null(error);
      Assert.NotNull(capturedShowtime);
      Assert.Equal(DefaultMovieId, capturedShowtime.MovieId);
      Assert.Equal(DefaultHallId, capturedShowtime.HallId);
      Assert.Equal(DefaultPrice, capturedShowtime.Price);
      Assert.Equal(ShowtimeStatus.Scheduled, capturedShowtime.Status);

      // 120m runtime + 15m buffer = 135m duration
      var expectedEnd = startTime.AddMinutes(135);
      Assert.Equal(expectedEnd.Minute, capturedShowtime.EndTime.Minute);

      _mockUow.Verify(u => u.SaveAsync(), Times.Once);
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_WhenStartTimeInPast_ReturnsPastError()
    {
      // Arrange
      var dto = new ShowtimeUpsertDto
      {
        Id = DefaultShowtimeId,
        MovieId = DefaultMovieId,
        HallId = DefaultHallId,
        StartTime = DateTime.UtcNow.AddHours(-1)
      };

      // Act
      var error = await _sut.UpdateAsync(dto);

      // Assert
      Assert.Equal("A showtime cannot be scheduled in the past.", error);
      _mockShowtimeRepo.Verify(r => r.UpdateAsync(It.IsAny<Showtime>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WhenStartTimeLessThan5HoursFromNow_Returns5HourAdvanceError()
    {
      // Arrange
      var dto = new ShowtimeUpsertDto
      {
        Id = DefaultShowtimeId,
        MovieId = DefaultMovieId,
        HallId = DefaultHallId,
        StartTime = DateTime.UtcNow.AddHours(2)
      };

      // Act
      var error = await _sut.UpdateAsync(dto);

      // Assert
      Assert.Equal("A showtime must be scheduled at least 5 hours from now.", error);
    }

    [Fact]
    public async Task UpdateAsync_WhenShowtimeNotFound_ReturnsShowtimeNotFoundError()
    {
      // Arrange
      var dto = new ShowtimeUpsertDto
      {
        Id = DefaultShowtimeId,
        MovieId = DefaultMovieId,
        HallId = DefaultHallId,
        StartTime = DateTime.UtcNow.AddHours(6)
      };

      _mockMovieRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Movie, bool>>>())).ReturnsAsync(new Movie());
      _mockHallRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Hall, bool>>>())).ReturnsAsync(new Hall());
      _mockShowtimeRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Showtime, bool>>>())).ReturnsAsync((Showtime?)null);

      // Act
      var error = await _sut.UpdateAsync(dto);

      // Assert
      Assert.Equal("Showtime not found.", error);
    }

    [Fact]
    public async Task UpdateAsync_WhenExistingShowtimeStartsInLessThan5Hours_ReturnsCannotEditError()
    {
      // Arrange: Existing showtime is starting in 2 hours (too soon to reschedule)
      var dto = new ShowtimeUpsertDto
      {
        Id = DefaultShowtimeId,
        MovieId = DefaultMovieId,
        HallId = DefaultHallId,
        StartTime = DateTime.UtcNow.AddHours(10)
      };

      var existingShowtime = new ShowtimeBuilder()
          .WithId(DefaultShowtimeId)
          .Build();
      existingShowtime.StartTime = DateTime.UtcNow.AddHours(2);

      _mockMovieRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Movie, bool>>>())).ReturnsAsync(new Movie());
      _mockHallRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Hall, bool>>>())).ReturnsAsync(new Hall());
      _mockShowtimeRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Showtime, bool>>>())).ReturnsAsync(existingShowtime);

      // Act
      var error = await _sut.UpdateAsync(dto);

      // Assert
      Assert.Equal("A showtime cannot be edited less than 5 hours before it starts.", error);
      _mockShowtimeRepo.Verify(r => r.UpdateAsync(It.IsAny<Showtime>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WhenShowtimeAlreadyCompleted_ReturnsCompletedError()
    {
      // Arrange: Completed showtimes are immutable
      var dto = new ShowtimeUpsertDto
      {
        Id = DefaultShowtimeId,
        MovieId = DefaultMovieId,
        HallId = DefaultHallId,
        StartTime = DateTime.UtcNow.AddHours(10)
      };

      var existingShowtime = new ShowtimeBuilder()
          .WithId(DefaultShowtimeId)
          .WithStatus(ShowtimeStatus.Completed)
          .Build();
      existingShowtime.StartTime = DateTime.UtcNow.AddHours(12);

      _mockMovieRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Movie, bool>>>())).ReturnsAsync(new Movie());
      _mockHallRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Hall, bool>>>())).ReturnsAsync(new Hall());
      _mockShowtimeRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Showtime, bool>>>())).ReturnsAsync(existingShowtime);

      // Act
      var error = await _sut.UpdateAsync(dto);

      // Assert
      Assert.Equal("The showtime is already completed", error);
      _mockShowtimeRepo.Verify(r => r.UpdateAsync(It.IsAny<Showtime>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_WhenValid_UpdatesStartTimeEndTimePriceAndSaves()
    {
      // Arrange
      var newStart = DateTime.UtcNow.AddHours(8);
      var dto = new ShowtimeUpsertDto
      {
        Id = DefaultShowtimeId,
        MovieId = DefaultMovieId,
        HallId = DefaultHallId,
        StartTime = newStart,
        Price = 150m
      };

      var movie = new Movie { Id = DefaultMovieId, Title = "Avatar", RuntimeMinutes = 180 };
      var hall = new Hall { Id = DefaultHallId, Name = "IMAX 1", Type = HallType.IMAX };

      var existingShowtime = new ShowtimeBuilder()
          .WithId(DefaultShowtimeId)
          .WithStatus(ShowtimeStatus.Scheduled)
          .Build();
      existingShowtime.StartTime = DateTime.UtcNow.AddHours(7);

      _mockMovieRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Movie, bool>>>())).ReturnsAsync(movie);
      _mockHallRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Hall, bool>>>())).ReturnsAsync(hall);
      _mockShowtimeRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Showtime, bool>>>())).ReturnsAsync(existingShowtime);
      _mockShowtimeRepo.Setup(r => r.HasConflictAsync(DefaultHallId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), DefaultShowtimeId)).ReturnsAsync(false);

      // Act
      var error = await _sut.UpdateAsync(dto);

      // Assert
      Assert.Null(error);
      Assert.Equal(150m, existingShowtime.Price);
      Assert.Equal(DefaultMovieId, existingShowtime.MovieId);
      Assert.Equal(DefaultHallId, existingShowtime.HallId);
      Assert.Equal(newStart, existingShowtime.StartTime);

      // 180m runtime + 15m buffer = 195m
      var expectedEnd = newStart.AddMinutes(195);
      Assert.Equal(expectedEnd, existingShowtime.EndTime);

      _mockShowtimeRepo.Verify(r => r.UpdateAsync(existingShowtime), Times.Once);
      _mockUow.Verify(u => u.SaveAsync(), Times.Once);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WhenShowtimeNotFound_ReturnsShowtimeNotFoundError()
    {
      // Arrange
      _mockShowtimeRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Showtime, bool>>>()))
          .ReturnsAsync((Showtime?)null);

      // Act
      var error = await _sut.DeleteAsync(DefaultShowtimeId);

      // Assert
      Assert.Equal("Showtime not found.", error);
      _mockShowtimeRepo.Verify(r => r.Delete(It.IsAny<Showtime>()), Times.Never);
      _mockUow.Verify(u => u.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenShowtimeHasBookings_ReturnsCannotRemoveError()
    {
      // Arrange: Financial audit integrity check
      var showtime = new ShowtimeBuilder().WithId(DefaultShowtimeId).Build();
      _mockShowtimeRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Showtime, bool>>>()))
          .ReturnsAsync(showtime);

      _mockBookingRepo
          .Setup(r => r.AnyForShowtimeAsync(DefaultShowtimeId))
          .ReturnsAsync(true); // Has booking history!

      // Act
      var error = await _sut.DeleteAsync(DefaultShowtimeId);

      // Assert
      Assert.Equal("Can't remove this showtime — it has bookings or payments.", error);
      _mockShowtimeRepo.Verify(r => r.Delete(It.IsAny<Showtime>()), Times.Never);
      _mockUow.Verify(u => u.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_WhenUnbooked_DeletesShowtimeAndSaves()
    {
      // Arrange
      var showtime = new ShowtimeBuilder().WithId(DefaultShowtimeId).Build();
      _mockShowtimeRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Showtime, bool>>>()))
          .ReturnsAsync(showtime);

      _mockBookingRepo
          .Setup(r => r.AnyForShowtimeAsync(DefaultShowtimeId))
          .ReturnsAsync(false); // Zero bookings

      // Act
      var error = await _sut.DeleteAsync(DefaultShowtimeId);

      // Assert
      Assert.Null(error);
      _mockShowtimeRepo.Verify(r => r.Delete(showtime), Times.Once);
      _mockUow.Verify(u => u.SaveAsync(), Times.Once);
    }

    #endregion

    #region GetSeatMapAsync Tests

    [Fact]
    public async Task GetSeatMapAsync_WhenShowtimeNotFound_ReturnsNull()
    {
      // Arrange
      _mockShowtimeRepo
          .Setup(r => r.GetEntityWithSpecAsync(It.IsAny<ShowtimeByIdSpecification>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((Showtime?)null);

      // Act
      var result = await _sut.GetSeatMapAsync(DefaultShowtimeId);

      // Assert
      Assert.Null(result);
    }

    [Fact]
    public async Task GetSeatMapAsync_WhenShowtimeExists_SynthesizesVirtualLayoutWithPricingAndBookedSeats()
    {
      // Arrange
      var showtime = new ShowtimeBuilder()
          .WithId(DefaultShowtimeId)
          .WithHallType(HallType.Standard)
          .WithPrice(100m)
          .Build();

      _mockShowtimeRepo
          .Setup(r => r.GetEntityWithSpecAsync(It.IsAny<ShowtimeByIdSpecification>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(showtime);

      var bookedSeats = new List<BookedSeat>
      {
        new BookedSeatBuilder().WithShowtimeId(DefaultShowtimeId).WithSeat(1, 1).Build(),
        new BookedSeatBuilder().WithShowtimeId(DefaultShowtimeId).WithSeat(1, 2).Build()
      };

      _mockBookedSeatRepo
          .Setup(r => r.GetByShowtimeIdAsync(DefaultShowtimeId, It.IsAny<CancellationToken>()))
          .ReturnsAsync(bookedSeats);

      // Act
      var result = await _sut.GetSeatMapAsync(DefaultShowtimeId);

      // Assert
      Assert.NotNull(result);
      Assert.Equal(DefaultShowtimeId, result.ShowtimeId);
      Assert.Equal(100m, result.BasePrice);
      Assert.Equal(12, result.Rows);
      Assert.Equal(16, result.SeatsPerRow);
      Assert.Equal(2, result.BookedSeats.Count);

      // Verify category prices: Regular = 100, VIP = 150
      Assert.True(result.CategoryPrices.ContainsKey("Regular"));
      Assert.Equal(100m, result.CategoryPrices["Regular"]);
      Assert.True(result.CategoryPrices.ContainsKey("VIP"));
      Assert.Equal(150m, result.CategoryPrices["VIP"]);
    }

    #endregion

    #region SaveBatchAsync Tests

    [Fact]
    public async Task SaveBatchAsync_WithValidBatchChanges_PerformsCreateUpdateDeleteAndCommits()
    {
      // Arrange
      var startTime = DateTime.UtcNow.AddHours(6).ToString("yyyy-MM-ddTHH:mm:ss");
      var dto = new ShowtimeBatchSaveDto
      {
        Date = "2026-09-08",
        Changes =
        [
          new ShowtimeBatchChangeDto
          {
            Action = "create",
            ClientId = "new-1",
            MovieId = DefaultMovieId,
            HallId = DefaultHallId,
            StartTime = startTime,
            Price = 100m
          },
          new ShowtimeBatchChangeDto
          {
            Action = "update",
            ClientId = "update-1",
            ShowtimeId = 2,
            Price = 120m
          },
          new ShowtimeBatchChangeDto
          {
            Action = "delete",
            ClientId = "del-1",
            ShowtimeId = 3
          }
        ]
      };

      var movie = new Movie { Id = DefaultMovieId, RuntimeMinutes = 120 };
      var hall = new Hall { Id = DefaultHallId, Name = "Hall 1" };
      var showtimeToUpdate = new ShowtimeBuilder().WithId(2).Build();
      var showtimeToDelete = new ShowtimeBuilder().WithId(3).Build();

      _mockMovieRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Movie, bool>>>())).ReturnsAsync(movie);
      _mockHallRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Hall, bool>>>())).ReturnsAsync(hall);

      _mockShowtimeRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Showtime, bool>>>()))
          .ReturnsAsync((Expression<Func<Showtime, bool>> predicate) =>
          {
            var func = predicate.Compile();
            if (func(showtimeToUpdate)) return showtimeToUpdate;
            if (func(showtimeToDelete)) return showtimeToDelete;
            return null;
          });

      _mockShowtimeRepo.Setup(r => r.HasConflictAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), null)).ReturnsAsync(false);
      _mockBookingRepo.Setup(r => r.AnyForShowtimeAsync(3)).ReturnsAsync(false);

      // Act
      var result = await _sut.SaveBatchAsync(dto);

      // Assert
      Assert.True(result.Success);
      Assert.Empty(result.Errors);

      _mockShowtimeRepo.Verify(r => r.CreateAsync(It.IsAny<Showtime>()), Times.Once);
      _mockShowtimeRepo.Verify(r => r.UpdateAsync(showtimeToUpdate), Times.Once);
      _mockShowtimeRepo.Verify(r => r.Delete(showtimeToDelete), Times.Once);
      _mockUow.Verify(u => u.SaveAsync(), Times.Once);
    }

    [Fact]
    public async Task SaveBatchAsync_WhenBatchItemsHaveErrors_PopulatesErrorsAndCalculatesSuccess()
    {
      // Arrange: Item 1 valid create; Item 2 delete fails because showtime has bookings
      var startTime = DateTime.UtcNow.AddHours(6).ToString("yyyy-MM-ddTHH:mm:ss");
      var dto = new ShowtimeBatchSaveDto
      {
        Date = "2026-09-08",
        Changes =
        [
          new ShowtimeBatchChangeDto
          {
            Action = "create",
            ClientId = "new-1",
            MovieId = DefaultMovieId,
            HallId = DefaultHallId,
            StartTime = startTime,
            Price = 100m
          },
          new ShowtimeBatchChangeDto
          {
            Action = "delete",
            ClientId = "del-has-bookings",
            ShowtimeId = 5
          }
        ]
      };

      var movie = new Movie { Id = DefaultMovieId, RuntimeMinutes = 120 };
      var hall = new Hall { Id = DefaultHallId, Name = "Hall 1" };
      var bookedShowtime = new ShowtimeBuilder().WithId(5).Build();

      _mockMovieRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Movie, bool>>>())).ReturnsAsync(movie);
      _mockHallRepo.Setup(r => r.GetAsync(It.IsAny<Expression<Func<Hall, bool>>>())).ReturnsAsync(hall);

      _mockShowtimeRepo
          .Setup(r => r.GetAsync(It.IsAny<Expression<Func<Showtime, bool>>>()))
          .ReturnsAsync((Expression<Func<Showtime, bool>> predicate) =>
          {
            var func = predicate.Compile();
            if (func(bookedShowtime)) return bookedShowtime;
            return null;
          });

      _mockShowtimeRepo.Setup(r => r.HasConflictAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), null)).ReturnsAsync(false);
      _mockBookingRepo.Setup(r => r.AnyForShowtimeAsync(5)).ReturnsAsync(true); // Has bookings!

      // Act
      var result = await _sut.SaveBatchAsync(dto);

      // Assert
      Assert.Single(result.Errors);
      Assert.Equal("del-has-bookings", result.Errors[0].ClientId);
      Assert.Equal("Cannot delete — it has bookings or payments.", result.Errors[0].Message);
      Assert.True(result.Success); // 2 total changes > 1 error -> partial success

      _mockShowtimeRepo.Verify(r => r.CreateAsync(It.IsAny<Showtime>()), Times.Once);
      _mockShowtimeRepo.Verify(r => r.Delete(It.IsAny<Showtime>()), Times.Never);
      _mockUow.Verify(u => u.SaveAsync(), Times.Once);
    }

    #endregion
  }
}