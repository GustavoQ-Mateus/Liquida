import {
  ApplicationConfig,
  importProvidersFrom,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import {
  Activity,
  CircleCheck,
  Clock,
  Inbox,
  Landmark,
  Layers,
  LucideAngularModule,
  Receipt,
  Send,
  TriangleAlert,
} from 'lucide-angular';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideHttpClient(),
    importProvidersFrom(
      LucideAngularModule.pick({
        Activity,
        Clock,
        Send,
        CircleCheck,
        TriangleAlert,
        Layers,
        Inbox,
        Receipt,
        Landmark,
      }),
    ),
  ],
};
