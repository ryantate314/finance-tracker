import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { FamilyContextService } from './family-context.service';
import { environment } from '../../../environments/environment';

export const familyIdInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith(environment.apiBaseUrl)) return next(req);
  if (req.url.endsWith('/api/families') || req.url.includes('/api/families?')) return next(req);

  const familyId = inject(FamilyContextService).activeFamilyId();
  if (!familyId) throw new Error('No active family selected — cannot make scoped API request.');

  return next(req.clone({ setHeaders: { 'X-Family-Id': familyId } }));
};
