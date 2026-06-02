import Link from "next/link";
import { LogoutButton } from "@/app/dashboard/logout-button";

const links = [
  { href: "/dashboard", label: "Dashboard" },
  { href: "/slack", label: "Slack" },
  { href: "/accounts", label: "Accounts" },
  { href: "/mapping", label: "Mapping" },
];

export function SiteNav() {
  return (
    <header className="border-b bg-background">
      <div className="mx-auto flex max-w-5xl items-center justify-between gap-4 p-4">
        <nav className="flex items-center gap-4 text-sm">
          <Link href="/dashboard" className="font-semibold">SlackTube</Link>
          {links.map((l) => (
            <Link key={l.href} href={l.href} className="text-muted-foreground hover:text-foreground">
              {l.label}
            </Link>
          ))}
        </nav>
        <LogoutButton />
      </div>
    </header>
  );
}
