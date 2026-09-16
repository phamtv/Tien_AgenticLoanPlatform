import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { loadServiceConfig } from './app/service-config';

// AZURE MIGRATION CHANGE: load real backend URLs from /config.json before
// the app renders anything, so no component ever sees the localhost
// placeholder values in a deployed environment. See service-config.ts.
loadServiceConfig().finally(() => {
  bootstrapApplication(App, appConfig)
    .catch((err) => console.error(err));
});
