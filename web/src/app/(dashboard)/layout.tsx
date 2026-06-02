import Link from "next/link";
import { UploadCloud } from "lucide-react";

import { SidebarNav } from "@/components/dashboard/sidebar-nav";
import { UserMenu } from "@/components/dashboard/user-menu";
import { Separator } from "@/components/ui/separator";

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  const admin = process.env.ADMIN_USER ?? "admin";

  return (
    <div className="flex min-h-svh w-full">
      <aside className="hidden w-64 shrink-0 flex-col border-r bg-sidebar md:flex">
        <div className="flex h-14 items-center gap-2 px-4">
          <UploadCloud className="size-5 text-primary" aria-hidden="true" />
          <Link href="/" className="text-sm font-semibold leading-tight">
            SlackTube
          </Link>
        </div>
        <Separator />
        <div className="flex-1 overflow-y-auto">
          <SidebarNav />
        </div>
      </aside>
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 items-center gap-2 border-b px-4 md:px-6">
          <UploadCloud className="size-5 text-primary md:hidden" aria-hidden="true" />
          <h1 className="text-sm font-medium text-muted-foreground">Slack → YouTube uploader</h1>
          <div className="ml-auto">
            <UserMenu admin={admin} />
          </div>
        </header>
        <main className="flex-1 overflow-y-auto p-4 md:p-6">{children}</main>
      </div>
    </div>
  );
}
