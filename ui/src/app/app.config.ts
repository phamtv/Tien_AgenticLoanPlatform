import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';

// BUG FIX (2026-09-14): this project has no zone.js polyfill at all — it's
// not in angular.json's build options and not even in package.json's
// dependencies (checked both). That's consistent with a modern Angular 21
// app meant to run zoneless, but this providers array never actually
// declared that — there was no provideZonelessChangeDetection() call and
// no zone.js either, so Angular had NEITHER a zone to patch async APIs NOR
// a properly configured zoneless change-detection scheduler telling it
// when to re-render after something changes.
//
// That's exactly what was breaking "click a row, nothing happens": the
// (click) handler on each row ran fine and updated expandedApplicationId()
// correctly every time, but with no real change-detection driver wired up,
// Angular never reliably re-ran its template checks afterward, so the DOM
// never repainted. It's also almost certainly the source of the hundreds
// of `[Violation] 'requestAnimationFrame' handler took Nms` console
// messages seen while debugging this — Angular's internal fallback
// scheduling was thrashing (rescheduling checks over and over, each one
// expensive because of the non-memoized `applicationRows` getter building
// a brand-new array every pass) without ever settling, instead of the
// clean, on-demand zoneless scheduling this call turns on.
//
// This also explains why the login screen "worked": restoreSession() in
// app.ts calls this.cdr.detectChanges() manually right after loading data,
// which was a workaround for this exact gap, papering over just that one
// spot rather than fixing change detection app-wide. Every other
// interaction (row expand, the Refresh button, submitting a new
// application, document extraction) had no such manual call and so never
// visibly updated. Adding the provider here fixes it everywhere at once,
// the standard/supported way, and the ad-hoc detectChanges() workaround in
// app.ts's restoreSession() can be removed once this is confirmed working
// (left in place for now since it's harmless).
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideHttpClient(),
  ]
};
