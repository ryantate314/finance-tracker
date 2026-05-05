export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
}

export function extractErrorMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'error' in error) {
    const body = (error as { error: unknown }).error;
    if (body && typeof body === 'object' && 'title' in body) {
      return (body as ProblemDetails).title ?? 'An error occurred';
    }
  }
  return 'An error occurred';
}
