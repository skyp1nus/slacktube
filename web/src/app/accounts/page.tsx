import { requireSession, backendGet, backendBaseUrl } from "@/lib/backend";
import { SiteNav } from "@/components/site-nav";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { AccountCard } from "./account-card";
import { OAuthToast } from "./oauth-toast";

export const dynamic = "force-dynamic";

type Account = {
  id: string;
  label: string;
  youTubeChannelId: string | null;
  youTubeChannelTitle: string | null;
  accountEmail: string | null;
  status: string;
  createdAt: string;
  quota: { usedUnits: number; remainingUploads: number; totalUploads: number };
};

export default async function AccountsPage() {
  await requireSession();
  const accounts = await backendGet<Account[]>("/api/admin/google/accounts").catch(() => [] as Account[]);

  return (
    <>
      <SiteNav />
      <OAuthToast />
      <div className="mx-auto w-full max-w-4xl space-y-6 p-6">
        <header className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold">Google accounts</h1>
            <p className="text-sm text-muted-foreground">
              Connect one or more YouTube channels. Each consent adds a new account.
            </p>
          </div>
          <a className={buttonVariants()} href={`${backendBaseUrl()}/google/oauth/start`}>
            Connect Google account
          </a>
        </header>

        {accounts.length === 0 ? (
          <Card>
            <CardContent className="py-12 text-center text-sm text-muted-foreground">
              No Google accounts connected yet. Click “Connect Google account” to add one.
            </CardContent>
          </Card>
        ) : (
          <div className="space-y-4">
            {accounts.map((a) => (
              <AccountCard key={a.id} account={a} />
            ))}
          </div>
        )}

        <p className="text-xs text-muted-foreground">
          Note: YouTube quota is enforced per Google Cloud project (OAuth client), not per channel —
          accounts sharing one OAuth client share the ~6 uploads/day cap.
        </p>
      </div>
    </>
  );
}
