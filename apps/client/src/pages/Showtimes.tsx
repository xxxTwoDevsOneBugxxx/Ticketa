import { useGetShowtimes } from "@/hooks/useShowtimes";
import { useNowPlayingMovies } from "@/hooks/useMovies";
import { Ticket, AlertCircle, RefreshCw, Film } from "lucide-react";
import { Link, useSearchParams } from "react-router-dom";
import { motion, AnimatePresence } from "framer-motion";
import { useMemo, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { format, parseISO } from "date-fns";
import ErrorState from "@/components/ErrorState";
import HeroShowtimes from "@/components/HeroShowtimes";
import TrailerDialog from "@/components/TrailerDialog";
import ShowtimesCards from "@/components/ShowtimesCards";
import { Separator } from "@/components/ui/separator";
import ShowtimeSkeleton, { SessionsSkeleton } from "@/components/skeletons/ShowtimeSkeleton";
import { TMDB_IMAGE_BASE_URL } from "@/api/constants";
import type { Showtime } from "@/types/showtimes";

interface DisplayMovie {
  movieId: string;
  title: string;
  posterPath: string;
  trailerKey: string;
  showtimes: Showtime[];
}

const Showtimes = () => {
  // 1. Fast Query: Fetch active movies list (cached or returns in ~600ms)
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
  const moviesList = useMemo<DisplayMovie[]>(() => {
    if (moviesWithShowtimes && moviesWithShowtimes.length > 0) {
      return moviesWithShowtimes.map((m) => ({
        movieId: String(m.movieId),
        title: m.title,
        posterPath: m.posterPath || "",
        trailerKey: m.trailerKey || "",
        showtimes: m.showtimes || [],
      }));
    }

    return baseMovies.map((m) => ({
      movieId: String(m.id),
      title: m.title,
      posterPath: m.posterPath || "",
      trailerKey: (m as any).trailerKey || "",
      showtimes: showtimesByMovieId.get(String(m.id)) || [],
    }));
  }, [baseMovies, moviesWithShowtimes, showtimesByMovieId]);

  // Currently selected movie for the Hero and Sessions views
  const selectedMovie = useMemo(() => {
    if (moviesList.length === 0) return null;
    return (
      moviesList.find((m) => String(m.movieId) === String(selectedMovieId)) ||
      moviesList[0]
    );
  }, [moviesList, selectedMovieId]);

  // Showtimes for the currently selected movie
  const selectedMovieShowtimes = useMemo(() => {
    if (!selectedMovie) return [];
    return showtimesByMovieId.get(String(selectedMovie.movieId)) ?? selectedMovie.showtimes ?? [];
  }, [selectedMovie, showtimesByMovieId]);

  // Group showtimes by day for selected movie
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
      {} as Record<string, Showtime[]>,
    );
  }, [selectedMovieShowtimes]);

  const hallNames = useMemo(
    () => [
      ...new Set(
        selectedMovieShowtimes.map((showtime) => showtime.hallName) ?? [],
      ),
    ],
    [selectedMovieShowtimes],
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
    <div className="min-h-screen bg-background text-foreground pb-20">
      {/* Hero Section for Selected Movie (Renders immediately as soon as movies arrive) */}
      <AnimatePresence mode="wait">
        {selectedMovie && (
          <HeroShowtimes
            key={selectedMovie.movieId}
            movieId={selectedMovie.movieId}
            title={selectedMovie.title}
            posterPath={selectedMovie.posterPath || ""}
            trailerKey={selectedMovie.trailerKey || ""}
            setIsVideoVisible={setIsVideoVisible}
          />
        )}
      </AnimatePresence>

      <div className="container mx-auto px-4 -mt-6 relative z-20">
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-8">
          {/* Movie Selection Sidebar (Loads immediately) */}
          <div className="lg:col-span-4 space-y-6">
            <div className="flex items-center justify-between">
              <h2 className="text-xl font-bold flex items-center gap-2">
                <Ticket className="w-5 h-5 text-primary" />
                Select Movie
              </h2>
              <span className="text-xs text-muted-foreground uppercase font-semibold">
                {moviesList.length} Titles
              </span>
            </div>

            <div className="flex flex-col gap-3 max-h-[70vh] overflow-y-auto pr-3 custom-scrollbar">
              {moviesList.map((movie) => {
                const isSelected =
                  String(selectedMovieId) === String(movie.movieId) ||
                  (!selectedMovieId &&
                    String(moviesList[0]?.movieId) === String(movie.movieId));

                return (
                  <motion.div
                    key={movie.movieId}
                    whileHover={{ x: 5 }}
                    whileTap={{ scale: 0.98 }}
                  >
                    <Card
                      className={`cursor-pointer transition-all duration-300 border-none overflow-hidden ${
                        isSelected
                          ? "bg-primary text-primary-foreground shadow-lg shadow-primary/20 ring-1 ring-primary"
                          : "bg-card hover:bg-accent"
                      }`}
                      onClick={() => handleMovieSelect(movie.movieId)}
                    >
                      <CardContent className="p-3">
                        <div className="flex gap-4 items-center">
                          <div className="w-12 h-18 rounded-md overflow-hidden shrink-0 bg-muted/40">
                            {movie.posterPath ? (
                              <img
                                src={`${TMDB_IMAGE_BASE_URL}/w200${movie.posterPath}`}
                                alt={movie.title}
                                className="w-full h-full object-cover"
                              />
                            ) : (
                              <div className="w-full h-full flex items-center justify-center">
                                <Film className="w-5 h-5 text-muted-foreground" />
                              </div>
                            )}
                          </div>
                          <div className="flex flex-col overflow-hidden flex-1">
                            <h3 className="font-bold truncate text-sm">
                              {movie.title}
                            </h3>
                            {isShowtimesLoading ? (
                              <span
                                className={`text-[11px] font-medium animate-pulse mt-0.5 ${
                                  isSelected
                                    ? "text-primary-foreground/90"
                                    : "text-primary"
                                }`}
                              >
                                Checking sessions...
                              </span>
                            ) : (
                              <p
                                className={`text-xs mt-0.5 ${
                                  isSelected
                                    ? "text-primary-foreground/80"
                                    : "text-muted-foreground"
                                }`}
                              >
                                {movie.showtimes.length > 0
                                  ? `${movie.showtimes.length} sessions available`
                                  : "No upcoming sessions"}
                              </p>
                            )}
                          </div>
                        </div>
                      </CardContent>
                    </Card>
                  </motion.div>
                );
              })}
            </div>
          </div>

          {/* Showtimes Sessions Display with Independent Suspense/Loading State */}
          <div className="lg:col-span-8 space-y-8">
            {isShowtimesLoading ? (
              <SessionsSkeleton movieTitle={selectedMovie?.title} />
            ) : isShowtimesError ? (
              <div className="bg-card rounded-3xl p-8 border border-destructive/20 text-center space-y-4 shadow-xl">
                <AlertCircle className="w-10 h-10 text-destructive mx-auto" />
                <h3 className="text-xl font-bold">Could not load sessions</h3>
                <p className="text-sm text-muted-foreground max-w-md mx-auto">
                  We encountered an issue fetching the showtime slots for this cinema.
                </p>
                <Button
                  onClick={() => refetchShowtimes()}
                  variant="outline"
                  className="gap-2 rounded-xl"
                >
                  <RefreshCw className="w-4 h-4" />
                  Try Again
                </Button>
              </div>
            ) : (
              <div className="bg-card rounded-3xl p-6 md:p-8 border border-white/5 shadow-xl">
                <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 mb-8">
                  <div className="space-y-1">
                    <h3 className="text-2xl font-bold">Available Sessions</h3>
                    {selectedMovie && (
                      <p className="text-xs text-muted-foreground">
                        Showing sessions for <span className="text-foreground font-semibold">{selectedMovie.title}</span>
                      </p>
                    )}
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {hallNames.map((hall) => (
                      <Badge
                        key={hall}
                        variant="outline"
                        className="text-primary border-primary/30 uppercase tracking-[0.2em] px-3 py-1"
                      >
                        {hall}
                      </Badge>
                    ))}
                  </div>
                </div>

                <div className="space-y-12">
                  <AnimatePresence mode="popLayout">
                    {Object.entries(groupedShowtimes).length > 0 ? (
                      Object.entries(groupedShowtimes).map(
                        ([date, showtimes]) => (
                          <div key={date} className="space-y-6">
                            <div className="flex items-center gap-4">
                              <h4 className="text-lg font-semibold whitespace-nowrap">
                                {format(parseISO(date), "EEEE, MMMM do")}
                              </h4>
                              <Separator className="flex-1" />
                            </div>
                            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                              {showtimes.map((showtime, index) => (
                                <Link
                                  to={`/showtimes/${showtime.id}`}
                                  key={showtime.id}
                                >
                                  <ShowtimesCards
                                    key={showtime.id}
                                    showtime={showtime}
                                    index={index}
                                  />
                                </Link>
                              ))}
                            </div>
                          </div>
                        ),
                      )
                    ) : (
                      <div className="col-span-full py-16 text-center text-muted-foreground border-2 border-dashed rounded-3xl space-y-2">
                        <p className="font-semibold text-lg">No sessions available</p>
                        <p className="text-xs text-muted-foreground">
                          There are currently no scheduled showtimes for this movie.
                        </p>
                      </div>
                    )}
                  </AnimatePresence>
                </div>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Video Modal */}
      <AnimatePresence>
        {isVideoVisible && selectedMovie?.trailerKey && (
          <TrailerDialog
            trailerKey={selectedMovie.trailerKey}
            setIsVideoVisible={setIsVideoVisible}
          />
        )}
      </AnimatePresence>
    </div>
  );
};

export default Showtimes;
