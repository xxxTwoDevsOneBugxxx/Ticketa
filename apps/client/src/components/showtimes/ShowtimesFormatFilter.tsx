import React from "react";
import { motion } from "framer-motion";
import type { HallFormat } from "@/types/showtimes";

export type FormatFilterOption = "ALL" | HallFormat;

interface ShowtimesFormatFilterProps {
  selectedFormat: FormatFilterOption;
  onSelectFormat: (format: FormatFilterOption) => void;
  formatCounts: Record<FormatFilterOption, number>;
}

export const ShowtimesFormatFilter: React.FC<ShowtimesFormatFilterProps> = ({
  selectedFormat,
  onSelectFormat,
  formatCounts,
}) => {
  const options: { id: FormatFilterOption; label: string; activeColor: string }[] = [
    {
      id: "ALL",
      label: "All Formats",
      activeColor: "bg-white/10 text-white border-white/40 shadow-sm",
    },
    {
      id: "IMAX",
      label: "IMAX",
      activeColor: "bg-cyan-500/15 text-cyan-400 border-cyan-400/50 shadow-[0_0_12px_rgba(6,182,212,0.25)]",
    },
    {
      id: "Standard",
      label: "Standard",
      activeColor: "bg-slate-400/15 text-slate-200 border-slate-300/50 shadow-sm",
    },
    {
      id: "Gold",
      label: "Gold",
      activeColor: "bg-amber-500/15 text-amber-400 border-amber-400/50 shadow-[0_0_12px_rgba(245,158,11,0.25)]",
    },
  ];

  return (
    <div className="w-full bg-[#09090B] py-2.5 px-4 border-b border-white/5">
      <div className="flex items-center gap-2 overflow-x-auto no-scrollbar">
        {options.map((opt) => {
          const isSelected = selectedFormat === opt.id;
          const count = formatCounts[opt.id] ?? 0;

          return (
            <motion.button
              key={opt.id}
              type="button"
              whileTap={{ scale: 0.95 }}
              onClick={() => onSelectFormat(opt.id)}
              className={`flex-shrink-0 flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold tracking-wide transition-all duration-200 border ${
                isSelected
                  ? opt.activeColor
                  : "bg-[#141418] text-muted-foreground border-white/5 hover:border-white/15 hover:text-white"
              }`}
            >
              <span>{opt.label}</span>
              <span
                className={`text-[10px] px-1.5 py-0.2 rounded-full font-mono ${
                  isSelected
                    ? "bg-white/20 text-white"
                    : "bg-white/5 text-muted-foreground"
                }`}
              >
                {count}
              </span>
            </motion.button>
          );
        })}
      </div>
    </div>
  );
};
