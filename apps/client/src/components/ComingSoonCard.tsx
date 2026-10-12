import type { Movie } from "@/types/movie";
import { Star, Ticket } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { motion, AnimatePresence } from "framer-motion";
import { useState } from "react";
import { TMDB_IMAGE_POSTER_URL } from "@/api/constants";

interface Props {
  movie: Movie;
}

const ComingSoonCard = ({ movie }: Props) => {
  const [isHovered, setIsHovered] = useState(false);

  return (
    <div
      className="group relative bg-[#121212] text-white rounded-2xl overflow-hidden transition-all duration-500 border border-white/5 hover:border-primary/50 cursor-pointer"
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
    >
      <div className="relative aspect-2/3 overflow-hidden">
        <motion.img
          src={`${TMDB_IMAGE_POSTER_URL}${movie.posterPath}`}
          alt={movie.title}
          loading="lazy"
          animate={{ scale: isHovered ? 1.05 : 1 }}
          transition={{ duration: 0.8, ease: [0.33, 1, 0.68, 1] }}
          className="w-full h-full object-cover grayscale"
        />

        <div className="absolute inset-0 bg-linear-to-t from-black via-black/60 to-transparent opacity-80 group-hover:opacity-100 transition-all duration-500" />

        <AnimatePresence>
          {isHovered && (
            <motion.div
              initial={{ opacity: 0, scale: 0.8 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.8 }}
              className="absolute top-3 left-3"
            >
              {movie.genres?.[0] && (
                <Badge className="bg-orange-600 hover:bg-orange-700 text-[10px] font-black uppercase tracking-wider px-3 py-1 border-none shadow-xl">
                  {movie.genres[0]}
                </Badge>
              )}
            </motion.div>
          )}
        </AnimatePresence>

        <AnimatePresence>
          {isHovered && (
            <motion.div
              initial={{ opacity: 0, scale: 0.8 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.8 }}
              className="absolute top-3 right-3 bg-black/60 backdrop-blur-xl px-2 py-1.5 rounded-xl flex items-center gap-1.5 border border-white/10 shadow-2xl z-10"
            >
              <Star className="w-3.5 h-3.5 fill-yellow-500 text-yellow-500" />
              <span className="text-xs font-black text-white">
                {Number(movie.voteAverage).toFixed(1)}
              </span>
            </motion.div>
          )}
        </AnimatePresence>

        <motion.div
          animate={{ y: isHovered ? 0 : 44 }}
          transition={{ duration: 0.4, ease: [0.33, 1, 0.68, 1] }}
          className="absolute inset-x-0 bottom-0 px-4 pb-4 pt-8 bg-linear-to-t from-black via-black/90 to-transparent flex flex-col gap-4"
        >
          <div className="space-y-1">
            <h3 className="text-xl font-black text-white leading-tight drop-shadow-lg">
              {movie.title}
            </h3>
            <div className="flex items-center gap-2 text-[8px] font-bold text-gray-300 uppercase tracking-widest">
              {movie.genres?.slice(0, 3).join(" • ") ?? ""}
            </div>
          </div>

          <motion.div
            animate={{ opacity: isHovered ? 1 : 0 }}
            transition={{ duration: 0.2 }}
          >
            <Button className="w-full bg-primary hover:bg-primary/90 text-white font-bold rounded-xl py-2.5 transition-all active:scale-95 shadow-lg shadow-primary/25 gap-2">
              <Ticket className="w-4 h-4" />
              View Details
            </Button>
          </motion.div>
        </motion.div>
      </div>
    </div>
  );
};

export default ComingSoonCard;
