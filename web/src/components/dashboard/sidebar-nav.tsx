"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  LayoutDashboard,
  Hash,
  MonitorPlay,
  Boxes,
  Link2,
  ScrollText,
  Settings,
  type LucideIcon,
} from "lucide-react";

import { cn } from "@/lib/utils";

type NavItem = {
  title: string;
  href: string;
  icon: LucideIcon;
};

const navItems: NavItem[] = [
  { title: "Dashboard", href: "/", icon: LayoutDashboard },
  { title: "Slack", href: "/slack", icon: Hash },
  { title: "YouTube Account", href: "/youtube-account", icon: MonitorPlay },
  { title: "Projects", href: "/projects", icon: Boxes },
  { title: "Mapping", href: "/mapping", icon: Link2 },
  { title: "History", href: "/history", icon: ScrollText },
  { title: "Settings", href: "/settings", icon: Settings },
];

export function SidebarNav() {
  const pathname = usePathname();

  return (
    <nav className="flex flex-col gap-1 p-2">
      {navItems.map((item) => {
        const isActive =
          item.href === "/"
            ? pathname === "/"
            : pathname === item.href || pathname.startsWith(`${item.href}/`);
        const Icon = item.icon;

        return (
          <Link
            key={item.href}
            href={item.href}
            aria-current={isActive ? "page" : undefined}
            className={cn(
              "flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors",
              "text-muted-foreground hover:bg-accent hover:text-accent-foreground",
              isActive && "bg-accent text-accent-foreground",
            )}
          >
            <Icon className="size-4 shrink-0" aria-hidden="true" />
            <span>{item.title}</span>
          </Link>
        );
      })}
    </nav>
  );
}
