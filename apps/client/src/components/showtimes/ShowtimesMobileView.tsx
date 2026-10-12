import React, { useMemo } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { format, parseISO, startOfDay, isValid, addDays, isSameDay } from "date-fns";
import { CalendarX, RotateCcw, Calendar, Layers } from "lucide-react";
import { motion, AnimatePresence } from "framer-motion";
import type { MoviesShowtimes, Showtime } from "@/types/showtimes";
import { getHallFormat } from "@/types/showtimes";
import { ShowtimesDateStrip } from "./ShowtimesDateStrip";
import {
  ShowtimesFormatFilter,
  type FormatFilterOption,
} from "./ShowtimesFormatFilter";
import { ShowtimesMovieCard } from "./ShowtimesMovieCard";

interface ShowtimesMobileViewProps {
  moviesList: MoviesShowtimes[];
  isShowtimesLoading: boolean;
  isShowtimesError: boolean;
  refetchShowtimes: () => void;
}

export const ShowtimesMobileView: React.FC<ShowtimesMobileViewProps> = ({
  moviesList,
  isShowtimesLoading,
  isShowtimesError,
  refetchShowtimes,
}) => {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();

  // 1. Date selection state (persisted in URL search params & sessionStorage)
  const selectedDate = useMemo(() => {
    const dateParam =
      searchParams.get("date") ||
      sessionStorage.getItem("ticketa_showtimes_date");
    if (dateParam) {
      try {
        const parsed = parseISO(dateParam);
        if (isValid(parsed)) {
          return startOfDay(parsed);
        }
      } catch {
        // fallback to today
      }
    }
    return startOfDay(new Date());
  }, [searchParams]);

  const handleSelectDate = (date: Date) => {
    const dateStr = format(date, "yyyy-MM-dd");
    sessionStorage.setItem("ticketa_showtimes_date", dateStr);
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        next.set("date", dateStr);
        return next;
      },
      { replace: true }
    );
  };

  // 2. Format filter state (persisted in URL search params & sessionStorage)
  const selectedFormat = useMemo<FormatFilterOption>(() => {
    const fmtParam = (searchParams.get("format") ||
      sessionStorage.getItem("ticketa_showtimes_format")) as FormatFilterOption | null;
    if (fmtParam && ["ALL", "IMAX", "Standard", "Gold"].includes(fmtParam)) {
      return fmtParam;
    }
    return "ALL";
  }, [searchParams]);

  const handleSelectFormat = (fmt: FormatFilterOption) => {
    sessionStorage.setItem("ticketa_showtimes_format", fmt);
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (fmt === "ALL") {
          next.delete("format");
        } else {
          next.set("format", fmt);
        }
        return next;
      },
      { replace: true }
    );
  };

  // Format string for date comparisons: "yyyy-MM-dd"
  const selectedDateStr = useMemo(
    () => format(selectedDate, "yyyy-MM-dd"),
    [selectedDate]
  );

  // Directly navigate to seat selection when time is tapped
  const handleShowtimeClick = (showtime: Showtime) => {
    navigate(`/showtimes/${showtime.id}`);
  };

  // Compute format session counts for the currently selected day
  const formatCounts = useMemo(() => {
    const counts: Record<FormatFilterOption, number> = {
      ALL: 0,
      IMAX: 0,
      Standard: 0,
      Gold: 0,
    };

    moviesList.forEach((movie) => {
      movie.showtimes.forEach((st) => {
        try {
          if (format(parseISO(st.startTime), "yyyy-MM-dd") === selectedDateStr) {
            counts.ALL += 1;
            const fmt = getHallFormat(st.hallName, st.hallType);
            counts[fmt] += 1;
          }
        } catch {
          // ignore parsing error
        }
      });
    });

    return counts;
  }, [moviesList, selectedDateStr]);

  // Find the next upcoming day that has available sessions
  const nextAvailableDate = useMemo(() => {
    const today = startOfDay(new Date());
    for (let i = 0; i < 7; i++) {
      const candidate = addDays(today, i);
      const candidateStr = format(candidate, "yyyy-MM-dd");
      if (candidateStr === selectedDateStr) continue;

      const hasSessions = moviesList.some((m) =>
        m.showtimes.some((st) => {
          try {
            return format(parseISO(st.startTime), "yyyy-MM-dd") === candidateStr;
          } catch {
            return false;
          }
        })
      );
      if (hasSessions) {
        return candidate;
      }
    }
    return null;
  }, [moviesList, selectedDateStr]);

  // Movies with their matching showtimes for the selected day and active format filter
  const moviesWithFilteredShowtimes = useMemo(() => {
    return moviesList
      .map((movie) => {
        // Filter showtimes for this movie on the chosen date
        const dayShowtimes = movie.showtimes.filter((st) => {
          try {
            return (
              format(parseISO(st.startTime), "yyyy-MM-dd") === selectedDateStr
            );
          } catch {
            return false;
          }
        });

        // Filter by format if not ALL
        const formatShowtimes =
          selectedFormat === "ALL"
            ? dayShowtimes
            : dayShowtimes.filter(
                (st) =>
                  getHallFormat(st.hallName, st.hallType) === selectedFormat
              );

        return {
          movie,
          showtimes: formatShowtimes,
        };
      })
      .filter(({ showtimes }) => {
        // Must have at least one session on this day with this format
        return showtimes.length > 0;
      });
  }, [moviesList, selectedDateStr, selectedFormat]);

  return (
    <div className="block lg:hidden min-h-screen bg-[#09090B] text-foreground pb-16">
      {/* 1. 7-Day Horizontal Date Strip (Preserves selected day across navigation) */}
      <ShowtimesDateStrip
        selectedDate={selectedDate}
        onSelectDate={handleSelectDate}
        daysCount={7}
      />

      {/* 2. Formats Filter (All, IMAX, Standard, Gold) */}
      <ShowtimesFormatFilter
        selectedFormat={selectedFormat}
        onSelectFormat={handleSelectFormat}
        formatCounts={formatCounts}
      />

      {/* 3. Movie Sessions Content */}
      <main className="p-4 space-y-4">
        {isShowtimesLoading ? (
          // Loading Skeletons
          <div className="space-y-4">
            {[1, 2, 3].map((i) => (
              <div
                key={i}
                className="bg-[#141418] border border-white/5 rounded-2xl p-4 animate-pulse space-y-4"
              >
                <div className="flex gap-3">
                  <div className="w-16 h-24 bg-white/10 rounded-xl" />
                  <div className="flex-1 space-y-2 py-1">
                    <div className="h-4 bg-white/10 rounded w-3/4" />
                    <div className="h-3 bg-white/10 rounded w-1/2" />
                    <div className="h-3 bg-white/5 rounded w-1/3" />
                  </div>
                </div>
                <div className="pt-2 border-t border-white/5 flex gap-2">
                  <div className="w-18 h-10 bg-white/10 rounded-xl" />
                  <div className="w-18 h-10 bg-white/10 rounded-xl" />
                  <div className="w-18 h-10 bg-white/10 rounded-xl" />
                </div>
              </div>
            ))}
          </div>
        ) : isShowtimesError ? (
          // Error State
          <div className="bg-[#141418] border border-destructive/20 rounded-2xl p-6 text-center space-y-3">
            <p className="text-sm text-destructive font-semibold">
              Unable to load sessions for this cinema.
            </p>
            <button
              type="button"
              onClick={refetchShowtimes}
              className="px-4 py-2 bg-primary text-primary-foreground text-xs font-bold rounded-xl cursor-pointer"
            >
              Try Again
            </button>
          </div>
        ) : moviesWithFilteredShowtimes.length > 0 ? (
          // Movie Cards
          <AnimatePresence mode="popLayout">
            {moviesWithFilteredShowtimes.map(({ movie, showtimes }) => (
              <ShowtimesMovieCard
                key={String(movie.movieId)}
                movie={movie}
                showtimesForDay={showtimes}
                onSelectShowtime={handleShowtimeClick}
              />
            ))}
          </AnimatePresence>
        ) : (
          // Enhanced Empty State with Prominent Action Button
          <motion.div
            initial={{ opacity: 0, scale: 0.96 }}
            animate={{ opacity: 1, scale: 1 }}
            className="py-12 px-6 text-center space-y-5 bg-[#141418] border border-white/10 rounded-2xl shadow-xl max-w-sm mx-auto my-6"
          >
            {/* Luminous Icon Badge */}
            <div className="w-14 h-14 rounded-2xl bg-primary/10 border border-primary/25 flex items-center justify-center mx-auto text-primary shadow-[0_0_24px_rgba(242,97,43,0.2)]">
              {selectedFormat !== "ALL" && formatCounts.ALL > 0 ? (
                <Layers className="w-6 h-6 text-primary" />
              ) : (
                <CalendarX className="w-6 h-6 text-primary" />
              )}
            </div>

            {/* Contextual Empty State Text */}
            <div className="space-y-1.5">
              <h4 className="text-base font-extrabold text-white tracking-tight">
                {selectedFormat !== "ALL"
                  ? `No ${selectedFormat} Sessions`
                  : "No Sessions Available"}
              </h4>
              <p className="text-xs text-muted-foreground leading-relaxed">
                {selectedFormat !== "ALL" && formatCounts.ALL > 0 ? (
                  <>
                    No <span className="text-white font-medium">{selectedFormat}</span> screenings on{" "}
                    <span className="text-white font-medium">{format(selectedDate, "EEE, MMM d")}</span>, but{" "}
                    <span className="text-primary font-bold">{formatCounts.ALL}</span> other sessions are available.
                  </>
                ) : (
                  <>
                    There are no sessions scheduled on{" "}
                    <span className="text-white font-medium">{format(selectedDate, "EEEE, MMMM do")}</span>.
                  </>
                )}
              </p>
            </div>

            {/* Enhanced Action Buttons */}
            <div className="flex flex-col items-center gap-2 pt-1">
              {selectedFormat !== "ALL" && formatCounts.ALL > 0 ? (
                <motion.button
                  type="button"
                  whileTap={{ scale: 0.95 }}
                  onClick={() => handleSelectFormat("ALL")}
                  className="w-full inline-flex items-center justify-center gap-2 px-5 py-3 rounded-xl bg-primary text-primary-foreground font-black text-xs uppercase tracking-wider shadow-[0_4px_20px_rgba(242,97,43,0.35)] hover:bg-primary/95 transition-all cursor-pointer border border-primary"
                >
                  <RotateCcw className="w-4 h-4" />
                  <span>Show All Formats</span>
                  <span className="ml-1 px-2 py-0.5 rounded-full bg-black/25 text-[11px] font-mono font-bold">
                    {formatCounts.ALL}
                  </span>
                </motion.button>
              ) : nextAvailableDate ? (
                <>
                  <motion.button
                    type="button"
                    whileTap={{ scale: 0.95 }}
                    onClick={() => {
                      handleSelectDate(nextAvailableDate);
                      handleSelectFormat("ALL");
                    }}
                    className="w-full inline-flex items-center justify-center gap-2 px-5 py-3 rounded-xl bg-primary text-primary-foreground font-black text-xs uppercase tracking-wider shadow-[0_4px_20px_rgba(242,97,43,0.35)] hover:bg-primary/95 transition-all cursor-pointer border border-primary"
                  >
                    <Calendar className="w-4 h-4" />
                    <span>View {format(nextAvailableDate, "EEE, MMM d")}</span>
                  </motion.button>

                  {!isSameDay(selectedDate, new Date()) && (
                    <button
                      type="button"
                      onClick={() => {
                        handleSelectDate(new Date());
                        handleSelectFormat("ALL");
                      }}
                      className="text-xs font-semibold text-muted-foreground hover:text-white transition-colors cursor-pointer pt-1"
                    >
                      Back to Today
                    </button>
                  )}
                </>
              ) : !isSameDay(selectedDate, new Date()) ? (
                <motion.button
                  type="button"
                  whileTap={{ scale: 0.95 }}
                  onClick={() => {
                    handleSelectDate(new Date());
                    handleSelectFormat("ALL");
                  }}
                  className="w-full inline-flex items-center justify-center gap-2 px-5 py-3 rounded-xl bg-[#1A1A22] text-white font-bold text-xs uppercase tracking-wider border border-white/10 hover:border-white/20 transition-all cursor-pointer"
                >
                  <RotateCcw className="w-4 h-4" />
                  <span>Back to Today</span>
                </motion.button>
              ) : null}
            </div>
          </motion.div>
        )}
      </main>
    </div>
  );
};
