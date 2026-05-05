import { APP_INITIALIZER, ApplicationConfig, inject, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { firstValueFrom } from 'rxjs';

import { routes } from './app.routes';
import { familyIdInterceptor } from './core/family-context/family-id.interceptor';
import { FamiliesService } from './features/families/families.service';
import { FamilyContextService } from './core/family-context/family-context.service';

const DEFAULT_FAMILY_ID = '00000000-0000-0000-0000-000000000001';

function bootstrapActiveFamily() {
  const familiesSvc = inject(FamiliesService);
  const familyCtx = inject(FamilyContextService);
  return async () => {
    const families = await firstValueFrom(familiesSvc.refresh());
    const stored = familyCtx.activeFamilyId();
    if (!stored || !families.find(f => f.id === stored)) {
      const fallback = families.find(f => f.id === DEFAULT_FAMILY_ID) ?? families[0];
      if (fallback) familyCtx.setActive(fallback.id);
    }
  };
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([familyIdInterceptor])),
    provideAnimationsAsync(),
    { provide: APP_INITIALIZER, useFactory: bootstrapActiveFamily, multi: true },
  ],
};
