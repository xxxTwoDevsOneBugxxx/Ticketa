import { ArrowLeft, Ticket, Play } from "lucide-react";
import {
  AnimatePresence,
  motion,
  useScroll,
  useTransform,
} from "framer-motion";
import { Button } from "@/components/ui/button";
import { useRef, useState } from "react";
import { useMovieDetails } from "@/hooks/useMovies";
import { useNavigate, useParams } from "react-router-dom";
import { MovieStats } from "@/components/MovieStats";
import { CastSection } from "@/components/CastSection";
import ErrorState from "@/components/ErrorState";
import TrailerDialog from "@/components/TrailerDialog";
import MovieDetailsSkeleton from "@/components/skeletons/MovieDetailsSkeleton";
import { TMDB_IMAGE_BASE_URL, TMDB_IMAGE_ORIGINAL_URL } from "@/api/constants";
import ExternalLinkIcons from "@/components/ExternalLinkIcons";

const MovieDetailsPage = () => {
  const { id } = useParams();
  const navigate = useNavigate();
  const { data: movie, isLoading, isError, refetch } = useMovieDetails(id!);
  const [isVideoVisible, setIsVideoVisible] = useState(false);

  const containerRef = useRef<HTMLDivElement>(null);
  const { scrollYProgress } = useScroll({
    target: containerRef,
    offset: ["start start", "end start"],
  });

  // Parallax & Motion effects
  const titleY = useTransform(scrollYProgress, [0, 0.3], [0, -50]);
  const titleOpacity = useTransform(scrollYProgress, [0, 0.2], [1, 0]);
  const backdropScale = useTransform(scrollYProgress, [0, 0.5], [1.1, 1.3]);
  const backdropBlur = useTransform(scrollYProgress, [0, 0.5], [0, 8]);

  if (isLoading) return <MovieDetailsSkeleton />;
  if (isError || !movie) return <ErrorState refetch={refetch} />;

  return (
    <div
      ref={containerRef}
      className="min-h-screen bg-white dark:bg-[#080a0f] text-black dark:text-[#e8e4dd] overflow-x-hidden"
    >
      {/* Cinematic Background Particles */}
      <div className="fixed inset-0 pointer-events-none z-0">
        <div className="absolute top-[-10%] left-[-10%] w-[40%] h-[40%] bg-primary/10 blur-[120px] rounded-full" />
        <div className="absolute bottom-[-10%] right-[-10%] w-[40%] h-[40%] bg-blue-500/10 blur-[120px] rounded-full" />
      </div>

      {/* 1. Immersive Hero Stage */}
      <section className="relative min-h-[95vh] lg:h-[95vh] w-full flex items-center justify-center py-20 lg:py-0">
        {/* Cinematic Background */}
        <motion.div
          style={{ scale: backdropScale, filter: `blur(${backdropBlur}px)` }}
          className="absolute inset-0 w-full h-[60vh] lg:h-full"
        >
          <img
            src={`${TMDB_IMAGE_ORIGINAL_URL}${movie.backdropPath}`}
            alt={movie.title}
            className="w-full h-full object-cover opacity-30 dark:opacity-50 contrast-125"
          />
          <div className="absolute inset-0 bg-linear-to-t from-white dark:from-[#080a0f] via-transparent dark:via-[#080a0f]/40 to-transparent" />
          <div className="absolute inset-0 bg-linear-to-r from-white dark:from-[#080a0f] via-transparent to-white dark:to-[#080a0f]" />
        </motion.div>

        {/* Global Navigation Overlay */}
        <div className="absolute top-0 left-0 right-0 p-4 lg:p-8 flex justify-between items-center z-50">
          <motion.div whileHover={{ x: -5 }}>
            <Button
              variant="ghost"
              onClick={() => navigate(-1)}
              className="group -z-50 flex items-center gap-3 px-6 py-6 rounded-full bg-black/5 dark:bg-white/5 border border-black/10 dark:border-white/10 backdrop-blur-3xl hover:bg-black/10 dark:hover:bg-white/10 text-black dark:text-white"
            >
              <ArrowLeft className="w-4 h-4 transition-transform group-hover:-translate-x-1 text-primary" />
              <span className="text-sm font-bold tracking-widest uppercase">
                Go Back
              </span>
            </Button>
          </motion.div>
        </div>

        {/* Hero Content - Poster Left, Details Right */}
        <div className="relative mt-24 z-20 w-full max-w-7xl px-6 grid grid-cols-1 lg:grid-cols-12 gap-12 lg:gap-24 items-center">
          {/* Poster Section (Left) */}
          <div className="hidden lg:block lg:col-span-4 relative group">
            <motion.div
              initial={{ opacity: 0, scale: 0.8, x: -50 }}
              animate={{ opacity: 1, scale: 1, x: 0 }}
              className="relative aspect-[2/3] rounded-[3rem] overflow-hidden shadow-[0_40px_80px_-15px_rgba(0,0,0,0.4)] dark:shadow-[0_40px_80px_-15px_rgba(0,0,0,0.8)] border border-black/10 dark:border-white/10 preserve-3d"
            >
              <img
                src={`${TMDB_IMAGE_BASE_URL}/w780${movie.posterPath}`}
                alt={movie.title}
                className="w-full h-full object-cover"
              />

              <div className="absolute inset-0 bg-linear-to-t from-black/60 to-transparent opacity-0 group-hover:opacity-100 transition-opacity" />
            </motion.div>
          </div>

          {/* Details Section (Right) */}
          <div className="lg:col-span-8 space-y-8">
            <motion.div
              initial={{ opacity: 0, y: 30 }}
              animate={{ opacity: 1, y: 0 }}
              className="flex flex-wrap gap-3"
            >
              {movie.genres?.map((genre) => (
                <span
                  key={genre}
                  className="px-4 py-1.5 rounded-full bg-black/5 dark:bg-white/5 border border-black/10 dark:border-white/10 text-[10px] font-black uppercase tracking-[0.2em] text-black/60 dark:text-white/60 backdrop-blur-md"
                >
                  {genre}
                </span>
              ))}
            </motion.div>

            <motion.div
              style={{ y: titleY, opacity: titleOpacity }}
              className="space-y-4"
            >
              <h1 className="text-4xl md:text-8xl font-black tracking-tighter leading-[0.9] text-black dark:text-white italic italic-outline-bold">
                {movie.title}
              </h1>
            </motion.div>

            <MovieStats
              rating={movie.voteAverage}
              runtime={movie.runtime}
              releaseDate={movie.releaseDate}
              language={movie.language}
            />

            {/* Synopsis next to poster */}
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.3 }}
              className="space-y-4"
            >
              <h3 className="text-[10px] font-black uppercase tracking-[0.5em] text-primary flex items-center gap-3">
                <span className="w-1 h-4 bg-primary rounded-full" />
                SYNOPSIS
              </h3>
              <p className="font-medium tracking-tight leading-relaxed text-black/80 dark:text-white/80 max-w-2xl">
                {movie.overview}
              </p>
            </motion.div>

            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.4 }}
              className="flex flex-col sm:flex-row flex-wrap gap-4 lg:gap-6 pt-4"
            >
              <Button
                size="lg"
                className="h-14 lg:h-16 px-8 lg:px-10 rounded-2xl bg-primary text-white font-black uppercase tracking-widest gap-3 shadow-[0_20px_40px_-10px_rgba(234,88,12,0.4)] hover:bg-primary/90 hover:scale-[1.02] active:scale-95 transition-all outline-none border-none"
                onClick={() => navigate(`/showtimes?movieId=${movie.id}`)}
              >
                <Ticket className="w-5 h-5 lg:w-6 lg:h-6" />
                Book Tickets Now
              </Button>

              {movie.trailerKey && (
                <Button
                  variant="outline"
                  size="lg"
                  onClick={() => setIsVideoVisible(true)}
                  className="h-14 lg:h-16 px-8 lg:px-10 rounded-2xl bg-black/5 dark:bg-white/5 border border-black/10 dark:border-white/10 text-black dark:text-white font-black uppercase tracking-widest gap-3 backdrop-blur-xl hover:bg-black/10 dark:hover:bg-white/10 transition-all shadow-none"
                >
                  <Play className="w-5 h-5 lg:w-6 lg:h-6 fill-primary text-primary" />
                  Watch Trailer
                </Button>
              )}
            </motion.div>
            <ExternalLinkIcons imdbId={movie.imdbId} tmdbId={movie.tmdbId} />
          </div>
        </div>
      </section>

      {/* 2. Cast Section - Underneath */}
      <section className="relative z-20 px-6 lg:px-20 max-w-7xl mx-auto py-20 pb-40 pt-0">
        <CastSection cast={movie.cast} />
      </section>

      {/* Video Modal */}
      <AnimatePresence>
        {isVideoVisible && movie.trailerKey && (
          <TrailerDialog
            trailerKey={movie.trailerKey}
            setIsVideoVisible={setIsVideoVisible}
          />
        )}
      </AnimatePresence>
    </div>
  );
};

export default MovieDetailsPage;
