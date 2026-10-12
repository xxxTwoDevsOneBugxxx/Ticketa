import { CATEGORY_COLORS } from "@/static/StaticData";
import type { Movie } from "@/types/movie";
import { clsx, type ClassValue } from "clsx"
import { twMerge } from "tailwind-merge"

export function iconClass(hasError?: string | boolean, isTouched?: boolean) {
  if (hasError && isTouched) return "text-destructive";
  if (isTouched && !hasError) return "text-emerald-500";
  return "text-muted-foreground/30";
}

export function errorBorderClass(error?: string, isTouched?: boolean) {
  if (error && isTouched)
    return "border-destructive/60 focus:border-destructive focus:ring-4 focus:ring-destructive/10";
  return "border-border/50 hover:border-border focus:border-primary/60 focus:ring-4 focus:ring-primary/8";
}

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

export function getCategoryStyle(category: string) {
  return CATEGORY_COLORS[category] ?? CATEGORY_COLORS["Default"];
}

export function seatKey(row: number, seat: number) {
  return `${row}-${seat}`;
}

export function rowLabel(rowIndex: number) {
  return String.fromCharCode(65 + rowIndex);
}

// Stadium bowl shape: how many seats to skip on each side of a row
export function getSkip(rowIndex: number, totalRows: number): number {
  if (rowIndex === 0) return 3;
  if (rowIndex === totalRows - 1) return 2;
  return 0;
}

// Total invisible spacer seats (skipped left + right per row)
export function getInvisibleCount(rows: number): number {
  let count = 0;
  for (let r = 0; r < rows; r++) {
    count += getSkip(r, rows) * 2;
  }
  return count;
}

export function movieByGenre(movies: Movie[]) {
      const genreMap = new Map<string, Movie[]>();

    movies.forEach((movie) => {
      const genres = movie.genres ?? [];
      if (genres.length === 0) {
        const list = genreMap.get("All Now Showing") || [];
        list.push(movie);
        genreMap.set("All Now Showing", list);
        return;
      }
      const primaryGenre = genres[0];
      const list = genreMap.get(primaryGenre) || [];
      list.push(movie);
      genreMap.set(primaryGenre, list);
    });

    const sorted = [...genreMap.entries()]
      .map(([genre, list]) => ({ genre, movies: list }))
      .sort((a, b) => b.movies.length - a.movies.length);

    const main = sorted.filter(
      (s) => s.movies.length >= 2 || s.genre === "All Now Showing",
    );
    const small = sorted.filter(
      (s) => s.movies.length < 2 && s.genre !== "All Now Showing",
    );

    const seen = new Set<string>();
    const mergedSmall: Movie[] = [];
    small.forEach((s) =>
      s.movies.forEach((m) => {
        if (!seen.has(m.id)) {
          seen.add(m.id);
          mergedSmall.push(m);
        }
      }),
    );

    return { sections: main, smallMovies: mergedSmall };
}