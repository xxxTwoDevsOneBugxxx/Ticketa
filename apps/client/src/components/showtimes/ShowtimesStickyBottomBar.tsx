import React from "react";
import { useNavigate } from "react-router-dom";
import { motion, AnimatePresence } from "framer-motion";
import { format, parseISO, isToday } from "date-fns";
import { ArrowRight, X } from "lucide-react";
import type { MoviesShowtimes, Showtime } from "@/types/showtimes";
import { getHallFormat } from "@/types/showtimes";

interface ShowtimesStickyBottomBarProps {
  selectedShowtime: Showtime | null;
  selectedMovie: MoviesShowtimes | null;
  onClear: () => void;
}

export const ShowtimesStickyBottomBar: React.FC<ShowtimesStickyBottomBarProps> = ({
  selectedShowtime,
  selectedMovie,
  onClear,
}) => {
  const navigate = useNavigate();

  if (!selectedShowtime || !selectedMovie) return null;

  const formatKey = getHallFormat(
    selectedShowtime.hallName,
    selectedShowtime.hallType
  );

  const formattedTime = (() => {
    try {
      const d = parseISO(selectedShowtime.startTime);
      const dayPrefix = isToday(d) ? "Today" : format(d, "EEE, MMM d");
      return `${dayPrefix} at ${format(d, "HH:mm")}`;
    } catch {
      return selectedShowtime.startTime;
    }
  })();

  const formatBadgeStyles =
    formatKey === "IMAX"
      ? "bg-cyan-500/15 text-cyan-400 border-cyan-500/30"
      : formatKey === "Gold"
      ? "bg-amber-500/15 text-amber-400 border-amber-500/30"
      : "bg-slate-500/15 text-slate-300 border-slate-500/30";

  const handleProceed = () => {
    navigate(`/showtimes/${selectedShowtime.id}`);
  };

  return (
    <AnimatePresence>
      <motion.div
        initial={{ y: 100, opacity: 0 }}
        animate={{ y: 0, opacity: 1 }}
        exit={{ y: 100, opacity: 0 }}
        transition={{ type: "spring", stiffness: 350, damping: 30 }}
        className="fixed bottom-0 left-0 right-0 z-50 p-4 bg-[#141418]/95 backdrop-blur-xl border-t border-white/10 shadow-[0_-12px_40px_rgba(0,0,0,0.8)] lg:hidden"
      >
        <div className="flex items-center justify-between gap-3 max-w-lg mx-auto">
          {/* Selected Details */}
          <div className="flex-1 min-w-0 pr-2">
            <div className="flex items-center gap-2">
              <span
                className={`text-[10px] font-black uppercase tracking-widest px-1.5 py-0.5 rounded border ${formatBadgeStyles}`}
              >
                {formatKey}
              </span>
              <h4 className="text-sm font-extrabold text-white truncate">
                {selectedMovie.title}
              </h4>
            </div>

            <p className="text-xs text-muted-foreground mt-0.5 truncate">
              {selectedShowtime.hallName} · {formattedTime}
            </p>

            <p className="text-xs font-black text-white mt-1">
              ${selectedShowtime.price}{" "}
              <span className="text-[10px] font-normal text-muted-foreground">
                / ticket
              </span>
            </p>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center gap-2 shrink-0">
            {/* Clear Selection */}
            <button
              type="button"
              onClick={onClear}
              className="w-10 h-10 rounded-xl bg-white/5 hover:bg-white/10 text-muted-foreground hover:text-white flex items-center justify-center border border-white/10 transition-colors"
              aria-label="Clear selection"
            >
              <X className="w-4 h-4" />
            </button>

            {/* Select Seats CTA */}
            <motion.button
              type="button"
              whileTap={{ scale: 0.95 }}
              onClick={handleProceed}
              className="flex items-center gap-2 px-5 h-11 rounded-xl bg-primary text-primary-foreground font-black text-sm tracking-wide shadow-[0_4px_20px_rgba(242,97,43,0.4)] border border-primary hover:bg-primary/95 transition-all"
            >
              <span>Select Seats</span>
              <ArrowRight className="w-4 h-4" />
            </motion.button>
          </div>
        </div>
      </motion.div>
    </AnimatePresence>
  );
};
