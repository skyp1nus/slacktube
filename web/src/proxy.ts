import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";

// Next.js 16 renamed Middleware → Proxy. This is an OPTIMISTIC gate only (presence check);
// the real session validation (HMAC) happens server-side in /dashboard and the API routes.
export function proxy(request: NextRequest) {
  const session = request.cookies.get("session")?.value;
  if (!session) {
    return NextResponse.redirect(new URL("/login", request.url));
  }
  return NextResponse.next();
}

export const config = {
  // Protect everything except the login page, API routes, and static assets.
  matcher: ["/((?!login|api|_next|favicon.ico).*)"],
};
