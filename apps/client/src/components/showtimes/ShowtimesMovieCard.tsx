import React, { useMemo } from "react";
import { Film, Star, Clock } from "lucide-react";
import { motion } from "framer-motion";
import type { MoviesShowtimes, Showtime, HallFormat } from "@/types/showtimes";
import { getHallFormat } from "@/types/showtimes";
import { ShowtimesTimeSlot } from "./ShowtimesTimeSlot";

interface ShowtimesMovieCardProps {
  movie: MoviesShowtimes;
  showtimesForDay: Showtime[];
  selectedShowtime?: Showtime | null;
  onSelectShowtime: (showtime: Showtime) => void;
}

const TMDB_IMAGE_BASE_URL = "https://image.tmdb.org/t/p";

export const ShowtimesMovieCard: React.FC<ShowtimesMovieCardProps> = ({
  movie,
  showtimesForDay,
  selectedShowtime = null,
  onSelectShowtime,
}) => {
  // Group showtimes by format (IMAX, Standard, Gold)
  const groupedByFormat = useMemo(() => {
    const groups: Record<HallFormat, Showtime[]> = {
      IMAX: [],
      Standard: [],
      Gold: [],
    };

    showtimesForDay.forEach((st) => {
      const format = getHallFormat(st.hallName, st.hallType);
      groups[format].push(st);
    });

    return groups;
  }, [showtimesForDay]);

  // Formats that actually have sessions today
  const activeFormats = useMemo(() => {
    return (["IMAX", "Standard", "Gold"] as HallFormat[]).filter(
      (fmt) => groupedByFormat[fmt].length > 0
    );
  }, [groupedByFormat]);

  // Format runtime in hours and minutes (e.g. 166m -> 2h 46m)
  const formattedRuntime = useMemo(() => {
    if (!movie.runtime) return null;
    const hours = Math.floor(movie.runtime / 60);
    const mins = movie.runtime % 60;
    return hours > 0 ? `${hours}h ${mins}m` : `${mins}m`;
  }, [movie.runtime]);

  const posterUrl = movie.posterPath
    ? movie.posterPath.startsWith("http")
      ? movie.posterPath
      : `${TMDB_IMAGE_BASE_URL}/w300${movie.posterPath}`
    : null;

  return (
    <motion.div
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: -12 }}
      transition={{ duration: 0.25 }}
      className="bg-[#141418] border border-white/5 rounded-2xl p-4 shadow-xl space-y-4"
    >
      {/* Movie Header / Info */}
      <div className="flex gap-3.5 items-start">
        {/* Poster Thumbnail */}
        <div className="w-16 h-24 rounded-xl overflow-hidden bg-black/40 border border-white/5 flex-shrink-0 relative">
          {posterUrl ? (
            <img
              src={posterUrl}
              alt={movie.title}
              className="w-full h-full object-cover"
              loading="lazy"
            />
          ) : (
            <div className="w-full h-full flex items-center justify-center text-muted-foreground">
              <Film className="w-6 h-6 opacity-40" />
            </div>
          )}
        </div>

        {/* Movie Title & Badges */}
        <div className="flex-1 min-w-0 space-y-1.5">
          <h3 className="text-base font-extrabold text-white tracking-tight leading-snug line-clamp-1">
            {movie.title}
          </h3>

          {/* Metadata Row: Rating, Duration, Genres */}
          <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
            {movie.rate !== undefined && movie.rate > 0 && (
              <span className="flex items-center gap-1 text-amber-400 font-bold">
                <Star className="w-3.5 h-3.5 fill-amber-400 text-amber-400" />
                {movie.rate.toFixed(1)}
              </span>
            )}

            {formattedRuntime && (
              <span className="flex items-center gap-1 text-muted-foreground/80 font-medium">
                <Clock className="w-3 h-3" />
                {formattedRuntime}
              </span>
            )}
          </div>

          {/* Genres */}
          {movie.genres && movie.genres.length > 0 && (
            <div className="flex flex-wrap gap-1.5 pt-0.5">
              {movie.genres.slice(0, 3).map((genre) => (
                <span
                  key={genre}
                  className="text-[10px] uppercase font-semibold tracking-wider text-muted-foreground bg-white/5 px-2 py-0.5 rounded-md border border-white/5"
                >
                  {genre}
                </span>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Formats and Sessions */}
      <div className="space-y-3.5 pt-1 border-t border-white/5">
        {activeFormats.map((formatKey) => {
          const sessions = groupedByFormat[formatKey];
          const badgeStyles =
            formatKey === "IMAX"
              ? "bg-cyan-500/15 text-cyan-400 border-cyan-500/30"
              : formatKey === "Gold"
              ? "bg-amber-500/15 text-amber-400 border-amber-500/30"
              : "bg-slate-500/15 text-slate-300 border-slate-500/30";

          return (
            <div key={formatKey} className="space-y-2">
              <div className="flex items-center gap-2">
                <span
                  className={`text-[11px] font-black tracking-widest uppercase px-2 py-0.5 rounded-md border ${badgeStyles}`}
                >
                  {formatKey}
                </span>
                <span className="text-[11px] text-muted-foreground font-medium">
                  {sessions[0]?.hallName || `${formatKey} Hall`}
                </span>
              </div>

              {/* Time Slots Chips Row */}
              <div className="flex flex-wrap gap-2">
                {sessions.map((st) => {
                  const isSelected = selectedShowtime?.id === st.id;
                  return (
                    <ShowtimesTimeSlot
                      key={st.id}
                      showtime={st}
                      isSelected={isSelected}
                      onSelect={onSelectShowtime}
                    />
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>
    </motion.div>
  );
};
