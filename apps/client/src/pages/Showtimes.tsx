import { useGetShowtimes } from "@/hooks/useShowtimes";
import { useNowPlayingMovies } from "@/hooks/useMovies";
import { useSearchParams } from "react-router-dom";
import { AnimatePresence } from "framer-motion";
import { useMemo, useState } from "react";
import { format, parseISO } from "date-fns";
import ErrorState from "@/components/ErrorState";
import TrailerDialog from "@/components/TrailerDialog";
import ShowtimeSkeleton from "@/components/skeletons/ShowtimeSkeleton";
import type { MoviesShowtimes, Showtime } from "@/types/showtimes";
import { ShowtimesMobileView } from "@/components/showtimes/ShowtimesMobileView";
import { ShowtimesDesktopView } from "@/components/showtimes/ShowtimesDesktopView";

const Showtimes = () => {
  // 1. Fast Query: Fetch active movies list
  const {
    data: nowPlayingPages,
    isLoading: isMoviesLoading,
    isError: isMoviesError,
    refetch: refetchMovies,
  } = useNowPlayingMovies(50);

  // 2. Heavy Query: Fetch full showtimes list in the background
  const {
    data: moviesWithShowtimes,
    isLoading: isShowtimesLoading,
    isError: isShowtimesError,
    refetch: refetchShowtimes,
  } = useGetShowtimes();

  const [searchParams, setSearchParams] = useSearchParams();
  const selectedMovieId = searchParams.get("movieId");
  const [isVideoVisible, setIsVideoVisible] = useState(false);

  const handleMovieSelect = (movieId: string | number) => {
    setSearchParams((prev) => {
      const newParams = new URLSearchParams(prev);
      newParams.set("movieId", String(movieId));
      return newParams;
    });
  };

  // Base list of movies from the fast query
  const baseMovies = useMemo(() => {
    return nowPlayingPages?.pages.flatMap((p) => p.items) ?? [];
  }, [nowPlayingPages]);

  // Lookup map for showtimes when they arrive
  const showtimesByMovieId = useMemo(() => {
    const map = new Map<string, Showtime[]>();
    if (moviesWithShowtimes) {
      for (const m of moviesWithShowtimes) {
        map.set(String(m.movieId), m.showtimes || []);
      }
    }
    return map;
  }, [moviesWithShowtimes]);

  // Unified list of movies: shows immediately from baseMovies, enriched with showtimes
  const moviesList = useMemo<MoviesShowtimes[]>(() => {
    if (moviesWithShowtimes && moviesWithShowtimes.length > 0) {
      return moviesWithShowtimes.map((m) => ({
        movieId: String(m.movieId),
        title: m.title,
        posterPath: m.posterPath || "",
        trailerKey: m.trailerKey || "",
        rate: m.rate,
        runtime: m.runtime,
        genres: m.genres,
        showtimes: m.showtimes || [],
      }));
    }

    return baseMovies.map((m) => ({
      movieId: String(m.id),
      title: m.title,
      posterPath: m.posterPath || "",
      trailerKey: "",
      rate: m.voteAverage,
      runtime: m.runtime,
      genres: m.genres,
      showtimes: showtimesByMovieId.get(String(m.id)) || [],
    }));
  }, [baseMovies, moviesWithShowtimes, showtimesByMovieId]);

  // Currently selected movie for Desktop Hero and Sessions views
  const selectedMovie = useMemo(() => {
    if (moviesList.length === 0) return null;
    return (
      moviesList.find((m) => String(m.movieId) === String(selectedMovieId)) ||
      moviesList[0]
    );
  }, [moviesList, selectedMovieId]);

  // Showtimes for currently selected movie (Desktop)
  const selectedMovieShowtimes = useMemo(() => {
    if (!selectedMovie) return [];
    return (
      showtimesByMovieId.get(String(selectedMovie.movieId)) ??
      selectedMovie.showtimes ??
      []
    );
  }, [selectedMovie, showtimesByMovieId]);

  // Group showtimes by day for selected movie (Desktop)
  const groupedShowtimes = useMemo(() => {
    if (!selectedMovieShowtimes || selectedMovieShowtimes.length === 0) return {};
    return selectedMovieShowtimes.reduce(
      (acc, showtime) => {
        const date = format(parseISO(showtime.startTime), "yyyy-MM-dd");
        if (!acc[date]) {
          acc[date] = [];
        }
        acc[date].push(showtime);
        return acc;
      },
      {} as Record<string, Showtime[]>
    );
  }, [selectedMovieShowtimes]);

  const hallNames = useMemo(
    () => [
      ...new Set(
        selectedMovieShowtimes.map((showtime) => showtime.hallName) ?? []
      ),
    ],
    [selectedMovieShowtimes]
  );

  // If both fast and heavy queries are loading and we have no movies at all yet
  if (isMoviesLoading && moviesList.length === 0) {
    return <ShowtimeSkeleton />;
  }

  // If fast movies query completely failed and we have no movies at all
  if (isMoviesError && moviesList.length === 0 && !isShowtimesLoading) {
    return (
      <ErrorState
        refetch={() => {
          refetchMovies();
          refetchShowtimes();
        }}
      />
    );
  }

  return (
    <>
      {/* 📱 Mobile Redesigned View (< lg) */}
      <ShowtimesMobileView
        moviesList={moviesList}
        isShowtimesLoading={isShowtimesLoading}
        isShowtimesError={isShowtimesError}
        refetchShowtimes={refetchShowtimes}
      />

      {/* 💻 Preserved Desktop View (>= lg) */}
      <ShowtimesDesktopView
        selectedMovie={selectedMovie}
        moviesList={moviesList}
        selectedMovieId={selectedMovieId}
        handleMovieSelect={handleMovieSelect}
        isShowtimesLoading={isShowtimesLoading}
        isShowtimesError={isShowtimesError}
        refetchShowtimes={refetchShowtimes}
        hallNames={hallNames}
        groupedShowtimes={groupedShowtimes}
        setIsVideoVisible={setIsVideoVisible}
      />

      {/* Video Modal (Shared) */}
      <AnimatePresence>
        {isVideoVisible && selectedMovie?.trailerKey && (
          <TrailerDialog
            trailerKey={selectedMovie.trailerKey}
            setIsVideoVisible={setIsVideoVisible}
          />
        )}
      </AnimatePresence>
    </>
  );
};

export default Showtimes;
