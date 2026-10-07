import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "./ui/sheet";
import { Link, NavLink } from "react-router-dom";
import { Button } from "./ui/button";
import { LogIn, LogOut, Menu, Ticket, User, UserPlus } from "lucide-react";

interface Props {
  navLinks: { to: string; label: string; icon: React.ElementType }[];
  name?: string;
  email?: string;
  isLoggedIn: boolean;
  isOpen: boolean;
  setIsOpen: (open: boolean) => void;
  handleLogout: () => void;
}

const MobileMenu = ({
  navLinks,
  name,
  email,
  isLoggedIn,
  isOpen,
  setIsOpen,
  handleLogout,
}: Props) => {
  return (
    <div className="md:hidden">
      <Sheet open={isOpen} onOpenChange={setIsOpen}>
        <SheetTrigger asChild>
          <Button
            variant="ghost"
            size="icon"
            className="hover:bg-accent rounded-full transition-colors"
            aria-label="Open menu"
          >
            <Menu size={24} />
          </Button>
        </SheetTrigger>
        <SheetContent
          side="right"
          className="w-80 p-0 border-l border-border/50"
        >
          <div className="h-full flex flex-col bg-background">
            <SheetHeader className="p-6 border-b border-border/10">
              <SheetTitle className="flex items-center gap-2">
                <Ticket size={24} className="text-primary" />
                <span className="font-black tracking-tighter uppercase text-2xl">
                  ticke<span className="text-primary">ta</span>
                </span>
              </SheetTitle>
            </SheetHeader>

            <div className="flex-1 overflow-y-auto py-6 px-4">
              {/* User Section for Mobile */}
              {isLoggedIn && (
                <div className="mb-8 px-2">
                  <div className="flex items-center gap-4 p-4 rounded-2xl bg-muted/30 border border-border/50">
                    <div className="size-12 rounded-full bg-primary/20 flex items-center justify-center text-lg font-bold text-primary">
                      {(name || email || "U").charAt(0).toUpperCase()}
                    </div>
                    <div className="flex flex-col overflow-hidden">
                      <span className="font-bold truncate">{name}</span>
                      <span className="text-xs text-muted-foreground truncate">
                        {email}
                      </span>
                    </div>
                  </div>
                </div>
              )}

              <div className="space-y-6">
                <div className="space-y-2">
                  <p className="text-[10px] font-bold text-muted-foreground uppercase tracking-[0.2em] px-3">
                    Navigation
                  </p>
                  <nav className="flex flex-col gap-1">
                    {navLinks.map((link) => (
                      <NavLink
                        key={link.to}
                        to={link.to}
                        onClick={() => setIsOpen(false)}
                        className={({ isActive }) =>
                          `flex items-center gap-4 px-4 py-3.5 rounded-xl transition-all duration-200 
                              ${
                                isActive
                                  ? "bg-primary text-primary-foreground font-bold shadow-md shadow-primary/20"
                                  : "text-muted-foreground hover:bg-accent hover:text-foreground"
                              }
                                `
                        }
                      >
                        {({ isActive }) => (
                          <>
                            <link.icon
                              size={20}
                              className={
                                isActive
                                  ? "text-primary-foreground"
                                  : "text-primary"
                              }
                            />
                            <span className="text-sm">{link.label}</span>
                          </>
                        )}
                      </NavLink>
                    ))}
                  </nav>
                </div>

                <div className="space-y-2">
                  <p className="text-[10px] font-bold text-muted-foreground uppercase tracking-[0.2em] px-3">
                    Account
                  </p>
                  <div className="flex flex-col gap-2">
                    {isLoggedIn ? (
                      <>
                        <Link to="/profile" onClick={() => setIsOpen(false)}>
                          <Button
                            variant="ghost"
                            className="w-full justify-start gap-4 rounded-xl h-12 text-muted-foreground hover:text-foreground hover:bg-accent px-4"
                          >
                            <User size={20} className="text-primary" />
                            <span className="text-sm font-medium">
                              My Profile
                            </span>
                          </Button>
                        </Link>
                        <Link to="/my-tickets" onClick={() => setIsOpen(false)}>
                          <Button
                            variant="ghost"
                            className="w-full justify-start gap-4 rounded-xl h-12 text-muted-foreground hover:text-foreground hover:bg-accent px-4"
                          >
                            <Ticket size={20} className="text-primary" />
                            <span className="text-sm font-medium">
                              My Tickets
                            </span>
                          </Button>
                        </Link>
                        <Button
                          variant="ghost"
                          onClick={handleLogout}
                          className="w-full justify-start gap-4 rounded-xl h-12 text-destructive hover:text-destructive hover:bg-destructive/10 px-4"
                        >
                          <LogOut size={20} />
                          <span className="text-sm font-medium">Logout</span>
                        </Button>
                      </>
                    ) : (
                      <>
                        <Link
                          to="/login"
                          onClick={() => setIsOpen(false)}
                          className="w-full"
                        >
                          <Button
                            variant="ghost"
                            className="w-full justify-start gap-4 rounded-xl h-12 text-muted-foreground hover:text-foreground hover:bg-accent px-4"
                          >
                            <LogIn size={20} className="text-primary" />
                            <span className="text-sm font-medium">Sign In</span>
                          </Button>
                        </Link>
                        <Link
                          to="/register"
                          onClick={() => setIsOpen(false)}
                          className="w-full"
                        >
                          <Button className="w-full justify-start gap-4 rounded-xl h-12 shadow-lg shadow-primary/20 px-4">
                            <UserPlus size={20} />
                            <span className="text-sm font-medium">
                              Create Account
                            </span>
                          </Button>
                        </Link>
                      </>
                    )}
                  </div>
                </div>
              </div>
            </div>

            <div className="p-6 bg-muted/10 border-t border-border/50 mt-auto">
              <p className="text-[10px] text-center text-muted-foreground font-medium uppercase tracking-widest">
                &copy; 2026 Ticketa • Cinematic Experience
              </p>
            </div>
          </div>
        </SheetContent>
      </Sheet>
    </div>
  );
};

export default MobileMenu;
