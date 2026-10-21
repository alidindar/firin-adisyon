export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string | null,
    message: string,
  ) {
    super(message);
  }
}

const defaultMessages: Record<number, string> = {
  401: 'Oturum kapandı, tekrar giriş yapın.',
  403: 'Bu işlem için yetkiniz yok.',
  404: 'Bulunamadı.',
};

/** `path` `/api` öneki olmadan verilir: api('/auth/me'). */
export async function api<T>(path: string, init: RequestInit & { json?: unknown } = {}): Promise<T> {
  const { json, ...rest } = init;
  const headers = new Headers(rest.headers);
  if (json !== undefined) headers.set('Content-Type', 'application/json');

  let res: Response;
  try {
    res = await fetch(`/api${path}`, {
      ...rest,
      headers,
      body: json !== undefined ? JSON.stringify(json) : rest.body,
      credentials: 'same-origin',
    });
  } catch {
    throw new ApiError(0, 'baglanti_yok', 'Kasa bilgisayarına ulaşılamıyor.');
  }

  if (!res.ok) {
    let code: string | null = null;
    let title: string | null = null;
    try {
      const body = await res.json();
      code = body.code ?? null;
      title = body.title ?? null;
    } catch {
      // gövde yok
    }
    // Yalnızca bizim DomainException'larımızın başlığı Türkçedir.
    const message = code && title ? title : (defaultMessages[res.status] ?? 'Bir hata oluştu.');
    throw new ApiError(res.status, code, message);
  }
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}
