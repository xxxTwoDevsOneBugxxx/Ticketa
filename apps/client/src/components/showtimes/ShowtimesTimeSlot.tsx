import React, { useMemo } from "react";
import { format, parseISO } from "date-fns";
import { motion } from "framer-motion";
import type { Showtime } from "@/types/showtimes";

interface ShowtimesTimeSlotProps {
  showtime: Showtime;
  onSelect: (showtime: Showtime) => void;
  isSelected?: boolean;
  isPast?: boolean;
}

export const ShowtimesTimeSlot: React.FC<ShowtimesTimeSlotProps> = ({
  showtime,
  onSelect,
  isSelected = false,
  isPast = false,
}) => {
  const formattedTime = useMemo(() => {
    try {
      return format(parseISO(showtime.startTime), "HH:mm");
    } catch {
      return showtime.startTime;
    }
  }, [showtime.startTime]);

  if (isPast) {
    return (
      <div
        className="flex flex-col items-center justify-center min-w-[76px] py-2 px-3 rounded-xl bg-[#141418]/50 border border-white/5 opacity-40 cursor-not-allowed select-none"
        title="Session already started or ended"
      >
        <span className="text-sm font-bold text-muted-foreground line-through">
          {formattedTime}
        </span>
        <span className="text-[10px] text-muted-foreground/60 font-medium">
          Passed
        </span>
      </div>
    );
  }

  return (
    <motion.button
      type="button"
      whileTap={{ scale: 0.93 }}
      onClick={() => onSelect(showtime)}
      className={`flex flex-col items-center justify-center min-w-[76px] py-2 px-3 rounded-xl transition-all duration-200 border cursor-pointer ${
        isSelected
          ? "bg-primary text-primary-foreground border-primary shadow-[0_4px_16px_rgba(242,97,43,0.35)] ring-2 ring-primary/40 ring-offset-2 ring-offset-[#09090B]"
          : "bg-[#141418] text-white border-white/10 hover:border-primary/50 hover:bg-[#1A1A20] active:bg-primary active:text-primary-foreground active:border-primary active:shadow-[0_4px_16px_rgba(242,97,43,0.35)]"
      }`}
    >
      <span className="text-sm font-extrabold tracking-tight">
        {formattedTime}
      </span>
      <span
        className={`text-[10px] font-semibold mt-0.5 ${
          isSelected
            ? "text-primary-foreground/90"
            : "text-muted-foreground group-hover:text-white"
        }`}
      >
        ${showtime.price}
      </span>
    </motion.button>
  );
};
