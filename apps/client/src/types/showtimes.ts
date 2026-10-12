export type HallFormat = "IMAX" | "Gold" | "Standard";

export function getHallFormat(hallName?: string, hallType?: string | number): HallFormat {
  if (hallType !== undefined && hallType !== null) {
    if (typeof hallType === "number") {
      if (hallType === 1) return "IMAX";
      if (hallType === 2) return "Gold";
      return "Standard";
    }
    const normType = String(hallType).toLowerCase().trim();
    if (normType.includes("imax")) return "IMAX";
    if (normType.includes("gold") || normType.includes("vip")) return "Gold";
    return "Standard";
  }

  if (hallName) {
    const norm = hallName.toLowerCase().trim();
    if (norm.includes("imax")) return "IMAX";
    if (norm.includes("gold") || norm.includes("vip")) return "Gold";
  }

  return "Standard";
}

export type MoviesShowtimes = {
  id?: string;
  movieId: string | number;
  tmdbId?: number;
  title: string;
  posterPath?: string;
  trailerKey?: string;
  rate?: number;
  runtime?: number;
  genres?: string[];
  showtimes: Showtime[];
};

export type Showtime = {
  id: string | number;
  hallName: string;
  hallType?: string | number;
  hallId?: number;
  startTime: string;
  endTime?: string;
  price: number;
};

export type BookedSeat = {
	row: number;
	seatNumber: number;
}

export type ShowtimeSeats = {
  movieId: number;
	showtimeId: string;
	movieTitle: string;
	moviePosterPath?: string;
	hallName: string;
	hallType: string;
	startsAt: string;
	price: number;
	rows: number;
	seatsPerRow: number;
	rowCategoryMap: Record<number, string>;
	bookedSeats: BookedSeat[];
}