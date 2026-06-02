/**
 * Typed fetch helper. Base URL is same-origin ("") so requests hit SlackTube's Next BFF
 * (`/api/admin/*` route handlers), which attach the server-only X-Admin-Token and proxy to the
 * backend. Sends cookies so the admin session rides along; throws ApiError on non-2xx.
 */

export const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "";

export class ApiError extends Error {
  readonly status: number;
  readonly data: unknown;

  constructor(status: number, message: string, data: unknown) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.data = data;
  }
}

type RequestOptions = Omit<RequestInit, "body" | "method">;

function buildUrl(path: string): string {
  if (/^https?:\/\//i.test(path)) return path;
  const base = API_URL.replace(/\/$/, "");
  const suffix = path.startsWith("/") ? path : `/${path}`;
  return `${base}${suffix}`;
}

function extractMessage(data: unknown): string | null {
  if (typeof data === "string" && data.length > 0) return data;
  if (data && typeof data === "object" && "message" in data) {
    const message = (data as { message: unknown }).message;
    if (typeof message === "string" && message.length > 0) return message;
  }
  return null;
}

async function request<TResponse>(
  method: string,
  path: string,
  body?: unknown,
  options: RequestOptions = {},
): Promise<TResponse> {
  const headers = new Headers(options.headers);
  headers.set("Accept", "application/json");

  const hasBody = body !== undefined && body !== null;
  if (hasBody) headers.set("Content-Type", "application/json");

  const res = await fetch(buildUrl(path), {
    ...options,
    method,
    headers,
    credentials: "include",
    cache: "no-store",
    body: hasBody ? JSON.stringify(body) : undefined,
  });

  const text = await res.text();
  let data: unknown = null;
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = text;
    }
  }

  if (!res.ok) {
    const message = extractMessage(data) ?? res.statusText ?? `Request failed with status ${res.status}`;
    throw new ApiError(res.status, message, data);
  }

  return data as TResponse;
}

export const api = {
  get: <TResponse>(path: string, options?: RequestOptions) => request<TResponse>("GET", path, undefined, options),
  post: <TResponse>(path: string, body?: unknown, options?: RequestOptions) =>
    request<TResponse>("POST", path, body, options),
  patch: <TResponse>(path: string, body?: unknown, options?: RequestOptions) =>
    request<TResponse>("PATCH", path, body, options),
  del: <TResponse>(path: string, options?: RequestOptions) => request<TResponse>("DELETE", path, undefined, options),
};

/** Pull the best human-readable message out of a thrown error (backend returns `{ error }`). */
export function apiErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    const data = error.data;
    if (
      data &&
      typeof data === "object" &&
      "error" in data &&
      typeof (data as { error: unknown }).error === "string" &&
      (data as { error: string }).error.length > 0
    ) {
      return (data as { error: string }).error;
    }
    if (error.message) return error.message;
  }
  if (error instanceof Error && error.message) return error.message;
  return fallback;
}
