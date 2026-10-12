import React, { useMemo, useRef, useEffect } from "react";
import { format, addDays, isSameDay, startOfDay } from "date-fns";
import { motion } from "framer-motion";

interface ShowtimesDateStripProps {
  selectedDate: Date;
  onSelectDate: (date: Date) => void;
  daysCount?: number;
}

export const ShowtimesDateStrip: React.FC<ShowtimesDateStripProps> = ({
  selectedDate,
  onSelectDate,
  daysCount = 7,
}) => {
  const dates = useMemo(() => {
    const today = startOfDay(new Date());
    return Array.from({ length: daysCount }, (_, i) => addDays(today, i));
  }, [daysCount]);

  const buttonRefs = useRef<(HTMLButtonElement | null)[]>([]);

  // Automatically scroll the selected date into view when returning or selecting
  useEffect(() => {
    const selectedIdx = dates.findIndex((d) => isSameDay(d, selectedDate));
    if (selectedIdx !== -1 && buttonRefs.current[selectedIdx]) {
      buttonRefs.current[selectedIdx]?.scrollIntoView({
        behavior: "smooth",
        inline: "center",
        block: "nearest",
      });
    }
  }, [selectedDate, dates]);

  return (
    <div className="w-full bg-[#09090B] py-3 border-b border-white/5">
      <div className="flex items-center gap-2 px-4 overflow-x-auto no-scrollbar scroll-smooth">
        {dates.map((date, idx) => {
          const isSelected = isSameDay(date, selectedDate);
          const isToday = idx === 0;

          return (
            <motion.button
              key={date.toISOString()}
              ref={(el) => {
                buttonRefs.current[idx] = el;
              }}
              type="button"
              whileTap={{ scale: 0.94 }}
              onClick={() => onSelectDate(date)}
              className={`flex-shrink-0 flex flex-col items-center justify-center w-[60px] h-[72px] rounded-2xl transition-all duration-200 border cursor-pointer ${
                isSelected
                  ? "bg-primary text-primary-foreground border-primary shadow-[0_4px_16px_rgba(242,97,43,0.35)]"
                  : "bg-[#141418] text-muted-foreground border-white/5 hover:border-white/15 hover:text-white"
              }`}
            >
              {/* Day Label (e.g. MON / TODAY) */}
              <span
                className={`text-[10px] font-bold tracking-wider uppercase ${
                  isSelected
                    ? "text-primary-foreground/90 font-extrabold"
                    : isToday
                    ? "text-primary font-semibold"
                    : "text-muted-foreground"
                }`}
              >
                {isToday ? "TODAY" : format(date, "EEE")}
              </span>

              {/* Day Number (e.g. 12) */}
              <span
                className={`text-xl font-black mt-0.5 ${
                  isSelected ? "text-white" : "text-white/90"
                }`}
              >
                {format(date, "d")}
              </span>

              {/* Month Short (e.g. OCT) */}
              <span
                className={`text-[9px] uppercase tracking-wider ${
                  isSelected
                    ? "text-primary-foreground/80 font-medium"
                    : "text-muted-foreground/60"
                }`}
              >
                {format(date, "MMM")}
              </span>
            </motion.button>
          );
        })}
      </div>
    </div>
  );
};
