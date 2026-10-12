import { useState, useEffect } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { Ticket, Info, Clock, ChevronRight, DollarSign } from "lucide-react";
import { Button } from "@/components/ui/button";
import type { MostPopularMovies } from "@/types/movie";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import {
  TMDB_IMAGE_ORIGINAL_URL,
  TMDB_IMAGE_POSTER_URL,
} from "@/api/constants";

interface HeroSectionProps {
  movies: MostPopularMovies[];
}

function formatRevenue(amount: number): string {
  if (amount >= 1_000_000_000)
    return `$${(amount / 1_000_000_000).toFixed(1)}B`;
  if (amount >= 1_000_000) return `$${(amount / 1_000_000).toFixed(1)}M`;
  if (amount >= 1_000) return `$${(amount / 1_000).toFixed(1)}K`;
  return `$${amount}`;
}

const HeroSection = ({ movies }: HeroSectionProps) => {
  const [activeIndex, setActiveIndex] = useState(0);
  const [progress, setProgress] = useState(0);
  const [resetKey, setResetKey] = useState(0);

  // Auto-slide every 10 seconds with smooth progress tracking
  useEffect(() => {
    if (!movies || movies.length === 0) return;
    const tickMs = 50;
    const durationMs = 10000;
    let elapsed = 0;
    const timer = setInterval(() => {
      elapsed += tickMs;
      if (elapsed >= durationMs) {
        elapsed = 0;
        setActiveIndex((prev) => (prev + 1) % Math.min(movies.length, 6));
      }
      setProgress(Math.min(elapsed / durationMs, 1));
    }, tickMs);
    return () => clearInterval(timer);
  }, [movies, resetKey]);

  if (!movies || movies.length === 0) return null;

  const currentMovie = movies[activeIndex];

  const backdropUrl = currentMovie.backdropPath
    ? `${TMDB_IMAGE_ORIGINAL_URL}${currentMovie.backdropPath}`
    : `${TMDB_IMAGE_ORIGINAL_URL}${currentMovie.posterPath}`;

  const posterUrl = `${TMDB_IMAGE_POSTER_URL}${currentMovie.posterPath}`;

  return (
    <section className="relative h-[85vh] w-full overflow-hidden bg-black text-white">
      {/* Background Image with Transitions */}
      <AnimatePresence mode="wait">
        <motion.div
          key={currentMovie.id}
          initial={{ opacity: 0, scale: 1.1 }}
          animate={{ opacity: 0.7, scale: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 1.5, ease: "easeOut" }}
          className="absolute inset-0"
        >
          <img
            src={backdropUrl}
            alt={currentMovie.title}
            className="h-full w-full object-cover"
          />
          {/* Advanced Overlays */}
          <div className="absolute inset-0 bg-linear-to-t from-[#030712] via-[#030712]/40 to-transparent" />
          <div className="absolute inset-0 bg-linear-to-r from-[#030712] via-transparent to-transparent" />
          <div className="absolute inset-0 bg-black/20" />
        </motion.div>
      </AnimatePresence>

      {/* Main Content Area */}
      <div className="relative h-full container mx-auto px-6 flex flex-col justify-end pb-20 md:pb-28 text-white">
        <div className="flex flex-col md:flex-row items-end gap-10">
          {/* Small Floating Poster at Bottom Left (Optional/Stunning touch) */}
          <motion.div
            key={`poster-${currentMovie.id}`}
            initial={{ opacity: 0, x: -50 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ duration: 0.8, delay: 0.3 }}
            className="hidden lg:block shrink-0"
          >
            <img
              src={posterUrl}
              alt={currentMovie.title}
              className="w-48 rounded-2xl shadow-[0_20px_50px_rgba(0,0,0,0.5)] border border-white/10"
            />
          </motion.div>

          <div className="flex-1 space-y-6">
            <AnimatePresence mode="wait">
              <motion.div
                key={currentMovie.id}
                initial={{ opacity: 0, y: 30 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -20 }}
                transition={{ duration: 0.6 }}
                className="space-y-4"
              >
                <div className="flex items-center gap-3">
                  <Badge className="bg-primary text-white font-black uppercase tracking-widest px-4 py-1 border-none rounded-md">
                    #{activeIndex + 1} Most Sold Tickets
                  </Badge>
                  <div className="flex items-center gap-2 text-sm font-bold text-white/80">
                    <div className="flex items-center gap-1">
                      <Clock className="w-4 h-4" />
                      <span>
                        {currentMovie.runtime
                          ? `${Math.floor(currentMovie.runtime / 60)}h ${currentMovie.runtime % 60}m`
                          : "2h 10m"}
                      </span>
                    </div>
                    <span className="text-white/40">|</span>
                    <div className="flex items-center gap-1">
                      <DollarSign className="w-4 h-4" />
                      <span>{formatRevenue(currentMovie.totalRevenue)}</span>
                    </div>
                  </div>
                </div>

                <h1 className="text-6xl md:text-8xl lg:text-8xl font-black tracking-tighter leading-[0.8] drop-shadow-2xl uppercase italic text-white">
                  {currentMovie.title}
                </h1>

                <p className="text-lg md:text-xl text-white/80 line-clamp-2 font-medium max-w-2xl leading-relaxed">
                  {currentMovie.overview ||
                    "Experience the thrill of cinematic storytelling. This masterpiece brings together an award-winning cast for an unforgettable journey."}
                </p>

                <div className="flex flex-wrap items-center gap-4 text-sm font-black uppercase tracking-[0.2em] text-primary">
                  {currentMovie.genres?.join(" • ") ?? ""}
                </div>

                <div className="flex flex-wrap items-center gap-4 pt-4">
                  <Link to={`/showtimes?movieId=${currentMovie.id}`}>
                    <Button
                      size="lg"
                      className="h-16 px-10 text-xl font-bold bg-primary hover:bg-primary/90 text-white rounded-2xl shadow-2xl transition-all hover:scale-105 active:scale-95"
                    >
                      <Ticket className="mr-3 h-7 w-7" />
                      Get Your Tickets Now
                    </Button>
                  </Link>
                  <Link to={`/movies/${currentMovie.id}`}>
                    <Button
                      size="icon"
                      variant="outline"
                      className="h-16 w-16 rounded-2xl border-white/20 bg-white/5 backdrop-blur-md hover:bg-white/10 hover:text-primary text-white transition-all hover:scale-105"
                    >
                      <Info className="h-7 w-7" />
                    </Button>
                  </Link>
                </div>
              </motion.div>
            </AnimatePresence>
          </div>
        </div>
      </div>

      {/* Thumbnails at Bottom Right */}
      <div className="absolute bottom-10 right-10 z-20 hidden md:flex items-center gap-3 p-3 bg-black/40 backdrop-blur-2xl rounded-[2rem] border border-white/10 shadow-2xl">
        {movies.slice(0, 6).map((movie, index) => {
          const clipRight =
            index < activeIndex
              ? 0
              : index === activeIndex
                ? (1 - progress) * 100
                : 100;
          return (
            <button
              key={movie.id}
              onClick={() => {
                setActiveIndex(index);
                setProgress(0);
                setResetKey((k) => k + 1);
              }}
              className={`relative shrink-0 transition-all duration-500 rounded-2xl overflow-hidden group ${
                activeIndex === index
                  ? "w-32 h-20 scale-110 mx-2"
                  : "w-20 h-14 hover:scale-105"
              }`}
            >
              <div className="absolute inset-0 rounded-2xl overflow-hidden">
                <img
                  src={`https://image.tmdb.org/t/p/w300${movie.backdropPath || movie.posterPath}`}
                  alt={movie.title}
                  className="w-full h-full object-cover grayscale"
                />
              </div>
              <div
                className="absolute inset-0 rounded-2xl overflow-hidden"
                style={{
                  clipPath: `inset(0 ${clipRight}% 0 0 round 16px)`,
                }}
              >
                <img
                  src={`https://image.tmdb.org/t/p/w300${movie.backdropPath || movie.posterPath}`}
                  alt={movie.title}
                  className="w-full h-full object-cover"
                />
                <div className="absolute inset-0 rounded-2xl border-2 border-primary pointer-events-none" />
              </div>
              <div className="absolute inset-0 bg-black/40 group-hover:bg-transparent transition-colors" />
              {index === 5 && movies.length > 6 && (
                <div className="absolute inset-0 bg-black/60 flex items-center justify-center">
                  <ChevronRight className="w-6 h-6 text-white" />
                </div>
              )}
            </button>
          );
        })}
      </div>
    </section>
  );
};

export default HeroSection;
