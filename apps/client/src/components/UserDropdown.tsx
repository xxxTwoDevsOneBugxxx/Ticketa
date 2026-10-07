import { ChevronDown, LogOut, Ticket, User } from "lucide-react";
import { Button } from "./ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "./ui/dropdown-menu";
import { useNavigate } from "react-router-dom";

interface Props {
  name: string;
  email: string;
  handleLogout: () => void;
}

const UserDropdown = ({ name, email, handleLogout }: Props) => {
  const navigate = useNavigate();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="ghost"
          size="sm"
          className="h-10 font-medium gap-3 rounded-full px-3.5 border border-zinc-200/50 dark:border-zinc-800/50 bg-zinc-50/50 dark:bg-zinc-900/50 transition-all hover:bg-zinc-100 dark:hover:bg-zinc-800 hover:border-zinc-300 dark:hover:border-zinc-700 active:scale-[0.98]"
        >
          <div className="size-7 rounded-full bg-linear-to-br from-primary to-primary/60 flex items-center justify-center text-[11px] text-primary-foreground font-bold shadow-md ring-2 ring-primary/20">
            {(name || email || "U").charAt(0).toUpperCase()}
          </div>
          <span className="truncate max-w-30 text-zinc-900 dark:text-zinc-100 font-semibold">
            {name || email || "User"}
          </span>
          <ChevronDown
            size={14}
            className="text-zinc-400 group-data-[state=open]:rotate-180 transition-transform duration-200"
          />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align="end"
        sideOffset={8}
        className="w-64 rounded-2xl p-2 shadow-2xl border-zinc-200/80 dark:border-zinc-800/80 bg-white dark:bg-zinc-950 backdrop-blur-xl"
      >
        <DropdownMenuLabel className="p-3 mb-1">
          <div className="flex flex-col space-y-1">
            <p className="text-sm font-bold text-zinc-900 dark:text-zinc-100">
              {name}
            </p>
            <p className="text-[11px] font-medium text-zinc-500 truncate">
              {email}
            </p>
          </div>
        </DropdownMenuLabel>
        <DropdownMenuSeparator className="bg-zinc-100 dark:bg-zinc-900 mx-1" />
        <DropdownMenuItem
          onClick={() => navigate("/profile")}
          className="rounded-xl gap-3 h-10 px-3 cursor-pointer transition-colors focus:bg-primary/10 focus:text-primary dark:focus:bg-primary/20"
        >
          <User size={18} className="text-zinc-500" />
          <span className="font-medium">My Profile</span>
        </DropdownMenuItem>
        <DropdownMenuItem
          onClick={() => navigate("/my-tickets")}
          className="rounded-xl gap-3 h-10 px-3 cursor-pointer transition-colors focus:bg-primary/10 focus:text-primary dark:focus:bg-primary/20"
        >
          <Ticket size={18} className="text-zinc-500" />
          <span className="font-medium">My Tickets</span>
        </DropdownMenuItem>
        <DropdownMenuSeparator className="bg-zinc-100 dark:bg-zinc-900 mx-1" />
        <DropdownMenuItem
          onClick={handleLogout}
          className="rounded-xl gap-3 h-10 px-3 text-destructive focus:bg-destructive/10 focus:text-destructive cursor-pointer transition-colors"
        >
          <LogOut size={18} />
          <span className="font-semibold text-destructive">Sign Out</span>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
};

export default UserDropdown;
