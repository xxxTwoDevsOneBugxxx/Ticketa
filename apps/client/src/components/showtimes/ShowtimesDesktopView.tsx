import React from "react";
import { Link } from "react-router-dom";
import { motion, AnimatePresence } from "framer-motion";
import { Ticket, Film, AlertCircle, RefreshCw } from "lucide-react";
import { format, parseISO } from "date-fns";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import HeroShowtimes from "@/components/HeroShowtimes";
import ShowtimesCards from "@/components/ShowtimesCards";
import { SessionsSkeleton } from "@/components/skeletons/ShowtimeSkeleton";
import type { MoviesShowtimes, Showtime } from "@/types/showtimes";

const TMDB_IMAGE_BASE_URL = "https://image.tmdb.org/t/p";

interface ShowtimesDesktopViewProps {
  selectedMovie: MoviesShowtimes | null;
  moviesList: MoviesShowtimes[];
  selectedMovieId: string | number | null;
  handleMovieSelect: (id: string | number) => void;
  isShowtimesLoading: boolean;
  isShowtimesError: boolean;
  refetchShowtimes: () => void;
  hallNames: string[];
  groupedShowtimes: Record<string, Showtime[]>;
  setIsVideoVisible: (visible: boolean) => void;
}

export const ShowtimesDesktopView: React.FC<ShowtimesDesktopViewProps> = ({
  selectedMovie,
  moviesList,
  selectedMovieId,
  handleMovieSelect,
  isShowtimesLoading,
  isShowtimesError,
  refetchShowtimes,
  hallNames,
  groupedShowtimes,
  setIsVideoVisible,
}) => {
  return (
    <div className="hidden lg:block min-h-screen bg-background text-foreground pb-20">
      {/* Hero Section for Selected Movie */}
      <AnimatePresence mode="wait">
        {selectedMovie && (
          <HeroShowtimes
            key={String(selectedMovie.movieId)}
            movieId={String(selectedMovie.movieId)}
            title={selectedMovie.title}
            posterPath={selectedMovie.posterPath || ""}
            trailerKey={selectedMovie.trailerKey || ""}
            setIsVideoVisible={setIsVideoVisible}
          />
        )}
      </AnimatePresence>

      <div className="container mx-auto px-4 -mt-6 relative z-20">
        <div className="grid grid-cols-12 gap-8">
          {/* Movie Selection Sidebar */}
          <div className="col-span-4 space-y-6">
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
                    key={String(movie.movieId)}
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

          {/* Showtimes Sessions Display */}
          <div className="col-span-8 space-y-8">
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
                  onClick={refetchShowtimes}
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
                        Showing sessions for{" "}
                        <span className="text-foreground font-semibold">
                          {selectedMovie.title}
                        </span>
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
                            <div className="grid grid-cols-2 gap-4">
                              {showtimes.map((showtime, index) => (
                                <Link
                                  to={`/showtimes/${showtime.id}`}
                                  key={String(showtime.id)}
                                >
                                  <ShowtimesCards
                                    key={String(showtime.id)}
                                    showtime={showtime}
                                    index={index}
                                  />
                                </Link>
                              ))}
                            </div>
                          </div>
                        )
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
    </div>
  );
};
