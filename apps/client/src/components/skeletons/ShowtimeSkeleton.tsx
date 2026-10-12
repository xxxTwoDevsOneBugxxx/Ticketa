import { Skeleton } from "@/components/ui/skeleton";
import { Card, CardContent } from "@/components/ui/card";

const ShowtimeSkeleton = () => {
  return (
    <div className="min-h-screen bg-background text-foreground pb-20">
      {/* Hero Section Skeleton */}
      <div className="relative h-[50vh] w-full overflow-hidden bg-muted/20">
        <div className="container mx-auto px-4 h-full flex items-end pb-12 relative z-10">
          <div className="flex flex-col md:flex-row gap-8 items-end w-full">
            <Skeleton className="hidden md:block w-48 aspect-2/3 rounded-2xl shadow-2xl" />
            <div className="flex-1 space-y-4">
              <Skeleton className="h-6 w-32 rounded-full mb-2" />
              <Skeleton className="h-12 w-3/4 md:w-1/2 rounded-lg" />
              <div className="flex gap-4">
                <Skeleton className="h-6 w-40 rounded-md" />
                <Skeleton className="h-10 w-36 rounded-full" />
              </div>
            </div>
          </div>
        </div>
      </div>

      <div className="container mx-auto px-4 -mt-6 relative z-20">
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-8">
          {/* Sidebar Skeleton */}
          <div className="lg:col-span-4 space-y-6">
            <div className="flex items-center justify-between">
              <Skeleton className="h-8 w-40" />
              <Skeleton className="h-4 w-16" />
            </div>
            <div className="flex flex-col gap-3">
              {[1, 2, 3, 4, 5].map((i) => (
                <Card key={i} className="border-none bg-card">
                  <CardContent className="p-3">
                    <div className="flex gap-4 items-center">
                      <Skeleton className="w-12 h-18 rounded-md" />
                      <div className="flex-1 space-y-2">
                        <Skeleton className="h-4 w-3/4" />
                        <Skeleton className="h-3 w-1/2" />
                      </div>
                    </div>
                  </CardContent>
                </Card>
              ))}
            </div>
          </div>

          {/* Showtimes Content Skeleton */}
          <div className="lg:col-span-8 space-y-8">
            <div className="bg-card rounded-3xl p-6 md:p-8 border border-white/5 shadow-xl">
              <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 mb-8">
                <div className="space-y-3">
                  <Skeleton className="h-8 w-56" />
                  <Skeleton className="h-4 w-44" />
                </div>
                <div className="flex gap-2">
                  <Skeleton className="h-7 w-20 rounded-full" />
                  <Skeleton className="h-7 w-20 rounded-full" />
                </div>
              </div>

              {/* Grouped Showtimes Skeletons */}
              <div className="space-y-12">
                {[1, 2].map((group) => (
                  <div key={group} className="space-y-6">
                    <div className="flex items-center gap-4">
                      <Skeleton className="h-7 w-48" />
                      <Skeleton className="h-px flex-1" />
                    </div>
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      {[1, 2, 3, 4].map((i) => (
                        <Skeleton key={i} className="h-24 w-full rounded-2xl" />
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};

export const SessionsSkeleton = ({ movieTitle }: { movieTitle?: string }) => {
  return (
    <div className="bg-card rounded-3xl p-6 md:p-8 border border-white/5 shadow-xl">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 mb-8">
        <div className="space-y-2">
          <div className="flex items-center gap-2">
            <h3 className="text-2xl font-bold">Available Sessions</h3>
            <span className="text-xs font-semibold px-2.5 py-0.5 rounded-full bg-primary/10 text-primary animate-pulse">
              Loading...
            </span>
          </div>
          {movieTitle ? (
            <p className="text-xs text-muted-foreground">
              Fetching upcoming sessions for <span className="text-foreground font-semibold">{movieTitle}</span>...
            </p>
          ) : (
            <Skeleton className="h-4 w-44" />
          )}
        </div>
        <div className="flex gap-2">
          <Skeleton className="h-7 w-20 rounded-full" />
          <Skeleton className="h-7 w-20 rounded-full" />
        </div>
      </div>

      {/* Grouped Showtimes Skeletons */}
      <div className="space-y-12">
        {[1, 2].map((group) => (
          <div key={group} className="space-y-6">
            <div className="flex items-center gap-4">
              <Skeleton className="h-7 w-48" />
              <Skeleton className="h-px flex-1" />
            </div>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {[1, 2, 3, 4].map((i) => (
                <div
                  key={i}
                  className="h-24 w-full rounded-2xl bg-accent/40 border border-white/5 p-4 flex items-center justify-between animate-pulse"
                >
                  <div className="flex items-center gap-3">
                    <Skeleton className="w-9 h-9 rounded-xl" />
                    <div className="space-y-2">
                      <Skeleton className="h-5 w-24" />
                      <Skeleton className="h-3 w-32" />
                    </div>
                  </div>
                  <div className="space-y-2 flex flex-col items-end">
                    <Skeleton className="h-4 w-12" />
                    <Skeleton className="h-3 w-16" />
                  </div>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};

export default ShowtimeSkeleton;
